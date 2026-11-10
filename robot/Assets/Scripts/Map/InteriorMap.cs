using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 巨大ロボットの内部マップ。人型ロボットを<b>横から見た断面図</b>として作る。
///
/// - 体の形（くびれのある人型。手の指の中まで空洞がつながっている）と中の機械は、
///   tools/art/robot_layers.py が3枚の絵（装甲・奥の機械・手前のケーブル）として描く
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

    // ------------------------------------------------------------------ 足場とはしご

    /// <summary>足場。x0～x1 の範囲に、高さ y の床がある（メートル）。</summary>
    public struct Catwalk { public float X0, X1, Y; }
    /// <summary>はしご。x の位置に、y0（下の床）から y1（上の床）まで（メートル）。</summary>
    public struct Ladder { public float X, Y0, Y1; }

    static Catwalk C(float x0, float x1, float y) => new Catwalk { X0 = x0 * Scale, X1 = x1 * Scale, Y = y * Scale };
    static Ladder L(float x, float y0, float y1) => new Ladder { X = x * Scale, Y0 = y0 * Scale, Y1 = y1 * Scale };

    public static readonly Catwalk[] Catwalks =
    {
        C(16.8f, 23.2f, 49.5f),   // 頭
        C(4.6f, 35.4f, 41.5f),    // 肩の関節から胸の上の段
        C(13.6f, 26.4f, 35.6f),   // 胸の下の段（動力炉）
        C(12.8f, 27.2f, 27.4f),   // 腰
        C(14.0f, 17.2f, 20.6f),   // 左もも（途中の足場）
        C(22.8f, 26.0f, 20.6f),   // 右もも
        C(3.6f, 9.0f, 17.6f),     // 左の手のひら
        C(31.0f, 36.4f, 17.6f),   // 右の手のひら
        C(12.2f, 18.6f, 4.6f),    // 左すね
        C(21.4f, 27.8f, 4.6f),    // 右すね
        C(9.0f, 18.6f, -10.8f),   // 左足
        C(21.4f, 31.0f, -10.8f),  // 右足
    };

    public static readonly Ladder[] Ladders =
    {
        L(20f, 27.4f, 49.5f),     // 腰 → 腹 → 胸 → 首 → 頭（体の真ん中を通る）
        L(6.4f, 17.6f, 41.5f),    // 左肩 → 上腕 → ひじ → 前腕 → 手首 → 手のひら
        L(33.6f, 17.6f, 41.5f),   // 右肩 → 右手のひら
        L(15.5f, -10.8f, 27.4f),  // 腰 → 左もも → ひざ → 左すね → 足首 → 左足
        L(24.5f, -10.8f, 27.4f),  // 腰 → 右足
    };

    public static readonly Vector2 Spawn = new Vector2(24f, 35.6f) * Scale;

    /// <summary>動力炉の本体の位置（足元、メートル）</summary>
    public static readonly Vector2 ReactorPos = new Vector2(17.2f, 35.6f) * Scale;

    // ------------------------------------------------------------------ 体の絵（tools/art/robot_layers.py で描く）

    /// <summary>
    /// 体の絵3枚（robot_armor / robot_back / robot_front）が覆う範囲（設計マス）。
    /// robot_layers.py の U_X0, U_X1, U_Z0, U_Z1 と同じにする。
    /// </summary>
    public static readonly Rect LayerRectUnits = Rect.MinMaxRect(-8f, -16f, 48f, 66f);
    /// <summary>体の絵の解像度（1メートルあたりのピクセル数）。robot_layers.py の PPM と同じ</summary>
    public const float LayerPixelsPerUnit = 32f;

    /// <summary>ロボット全体の範囲（メートル）。ミニマップと全体図に使う。</summary>
    public static Rect Bounds => Rect.MinMaxRect(LayerRectUnits.xMin * Scale, LayerRectUnits.yMin * Scale, LayerRectUnits.xMax * Scale, LayerRectUnits.yMax * Scale);

    public static Vector2 Center => Bounds.center;

    /// <summary>場所の名前（画面下の案内に出す）。設計マスのおおよその範囲</summary>
    static readonly (string name, Rect rect)[] Regions =
    {
        ("頭", Rect.MinMaxRect(15f, 49f, 25f, 57f)), ("首", Rect.MinMaxRect(17f, 46.5f, 23f, 49f)),
        ("左肩", Rect.MinMaxRect(-2f, 37f, 10f, 49f)), ("右肩", Rect.MinMaxRect(30f, 37f, 42f, 49f)),
        ("胸", Rect.MinMaxRect(9f, 35f, 31f, 46.5f)), ("腹", Rect.MinMaxRect(14f, 31f, 26f, 35f)), ("腰", Rect.MinMaxRect(11f, 23f, 29f, 31f)),
        ("左腕", Rect.MinMaxRect(1f, 24f, 11f, 37f)), ("右腕", Rect.MinMaxRect(29f, 24f, 39f, 37f)),
        ("左手", Rect.MinMaxRect(0f, 10f, 11.5f, 24f)), ("右手", Rect.MinMaxRect(28.5f, 10f, 40f, 24f)),
        ("左もも", Rect.MinMaxRect(11f, 15f, 20f, 23f)), ("右もも", Rect.MinMaxRect(20f, 15f, 29f, 23f)),
        ("左すね", Rect.MinMaxRect(10f, -6f, 20f, 15f)), ("右すね", Rect.MinMaxRect(20f, -6f, 30f, 15f)),
        ("左足", Rect.MinMaxRect(7f, -13f, 20f, -6f)), ("右足", Rect.MinMaxRect(20f, -13f, 33f, -6f)),
    };

    /// <summary>足元 (x, y) から、立っている足場を探す。なければ -1</summary>
    public static int CatwalkAt(float x, float y, float tolerance = 0.02f)
    {
        for (int i = 0; i < Catwalks.Length; i++)
        {
            var c = Catwalks[i];
            if (x >= c.X0 - 0.01f && x <= c.X1 + 0.01f && Mathf.Abs(y - c.Y) <= tolerance) return i;
        }
        return -1;
    }

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
        var u = new Vector2(p.x, p.y + 0.6f) / Scale;
        foreach (var (name, rect) in Regions)
            if (rect.Contains(u)) return name;
        return "";
    }

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
    const int OrderArmor = -200, OrderCavity = -95, OrderGirder = -88, OrderBrace = -87, OrderGear = -82, OrderActuator = -80, OrderBackCable = -76,
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

        // 体の絵3枚を同じ位置に重ねる：装甲 → 空洞の奥（機械） → …足場や操縦士… → 手前のケーブル
        var center = LayerRectUnits.center * Scale;
        var pos = new Vector3(center.x, 0f, center.y);
        Art.Place(root, "robot_armor", pos + new Vector3(0, -0.3f, 0), OrderArmor, null, "装甲");
        Art.Place(root, "robot_back", pos + new Vector3(0, -0.1f, 0), OrderCavity, null, "内部の機械");
        Art.Place(root, "robot_front", pos + new Vector3(0, 0.05f, 0), OrderFrontCable, null, "手前のケーブル");

        BuildCrewLayer(root, v);
        return v;
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
            ("crate", 26.2f, 35.6f, 0.8f),
            ("machine", 28.6f, 41.5f, 0.6f), ("tanks", 15.2f, 41.5f, 0.6f),
            ("machine", 21.6f, 27.4f, 0.55f),
            ("crate", 12.6f, -10.8f, 0.8f), ("crate", 13.6f, -10.8f, 0.8f), ("crate", 28.4f, -10.8f, 0.8f), ("tanks", 26.6f, -10.8f, 0.7f),
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

}
