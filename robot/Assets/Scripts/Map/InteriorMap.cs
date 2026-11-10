using UnityEngine;

/// <summary>
/// 巨大ロボットの内部マップ。人型ロボットの断面図のように、頭・肩・腕・胸・腰・脚の形に沿って
/// 部屋と通路（長方形）を並べ、そこから「歩ける場所」の判定と見た目を作る。
/// まわりにはロボットの装甲の外形を描く。
/// 内部は外の世界から遠く離れた場所（Origin）に置き、真上からのカメラで映す。
///
///                 [頭部 操縦室]
///                      |
///   [左肩]======[  胸部 動力炉室  ]======[右肩]
///     ||               |                 ||
///   左上腕           腹の通路           右上腕
///     ||          [  腰のホール  ]        ||
///   [左腕室]        ||        ||        [右腕室]
///                左もも      右もも
///               [左脚室]    [右脚室]
///               [ 左足 ]    [ 右足 ]
/// </summary>
public static class InteriorMap
{
    public static readonly Vector3 Origin = new Vector3(2000f, 0f, 0f);

    /// <summary>
    /// 内部の広さの倍率。部屋の配置（Areas）は「設計マス」で書き、実際の大きさ（メートル）はこれを掛けたもの。
    /// 巨大ロボットらしく広くしたいときはこの数字を上げる。
    /// </summary>
    public const float Scale = 1.3f;

    /// <summary>装甲が部屋からはみ出す幅（設計マス）</summary>
    const float ArmorMargin = 1.6f;

    public static readonly Vector2 Spawn = new Vector2(15f, 38f) * Scale;

    /// <summary>部屋や通路。座標は設計マス（実際の大きさは Scale 倍）。</summary>
    public struct Area
    {
        public string Name;
        public RectInt Rect; // x, y(=z), 幅, 高さ
        public Color Floor;  // 床のイラストに掛ける色
        public Color Armor;  // まわりの装甲の色

        public Area(string name, int x, int z, int w, int h, string floorHex, string armorHex)
        {
            Name = name;
            Rect = new RectInt(x, z, w, h);
            Floor = Prims.Hex(floorHex);
            Armor = Prims.Hex(armorHex);
        }
    }

    // 床の色（イラストが白っぽいので明るめ）
    const string Hall = "#b0bac4", Shaft = "#969fa8";
    // 装甲の色（スーパーロボットらしい白・赤・青・黄）
    const string White = "#d9dee2", Red = "#c8402f", Blue = "#3c6fb4", Yellow = "#f2b630", Dark = "#3a4a5c";

    public static readonly Area[] Areas =
    {
        new Area("頭部 操縦室",   15, 50, 10,  8, "#aac8c0", White),
        new Area("首の通路",      18, 46,  4,  4, Shaft,     Dark),
        new Area("胸部 動力炉室", 11, 34, 18, 12, "#d2c896", Red),
        new Area("左肩の通路",     4, 41,  7,  4, Shaft,     Yellow),
        new Area("右肩の通路",    29, 41,  7,  4, Shaft,     Yellow),
        new Area("左上腕の縦穴",   5, 27,  4, 14, Shaft,     White),
        new Area("右上腕の縦穴",  31, 27,  4, 14, Shaft,     White),
        new Area("左腕室",         2, 15,  9, 12, "#d6be9f", White),
        new Area("右腕室",        29, 15,  9, 12, "#d6be9f", White),
        new Area("腹の通路",      17, 28,  6,  6, Shaft,     Dark),
        new Area("腰のホール",    12, 22, 16,  6, Hall,      Dark),
        new Area("左ももの縦穴",  13,  8,  5, 14, Shaft,     Blue),
        new Area("右ももの縦穴",  22,  8,  5, 14, Shaft,     Blue),
        new Area("左脚室",        11, -4,  8, 12, "#a3bfcf", Blue),
        new Area("右脚室",        21, -4,  8, 12, "#a3bfcf", Blue),
        new Area("左足",           9, -8, 10,  4, Hall,      Red),
        new Area("右足",          21, -8, 10,  4, Hall,      Red),
    };

    /// <summary>歩けない、装甲だけの部分（すき間を埋めて体の形をつなげる）。設計マス。</summary>
    public static readonly (RectInt rect, Color color)[] ArmorOnly =
    {
        (new RectInt(10, 27, 20, 8), Prims.Hex(Dark)),   // 腹まわり
    };

    // 歩ける場所の表（設計マス単位）。範囲は Areas から自動で決める
    static int GX0, GZ0, GW, GH;
    static bool[,] s_Grid;

    static void EnsureGrid()
    {
        if (s_Grid != null) return;
        int x0 = int.MaxValue, z0 = int.MaxValue, x1 = int.MinValue, z1 = int.MinValue;
        foreach (var a in Areas)
        {
            x0 = Mathf.Min(x0, a.Rect.xMin); z0 = Mathf.Min(z0, a.Rect.yMin);
            x1 = Mathf.Max(x1, a.Rect.xMax); z1 = Mathf.Max(z1, a.Rect.yMax);
        }
        GX0 = x0 - 1; GZ0 = z0 - 1; GW = x1 - x0 + 3; GH = z1 - z0 + 3;
        s_Grid = new bool[GW, GH];
        foreach (var a in Areas)
            for (int x = a.Rect.xMin; x < a.Rect.xMax; x++)
                for (int z = a.Rect.yMin; z < a.Rect.yMax; z++)
                    s_Grid[x - GX0, z - GZ0] = true;
    }

    /// <summary>装甲も含めたロボット全体の範囲（メートル）。ミニマップと重なり順に使う。</summary>
    public static Rect Bounds
    {
        get
        {
            EnsureGrid();
            float m = ArmorMargin + 0.5f;
            return Rect.MinMaxRect((GX0 + 1 - m) * Scale, (GZ0 + 1 - m) * Scale, (GX0 + GW - 1 + m) * Scale, (GZ0 + GH - 1 + m) * Scale);
        }
    }

    public static Vector2 Center => Bounds.center;

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

    /// <summary>装甲の長方形（メートル）。部屋をひとまわり大きくしたもの。ミニマップでも使う。</summary>
    public static Rect ArmorRect(Area a, float extra = 0f)
    {
        float m = ArmorMargin + extra;
        return Rect.MinMaxRect((a.Rect.xMin - m) * Scale, (a.Rect.yMin - m) * Scale, (a.Rect.xMax + m) * Scale, (a.Rect.yMax + m) * Scale);
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
    public static readonly Vector2 ReactorPos = new Vector2(20f, 41.5f) * Scale;

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

        BuildArmor(root);
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
    /// 部屋のまわりに装甲（ロボットの外形）を描き、関節の歯車や油圧ピストン、目、角を足す。
    /// どれも床より奥に描くので、部屋に重なったところは隠れる。
    /// </summary>
    static void BuildArmor(Transform root)
    {
        var outline = Prims.Hex("#14181d");
        for (int i = 0; i < Areas.Length; i++)
        {
            var o = ArmorRect(Areas[i], 0.3f);
            Art.Box(root, o, -0.30f, -200 + i, outline, "装甲のふち");
        }
        foreach (var (rect, color) in ArmorOnly)
        {
            var r = Rect.MinMaxRect(rect.xMin * Scale, rect.yMin * Scale, rect.xMax * Scale, rect.yMax * Scale);
            Art.Box(root, r, -0.27f, -170, color, "装甲");
        }
        for (int i = 0; i < Areas.Length; i++)
        {
            var a = Areas[i];
            var r = ArmorRect(a);
            Art.Box(root, r, -0.25f, -150 + i, a.Armor, a.Name + " 装甲");
            // 装甲の継ぎ目（少し暗い帯）
            var band = new Rect(r.xMin, r.yMin, r.width, 0.35f * Scale);
            Art.Box(root, band, -0.24f, -100 + i, a.Armor * 0.78f + new Color(0, 0, 0, 0.22f), "継ぎ目");
        }

        // 関節の歯車（設計マスでの中心と直径）
        (float x, float z, float d)[] gears =
        {
            (3f, 43f, 4f), (37f, 43f, 4f),          // 肩
            (3.5f, 28f, 3f), (36.5f, 28f, 3f),      // ひじ
            (11.2f, 23f, 3.5f), (28.8f, 23f, 3.5f), // 股関節
            (10.5f, 8.5f, 3f), (29.5f, 8.5f, 3f),   // ひざ
        };
        foreach (var g in gears)
        {
            var gr = Art.Place(root, "gear", new Vector3(g.x * Scale, -0.2f, g.z * Scale), -20, null, "歯車");
            gr.transform.localScale = Vector3.one * (g.d * Scale / 3f);
        }

        // 油圧ピストン（設計マスでの中心と長さ）
        (float x, float z, float len)[] pistons =
        {
            (11.4f, 15f, 10f), (28.6f, 15f, 10f),   // もも
            (10.2f, 32f, 8f), (29.8f, 32f, 8f),     // 上腕の内側
        };
        foreach (var pz in pistons)
        {
            var pr = Art.Place(root, "piston", new Vector3(pz.x * Scale, -0.19f, pz.z * Scale), -19, null, "ピストン");
            pr.drawMode = SpriteDrawMode.Sliced;
            pr.size = new Vector2(1f, pz.len * Scale);
        }

        // 目と角（頭）
        var eye = Prims.Hex("#7ff4ff");
        Art.Box(root, Rect.MinMaxRect(16.5f * Scale, 58.3f * Scale, 19.2f * Scale, 59.0f * Scale), -0.18f, -18, eye, "目");
        Art.Box(root, Rect.MinMaxRect(20.8f * Scale, 58.3f * Scale, 23.5f * Scale, 59.0f * Scale), -0.18f, -18, eye, "目");
        foreach (var side in new[] { -1f, 1f })
        {
            var horn = Art.Box(root, Rect.MinMaxRect(-0.45f * Scale, -2.6f * Scale, 0.45f * Scale, 2.6f * Scale), -0.26f, -160, Prims.Hex(Yellow), "角");
            horn.transform.localPosition = new Vector3((20f + side * 3.2f) * Scale, -0.26f, 61.2f * Scale);
            horn.transform.localRotation = Quaternion.Euler(90f, side * 35f, 0f);
        }
    }

    /// <summary>
    /// 歩ける場所と歩けない場所の境目に壁を立てる（同じ列の壁はまとめて1本にする）。
    /// 部屋の北側の壁には手前の面（パネル）も描いて、奥行きがあるように見せる。
    /// </summary>
    static void BuildWalls(Transform root)
    {
        EnsureGrid();
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
