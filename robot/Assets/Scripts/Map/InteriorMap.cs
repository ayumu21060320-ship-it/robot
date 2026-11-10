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

    // 床のイラストに掛ける色（イラストが白っぽいので明るめの色）
    const string Hall = "#b0bac4", Corridor = "#969fa8";

    public static readonly Area[] Areas =
    {
        new Area("頭部 操縦室", 14, 25, 12, 8, "#aac8c0"),
        new Area("首の通路",    18, 21,  4, 4, Corridor),
        new Area("胸部ホール",  12, 14, 16, 7, Hall),
        new Area("左肩の通路",   6, 16,  6, 3, Corridor),
        new Area("左腕室",       0, 12,  6, 10, "#d6be9f"),
        new Area("右肩の通路",  28, 16,  6, 3, Corridor),
        new Area("右腕室",      34, 12,  6, 10, "#d6be9f"),
        new Area("胸の下の通路", 18, 12, 4, 2, Corridor),
        new Area("動力炉室",    15,  6, 10, 6, "#d2c896"),
        new Area("腰へ降りる通路", 18, 5, 4, 1, Corridor),
        new Area("腰の通路",     6,  2, 28, 3, Corridor),
        new Area("左脚の通路",   9,  0,  3, 2, Corridor),
        new Area("左脚室",       6, -8,  9, 8, "#a3bfcf"),
        new Area("右脚の通路",  28,  0,  3, 2, Corridor),
        new Area("右脚室",      25, -8,  9, 8, "#a3bfcf"),
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

    // ------------------------------------------------------------------ 見た目（2Dイラスト）

    public class View
    {
        public Transform Root;
        public SpriteRenderer[] Pads = new SpriteRenderer[Stations.Count];
        public SpriteRenderer[] Fires = new SpriteRenderer[Stations.Count];
        public SpriteRenderer ReactorCore;
    }

    /// <summary>動力炉の本体の位置（メートル）</summary>
    public static readonly Vector2 ReactorPos = new Vector2(20f, 10.8f) * Scale;

    const float WallT = 0.4f;      // 壁の上面の太さ
    const float FaceH = 1.0f;      // 壁の手前の面の高さ（画面上の縦の長さ）

    public static View Build(Transform parent)
    {
        var v = new View();
        var root = new GameObject("Interior").transform;
        root.SetParent(parent, false);
        root.position = Origin;
        v.Root = root;

        // 床
        foreach (var a in Areas)
        {
            var c = a.Rect.center * Scale;
            Art.Tiled(root, "floor", new Vector3(c.x, 0f, c.y), new Vector2(a.Rect.width, a.Rect.height) * Scale, Art.OrderFloor, a.Floor, a.Name);
        }

        BuildWalls(root);

        // 操作盤と立ち位置
        for (int i = 0; i < Stations.Count; i++)
        {
            var s = Stations.All[i];
            v.Pads[i] = Art.Place(root, "pad", new Vector3(s.Pos.x, 0.01f, s.Pos.y), Art.OrderPad, s.Color, s.Name + " 立ち位置");
            if (s.Type != StationType.Core)
            {
                string sprite = s.Type == StationType.Leg ? "console_leg" : s.Type == StationType.Arm ? "console_arm" : "console_head";
                var cp = s.Pos + s.ConsoleOffset;
                Art.Place(root, sprite, new Vector3(cp.x, 0.02f, cp.y), Art.DepthOrder(cp.y - 0.8f), null, s.Name + " 操作盤");
            }
            var fire = Art.Place(root, "fire_0", new Vector3(s.Pos.x, 0.03f, s.Pos.y + 0.6f), Art.DepthOrder(s.Pos.y - 0.2f), null, s.Name + " 火災");
            fire.transform.localScale = Vector3.one * 1.2f;
            fire.gameObject.SetActive(false);
            v.Fires[i] = fire;
        }

        // 動力炉の本体（光る芯は明るさを変えるので別の層）
        int ro = Art.DepthOrder(ReactorPos.y - 1.2f);
        Art.Place(root, "reactor_base", new Vector3(ReactorPos.x, 0.02f, ReactorPos.y), ro, null, "動力炉 本体");
        v.ReactorCore = Art.Place(root, "reactor_core", new Vector3(ReactorPos.x, 0.02f, ReactorPos.y), ro + 1, Prims.Hex("#ffd23f"), "動力炉 芯");

        return v;
    }

    /// <summary>
    /// 歩ける場所と歩けない場所の境目に壁を立てる（同じ列の壁はまとめて1本にする）。
    /// 部屋の北側の壁には手前の面（パネル）も描いて、奥行きがあるように見せる。
    /// </summary>
    static void BuildWalls(Transform root)
    {
        var cap = Prims.Hex("#ffffff");
        // 横向きの境目（z = 一定の線）。種類 1 = 南側が部屋（北の壁）、2 = 北側が部屋（南の壁）
        for (int z = GZ0; z <= GZ0 + GH; z++)
        {
            int start = int.MinValue, kind = 0;
            for (int x = GX0; x <= GX0 + GW + 1; x++)
            {
                bool below = Cell(x, z - 1), above = Cell(x, z);
                int k = below && !above ? 1 : above && !below ? 2 : 0;
                if (k != kind)
                {
                    if (kind != 0) HWall(root, start, x, z, kind == 1, cap);
                    start = x;
                    kind = k;
                }
            }
        }
        // 縦向きの境目（x = 一定の線）
        for (int x = GX0; x <= GX0 + GW; x++)
        {
            int start = int.MinValue;
            for (int z = GZ0; z <= GZ0 + GH + 1; z++)
            {
                bool edge = Cell(x - 1, z) != Cell(x, z);
                if (edge && start == int.MinValue) start = z;
                if (!edge && start != int.MinValue)
                {
                    float len = (z - start) * Scale + WallT;
                    Art.Sliced(root, "wall_cap", new Vector3(x * Scale, 0.05f, (start + z) * 0.5f * Scale + WallT * 0.5f), new Vector2(WallT, len), Art.OrderWallCap, cap, "壁");
                    start = int.MinValue;
                }
            }
        }
    }

    static void HWall(Transform root, int x0, int x1, int z, bool northWall, Color cap)
    {
        float len = (x1 - x0) * Scale;
        float cx = (x0 + x1) * 0.5f * Scale;
        float lineZ = z * Scale;
        if (northWall)
            Art.Tiled(root, "wall_face", new Vector3(cx, 0.04f, lineZ - FaceH * 0.5f), new Vector2(len, FaceH), Art.OrderWallFace, null, "壁の面");
        Art.Sliced(root, "wall_cap", new Vector3(cx, 0.05f, lineZ + WallT * 0.5f), new Vector2(len + WallT, WallT), Art.OrderWallCap, cap, "壁");
    }
}
