using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 外の世界の巨大ロボットと敵の見た目。RobotState の値を読んで動かすだけで、ゲームの判定はしない。
/// （本番のモデルができたら、このクラスの Build を差し替える）
/// </summary>
public class RobotVisual : MonoBehaviour
{
    public static RobotVisual Instance { get; private set; }
    /// <summary>外カメラの揺れの強さ</summary>
    public static float Shake;

    public Transform Root { get; private set; }

    RobotState m_State;
    Transform m_Body, m_Head;
    readonly Transform[] m_Hip = new Transform[2], m_Knee = new Transform[2], m_Foot = new Transform[2], m_Arm = new Transform[2];
    GameObject m_Sight;
    readonly float[] m_Phase = new float[2];
    readonly float[] m_PunchT = { -1f, -1f };
    readonly int[] m_LastPunch = new int[2];
    readonly int[] m_StepSign = { 1, 1 };
    Vector2 m_Pos;
    float m_Yaw;

    readonly Transform[] m_Drones = new Transform[RobotState.MaxDrones];
    readonly Transform[] m_DroneRing = new Transform[RobotState.MaxDrones];
    readonly List<Fx> m_Fx = new List<Fx>();
    Transform m_FxRoot;

    class Fx
    {
        public Transform T;
        public Vector3 V;
        public float Life;
        public bool Gravity;
        public bool IsShot;
    }


    public void Init(RobotState s)
    {
        m_State = s;
        Instance = this;
        m_Pos = s.Pos.Value;
        m_Yaw = s.Yaw.Value;
        m_LastPunch[0] = s.PunchL.Value;
        m_LastPunch[1] = s.PunchR.Value;
        Build();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Root) Destroy(Root.gameObject);
        if (m_FxRoot) Destroy(m_FxRoot.gameObject);
        foreach (var d in m_Drones) if (d) Destroy(d.gameObject);
    }

    // ---------------------------------------------------------------- 組み立て

    /// <summary>傾けた箱（面取りした装甲の角に使う）</summary>
    static GameObject Plate(Transform parent, Vector3 pos, Vector3 size, Color c, Vector3 euler, bool glow = false, string name = null)
    {
        var g = Prims.Box(parent, pos, size, c, glow, name);
        g.transform.localRotation = Quaternion.Euler(euler);
        return g;
    }

    /// <summary>円柱。axis: 0 = 左右（x）、1 = 上下（y）、2 = 前後（z）</summary>
    static GameObject Cyl(Transform parent, Vector3 pos, float radius, float length, Color c, int axis, bool glow = false, string name = null)
    {
        var g = Prims.Shape(PrimitiveType.Cylinder, parent, pos, new Vector3(radius * 2f, length * 0.5f, radius * 2f), c, glow, name);
        g.transform.localRotation = axis == 0 ? Quaternion.Euler(0, 0, 90f) : axis == 2 ? Quaternion.Euler(90f, 0, 0) : Quaternion.identity;
        return g;
    }

    /// <summary>
    /// 左半分の断面図（tools/art/robot_layers.py）と同じ形・同じ配色の巨大ロボット（全高 約19m）。
    /// 脚が長く、すねは太く、足は大きな台形。胸は広く角ばった赤、腕は胸の横の黄色い丸い関節から生え、
    /// 前腕は太いブロックで外側に黄色い装甲板。頭は小さく、アンテナが2本。
    /// 前 = +z、左 = -x（画面の左側に見える側）。
    /// </summary>
    void Build()
    {
        var white = Prims.Hex("#d9dee2");
        var whiteHi = Prims.Hex("#eef1f3");
        var red = Prims.Hex("#c8402f");
        var redHi = Prims.Hex("#de5a48");
        var blue = Prims.Hex("#3c6fb4");
        var blueHi = Prims.Hex("#5486c8");
        var yellow = Prims.Hex("#f2b630");
        var dark = Prims.Hex("#3a4652");
        var frame = Prims.Hex("#262d34");
        var glow = Prims.Hex("#7ff4ff");

        Root = new GameObject("Robot").transform;
        m_Body = Prims.Pivot(Root, Vector3.zero, "Body");
        m_FxRoot = new GameObject("RobotFx").transform;

        // ---------------- 腰まわり
        Prims.Box(m_Body, new Vector3(0, 11.1f, 0), new Vector3(3.6f, 1.8f, 2.4f), dark, false, "骨盤");
        Prims.Box(m_Body, new Vector3(0, 10.3f, 1.05f), new Vector3(1.3f, 1.5f, 0.4f), dark, false, "股の装甲");
        Prims.Box(m_Body, new Vector3(0, 10.6f, 1.27f), new Vector3(0.9f, 0.25f, 0.06f), yellow);
        for (int k = -1; k <= 1; k += 2)
            Plate(m_Body, new Vector3(k * 2.15f, 10.6f, 0f), new Vector3(0.45f, 2.6f, 2.3f), dark, new Vector3(0, 0, -k * 12f), false, "腰の装甲");
        Prims.Box(m_Body, new Vector3(0, 12.55f, 0), new Vector3(2.6f, 1.6f, 2.0f), frame, false, "腰");

        // ---------------- 脚（股関節 → もも → ひざ → すね → 足首 → 足）
        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            var hip = Prims.Pivot(m_Body, new Vector3(s * 1.6f, 10.6f, 0f), k == 0 ? "左脚" : "右脚");
            Cyl(hip, Vector3.zero, 0.8f, 1.1f, frame, 0, false, "股関節");
            Prims.Box(hip, new Vector3(0, -1.6f, 0), new Vector3(1.5f, 2.9f, 1.6f), blue, false, "もも");
            Prims.Box(hip, new Vector3(0, -1.4f, 0.82f), new Vector3(1.1f, 2.0f, 0.06f), blueHi);
            var knee = Prims.Pivot(hip, new Vector3(0, -3.0f, 0), "ひざ");
            Cyl(knee, Vector3.zero, 0.7f, 1.5f, frame, 0, false, "ひざの関節");
            Plate(knee, new Vector3(0, -0.1f, 0.85f), new Vector3(2.0f, 1.3f, 0.6f), dark, new Vector3(-12f, 0, 0), false, "ひざ当て");
            // すね：ひざの下が一番太く、足首に向かって細くなる
            Prims.Box(knee, new Vector3(0, -1.5f, 0), new Vector3(2.5f, 2.8f, 2.6f), blue, false, "すね（上）");
            Prims.Box(knee, new Vector3(0, -4.25f, -0.1f), new Vector3(2.0f, 3.1f, 2.2f), blue, false, "すね（下）");
            for (int e = -1; e <= 1; e += 2)
                Plate(knee, new Vector3(e * 1.05f, -2.7f, 0f), new Vector3(0.35f, 1.6f, 2.2f), blue, new Vector3(0, 0, e * 10f), false, "すねの面取り");
            Prims.Box(knee, new Vector3(s * 1.27f, -1.5f, 0.3f), new Vector3(0.1f, 1.6f, 1.3f), frame, false, "吸気口");
            Prims.Box(knee, new Vector3(0, -1.6f, 1.32f), new Vector3(1.8f, 2.2f, 0.06f), blueHi);
            var foot = Prims.Pivot(knee, new Vector3(0, -5.9f, 0), "足");
            Prims.Box(foot, new Vector3(0, -0.15f, 0), new Vector3(1.1f, 1.0f, 1.1f), frame, false, "足首");
            Prims.Box(foot, new Vector3(0, -1.15f, 0.35f), new Vector3(2.5f, 1.0f, 3.6f), red, false, "足");
            Plate(foot, new Vector3(0, -0.85f, 1.95f), new Vector3(2.4f, 0.45f, 1.3f), redHi, new Vector3(28f, 0, 0), false, "つま先");
            Cyl(foot, new Vector3(0, -1.0f, -1.55f), 0.5f, 0.6f, frame, 2, false, "かかとの噴射口");
            Prims.Box(foot, new Vector3(0, -0.62f, 0.4f), new Vector3(2.55f, 0.12f, 3.0f), yellow);
            m_Hip[k] = hip; m_Knee[k] = knee; m_Foot[k] = foot;
        }

        // ---------------- 胸（広く角ばった赤）と背中
        Prims.Box(m_Body, new Vector3(0, 14.9f, 0), new Vector3(6.0f, 3.2f, 3.0f), red, false, "胸");
        for (int k = -1; k <= 1; k += 2)
            Plate(m_Body, new Vector3(k * 2.3f, 13.55f, 0f), new Vector3(1.6f, 0.35f, 2.8f), red, new Vector3(0, 0, k * 34f), false, "胸の下の面取り");
        Prims.Box(m_Body, new Vector3(0, 16.55f, -0.1f), new Vector3(7.2f, 0.5f, 3.1f), redHi, false, "胸の上の板");
        Prims.Box(m_Body, new Vector3(0, 13.4f, 0.2f), new Vector3(2.4f, 1.2f, 2.4f), red, false, "みぞおち");
        for (int k = -1; k <= 1; k += 2)
        {
            Prims.Box(m_Body, new Vector3(k * 1.6f, 14.6f, 1.52f), new Vector3(1.7f, 1.9f, 0.1f), frame, false, "吸気口");
            for (int g = 0; g < 4; g++)
                Prims.Box(m_Body, new Vector3(k * 1.6f, 14.0f + g * 0.4f, 1.58f), new Vector3(1.5f, 0.1f, 0.06f), dark);
        }
        Prims.Box(m_Body, new Vector3(0, 15.5f, 1.53f), new Vector3(1.0f, 0.5f, 0.08f), yellow, true, "胸の灯り");
        Prims.Box(m_Body, new Vector3(0, 14.8f, -2.0f), new Vector3(4.2f, 3.0f, 1.4f), dark, false, "背中");
        for (int k = -1; k <= 1; k += 2)
            Cyl(m_Body, new Vector3(k * 1.3f, 15.6f, -2.4f), 0.45f, 2.6f, frame, 1, false, "排気筒");

        // ---------------- 首と頭（小さめ・アンテナ2本）
        Cyl(m_Body, new Vector3(0, 16.95f, 0.1f), 0.45f, 0.9f, frame, 1, false, "首");
        m_Head = Prims.Pivot(m_Body, new Vector3(0, 17.4f, 0.25f), "頭");
        Prims.Box(m_Head, new Vector3(0, 0.75f, 0), new Vector3(1.9f, 1.6f, 1.9f), white, false, "頭");
        Plate(m_Head, new Vector3(0, 1.65f, -0.1f), new Vector3(1.7f, 0.4f, 1.7f), whiteHi, new Vector3(-8f, 0, 0), false, "頭のてっぺん");
        Prims.Box(m_Head, new Vector3(0, 0.25f, 0.95f), new Vector3(1.1f, 0.6f, 0.2f), white, false, "口もと");
        Prims.Box(m_Head, new Vector3(0, 0.95f, 0.97f), new Vector3(1.6f, 0.32f, 0.1f), glow, true, "目");
        Prims.Box(m_Head, new Vector3(0, 1.95f, 0.5f), new Vector3(0.25f, 0.6f, 0.6f), red, false, "とさか");
        for (int k = -1; k <= 1; k += 2)
            Plate(m_Head, new Vector3(k * 0.65f, 2.35f, 0f), new Vector3(0.08f, 1.6f, 0.08f), whiteHi, new Vector3(0, 0, -k * 8f), false, "アンテナ");
        Cyl(m_Head, new Vector3(1.05f, 0.6f, 0.6f), 0.18f, 1.0f, frame, 2, false, "頭部砲");
        m_Sight = Prims.Box(m_Head, new Vector3(1.05f, 0.6f, 36f), new Vector3(0.12f, 0.12f, 70f), glow, true, "照準線");

        // ---------------- 腕（黄色い丸い肩の関節 → 細い上腕 → ひじ → 太い前腕 → 指の分かれた手）
        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            Cyl(m_Body, new Vector3(s * 3.75f, 15.4f, 0f), 1.05f, 1.3f, yellow, 0, false, "肩の関節");
            var arm = Prims.Pivot(m_Body, new Vector3(s * 3.75f, 15.4f, 0f), k == 0 ? "左腕" : "右腕");
            Prims.Box(arm, new Vector3(0, -1.9f, 0), new Vector3(1.25f, 2.9f, 1.3f), white, false, "上腕");
            Cyl(arm, new Vector3(0, -3.35f, 0), 0.62f, 1.4f, frame, 0, false, "ひじ");
            Prims.Box(arm, new Vector3(0, -4.9f, 0.05f), new Vector3(2.0f, 2.3f, 1.9f), white, false, "前腕");
            Prims.Box(arm, new Vector3(0, -4.9f, 1.0f), new Vector3(1.5f, 1.6f, 0.06f), whiteHi);
            Prims.Box(arm, new Vector3(s * 1.2f, -4.7f, 0.05f), new Vector3(0.3f, 3.1f, 2.3f), yellow, false, "前腕の装甲板");
            Prims.Box(arm, new Vector3(0, -6.25f, 0.05f), new Vector3(0.9f, 0.5f, 0.9f), frame, false, "手首");
            Prims.Box(arm, new Vector3(0, -7.0f, 0.15f), new Vector3(1.6f, 1.25f, 0.85f), white, false, "手のひら");
            for (int f = 0; f < 4; f++)
            {
                float fx = -0.6f + f * 0.4f;
                float len = f == 1 || f == 2 ? 1.0f : 0.85f;
                Prims.Box(arm, new Vector3(fx, -7.85f, 0.2f), new Vector3(0.32f, 0.5f, 0.38f), white, false, "指");
                Prims.Box(arm, new Vector3(fx, -8.1f, 0.2f), new Vector3(0.2f, 0.12f, 0.3f), frame);
                Prims.Box(arm, new Vector3(fx, -8.15f - len * 0.4f, 0.22f), new Vector3(0.28f, len * 0.7f, 0.34f), white, false, "指先");
            }
            Plate(arm, new Vector3(-s * 0.95f, -7.15f, 0.35f), new Vector3(0.32f, 0.9f, 0.38f), white, new Vector3(0, 0, s * 25f), false, "親指");
            m_Arm[k] = arm;
        }

        // 敵ドローン（決まった数を先に作っておき、使う分だけ表示する）
        var droneBody = Prims.Hex("#3a2140");
        var ring = Prims.Hex("#ff3d7f");
        for (int i = 0; i < RobotState.MaxDrones; i++)
        {
            var d = new GameObject("ドローン " + i).transform;
            Prims.Shape(PrimitiveType.Sphere, d, Vector3.zero, new Vector3(2.6f, 1.5f, 2.6f), droneBody);
            var r = Prims.Shape(PrimitiveType.Cylinder, d, Vector3.zero, new Vector3(4f, 0.08f, 4f), ring, true);
            Prims.Shape(PrimitiveType.Sphere, d, new Vector3(0, 0.1f, 1.15f), Vector3.one * 0.7f, Prims.Hex("#ff2a2a"), true);
            d.gameObject.SetActive(false);
            m_Drones[i] = d;
            m_DroneRing[i] = r.transform;
        }
    }

    // ---------------------------------------------------------------- 毎フレーム

    void Update()
    {
        if (m_State == null || !m_State.IsSpawned) return;
        float dt = Time.deltaTime;
        var s = m_State;

        // 位置と向き（ホストからの値に向けてなめらかに）
        float k = 1f - Mathf.Exp(-12f * dt);
        if (Vector2.Distance(m_Pos, s.Pos.Value) > 20f) m_Pos = s.Pos.Value;
        m_Pos = Vector2.Lerp(m_Pos, s.Pos.Value, k);
        m_Yaw = Mathf.LerpAngle(m_Yaw * Mathf.Rad2Deg, s.Yaw.Value * Mathf.Rad2Deg, k) * Mathf.Deg2Rad;
        Root.SetPositionAndRotation(new Vector3(m_Pos.x, 0, m_Pos.y), Quaternion.Euler(0, m_Yaw * Mathf.Rad2Deg, 0));

        // 脚
        bool powered = s.Power.Value > 0f;
        float[] levers = { s.LegL.Value, s.LegR.Value };
        bool moving = false;
        for (int i = 0; i < 2; i++)
        {
            float lev = levers[i];
            if (powered) m_Phase[i] += lev * dt * 3.4f;
            if (Mathf.Abs(lev) > 0.05f && powered) moving = true;
            float amp = Mathf.Min(1f, Mathf.Abs(lev) * 2.2f) * 28f;
            float hip = -Mathf.Sin(m_Phase[i]) * amp;
            float knee = Mathf.Max(0f, Mathf.Cos(m_Phase[i])) * amp * 1.3f;
            m_Hip[i].localRotation = Quaternion.Euler(hip, 0, 0);
            m_Knee[i].localRotation = Quaternion.Euler(knee, 0, 0);
            m_Foot[i].localRotation = Quaternion.Euler(-(hip + knee), 0, 0);
            int sign = Mathf.Sin(m_Phase[i]) >= 0f ? 1 : -1;
            if (sign != m_StepSign[i] && Mathf.Abs(lev) > 0.05f)
            {
                m_StepSign[i] = sign;
                if (sign > 0) Shake = Mathf.Max(Shake, 0.25f);
            }
        }
        float bob = moving ? Mathf.Abs(Mathf.Sin((m_Phase[0] + m_Phase[1]) * 0.5f)) * 0.3f : 0f;
        m_Body.localPosition = new Vector3(0, Mathf.Lerp(m_Body.localPosition.y, bob, k), 0);

        // 腕（パンチの回数が増えたら振る）
        int[] punches = { s.PunchL.Value, s.PunchR.Value };
        float[] aims = { s.AimL.Value, s.AimR.Value };
        for (int i = 0; i < 2; i++)
        {
            if (punches[i] != m_LastPunch[i]) { m_LastPunch[i] = punches[i]; m_PunchT[i] = 0f; }
            float e = 0f;
            if (m_PunchT[i] >= 0f)
            {
                m_PunchT[i] += dt / 0.5f;
                float t = m_PunchT[i];
                e = t < 0.3f ? 1f - Mathf.Pow(1f - t / 0.3f, 3f) : 1f - (t - 0.3f) / 0.7f;
                if (t >= 1f) m_PunchT[i] = -1f;
            }
            float idle = -Mathf.Sin(m_Phase[1 - i]) * 7f - 4f;
            m_Arm[i].localRotation = Quaternion.Euler(Mathf.Lerp(idle, -86f, e), aims[i] * Mathf.Rad2Deg, 0);
        }

        // 頭と照準線（頭部砲に誰か座っているときだけ表示）
        m_Head.localRotation = Quaternion.Euler(0, s.AimHead.Value * Mathf.Rad2Deg, 0);
        m_Sight.SetActive(s.Occupant.Count > Stations.Head && s.Occupant[Stations.Head] != 0);

        // 敵の巣
        for (int i = 0; i < ExteriorWorld.HiveObjects.Length; i++)
        {
            var go = ExteriorWorld.HiveObjects[i];
            if (!go) continue;
            bool alive = s.HiveAlive(i);
            if (go.activeSelf != alive) go.SetActive(alive);
            if (alive) ExteriorWorld.HiveCores[i].localScale = Vector3.one * (6.5f + Mathf.Sin(Time.time * 3f) * 0.4f);
        }

        // ドローン
        for (int i = 0; i < RobotState.MaxDrones && i < s.Drones.Count; i++)
        {
            var d = s.Drones[i];
            var t = m_Drones[i];
            bool alive = d.w > 0f;
            if (t.gameObject.activeSelf != alive)
            {
                t.gameObject.SetActive(alive);
                if (alive) t.position = new Vector3(d.x, d.y, d.z);
            }
            if (!alive) continue;
            var target = new Vector3(d.x, d.y, d.z);
            var dir = new Vector3(m_Pos.x, 0, m_Pos.y) - t.position;
            t.position = Vector3.Lerp(t.position, target, 1f - Mathf.Exp(-8f * dt));
            dir.y = 0;
            if (dir.sqrMagnitude > 0.01f) t.rotation = Quaternion.LookRotation(dir);
            m_DroneRing[i].localRotation = Quaternion.Euler(0, Time.time * 240f, 0);
        }

        UpdateFx(dt);
        Shake = Mathf.Max(0f, Shake - dt * 2.2f);
    }

    // ---------------------------------------------------------------- 演出

    public void SpawnBolt(Vector3 from, float yaw)
    {
        var b = Prims.Box(m_FxRoot, from, new Vector3(0.35f, 0.35f, 3.2f), Prims.Hex("#8ff6ff"), true, "Bolt");
        b.transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
        var dir = new Vector3(Mathf.Sin(yaw), -0.12f, Mathf.Cos(yaw));
        m_Fx.Add(new Fx { T = b.transform, V = dir * 75f, Life = 1.4f });
    }

    public void SpawnShot(Vector3 from, Vector3 velocity)
    {
        var b = Prims.Shape(PrimitiveType.Sphere, m_FxRoot, from, Vector3.one * 0.9f, Prims.Hex("#ff3d5a"), true, "Shot");
        m_Fx.Add(new Fx { T = b.transform, V = velocity, Life = 4f, IsShot = true });
    }

    public void Boom(Vector3 at, float size, Color color)
    {
        int n = Mathf.RoundToInt(10 * size);
        for (int i = 0; i < n; i++)
        {
            var f = Prims.Box(m_FxRoot, at, Vector3.one * Random.Range(0.4f, 1f) * size, i % 3 == 0 ? Prims.Hex("#ffd23f") : color, true, "Spark");
            var v = Random.insideUnitSphere * 14f * Mathf.Sqrt(size);
            v.y = Mathf.Abs(v.y) + 6f;
            m_Fx.Add(new Fx { T = f.transform, V = v, Life = Random.Range(0.6f, 1.2f), Gravity = true });
        }
        float d = Vector2.Distance(new Vector2(at.x, at.z), m_Pos);
        if (d < 40f) Shake = Mathf.Max(Shake, 0.3f + size * 0.2f);
    }

    public void Hit()
    {
        Shake = Mathf.Max(Shake, 0.7f);
        HitFlash = 1f;
    }

    /// <summary>被弾したときに画面のふちを赤くする強さ（HUDが読む）</summary>
    public static float HitFlash;

    void UpdateFx(float dt)
    {
        HitFlash = Mathf.Max(0f, HitFlash - dt * 3f);
        for (int i = m_Fx.Count - 1; i >= 0; i--)
        {
            var f = m_Fx[i];
            f.Life -= dt;
            if (f.Gravity) f.V.y -= 26f * dt;
            var p = f.T.position + f.V * dt;
            if (p.y < 0.2f) { p.y = 0.2f; f.V *= 0.5f; f.V.y = -f.V.y * 0.3f; }
            f.T.position = p;
            if (f.IsShot && Vector2.Distance(new Vector2(p.x, p.z), m_Pos) < 5.4f && p.y < 15f) f.Life = 0f;
            if (f.Life <= 0f)
            {
                if (f.IsShot) Boom(p, 0.5f, Prims.Hex("#ff3d5a"));
                Destroy(f.T.gameObject);
                m_Fx.RemoveAt(i);
            }
        }
    }
}
