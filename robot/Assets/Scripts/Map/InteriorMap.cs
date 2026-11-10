using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 巨大ロボットの内部マップ。人型ロボットを<b>横から見た断面図</b>として作る。
///
/// - 体の中の空洞（Areas）は、頭・肩・腕・胸・腰・脚の形に並んでいる
/// - 操縦士は<b>足場（Catwalks）</b>の上を左右に歩き、<b>はしご（Ladders）</b>で上下に移動する。重力がある
/// - 見た目は3つの層に分かれている
///     奥　　：骨組み・小さな歯車・油圧シリンダー・フレームに沿ったケーブル（もやを掛けて薄くする）
///     中　　：足場・はしご・操作盤・整備装置・操縦士（はっきりした色）
///     手前　：垂れ下がったケーブル（濃い色。操縦士より手前）
///
/// 内部は外の世界から遠く離れた場所（Origin）に置き、正面から見るカメラで映す
/// （スプライトは寝かせて置き、カメラは真上から見下ろしているので、画面の上 = z の正の向き）。
/// </summary>
public static class InteriorMap
{
    public static readonly Vector3 Origin = new Vector3(2000f, 0f, 0f);

    /// <summary>
    /// 内部の広さの倍率。空洞・足場・はしごは「設計マス」で書き、実際の大きさ（メートル）はこれを掛けたもの。
    /// </summary>
    public const float Scale = 1.3f;

    /// <summary>装甲が空洞からはみ出す幅（設計マス）</summary>
    const float ArmorMargin = 1.6f;

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
        new Area("首",            18, 46,  4,  4, Shaft,     Dark),
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

    // ------------------------------------------------------------------ 足場とはしご

    /// <summary>足場。x0～x1 の範囲に、高さ y の床がある（メートル）。</summary>
    public struct Catwalk { public float X0, X1, Y; }
    /// <summary>はしご。x の位置に、y0（下の床）から y1（上の床）まで（メートル）。</summary>
    public struct Ladder { public float X, Y0, Y1; }

    static Catwalk C(float x0, float x1, float y) => new Catwalk { X0 = x0 * Scale, X1 = x1 * Scale, Y = y * Scale };
    static Ladder L(float x, float y0, float y1) => new Ladder { X = x * Scale, Y0 = y0 * Scale, Y1 = y1 * Scale };

    public static readonly Catwalk[] Catwalks =
    {
        C(15f, 25f, 50.4f),   // 頭
        C(4f, 36f, 40.6f),    // 肩から胸の上の段
        C(11f, 29f, 34.4f),   // 胸の下の段（動力炉）
        C(2f, 11f, 15.4f),    // 左腕室
        C(29f, 38f, 15.4f),   // 右腕室
        C(12f, 28f, 22.4f),   // 腰
        C(13f, 18f, 14.5f),   // 左もも（途中の足場）
        C(22f, 27f, 14.5f),   // 右もも
        C(11f, 19f, -3.6f),   // 左脚室
        C(21f, 29f, -3.6f),   // 右脚室
        C(9f, 19f, -7.6f),    // 左足
        C(21f, 31f, -7.6f),   // 右足
    };

    public static readonly Ladder[] Ladders =
    {
        L(20f, 22.4f, 50.4f),   // 腰 → 胸 → 首 → 頭（体の真ん中を通る）
        L(7f, 15.4f, 40.6f),    // 左肩 → 左腕室
        L(33f, 15.4f, 40.6f),   // 右肩 → 右腕室
        L(15.5f, -3.6f, 22.4f), // 腰 → 左もも → 左脚室
        L(24.5f, -3.6f, 22.4f), // 腰 → 右もも → 右脚室
        L(17.5f, -7.6f, -3.6f), // 左脚室 → 左足
        L(22.5f, -7.6f, -3.6f), // 右脚室 → 右足
    };

    public static readonly Vector2 Spawn = new Vector2(24f, 34.4f) * Scale;

    /// <summary>動力炉の本体の位置（足元、メートル）</summary>
    public static readonly Vector2 ReactorPos = new Vector2(17f, 34.4f) * Scale;

    // ------------------------------------------------------------------ 空洞の表

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

    /// <summary>装甲も含めたロボット全体の範囲（メートル）。ミニマップに使う。</summary>
    public static Rect Bounds
    {
        get
        {
            EnsureGrid();
            float m = ArmorMargin + 0.5f;
            // 手やブーツは空洞より外にはみ出すので、下は多めにとる
            return Rect.MinMaxRect((GX0 + 1 - m) * Scale, (GZ0 + 1 - m - 3f) * Scale, (GX0 + GW - 1 + m) * Scale, (GZ0 + GH - 1 + m) * Scale);
        }
    }

    public static Vector2 Center => Bounds.center;

    /// <summary>設計マス (x, z) が体の中の空洞か</summary>
    public static bool Cell(int x, int z)
    {
        EnsureGrid();
        int gx = x - GX0, gz = z - GZ0;
        if (gx < 0 || gz < 0 || gx >= GW || gz >= GH) return false;
        return s_Grid[gx, gz];
    }

    /// <summary>足元が y の人が、横位置 x に立てるか（体の高さのところが空洞か）</summary>
    public static bool InCavity(float x, float y) => Cell(Mathf.FloorToInt(x / Scale), Mathf.FloorToInt((y + 0.6f) / Scale));

    /// <summary>足元 (x, y) のすぐ下に足場があるか</summary>
    public static bool OnFloor(float x, float y, float tolerance, out float floorY)
    {
        foreach (var c in Catwalks)
            if (x >= c.X0 && x <= c.X1 && Mathf.Abs(y - c.Y) <= tolerance) { floorY = c.Y; return true; }
        floorY = 0f;
        return false;
    }

    /// <summary>yFrom から yTo まで落ちるあいだに着地する足場（いちばん高いもの）</summary>
    public static bool FloorBetween(float x, float yFrom, float yTo, out float floorY)
    {
        floorY = float.NegativeInfinity;
        foreach (var c in Catwalks)
            if (x >= c.X0 && x <= c.X1 && c.Y <= yFrom + 0.001f && c.Y >= yTo && c.Y > floorY) floorY = c.Y;
        return !float.IsNegativeInfinity(floorY);
    }

    /// <summary>近くにある、上（up=true）または下に進めるはしご。なければ -1</summary>
    public static int LadderNear(float x, float y, bool up)
    {
        for (int i = 0; i < Ladders.Length; i++)
        {
            var l = Ladders[i];
            if (Mathf.Abs(l.X - x) > 0.7f) continue;
            if (up && y >= l.Y0 - 0.05f && y < l.Y1 - 0.05f) return i;
            if (!up && y > l.Y0 + 0.05f && y <= l.Y1 + 0.05f) return i;
        }
        return -1;
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
        p.y += 0.6f;
        p /= Scale;
        foreach (var a in Areas)
            if (p.x >= a.Rect.xMin && p.x < a.Rect.xMax && p.y >= a.Rect.yMin && p.y < a.Rect.yMax)
                return a.Name;
        return "";
    }

    /// <summary>装甲の長方形（メートル）。空洞をひとまわり大きくしたもの。ミニマップでも使う。</summary>
    public static Rect ArmorRect(Area a, float extra = 0f)
    {
        float m = ArmorMargin + extra;
        return Rect.MinMaxRect((a.Rect.xMin - m) * Scale, (a.Rect.yMin - m) * Scale, (a.Rect.xMax + m) * Scale, (a.Rect.yMax + m) * Scale);
    }

    public static Rect CavityRect(Area a) =>
        Rect.MinMaxRect(a.Rect.xMin * Scale, a.Rect.yMin * Scale, a.Rect.xMax * Scale, a.Rect.yMax * Scale);

    // ------------------------------------------------------------------ 見た目

    public class View
    {
        public Transform Root;
        public SpriteRenderer[] Pads = new SpriteRenderer[Stations.Count];
        public SpriteRenderer[] Fires = new SpriteRenderer[Stations.Count];
        public SpriteRenderer ReactorCore;
        public SpriteRenderer[] TaskAlerts = new SpriteRenderer[Tasks.Count];
    }

    // 重なり順（小さいほど奥）
    const int OrderCavity = -95, OrderGirder = -88, OrderBrace = -87, OrderGear = -82, OrderActuator = -80, OrderBackCable = -76,
              OrderHaze = -60, OrderEdge = -55, OrderLadder = 8, OrderCatwalk = 10, OrderProp = 40, OrderFrontCable = 5000;
    public const int OrderPilot = 200;

    /// <summary>足場の絵の床の上面が、絵の下端から何ピクセルか（generate_art.py の catwalk と合わせる）</summary>
    const float CatwalkFloorPx = 18f;

    public static View Build(Transform parent)
    {
        var v = new View();
        var root = new GameObject("Interior").transform;
        root.SetParent(parent, false);
        root.position = Origin;
        v.Root = root;

        BuildArmor(root);
        BuildBackLayer(root);
        BuildCrewLayer(root, v);
        BuildFrontLayer(root);
        return v;
    }

    // ---------------- 奥の層：骨組み・歯車・シリンダー・ケーブル（最後にもやを掛けて薄くする）

    static void BuildBackLayer(Transform root)
    {
        var rnd = new System.Random(9);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        var steel = Prims.Hex("#58626c");
        var brace = Prims.Hex("#3e464e");

        foreach (var a in Areas)
        {
            var r = CavityRect(a);
            Art.Box(root, r, -0.1f, OrderCavity, Prims.Hex("#1e2329"), a.Name + " 奥");

            // 骨組み：縦の柱と横のはり、X 字の筋かい
            float colStep = 3f * Scale, rowStep = 4f * Scale;
            for (float x = r.xMin + 0.6f * Scale; x < r.xMax; x += colStep)
                Art.Box(root, Rect.MinMaxRect(x - 0.28f, r.yMin, x + 0.28f, r.yMax), -0.09f, OrderGirder, steel, "柱");
            for (float y = r.yMin + 1.5f * Scale; y < r.yMax; y += rowStep)
                Art.Box(root, Rect.MinMaxRect(r.xMin, y - 0.22f, r.xMax, y + 0.22f), -0.09f, OrderGirder, steel, "はり");
            for (float x = r.xMin + 0.6f * Scale; x + colStep <= r.xMax + 0.5f; x += colStep)
                for (float y = r.yMin + 1.5f * Scale; y + rowStep <= r.yMax + 0.5f; y += rowStep)
                    foreach (var sign in new[] { 1f, -1f })
                    {
                        float len = Mathf.Sqrt(colStep * colStep + rowStep * rowStep);
                        var b = Art.Box(root, new Rect(-0.12f, -len * 0.5f, 0.24f, len), -0.095f, OrderBrace, brace, "筋かい");
                        b.transform.localPosition = new Vector3(x + colStep * 0.5f, -0.095f, y + rowStep * 0.5f);
                        b.transform.localRotation = Quaternion.Euler(90f, sign * Mathf.Atan2(colStep, rowStep) * Mathf.Rad2Deg, 0f);
                    }

            // 小さな歯車をたくさん散らす
            int gears = Mathf.Max(2, Mathf.RoundToInt(a.Rect.width * a.Rect.height / 5f));
            for (int i = 0; i < gears; i++)
            {
                var g = Art.Place(root, "gear", new Vector3(R(r.xMin + 0.5f, r.xMax - 0.5f), -0.08f, R(r.yMin + 0.5f, r.yMax - 0.5f)), OrderGear, null, "歯車");
                g.transform.localScale = Vector3.one * R(0.18f, 0.5f) * Scale;
                g.transform.localRotation = Quaternion.Euler(90f, R(0f, 30f), 0f);
            }
        }

        // 手足の油圧シリンダー（設計マス：x, 下, 上）
        (float x, float y0, float y1)[] actuators =
        {
            (14f, 9f, 21f), (17.4f, 9f, 21f), (22.6f, 9f, 21f), (26f, 9f, 21f),
            (5.6f, 28f, 40f), (8.4f, 28f, 40f), (31.6f, 28f, 40f), (34.4f, 28f, 40f),
            (12.2f, -3f, 7f), (17.8f, -3f, 7f), (22.2f, -3f, 7f), (27.8f, -3f, 7f),
        };
        foreach (var (x, y0, y1) in actuators)
        {
            var pr = Art.Place(root, "piston", new Vector3(x * Scale, -0.07f, (y0 + y1) * 0.5f * Scale), OrderActuator, null, "シリンダー");
            pr.drawMode = SpriteDrawMode.Sliced;
            pr.size = new Vector2(0.9f, (y1 - y0) * Scale);
        }

        // フレームに沿って走るケーブルの束（設計マス：x, 下, 上。縦向き）
        (float x, float y0, float y1)[] runs =
        {
            (11.5f, 34f, 46f), (28.5f, 34f, 46f), (18.4f, 28f, 34f), (21.6f, 28f, 34f),
            (5.3f, 27f, 41f), (8.7f, 27f, 41f), (31.3f, 27f, 41f), (34.7f, 27f, 41f),
            (13.3f, 8f, 22f), (17.7f, 8f, 22f), (22.3f, 8f, 22f), (26.7f, 8f, 22f),
            (2.4f, 15f, 27f), (37.6f, 15f, 27f), (11.4f, -4f, 8f), (28.6f, -4f, 8f), (15.4f, 50f, 58f), (24.6f, 50f, 58f),
        };
        foreach (var (x, y0, y1) in runs)
            Art.Tiled(root, "cable_run", new Vector3(x * Scale, -0.06f, (y0 + y1) * 0.5f * Scale), new Vector2(0.75f, (y1 - y0) * Scale), OrderBackCable, null, "ケーブル");
        // 横向き（はりに沿う）
        (float x0, float x1, float y)[] hruns = { (11f, 29f, 45.4f), (4f, 11f, 44.6f), (29f, 36f, 44.6f), (12f, 28f, 27.5f), (2f, 11f, 26.5f), (29f, 38f, 26.5f), (15f, 25f, 57.5f) };
        foreach (var (x0, x1, y) in hruns)
        {
            var c = Art.Tiled(root, "cable_run", new Vector3((x0 + x1) * 0.5f * Scale, -0.06f, y * Scale), new Vector2(0.75f, (x1 - x0) * Scale), OrderBackCable, null, "ケーブル");
            c.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);
        }
        // はりから垂れたケーブル
        foreach (var a in Areas)
        {
            var r = CavityRect(a);
            int n = Mathf.Max(1, Mathf.RoundToInt(a.Rect.width * a.Rect.height / 25f));
            for (int i = 0; i < n; i++)
            {
                float w = Mathf.Min(4f, r.width - 0.4f);
                var c = Art.Place(root, "cable_back_" + rnd.Next(3), new Vector3(R(r.xMin + w * 0.5f, r.xMax - w * 0.5f), -0.055f, R(r.yMin + 2f, r.yMax - 0.9f)), OrderBackCable + 1, null, "垂れたケーブル");
                c.transform.localScale = new Vector3(w / 4f, 1f, 1f);
            }
        }

        // もや：奥の層全体を少し薄くして、操縦士の層を見やすくする
        var haze = new Color(0.2f, 0.24f, 0.28f, 0.45f);
        foreach (var a in Areas) Art.Box(root, CavityRect(a), -0.04f, OrderHaze, haze, "もや");
        // 空洞のふち（装甲の断面）
        var edge = Prims.Hex("#78828c");
        foreach (var a in Areas)
        {
            var r = CavityRect(a);
            const float t = 0.18f;
            Art.Box(root, Rect.MinMaxRect(r.xMin - t, r.yMin - t, r.xMax + t, r.yMin), -0.03f, OrderEdge, edge);
            Art.Box(root, Rect.MinMaxRect(r.xMin - t, r.yMax, r.xMax + t, r.yMax + t), -0.03f, OrderEdge, edge);
            Art.Box(root, Rect.MinMaxRect(r.xMin - t, r.yMin, r.xMin, r.yMax), -0.03f, OrderEdge, edge);
            Art.Box(root, Rect.MinMaxRect(r.xMax, r.yMin, r.xMax + t, r.yMax), -0.03f, OrderEdge, edge);
        }
    }

    // ---------------- 中の層：足場・はしご・操作盤・整備装置

    static void BuildCrewLayer(Transform root, View v)
    {
        float lift = (96f * 0.5f - CatwalkFloorPx) / Art.PixelsPerUnit; // 絵の中心から床の上面まで
        foreach (var c in Catwalks)
            Art.Tiled(root, "catwalk", new Vector3((c.X0 + c.X1) * 0.5f, 0.01f, c.Y + lift), new Vector2(c.X1 - c.X0, 1.5f), OrderCatwalk, null, "足場");
        foreach (var l in Ladders)
            Art.Tiled(root, "ladder", new Vector3(l.X, 0.005f, (l.Y0 + l.Y1) * 0.5f + 0.55f), new Vector2(1f, l.Y1 - l.Y0 + 1.1f), OrderLadder, null, "はしご");

        // 操作盤（立ち位置の横）と、立ち位置を示す床の光
        for (int i = 0; i < Stations.Count; i++)
        {
            var s = Stations.All[i];
            v.Pads[i] = Art.Box(root, new Rect(s.Pos.x - 0.7f, s.Pos.y + 0.02f, 1.4f, 0.14f), 0.02f, OrderCatwalk + 1, s.Color, s.Name + " 立ち位置");
            if (s.Type != StationType.Core)
            {
                string sprite = s.Type == StationType.Leg ? "console_leg" : s.Type == StationType.Arm ? "console_arm" : "console_head";
                var cp = s.Pos + s.ConsoleOffset;
                var con = Art.Place(root, sprite, new Vector3(cp.x, 0.02f, cp.y), OrderProp, null, s.Name + " 操作盤");
                con.transform.localScale = Vector3.one * 0.8f;
            }
            var fire = Art.Place(root, "fire_0", new Vector3(s.Pos.x, 0.03f, s.Pos.y + 0.75f), OrderPilot + 10, null, s.Name + " 火災");
            fire.gameObject.SetActive(false);
            v.Fires[i] = fire;
        }

        // 動力炉（足元が ReactorPos）
        float rh = 192f / Art.PixelsPerUnit * 0.5f;
        Art.Place(root, "reactor_base", new Vector3(ReactorPos.x, 0.02f, ReactorPos.y + rh), OrderProp, null, "動力炉 本体");
        v.ReactorCore = Art.Place(root, "reactor_core", new Vector3(ReactorPos.x, 0.02f, ReactorPos.y + rh), OrderProp + 1, Prims.Hex("#ffd23f"), "動力炉 芯");

        // 足場に置いた荷物や機械（設計マス：x, 足場の高さ, 大きさ）
        (string sprite, float x, float y, float scale)[] props =
        {
            ("tanks", 13f, 34.4f, 0.8f), ("crate", 27.6f, 34.4f, 0.8f), ("crate", 26.8f, 34.4f, 0.8f),
            ("machine", 27f, 40.6f, 0.6f), ("tanks", 14.5f, 40.6f, 0.6f), ("monitors", 17f, 50.4f, 0.7f),
            ("machine", 22f, 22.4f, 0.55f), ("crate", 12.6f, 22.4f, 0.8f),
            ("tanks", 9.5f, 15.4f, 0.7f), ("tanks", 30.5f, 15.4f, 0.7f), ("crate", 37.2f, 15.4f, 0.8f), ("crate", 2.8f, 15.4f, 0.8f),
            ("crate", 13f, -7.6f, 0.8f), ("crate", 14f, -7.6f, 0.8f), ("crate", 28f, -7.6f, 0.8f), ("tanks", 30f, -7.6f, 0.7f),
        };
        foreach (var (sprite, x, y, scale) in props)
        {
            var sp = Art.Get(sprite);
            float half = sp.rect.height / Art.PixelsPerUnit * 0.5f * scale;
            var r = Art.Place(root, sprite, new Vector3(x * Scale, 0.015f, y * Scale + half), OrderProp - 5, null, sprite);
            r.transform.localScale = Vector3.one * scale;
        }

        // 整備装置（足場の上、立ち位置の後ろの壁ぎわ）
        for (int i = 0; i < Tasks.Count; i++)
        {
            var t = Tasks.All[i];
            var p = t.Pos + t.PanelOffset;
            var panel = Art.Place(root, Tasks.SpriteFor(t.Kind), new Vector3(p.x, 0.02f, p.y), OrderProp - 2, null, t.Name);
            panel.transform.localScale = Vector3.one * 0.85f;
            var alert = Art.Place(root, "alert", new Vector3(p.x, 0.03f, p.y + 1.2f), OrderFrontCable + 100, null, t.Name + " 故障");
            alert.gameObject.SetActive(false);
            v.TaskAlerts[i] = alert;
        }
    }

    // ---------------- 手前の層：濃い色の垂れたケーブル（操縦士より手前）

    static void BuildFrontLayer(Transform root)
    {
        var rnd = new System.Random(21);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
        foreach (var a in Areas)
        {
            var r = CavityRect(a);
            int n = Mathf.Max(1, Mathf.RoundToInt(a.Rect.width * a.Rect.height / 45f));
            for (int i = 0; i < n; i++)
            {
                float w = Mathf.Min(R(2.5f, 4.5f), r.width - 0.4f);
                var c = Art.Place(root, "cable_front_" + rnd.Next(3), new Vector3(R(r.xMin + w * 0.5f, r.xMax - w * 0.5f), 0.05f, R(r.yMin + 2.2f, r.yMax - 0.9f)), OrderFrontCable, null, "手前のケーブル");
                c.transform.localScale = new Vector3(w / 4f, R(0.8f, 1.1f), 1f);
            }
            int drops = Mathf.Max(1, a.Rect.width / 4);
            for (int i = 0; i < drops; i++)
            {
                var c = Art.Place(root, "cable_drop_front", new Vector3(R(r.xMin + 0.3f, r.xMax - 0.3f), 0.05f, r.yMax - 1.25f * R(0.6f, 1f)), OrderFrontCable + 1, null, "垂れた線");
                c.transform.localScale = Vector3.one * R(0.7f, 1f);
            }
        }
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
            // 装甲の立体感：上のふちは明るく、下のふちは暗く、縦の継ぎ目
            var dark = new Color(a.Armor.r * 0.72f, a.Armor.g * 0.72f, a.Armor.b * 0.72f, 1f);
            var light = Color.Lerp(a.Armor, Color.white, 0.35f);
            Art.Box(root, new Rect(r.xMin, r.yMin, r.width, 0.35f * Scale), -0.24f, -100 + i, dark, "下のふち");
            Art.Box(root, new Rect(r.xMin, r.yMax - 0.25f * Scale, r.width, 0.25f * Scale), -0.24f, -100 + i, light, "上のふち");
            for (float x = r.xMin + 2.6f * Scale; x < r.xMax - 1f; x += 3f * Scale)
                Art.Box(root, Rect.MinMaxRect(x - 0.06f, r.yMin, x + 0.06f, r.yMax), -0.24f, -100 + i, dark, "継ぎ目");
        }

        var white = Prims.Hex(White);
        var red = Prims.Hex(Red);
        var yellow = Prims.Hex(Yellow);

        // 手（指が分かれた機械の手）。手首のカフは腕の装甲の下にもぐる
        foreach (var (x, flip) in new[] { (6.5f, false), (33.5f, true) })
        {
            var hand = Art.Place(root, "armor_hand", new Vector3(x * Scale, -0.33f, 8.6f * Scale), -165, white, "手");
            hand.flipX = flip;
            hand.transform.localScale = Vector3.one * 3.4f;
        }
        // ブーツ（足首から下）
        foreach (var x in new[] { 14f, 26f })
        {
            var boot = Art.Place(root, "armor_boot", new Vector3(x * Scale, -0.33f, -10.3f * Scale), -165, red, "ブーツ");
            boot.transform.localScale = new Vector3(3.9f, 1.7f, 1f);
        }
        // 丸い関節：装甲より手前、空洞より奥。空洞のまわりにリングが見える（設計マス：x, z, 直径）
        (float x, float z, float d)[] joints =
        {
            (4.5f, 43f, 6f), (35.5f, 43f, 6f),       // 肩
            (7f, 27f, 5.5f), (33f, 27f, 5.5f),       // ひじ
            (6.5f, 14.4f, 4f), (33.5f, 14.4f, 4f),   // 手首
            (14.5f, 22.5f, 5f), (25.5f, 22.5f, 5f),  // 股関節
            (15.5f, 8f, 5.5f), (24.5f, 8f, 5.5f),    // ひざ
            (15f, -4.2f, 4f), (25f, -4.2f, 4f),      // 足首
            (20f, 47.5f, 4.5f),                      // 首
        };
        foreach (var j in joints)
        {
            var jr = Art.Place(root, "armor_joint", new Vector3(j.x * Scale, -0.2f, j.z * Scale), -140, null, "関節");
            jr.transform.localScale = Vector3.one * (j.d * Scale / 2f);
        }
        // 肩アーマー（関節の上にかぶさる）
        foreach (var (x, flip) in new[] { (3.2f, false), (36.8f, true) })
        {
            var pa = Art.Place(root, "armor_pauldron", new Vector3(x * Scale, -0.19f, 46.2f * Scale), -138, yellow, "肩アーマー");
            pa.flipX = flip;
            pa.transform.localScale = Vector3.one * 2.6f;
        }

        // 油圧ピストン（設計マスでの中心と長さ）
        (float x, float z, float len)[] pistons =
        {
            (11.4f, 15f, 10f), (28.6f, 15f, 10f),   // もも
            (10.2f, 32f, 8f), (29.8f, 32f, 8f),     // 上腕の内側
        };
        foreach (var pz in pistons)
        {
            var pr = Art.Place(root, "piston", new Vector3(pz.x * Scale, -0.19f, pz.z * Scale), -119, null, "ピストン");
            pr.drawMode = SpriteDrawMode.Sliced;
            pr.size = new Vector2(1f, pz.len * Scale);
        }

        // 目と角（頭）
        var eye = Prims.Hex("#7ff4ff");
        Art.Box(root, Rect.MinMaxRect(16.5f * Scale, 58.3f * Scale, 19.2f * Scale, 59.0f * Scale), -0.18f, -118, eye, "目");
        Art.Box(root, Rect.MinMaxRect(20.8f * Scale, 58.3f * Scale, 23.5f * Scale, 59.0f * Scale), -0.18f, -118, eye, "目");
        foreach (var side in new[] { -1f, 1f })
        {
            var horn = Art.Box(root, Rect.MinMaxRect(-0.45f * Scale, -2.6f * Scale, 0.45f * Scale, 2.6f * Scale), -0.26f, -160, Prims.Hex(Yellow), "角");
            horn.transform.localPosition = new Vector3((20f + side * 3.2f) * Scale, -0.26f, 61.2f * Scale);
            horn.transform.localRotation = Quaternion.Euler(90f, side * 35f, 0f);
        }
    }

}
