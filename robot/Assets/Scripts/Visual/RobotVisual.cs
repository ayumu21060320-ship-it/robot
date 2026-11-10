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

    // 巨大な敵
    Transform m_Boss, m_BossTorso, m_BossHead, m_BossTail;
    readonly Transform[] m_BossHip = new Transform[2], m_BossKnee = new Transform[2], m_BossArm = new Transform[2], m_BossForearm = new Transform[2];
    Renderer m_BossCore, m_BossMouth;
    Vector2 m_BossPos;
    float m_BossYaw, m_BossPhase, m_BossT = -1f, m_BossLastHp, m_BossFlash;
    int m_BossLastId, m_BossKind;
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
        m_BossPos = s.BossPos.Value;
        m_BossYaw = s.BossYaw.Value;
        m_BossLastId = s.BossActionId.Value;
        m_BossLastHp = s.BossHp.Value;
        Build();
        Root.localScale = Vector3.one * RobotState.TitanScale;
        BuildBoss();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Root) Destroy(Root.gameObject);
        if (m_FxRoot) Destroy(m_FxRoot.gameObject);
        if (m_Boss) Destroy(m_Boss.gameObject);
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
        // 実際の歩く速さ（ゆっくり加速する）に合わせて、大きな歩幅でゆっくり脚を動かす
        bool moving = false;
        for (int i = 0; i < 2; i++)
        {
            float legSpeed = s.MoveSpeed.Value + (i == 0 ? 1f : -1f) * s.TurnRate.Value * 4f;
            float lev = legSpeed / 3.4f;
            m_Phase[i] += legSpeed * dt * 0.42f;
            if (Mathf.Abs(lev) > 0.05f) moving = true;
            float amp = Mathf.Min(1f, Mathf.Abs(lev) * 1.6f) * 26f;
            float hip = -Mathf.Sin(m_Phase[i]) * amp;
            float knee = Mathf.Max(0f, Mathf.Cos(m_Phase[i])) * amp * 1.3f;
            m_Hip[i].localRotation = Quaternion.Euler(hip, 0, 0);
            m_Knee[i].localRotation = Quaternion.Euler(knee, 0, 0);
            m_Foot[i].localRotation = Quaternion.Euler(-(hip + knee), 0, 0);
            int sign = Mathf.Sin(m_Phase[i]) >= 0f ? 1 : -1;
            if (sign != m_StepSign[i] && Mathf.Abs(lev) > 0.05f)
            {
                m_StepSign[i] = sign;
                if (sign > 0) Shake = Mathf.Max(Shake, 0.55f); // 一歩ごとにズシンと揺れる
            }
        }
        float bob = moving ? Mathf.Abs(Mathf.Sin((m_Phase[0] + m_Phase[1]) * 0.5f)) * 0.25f : 0f;
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
                m_PunchT[i] += dt / RobotState.PunchTime;   // 重い腕なのでゆっくり振る
                float t = m_PunchT[i];
                // 振りかぶり（少し後ろへ）→ 突き出し → ゆっくり戻す
                e = t < 0.2f ? -0.25f * (t / 0.2f) : t < 0.43f ? Mathf.Lerp(-0.25f, 1f, 1f - Mathf.Pow(1f - (t - 0.2f) / 0.23f, 3f)) : 1f - (t - 0.43f) / 0.57f;
                if (t >= 1f) m_PunchT[i] = -1f;
            }
            float idle = -Mathf.Sin(m_Phase[1 - i]) * 7f - 4f;
            m_Arm[i].localRotation = Quaternion.Euler(Mathf.LerpUnclamped(idle, -86f, e), aims[i] * Mathf.Rad2Deg, 0);
        }

        // 頭と照準線（頭部砲に誰か座っているときだけ表示）
        m_Head.localRotation = Quaternion.Euler(0, s.AimHead.Value * Mathf.Rad2Deg, 0);
        m_Sight.SetActive(s.Occupant.Count > Stations.Head && s.Occupant[Stations.Head] != 0);

        UpdateBoss(dt);

        UpdateFx(dt);
        Shake = Mathf.Max(0f, Shake - dt * 2.2f);
    }

    // ---------------------------------------------------------------- 巨大な敵

    /// <summary>
    /// 巨大な敵（全高 約42m）。前かがみの胴体、大きな爪の腕、角のある頭、背中のトゲ、しっぽ。
    /// 胸の光るコアが弱点に見えるようにしてある（当たり判定は体全体）。
    /// </summary>
    void BuildBoss()
    {
        var hull = Prims.Hex("#3a2140");
        var plate = Prims.Hex("#5a2d5e");
        var bone = Prims.Hex("#c9c4cf");
        var glowC = Prims.Hex("#ff3d7f");
        var eye = Prims.Hex("#ff2a2a");

        m_Boss = new GameObject("巨大な敵").transform;
        Prims.Box(m_Boss, new Vector3(0, 13.5f, -1f), new Vector3(12f, 6f, 9f), hull, false, "腰");

        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            var hip = Prims.Pivot(m_Boss, new Vector3(s * 5.2f, 13.5f, -1f), "脚");
            Prims.Box(hip, new Vector3(0, -3.6f, 0.4f), new Vector3(5.2f, 7.6f, 6.2f), plate, false, "もも");
            var knee = Prims.Pivot(hip, new Vector3(0, -7.2f, 0.6f), "ひざ");
            Prims.Box(knee, new Vector3(0, -2.9f, -0.6f), new Vector3(4.4f, 6.2f, 5f), hull, false, "すね");
            Prims.Box(knee, new Vector3(0, -6.2f, 1.6f), new Vector3(7f, 1.6f, 10f), plate, false, "足");
            for (int c = -1; c <= 1; c++)
                Plate(knee, new Vector3(c * 2.4f, -6.4f, 7f), new Vector3(1.1f, 1.1f, 3f), bone, new Vector3(20f, 0, 0), false, "足の爪");
            m_BossHip[k] = hip; m_BossKnee[k] = knee;
        }

        m_BossTorso = Prims.Pivot(m_Boss, new Vector3(0, 16f, -1f), "胴");
        m_BossTorso.localRotation = Quaternion.Euler(18f, 0, 0);
        Prims.Box(m_BossTorso, new Vector3(0, 7f, 0.5f), new Vector3(16f, 14f, 11f), hull, false, "胴体");
        Prims.Box(m_BossTorso, new Vector3(0, 9.5f, 6.1f), new Vector3(12f, 7f, 1.2f), plate, false, "胸の装甲");
        m_BossCore = Prims.Shape(PrimitiveType.Sphere, m_BossTorso, new Vector3(0, 8.5f, 6.6f), Vector3.one * 4.2f, glowC, true, "コア").GetComponent<Renderer>();
        for (int i = 0; i < 5; i++)
            Plate(m_BossTorso, new Vector3(0, 13.5f - i * 2.6f, -5.6f), new Vector3(1.6f, 4.2f - i * 0.4f, 1.6f), bone, new Vector3(-40f, 0, 0), false, "背中のトゲ");
        for (int k = -1; k <= 1; k += 2)
            Plate(m_BossTorso, new Vector3(k * 7.6f, 13.6f, 0.5f), new Vector3(5f, 3f, 7f), plate, new Vector3(0, 0, -k * 18f), false, "肩の甲羅");

        // 腕（大きな爪）。右腕で振り下ろす
        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            var arm = Prims.Pivot(m_BossTorso, new Vector3(s * 9.6f, 12.5f, 1.5f), "腕");
            arm.localRotation = Quaternion.Euler(-18f, 0, 0);
            Prims.Box(arm, new Vector3(0, -4.5f, 0), new Vector3(4.4f, 9f, 4.4f), plate, false, "上腕");
            var fore = Prims.Pivot(arm, new Vector3(0, -9f, 0), "前腕");
            Prims.Box(fore, new Vector3(0, -4.5f, 0.5f), new Vector3(5.6f, 9f, 5.6f), hull, false, "前腕");
            for (int c = -1; c <= 1; c++)
                Plate(fore, new Vector3(c * 1.8f, -11f, 1.6f), new Vector3(1.2f, 5.5f, 1.2f), bone, new Vector3(-25f, 0, c * 10f), false, "爪");
            m_BossArm[k] = arm; m_BossForearm[k] = fore;
        }

        // 頭
        m_BossHead = Prims.Pivot(m_BossTorso, new Vector3(0, 14f, 5f), "頭");
        Prims.Box(m_BossHead, new Vector3(0, 2f, 2.5f), new Vector3(8f, 6f, 8f), plate, false, "頭");
        Prims.Box(m_BossHead, new Vector3(0, -1.2f, 4f), new Vector3(6.6f, 2.2f, 6f), hull, false, "あご");
        m_BossMouth = Prims.Box(m_BossHead, new Vector3(0, 0.3f, 7.05f), new Vector3(5f, 0.9f, 0.3f), glowC, true, "口").GetComponent<Renderer>();
        for (int k = -1; k <= 1; k += 2)
        {
            Prims.Box(m_BossHead, new Vector3(k * 2f, 3.2f, 6.55f), new Vector3(1.8f, 0.8f, 0.2f), eye, true, "目");
            Plate(m_BossHead, new Vector3(k * 3.4f, 6.8f, 0.5f), new Vector3(1.3f, 7.5f, 1.3f), bone, new Vector3(-25f, 0, -k * 28f), false, "角");
        }

        // しっぽ
        m_BossTail = Prims.Pivot(m_Boss, new Vector3(0, 13f, -5f), "しっぽ");
        var seg = m_BossTail;
        for (int i = 0; i < 4; i++)
        {
            float w = 5f - i * 1.1f;
            Prims.Box(seg, new Vector3(0, 0, -2.6f), new Vector3(w, w * 0.8f, 5.6f), i % 2 == 0 ? hull : plate, false, "しっぽ");
            var next = Prims.Pivot(seg, new Vector3(0, -0.6f, -5.2f), "しっぽ");
            next.localRotation = Quaternion.Euler(12f, 0, 0);
            seg = next;
        }
    }

    void UpdateBoss(float dt)
    {
        var s = m_State;
        bool alive = s.BossAlive;
        if (m_Boss.gameObject.activeSelf != alive) m_Boss.gameObject.SetActive(alive);
        if (!alive) return;

        float k = 1f - Mathf.Exp(-6f * dt);
        m_BossPos = Vector2.Lerp(m_BossPos, s.BossPos.Value, k);
        m_BossYaw = Mathf.LerpAngle(m_BossYaw * Mathf.Rad2Deg, s.BossYaw.Value * Mathf.Rad2Deg, k) * Mathf.Deg2Rad;
        m_Boss.SetPositionAndRotation(new Vector3(m_BossPos.x, 0, m_BossPos.y), Quaternion.Euler(0, m_BossYaw * Mathf.Rad2Deg, 0));

        // 歩き：大きな歩幅でゆっくり。一歩ごとに地面が揺れる
        float sp = s.BossSpeed.Value;
        float prev = Mathf.Sin(m_BossPhase);
        m_BossPhase += sp * dt * 0.32f;
        float amp = Mathf.Min(1f, sp / 2.3f) * 22f;
        for (int i = 0; i < 2; i++)
        {
            float ph = m_BossPhase + i * Mathf.PI;
            m_BossHip[i].localRotation = Quaternion.Euler(-Mathf.Sin(ph) * amp, 0, 0);
            m_BossKnee[i].localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Cos(ph)) * amp * 1.2f, 0, 0);
        }
        if (sp > 0.1f && Mathf.Sign(prev) != Mathf.Sign(Mathf.Sin(m_BossPhase)) && Vector2.Distance(m_BossPos, m_Pos) < 160f)
            Shake = Mathf.Max(Shake, 0.45f);
        m_BossTail.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 0.9f) * 12f, 0);

        // 攻撃のアニメーション（ホストが攻撃を始めた合図で始める）
        if (s.BossActionId.Value != m_BossLastId)
        {
            m_BossLastId = s.BossActionId.Value;
            m_BossKind = s.BossAction.Value;
            m_BossT = 0f;
        }
        float armR = -18f, foreR = 0f, torsoX = 18f, headX = 0f, lift = 0f;
        float mouth = 0.3f;
        if (m_BossT >= 0f)
        {
            m_BossT += dt;
            float t = m_BossT;
            switch (m_BossKind)
            {
                case RobotState.BossSwipe:
                {
                    float w = RobotState.BossSwipeWindup;
                    if (t < w) { float e = t / w; armR = Mathf.Lerp(-18f, -150f, e * e); foreR = -30f * e; torsoX = 18f - 10f * e; }
                    else if (t < w + 0.35f) { float e = (t - w) / 0.35f; armR = Mathf.Lerp(-150f, 20f, e); foreR = Mathf.Lerp(-30f, 10f, e); torsoX = Mathf.Lerp(8f, 30f, e); }
                    else if (t < w + 1.2f) { float e = (t - w - 0.35f) / 0.85f; armR = Mathf.Lerp(20f, -18f, e); foreR = Mathf.Lerp(10f, 0f, e); torsoX = Mathf.Lerp(30f, 18f, e); }
                    else m_BossT = -1f;
                    break;
                }
                case RobotState.BossBarrage:
                    headX = -12f * Mathf.Min(1f, t / 0.6f);
                    mouth = 0.6f + Mathf.Abs(Mathf.Sin(t * 9f)) * 0.8f;
                    if (s.BossAction.Value != RobotState.BossBarrage && t > 1f) m_BossT = -1f;
                    break;
                case RobotState.BossStomp:
                    if (t < 1.4f) lift = Mathf.Sin(t / 1.4f * Mathf.PI * 0.5f) * 30f;
                    else if (t < 1.6f) { lift = Mathf.Lerp(30f, 0f, (t - 1.4f) / 0.2f); }
                    else if (t > 2.6f) m_BossT = -1f;
                    if (t >= 1.55f && t - dt < 1.55f && Vector2.Distance(m_BossPos, m_Pos) < 160f) Shake = Mathf.Max(Shake, 1.4f);
                    break;
            }
        }
        m_BossArm[1].localRotation = Quaternion.Euler(armR, 0, 0);
        m_BossForearm[1].localRotation = Quaternion.Euler(foreR, 0, 0);
        m_BossArm[0].localRotation = Quaternion.Euler(-18f + Mathf.Sin(m_BossPhase) * 10f, 0, 0);
        m_BossTorso.localRotation = Quaternion.Euler(torsoX + Mathf.Sin(Time.time * 1.1f) * 1.5f, 0, 0);
        m_BossHead.localRotation = Quaternion.Euler(headX, 0, 0);
        if (lift > 0f) m_BossHip[0].localRotation = Quaternion.Euler(-lift, 0, 0);

        // コアの脈動と、ダメージを受けたときの白い点滅
        if (s.BossHp.Value < m_BossLastHp) m_BossFlash = 1f;
        m_BossLastHp = s.BossHp.Value;
        m_BossFlash = Mathf.Max(0f, m_BossFlash - dt * 4f);
        float pulse = 0.75f + Mathf.Sin(Time.time * 2.4f) * 0.25f;
        Prims.SetColor(m_BossCore, Color.Lerp(Prims.Hex("#ff3d7f") * pulse, Color.white, m_BossFlash));
        m_BossCore.transform.localScale = Vector3.one * (4.2f + m_BossFlash * 0.8f);
        Prims.SetColor(m_BossMouth, Prims.Hex("#ff3d7f") * Mathf.Clamp01(mouth));
    }

    // ---------------------------------------------------------------- 演出

    public void SpawnBolt(Vector3 from, float yaw)
    {
        var b = Prims.Box(m_FxRoot, from, new Vector3(0.6f, 0.6f, 5f), Prims.Hex("#8ff6ff"), true, "Bolt");
        b.transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
        var dir = new Vector3(Mathf.Sin(yaw), -0.12f, Mathf.Cos(yaw));
        m_Fx.Add(new Fx { T = b.transform, V = dir * 75f, Life = 1.4f });
    }

    public void SpawnShot(Vector3 from, Vector3 velocity)
    {
        var b = Prims.Shape(PrimitiveType.Sphere, m_FxRoot, from, Vector3.one * 2.6f, Prims.Hex("#ff3d5a"), true, "Shot");
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
            if (f.IsShot && Vector2.Distance(new Vector2(p.x, p.z), m_Pos) < ExteriorWorld.RobotRadius && p.y < 19f * RobotState.TitanScale) f.Life = 0f;
            if (f.Life <= 0f)
            {
                if (f.IsShot) Boom(p, 0.5f, Prims.Hex("#ff3d5a"));
                Destroy(f.T.gameObject);
                m_Fx.RemoveAt(i);
            }
        }
    }
}
