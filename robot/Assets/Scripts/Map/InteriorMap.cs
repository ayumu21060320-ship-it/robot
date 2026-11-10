using UnityEngine;

/// <summary>
/// 巨大ロボットの内部マップ。部屋と通路を長方形で定義し、
/// そこから「歩ける場所」の判定と、床・壁・操作盤の見た目を作る。
/// 内部は外の世界から遠く離れた場所（Origin）に置き、真上からのカメラで映す。
///
///             [ 頭部 操縦室 ]
///                   |
///  [左腕]---[    胸部ホール    ]---[右腕]
///                   |
///             [  動力炉室  ]
///                   |
///        [========= 腰の通路 =========]
///           |                    |
///        [左脚室]            [右脚室]
/// </summary>
public static class InteriorMap
{
    public static readonly Vector3 Origin = new Vector3(2000f, 0f, 0f);

    /// <summary>
    /// 内部の広さの倍率。部屋の配置（Areas）は「設計マス」で書き、実際の大きさ（メートル）はこれを掛けたもの。
    /// 巨大ロボットらしく広くしたいときはこの数字を上げる。
    /// </summary>
    public const float Scale = 1.6f;

    public static readonly Vector2 Spawn = new Vector2(20f, 17.5f) * Scale;
    public static readonly Vector2 Center = new Vector2(20f, 12.5f) * Scale;
    /// <summary>内部全体の範囲（メートル）。ミニマップ用。</summary>
    public static readonly Rect Bounds = new Rect(0f, -8f * Scale, 40f * Scale, 41f * Scale);

    /// <summary>部屋や通路。座標は設計マス（実際の大きさは Scale 倍）。</summary>
    public struct Area
    {
        public string Name;
        public RectInt Rect; // x, y(=z), 幅, 高さ（メートル）
        public Color Floor;

        public Area(string name, int x, int z, int w, int h, string floorHex)
        {
            Name = name;
            Rect = new RectInt(x, z, w, h);
            Floor = Prims.Hex(floorHex);
        }
    }

    const string Hall = "#3a434a", Corridor = "#2c3338";

    public static readonly Area[] Areas =
    {
        new Area("頭部 操縦室", 14, 25, 12, 8, "#34424a"),
        new Area("首の通路",    18, 21,  4, 4, Corridor),
        new Area("胸部ホール",  12, 14, 16, 7, Hall),
        new Area("左肩の通路",   6, 16,  6, 3, Corridor),
        new Area("左腕室",       0, 12,  6, 10, "#4a3c30"),
        new Area("右肩の通路",  28, 16,  6, 3, Corridor),
        new Area("右腕室",      34, 12,  6, 10, "#4a3c30"),
        new Area("胸の下の通路", 18, 12, 4, 2, Corridor),
        new Area("動力炉室",    15,  6, 10, 6, "#47432a"),
        new Area("腰へ降りる通路", 18, 5, 4, 1, Corridor),
        new Area("腰の通路",     6,  2, 28, 3, Corridor),
        new Area("左脚の通路",   9,  0,  3, 2, Corridor),
        new Area("左脚室",       6, -8,  9, 8, "#2e4048"),
        new Area("右脚の通路",  28,  0,  3, 2, Corridor),
        new Area("右脚室",      25, -8,  9, 8, "#2e4048"),
    };

    const int GX0 = -1, GZ0 = -9, GW = 43, GH = 44;
    static bool[,] s_Grid;

    static void EnsureGrid()
    {
        if (s_Grid != null) return;
        s_Grid = new bool[GW, GH];
        foreach (var a in Areas)
            for (int x = a.Rect.xMin; x < a.Rect.xMax; x++)
                for (int z = a.Rect.yMin; z < a.Rect.yMax; z++)
                    s_Grid[x - GX0, z - GZ0] = true;
    }

    public static bool Cell(int x, int z)
    {
        EnsureGrid();
        int gx = x - GX0, gz = z - GZ0;
        if (gx < 0 || gz < 0 || gx >= GW || gz >= GH) return false;
        return s_Grid[gx, gz];
    }

    public static bool Walkable(Vector2 p) => Cell(Mathf.FloorToInt(p.x / Scale), Mathf.FloorToInt(p.y / Scale));

    public static bool CanStand(Vector2 p, float r) =>
        Walkable(p + new Vector2(r, r)) && Walkable(p + new Vector2(-r, r)) &&
        Walkable(p + new Vector2(r, -r)) && Walkable(p + new Vector2(-r, -r));

    /// <summary>壁にぶつかったら滑るように移動する。</summary>
    public static Vector2 Move(Vector2 p, Vector2 delta, float r)
    {
        var n = p + delta;
        if (CanStand(n, r)) return n;
        var nx = new Vector2(p.x + delta.x, p.y);
        if (CanStand(nx, r)) return nx;
        var nz = new Vector2(p.x, p.y + delta.y);
        if (CanStand(nz, r)) return nz;
        return p;
    }

    public static Vector3 ToWorld(Vector2 p, float y = 0f) => Origin + new Vector3(p.x, y, p.y);

    public static int NearestStation(Vector2 p, float maxDist)
    {
        int best = -1;
        float bd = maxDist;
        for (int i = 0; i < Stations.Count; i++)
        {
            float d = Vector2.Distance(p, Stations.All[i].Pos);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    public static string AreaName(Vector2 p)
    {
        p /= Scale;
        foreach (var a in Areas)
            if (p.x >= a.Rect.xMin && p.x < a.Rect.xMax && p.y >= a.Rect.yMin && p.y < a.Rect.yMax)
                return a.Name;
        return "";
    }

    // ------------------------------------------------------------------ 見た目

    public class View
    {
        public Transform Root;
        public Renderer[] Pads = new Renderer[Stations.Count];
        public GameObject[] Fires = new GameObject[Stations.Count];
        public Transform[] FireFlames = new Transform[Stations.Count * 3];
        public Renderer Reactor;
    }

    const float WallH = 1.4f, WallT = 0.3f;

    public static View Build(Transform parent)
    {
        var v = new View();
        var root = new GameObject("Interior").transform;
        root.SetParent(parent, false);
        root.position = Origin;
        v.Root = root;

        // 外の暗い背景（ロボットの外殻）
        Prims.Box(root, new Vector3(20f * Scale, -0.3f, 12f * Scale), new Vector3(70f * Scale, 0.2f, 70f * Scale), Prims.Hex("#0b0f12"));

        // 床
        foreach (var a in Areas)
        {
            var c = a.Rect.center * Scale;
            Prims.Box(root, new Vector3(c.x, -0.05f, c.y), new Vector3(a.Rect.width * Scale, 0.1f, a.Rect.height * Scale), a.Floor, false, a.Name);
        }
        // 床に2mごとの鋲（広さの目安）
        for (int z = GZ0; z < GZ0 + GH; z += 2)
            for (int x = GX0; x < GX0 + GW; x += 2)
                if (Cell(x, z)) Prims.Box(root, new Vector3((x + 0.5f) * Scale, 0.005f, (z + 0.5f) * Scale), new Vector3(0.12f, 0.01f, 0.12f), Prims.Hex("#1c2226"));

        BuildWalls(root);

        // 操作盤
        for (int i = 0; i < Stations.Count; i++)
        {
            var s = Stations.All[i];
            var pad = Prims.Shape(PrimitiveType.Cylinder, root, new Vector3(s.Pos.x, 0.02f, s.Pos.y), new Vector3(1.5f, 0.02f, 1.5f), s.Color * 0.5f, true, s.Name + " パッド");
            v.Pads[i] = pad.GetComponent<Renderer>();
            var cp = s.Pos + s.ConsoleOffset;
            var console = Prims.Box(root, new Vector3(cp.x, 0.45f, cp.y), new Vector3(1.6f, 0.9f, 1.0f), Prims.Hex("#1d2428"), false, s.Name + " 操作盤");
            Prims.Box(console.transform, new Vector3(0, 0.52f, 0), new Vector3(0.8f, 0.05f, 0.7f), s.Color, true);

            // 火災（ふだんは非表示）
            var fire = new GameObject(s.Name + " 火災").transform;
            fire.SetParent(root, false);
            fire.localPosition = new Vector3(s.Pos.x, 0f, s.Pos.y);
            for (int k = 0; k < 3; k++)
            {
                var f = Prims.Box(fire, new Vector3((k - 1) * 0.45f, 0.4f, (k % 2) * 0.3f - 0.15f), new Vector3(0.4f, 0.8f, 0.4f),
                    k == 1 ? Prims.Hex("#ffd23f") : Prims.Hex("#ff5a1f"), true);
                v.FireFlames[i * 3 + k] = f.transform;
            }
            fire.gameObject.SetActive(false);
            v.Fires[i] = fire.gameObject;
        }

        // 動力炉の本体
        var reactor = Prims.Shape(PrimitiveType.Cylinder, root, new Vector3(20f * Scale, 0.8f, 10.6f * Scale), new Vector3(2.4f, 0.8f, 2.4f), Prims.Hex("#ffd23f"), true, "動力炉 本体");
        v.Reactor = reactor.GetComponent<Renderer>();
        Prims.Shape(PrimitiveType.Cylinder, root, new Vector3(20f * Scale, 1.65f, 10.6f * Scale), new Vector3(2.9f, 0.08f, 2.9f), Prims.Hex("#1d2428"));

        // 操縦室の窓（外が見える）
        Prims.Box(root, new Vector3(20f * Scale, 1.0f, 33f * Scale - 0.15f), new Vector3(8f * Scale, 0.6f, 0.2f), Prims.Hex("#5fe8ff"), true, "窓");
        // 左右の腕室・脚室の外壁に機械っぽいライン
        Prims.Box(root, new Vector3(0.15f, 0.9f, 17f * Scale), new Vector3(0.2f, 0.3f, 7f * Scale), Prims.Hex("#ff9a3c"), true);
        Prims.Box(root, new Vector3(40f * Scale - 0.15f, 0.9f, 17f * Scale), new Vector3(0.2f, 0.3f, 7f * Scale), Prims.Hex("#ff9a3c"), true);
        Prims.Box(root, new Vector3(10.5f * Scale, 0.9f, -8f * Scale + 0.15f), new Vector3(6f * Scale, 0.3f, 0.2f), Prims.Hex("#4fd2ff"), true);
        Prims.Box(root, new Vector3(29.5f * Scale, 0.9f, -8f * Scale + 0.15f), new Vector3(6f * Scale, 0.3f, 0.2f), Prims.Hex("#4fd2ff"), true);

        return v;
    }

    /// <summary>歩ける場所と歩けない場所の境目に壁を立てる（同じ列の壁はまとめて1本にする）。</summary>
    static void BuildWalls(Transform root)
    {
        var wall = Prims.Hex("#56656e");
        var top = Prims.Hex("#8a9aa3");
        // 横向きの境目（z = 一定の線）
        for (int z = GZ0; z <= GZ0 + GH; z++)
        {
            int start = int.MinValue;
            for (int x = GX0; x <= GX0 + GW; x++)
            {
                bool edge = Cell(x, z - 1) != Cell(x, z);
                if (edge && start == int.MinValue) start = x;
                if (!edge && start != int.MinValue)
                {
                    MakeWall(root, new Vector3((start + x) * 0.5f * Scale, WallH * 0.5f, z * Scale), new Vector3((x - start) * Scale + WallT, WallH, WallT), wall, top);
                    start = int.MinValue;
                }
            }
        }
        // 縦向きの境目（x = 一定の線）
        for (int x = GX0; x <= GX0 + GW; x++)
        {
            int start = int.MinValue;
            for (int z = GZ0; z <= GZ0 + GH; z++)
            {
                bool edge = Cell(x - 1, z) != Cell(x, z);
                if (edge && start == int.MinValue) start = z;
                if (!edge && start != int.MinValue)
                {
                    MakeWall(root, new Vector3(x * Scale, WallH * 0.5f, (start + z) * 0.5f * Scale), new Vector3(WallT, WallH, (z - start) * Scale + WallT), wall, top);
                    start = int.MinValue;
                }
            }
        }
    }

    static void MakeWall(Transform root, Vector3 pos, Vector3 size, Color wall, Color top)
    {
        var w = Prims.Box(root, pos, size, wall);
        Prims.Box(w.transform, new Vector3(0, 0.5f, 0), new Vector3(1f, 0.04f / size.y, 1f), top);
    }
}
