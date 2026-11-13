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

    /// <summary>巨大な敵の頭のすぐ上の位置（体力ゲージを出す場所）。敵がいなければ null</summary>
    public Vector3? BossOverhead => m_Boss != null && m_Boss.gameObject.activeSelf && m_BossHead != null
        ? m_BossHead.position + Vector3.up * 10f
        : (Vector3?)null;

    RobotState m_State;
    Transform m_Body, m_Upper, m_Head;
    readonly Transform[] m_Hip = new Transform[2], m_Knee = new Transform[2], m_Foot = new Transform[2], m_Arm = new Transform[2];
    readonly Transform[] m_Elbow = new Transform[2], m_Hand = new Transform[2], m_Thumb = new Transform[2];
    readonly Transform[,] m_Finger = new Transform[2, 4];
    float m_Recoil, m_HitReact, m_HitSide = 1f;
    readonly float[] m_SparkT = new float[2];
    readonly float[] m_LegSparkT = new float[2];
    readonly float[] m_GuardBlend = new float[2];   // ガードの構えの度合い（0～1）

    // 必殺技：胸の灯りがためで強く光り、ビームを撃つ
    Renderer m_ChestLight;
    Color m_ChestBase;
    int m_LastSpecialId;
    float m_ChargeT = -1f;
    readonly List<Beam> m_Beams = new List<Beam>();
    class Beam { public Transform Core, Glow; public float Life, Max, Width = 6f; }

    // ビルが崩れるアニメーション（-1 = 立っている）
    float[] m_BldFall;

    /// <summary>腕1本の姿勢（角度は度）</summary>
    struct ArmPose
    {
        public float Pitch, Roll, Elbow, Curl, Twist, Lean, Drop;
        public static ArmPose Lerp(ArmPose a, ArmPose b, float t) => new ArmPose
        {
            Pitch = Mathf.LerpUnclamped(a.Pitch, b.Pitch, t), Roll = Mathf.LerpUnclamped(a.Roll, b.Roll, t),
            Elbow = Mathf.LerpUnclamped(a.Elbow, b.Elbow, t), Curl = Mathf.LerpUnclamped(a.Curl, b.Curl, t),
            Twist = Mathf.LerpUnclamped(a.Twist, b.Twist, t), Lean = Mathf.LerpUnclamped(a.Lean, b.Lean, t),
            Drop = Mathf.LerpUnclamped(a.Drop, b.Drop, t),
        };
    }
    GameObject m_Sight;
    readonly float[] m_Phase = new float[2];
    readonly float[] m_PunchT = { -1f, -1f };
    readonly int[] m_LastPunch = new int[2];
    readonly int[] m_StepSign = { 1, 1 };
    Vector2 m_Pos;
    float m_Yaw;

    // 巨大な敵
    Transform m_Boss, m_BossBody, m_BossTorso, m_BossHead, m_BossJaw, m_BossTail;
    readonly Transform[] m_BossTailSeg = new Transform[4];
    float m_BossFlinch, m_BossFlinchSide = 1f, m_BossDeadT = -1f, m_BossLook, m_BossWalkAmt;
    readonly Transform[] m_BossHip = new Transform[2], m_BossKnee = new Transform[2], m_BossArm = new Transform[2], m_BossForearm = new Transform[2];
    Renderer m_BossCore, m_BossMouth;
    Vector2 m_BossPos;
    float m_BossYaw, m_BossPhase, m_BossT = -1f, m_BossLastHp, m_BossFlash;
    int m_BossLastId, m_BossKind;
    float m_BossCharge, m_BossStunAmt, m_StunStarT;
    /// <summary>敵が今の攻撃を始めてからの時間（攻撃していなければ -1）。HUD の残り時間に使う</summary>
    public float BossActionTime => m_BossT;
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
        BuildZones();
        BuildRockets();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Root) Destroy(Root.gameObject);
        if (m_FxRoot) Destroy(m_FxRoot.gameObject);
        if (m_Boss) Destroy(m_Boss.gameObject);
    }

    // ---------------------------------------------------------------- 組み立て

    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

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

        // ---------------- 上半身（腰から上をまとめて、ひねったり前かがみになったりする）
        m_Upper = Prims.Pivot(m_Body, new Vector3(0, 12f, 0), "上半身");
        Prims.Box(m_Upper, new Vector3(0, 0.55f, 0), new Vector3(2.6f, 1.6f, 2.0f), frame, false, "腰");
        Prims.Box(m_Upper, new Vector3(0, 2.9f, 0), new Vector3(6.0f, 3.2f, 3.0f), red, false, "胸");
        for (int k = -1; k <= 1; k += 2)
            Plate(m_Upper, new Vector3(k * 2.3f, 1.55f, 0f), new Vector3(1.6f, 0.35f, 2.8f), red, new Vector3(0, 0, k * 34f), false, "胸の下の面取り");
        Prims.Box(m_Upper, new Vector3(0, 4.55f, -0.1f), new Vector3(7.2f, 0.5f, 3.1f), redHi, false, "胸の上の板");
        Prims.Box(m_Upper, new Vector3(0, 1.4f, 0.2f), new Vector3(2.4f, 1.2f, 2.4f), red, false, "みぞおち");
        for (int k = -1; k <= 1; k += 2)
        {
            Prims.Box(m_Upper, new Vector3(k * 1.6f, 2.6f, 1.52f), new Vector3(1.7f, 1.9f, 0.1f), frame, false, "吸気口");
            for (int g = 0; g < 4; g++)
                Prims.Box(m_Upper, new Vector3(k * 1.6f, 2.0f + g * 0.4f, 1.58f), new Vector3(1.5f, 0.1f, 0.06f), dark);
        }
        m_ChestLight = Prims.Box(m_Upper, new Vector3(0, 3.5f, 1.53f), new Vector3(1.0f, 0.5f, 0.08f), yellow, true, "胸の灯り").GetComponent<Renderer>();
        m_ChestBase = yellow;
        Prims.Box(m_Upper, new Vector3(0, 2.8f, -2.0f), new Vector3(4.2f, 3.0f, 1.4f), dark, false, "背中");
        for (int k = -1; k <= 1; k += 2)
            Cyl(m_Upper, new Vector3(k * 1.3f, 3.6f, -2.4f), 0.45f, 2.6f, frame, 1, false, "排気筒");

        // ---------------- 首と頭（小さめ・アンテナ2本）
        Cyl(m_Upper, new Vector3(0, 4.95f, 0.1f), 0.45f, 0.9f, frame, 1, false, "首");
        m_Head = Prims.Pivot(m_Upper, new Vector3(0, 5.4f, 0.25f), "頭");
        Prims.Box(m_Head, new Vector3(0, 0.75f, 0), new Vector3(1.9f, 1.6f, 1.9f), white, false, "頭");
        Plate(m_Head, new Vector3(0, 1.65f, -0.1f), new Vector3(1.7f, 0.4f, 1.7f), whiteHi, new Vector3(-8f, 0, 0), false, "頭のてっぺん");
        Prims.Box(m_Head, new Vector3(0, 0.25f, 0.95f), new Vector3(1.1f, 0.6f, 0.2f), white, false, "口もと");
        Prims.Box(m_Head, new Vector3(0, 0.95f, 0.97f), new Vector3(1.6f, 0.32f, 0.1f), glow, true, "目");
        Prims.Box(m_Head, new Vector3(0, 1.95f, 0.5f), new Vector3(0.25f, 0.6f, 0.6f), red, false, "とさか");
        for (int k = -1; k <= 1; k += 2)
            Plate(m_Head, new Vector3(k * 0.65f, 2.35f, 0f), new Vector3(0.08f, 1.6f, 0.08f), whiteHi, new Vector3(0, 0, -k * 8f), false, "アンテナ");
        Cyl(m_Head, new Vector3(1.05f, 0.6f, 0.6f), 0.18f, 1.0f, frame, 2, false, "頭部砲");
        m_Sight = Prims.Box(m_Head, new Vector3(1.05f, 0.6f, 36f), new Vector3(0.3f, 0.3f, 70f), glow, true, "照準線");

        // ---------------- 腕：肩 → 上腕 → ひじ（曲がる）→ 前腕 → 手首 → 手（指が曲がって拳になる）
        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            Cyl(m_Upper, new Vector3(s * 3.75f, 3.4f, 0f), 1.05f, 1.3f, yellow, 0, false, "肩の関節");
            var arm = Prims.Pivot(m_Upper, new Vector3(s * 3.75f, 3.4f, 0f), k == 0 ? "左腕" : "右腕");
            arm.localRotation = Quaternion.identity;
            Prims.Box(arm, new Vector3(0, -1.9f, 0), new Vector3(1.25f, 2.9f, 1.3f), white, false, "上腕");

            var elbow = Prims.Pivot(arm, new Vector3(0, -3.35f, 0), "ひじ");
            Cyl(elbow, Vector3.zero, 0.62f, 1.4f, frame, 0, false, "ひじの関節");
            Prims.Box(elbow, new Vector3(0, -1.55f, 0.05f), new Vector3(2.0f, 2.3f, 1.9f), white, false, "前腕");
            Prims.Box(elbow, new Vector3(0, -1.55f, 1.0f), new Vector3(1.5f, 1.6f, 0.06f), whiteHi);
            Prims.Box(elbow, new Vector3(s * 1.2f, -1.35f, 0.05f), new Vector3(0.3f, 3.1f, 2.3f), yellow, false, "前腕の装甲板");
            Prims.Box(elbow, new Vector3(0, -2.9f, 0.05f), new Vector3(0.9f, 0.5f, 0.9f), frame, false, "手首");

            var hand = Prims.Pivot(elbow, new Vector3(0, -3.2f, 0.1f), "手");
            Prims.Box(hand, new Vector3(0, -0.45f, 0.05f), new Vector3(1.6f, 1.25f, 0.85f), white, false, "手のひら");
            for (int f = 0; f < 4; f++)
            {
                float fx = -0.6f + f * 0.4f;
                float len = f == 1 || f == 2 ? 1.0f : 0.85f;
                var finger = Prims.Pivot(hand, new Vector3(fx, -1.05f, 0.05f), "指");
                Prims.Box(finger, new Vector3(0, -0.25f, 0.05f), new Vector3(0.32f, 0.5f, 0.38f), white, false, "指の根元");
                Prims.Box(finger, new Vector3(0, -0.5f, 0.05f), new Vector3(0.2f, 0.12f, 0.3f), frame);
                Prims.Box(finger, new Vector3(0, -0.55f - len * 0.4f, 0.07f), new Vector3(0.28f, len * 0.7f, 0.34f), white, false, "指先");
                m_Finger[k, f] = finger;
            }
            var thumb = Prims.Pivot(hand, new Vector3(-s * 0.8f, -0.3f, 0.3f), "親指");
            Plate(thumb, new Vector3(-s * 0.15f, -0.35f, 0f), new Vector3(0.32f, 0.9f, 0.38f), white, new Vector3(0, 0, s * 25f), false, "親指");
            m_Arm[k] = arm; m_Elbow[k] = elbow; m_Hand[k] = hand; m_Thumb[k] = thumb;
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
            // 油圧が切れた脚は動かない：まっすぐ突っ張って小刻みに震え、ももから火花が散る
            if (!s.LegWorks(i))
            {
                float jit = Mathf.Sin(Time.time * 23f + i * 2f) * 1.2f;
                m_Hip[i].localRotation = Quaternion.Slerp(m_Hip[i].localRotation, Quaternion.Euler(jit, 0, 0), k);
                m_Knee[i].localRotation = Quaternion.Slerp(m_Knee[i].localRotation, Quaternion.Euler(2f, 0, 0), k);
                m_Foot[i].localRotation = Quaternion.Slerp(m_Foot[i].localRotation, Quaternion.identity, k);
                m_LegSparkT[i] -= dt;
                if (m_LegSparkT[i] <= 0f)
                {
                    m_LegSparkT[i] = Random.Range(0.25f, 0.9f);
                    Boom(m_Hip[i].position + Vector3.down * 1.5f, 0.35f, new Color(1f, 0.92f, 0.5f));
                }
                continue;
            }
            float legSpeed = s.MoveSpeed.Value + (i == 0 ? 1f : -1f) * s.TurnRate.Value * 4f;
            float lev = legSpeed / RobotState.WalkSpeedMax;
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
        float walk = Mathf.Clamp01(Mathf.Abs(s.MoveSpeed.Value) / (RobotState.WalkSpeedMax * 0.7f));
        float stride = (m_Phase[0] + m_Phase[1]) * 0.5f;
        float bob = moving ? Mathf.Abs(Mathf.Sin(stride)) * 0.25f : 0f;
        float breathe = Mathf.Sin(Time.time * 1.3f);

        // ---- 腕：パンチの回数が増えたら、振りかぶり → 突き出し → ため → 戻し
        int[] punches = { s.PunchL.Value, s.PunchR.Value };
        float[] aims = { s.AimL.Value, s.AimR.Value };
        var pose = new ArmPose[2];
        float twist = Mathf.Sin(m_Phase[0]) * 5f * walk;    // 歩くと上半身が脚と逆にひねれる
        float lean = 3f * walk + breathe * 0.6f;
        float drop = 0f;
        bool[] punching = new bool[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            if (punches[i] != m_LastPunch[i]) { m_LastPunch[i] = punches[i]; m_PunchT[i] = 0f; }

            // ふだん：腕は少し外へ開き、ひじを軽く曲げて、歩くと脚と逆に振る
            var idle = new ArmPose
            {
                Pitch = -4f - Mathf.Sin(m_Phase[1 - i]) * 16f * walk + breathe,
                Roll = side * 6f,
                Elbow = -16f - 14f * walk - Mathf.Max(0f, Mathf.Sin(m_Phase[1 - i])) * 12f * walk,
                Curl = 25f,
            };
            pose[i] = idle;
            if (m_PunchT[i] < 0f) continue;

            m_PunchT[i] += dt / RobotState.PunchTime;
            float t = m_PunchT[i];
            punching[i] = true;
            // 振りかぶった形：腕を後ろへ引いてひじを大きく曲げ、拳を握り、上半身を反対へひねる
            var wind = new ArmPose { Pitch = 34f, Roll = side * 22f, Elbow = -118f, Curl = 100f, Twist = side * 24f, Lean = -5f, Drop = 0.15f };
            // 打ち抜いた形：腕を前にまっすぐ伸ばし、上半身を打つ側へひねって前のめり
            var strike = new ArmPose { Pitch = -94f, Roll = side * 2f, Elbow = -2f, Curl = 105f, Twist = -side * 28f, Lean = 11f, Drop = 0.45f };
            if (t < 0.3f) pose[i] = ArmPose.Lerp(idle, wind, Smooth(t / 0.3f));
            else if (t < 0.43f) pose[i] = ArmPose.Lerp(wind, strike, 1f - Mathf.Pow(1f - (t - 0.3f) / 0.13f, 3f));
            else if (t < 0.56f)
            {
                // 当たった瞬間：少し行き過ぎてから戻る（衝撃）
                float e = (t - 0.43f) / 0.13f;
                var over = strike; over.Pitch -= 6f; over.Lean += 3f;
                pose[i] = ArmPose.Lerp(over, strike, e);
                if (t - dt / RobotState.PunchTime < 0.43f) Shake = Mathf.Max(Shake, 0.6f);
            }
            else if (t < 1f) pose[i] = ArmPose.Lerp(strike, idle, Smooth((t - 0.56f) / 0.44f));
            else { m_PunchT[i] = -1f; pose[i] = idle; punching[i] = false; }
        }
        // 肩の配線が切れている腕は、力が抜けてだらりと垂れ下がり、肩から火花が散る
        for (int i = 0; i < 2; i++)
        {
            if (s.TaskOk(i == 0 ? Tasks.WiresL : Tasks.WiresR)) continue;
            float side = i == 0 ? -1f : 1f;
            m_PunchT[i] = -1f;
            punching[i] = false;
            pose[i] = new ArmPose { Pitch = 6f + Mathf.Sin(Time.time * 1.7f + i) * 4f * (0.4f + walk), Roll = side * 1f, Elbow = -2f, Curl = 4f };
            m_SparkT[i] -= dt;
            if (m_SparkT[i] <= 0f)
            {
                m_SparkT[i] = Random.Range(0.25f, 0.9f);
                Boom(m_Arm[i].position, 0.35f, new Color(1f, 0.92f, 0.5f));
            }
        }
        // ガード：両腕を顔と胸の前で交差させ、前腕の装甲板を敵に向ける
        bool[] guarding = { s.GuardL.Value, s.GuardR.Value };
        for (int i = 0; i < 2; i++)
        {
            m_GuardBlend[i] = Mathf.MoveTowards(m_GuardBlend[i], guarding[i] ? 1f : 0f, dt * 5f);
            if (m_GuardBlend[i] <= 0f) continue;
            float side = i == 0 ? -1f : 1f;
            var block = new ArmPose { Pitch = -62f, Roll = -side * 14f, Elbow = -118f, Curl = 100f, Lean = 3f, Drop = 0.25f };
            pose[i] = ArmPose.Lerp(pose[i], block, Smooth(m_GuardBlend[i]));
        }
        // パンチしていない腕は、顔の前で構える（ガード）
        for (int i = 0; i < 2; i++)
        {
            int o = 1 - i;
            if (!punching[o] || punching[i]) continue;
            float side = i == 0 ? -1f : 1f;
            float g = Mathf.Clamp01(Mathf.Min(m_PunchT[o] / 0.2f, (1f - m_PunchT[o]) / 0.3f));
            var guard = new ArmPose { Pitch = -48f, Roll = side * 10f, Elbow = -105f, Curl = 95f };
            pose[i] = ArmPose.Lerp(pose[i], guard, Smooth(g));
        }
        for (int i = 0; i < 2; i++)
        {
            twist += pose[i].Twist; lean += pose[i].Lean; drop = Mathf.Max(drop, pose[i].Drop);
            m_Arm[i].localRotation = Quaternion.Euler(pose[i].Pitch, aims[i] * Mathf.Rad2Deg, pose[i].Roll);
            m_Elbow[i].localRotation = Quaternion.Euler(pose[i].Elbow, 0, 0);
            m_Hand[i].localRotation = Quaternion.Euler(-pose[i].Curl * 0.15f, 0, 0);
            for (int f = 0; f < 4; f++) m_Finger[i, f].localRotation = Quaternion.Euler(-pose[i].Curl * (0.9f + f * 0.03f), 0, 0);
            float side = i == 0 ? -1f : 1f;
            m_Thumb[i].localRotation = Quaternion.Euler(-pose[i].Curl * 0.4f, 0, side * pose[i].Curl * 0.35f);
        }

        // ---- 攻撃を受けたとき：上半身がのけぞる。頭部砲を撃つと頭が反動で跳ねる
        m_HitReact = Mathf.Max(0f, m_HitReact - dt * 2.2f);
        m_Recoil = Mathf.Max(0f, m_Recoil - dt * 7f);
        float h = m_HitReact * m_HitReact;
        lean -= 12f * h;
        float roll = 7f * h * m_HitSide + Mathf.Sin(stride) * 2f * walk;

        // パンチの踏ん張り：体を沈めてひざを曲げる
        for (int i = 0; i < 2; i++)
            if (drop > 0.01f)
            {
                m_Knee[i].localRotation *= Quaternion.Euler(drop * 22f, 0, 0);
                m_Hip[i].localRotation *= Quaternion.Euler(-drop * 12f, 0, 0);
                m_Foot[i].localRotation *= Quaternion.Euler(-drop * 10f, 0, 0);
            }
        m_Body.localPosition = new Vector3(0, Mathf.Lerp(m_Body.localPosition.y, bob - drop * 0.6f, k), 0);
        m_Upper.localRotation = Quaternion.Euler(Mathf.Clamp(lean, -20f, 25f), Mathf.Clamp(twist, -35f, 35f), roll);

        // ---- 頭と照準線（頭部砲に誰か座っているときだけ表示）。上半身のひねりを少し打ち消して前を向く
        m_Head.localRotation = Quaternion.Euler(-9f * m_Recoil - 6f * h + Mathf.Sin(stride * 2f) * 1.5f * walk, s.AimHead.Value * Mathf.Rad2Deg - twist * 0.6f, 0);
        m_Sight.SetActive(s.Occupant.Count > Stations.Head && s.Occupant[Stations.Head] != 0);

        UpdateSpecial(dt);
        UpdateBuffs(dt);
        UpdateRocketVisuals(dt);
        UpdateBuildings(dt);
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
        m_BossBody = Prims.Pivot(m_Boss, Vector3.zero, "体");   // 歩くときの上下・左右の揺れはここに掛ける
        Prims.Box(m_BossBody, new Vector3(0, 13.5f, -1f), new Vector3(12f, 6f, 9f), hull, false, "腰");

        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            var hip = Prims.Pivot(m_BossBody, new Vector3(s * 5.2f, 13.5f, -1f), "脚");
            Prims.Box(hip, new Vector3(0, -3.6f, 0.4f), new Vector3(5.2f, 7.6f, 6.2f), plate, false, "もも");
            var knee = Prims.Pivot(hip, new Vector3(0, -7.2f, 0.6f), "ひざ");
            Prims.Box(knee, new Vector3(0, -2.9f, -0.6f), new Vector3(4.4f, 6.2f, 5f), hull, false, "すね");
            Prims.Box(knee, new Vector3(0, -6.2f, 1.6f), new Vector3(7f, 1.6f, 10f), plate, false, "足");
            for (int c = -1; c <= 1; c++)
                Plate(knee, new Vector3(c * 2.4f, -6.4f, 7f), new Vector3(1.1f, 1.1f, 3f), bone, new Vector3(20f, 0, 0), false, "足の爪");
            m_BossHip[k] = hip; m_BossKnee[k] = knee;
        }

        m_BossTorso = Prims.Pivot(m_BossBody, new Vector3(0, 16f, -1f), "胴");
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
        m_BossJaw = Prims.Pivot(m_BossHead, new Vector3(0, -0.2f, 1.0f), "あご");
        Prims.Box(m_BossJaw, new Vector3(0, -1.0f, 3.0f), new Vector3(6.6f, 2.2f, 6f), hull, false, "あご");
        for (int t = -1; t <= 1; t++)
            Prims.Box(m_BossJaw, new Vector3(t * 1.8f, 0.2f, 5.6f), new Vector3(0.6f, 0.9f, 0.6f), bone, false, "牙");
        m_BossMouth = Prims.Box(m_BossHead, new Vector3(0, 0.3f, 7.05f), new Vector3(5f, 0.9f, 0.3f), glowC, true, "口").GetComponent<Renderer>();
        for (int k = -1; k <= 1; k += 2)
        {
            Prims.Box(m_BossHead, new Vector3(k * 2f, 3.2f, 6.55f), new Vector3(1.8f, 0.8f, 0.2f), eye, true, "目");
            Plate(m_BossHead, new Vector3(k * 3.4f, 6.8f, 0.5f), new Vector3(1.3f, 7.5f, 1.3f), bone, new Vector3(-25f, 0, -k * 28f), false, "角");
        }

        // しっぽ
        m_BossTail = Prims.Pivot(m_BossBody, new Vector3(0, 13f, -5f), "しっぽ");
        var seg = m_BossTail;
        for (int i = 0; i < 4; i++)
        {
            float w = 5f - i * 1.1f;
            Prims.Box(seg, new Vector3(0, 0, -2.6f), new Vector3(w, w * 0.8f, 5.6f), i % 2 == 0 ? hull : plate, false, "しっぽ");
            m_BossTailSeg[i] = seg;
            var next = Prims.Pivot(seg, new Vector3(0, -0.6f, -5.2f), "しっぽ");
            seg = next;
        }
    }

    void UpdateBoss(float dt)
    {
        var s = m_State;
        bool alive = s.BossAlive;

        // ---- 倒されたとき：前に倒れ込んで地面に沈み、しばらくして消える
        if (!alive)
        {
            // 倒れた敵の攻撃の範囲は消す
            if (m_SwipeZone != null && m_SwipeZone.gameObject.activeSelf) m_SwipeZone.gameObject.SetActive(false);
            if (m_CircleZone != null && m_CircleZone.gameObject.activeSelf) m_CircleZone.gameObject.SetActive(false);
            m_BossT = -1f; m_BossCharge = 0f; m_BossStunAmt = 0f;
            if (!m_Boss.gameObject.activeSelf) return;
            if (m_BossDeadT < 0f) m_BossDeadT = 0f;
            m_BossDeadT += dt;
            float t = m_BossDeadT;
            float fall = Mathf.Clamp01(t / 2.2f);
            fall = fall * fall;                                  // だんだん速く倒れる
            m_BossBody.localRotation = Quaternion.Euler(fall * 80f, 0, Mathf.Sin(t * 3f) * 4f * (1f - fall));
            m_BossBody.localPosition = new Vector3(0, -Mathf.Clamp01((t - 2.2f) / 3f) * 14f, fall * 6f);
            m_BossTorso.localRotation = Quaternion.Euler(18f + fall * 20f, 0, 0);
            m_BossHead.localRotation = Quaternion.Euler(-20f * (1f - fall) + 30f * fall, 0, 0);
            m_BossJaw.localRotation = Quaternion.Euler(30f * (1f - fall), 0, 0);
            for (int i = 0; i < 2; i++) m_BossArm[i].localRotation = Quaternion.Euler(Mathf.Lerp(-18f, -70f, fall), 0, (i == 0 ? -1f : 1f) * 20f * fall);
            if (t >= 2.2f && t - dt < 2.2f) Shake = Mathf.Max(Shake, 1.6f); // 地面に倒れた衝撃
            Prims.SetColor(m_BossCore, Prims.Hex("#ff3d7f") * Mathf.Clamp01(1f - t / 2f));
            Prims.SetColor(m_BossMouth, Color.black);
            if (t > 6f) m_Boss.gameObject.SetActive(false);
            return;
        }
        if (m_BossDeadT >= 0f || !m_Boss.gameObject.activeSelf)
        {
            // もう一度出撃したとき：立った姿に戻す
            m_BossDeadT = -1f;
            m_Boss.gameObject.SetActive(true);
            m_BossBody.localRotation = Quaternion.identity;
            m_BossBody.localPosition = Vector3.zero;
            m_BossPos = s.BossPos.Value;
        }

        float k = 1f - Mathf.Exp(-6f * dt);
        m_BossPos = Vector2.Lerp(m_BossPos, s.BossPos.Value, k);
        m_BossYaw = Mathf.LerpAngle(m_BossYaw * Mathf.Rad2Deg, s.BossYaw.Value * Mathf.Rad2Deg, k) * Mathf.Deg2Rad;
        m_Boss.SetPositionAndRotation(new Vector3(m_BossPos.x, 0, m_BossPos.y), Quaternion.Euler(0, m_BossYaw * Mathf.Rad2Deg, 0));
        float time = Time.time;

        // ---- 歩き：大きな歩幅でゆっくり。体が上下・左右に揺れ、腕は脚と逆に振る
        float sp = s.BossSpeed.Value;
        m_BossWalkAmt = Mathf.MoveTowards(m_BossWalkAmt, Mathf.Min(1f, sp / RobotState.BossWalkSpeed), dt * 1.5f);
        float w = m_BossWalkAmt;
        float prev = Mathf.Sin(m_BossPhase);
        m_BossPhase += sp * dt * 0.32f;
        float ph = m_BossPhase;
        float amp = w * 22f;
        for (int i = 0; i < 2; i++)
        {
            float lp = ph + i * Mathf.PI;
            m_BossHip[i].localRotation = Quaternion.Euler(-Mathf.Sin(lp) * amp, 0, 0);
            m_BossKnee[i].localRotation = Quaternion.Euler(Mathf.Max(0f, Mathf.Cos(lp)) * amp * 1.3f, 0, 0);
        }
        if (sp > 0.1f && Mathf.Sign(prev) != Mathf.Sign(Mathf.Sin(ph)) && Vector2.Distance(m_BossPos, m_Pos) < 160f)
            Shake = Mathf.Max(Shake, 0.45f);
        float breathe = Mathf.Sin(time * 1.1f);
        float bob = -Mathf.Abs(Mathf.Sin(ph)) * 0.9f * w + breathe * 0.15f;
        float roll = Mathf.Sin(ph) * 3.5f * w;

        // ---- 頭はいつもタイタンのほうを見る
        var toTitan = m_Pos - m_BossPos;
        float rel = Mathf.DeltaAngle(m_BossYaw * Mathf.Rad2Deg, Mathf.Atan2(toTitan.x, toTitan.y) * Mathf.Rad2Deg);
        m_BossLook = Mathf.LerpAngle(m_BossLook, Mathf.Clamp(rel, -40f, 40f), 1f - Mathf.Exp(-3f * dt));

        // ---- ふだんの姿勢（攻撃中は下で上書きする）
        float armR = -18f + Mathf.Sin(ph) * 14f * w + breathe * 2f, foreR = -6f * w;
        float armL = -18f - Mathf.Sin(ph) * 14f * w + breathe * 2f, foreL = -6f * w;
        float torsoX = 18f + 4f * w + breathe * 2f;
        float headX = Mathf.Sin(ph * 2f) * 2f * w - breathe * 1.5f;
        float jaw = 4f + Mathf.Sin(time * 0.7f) * 3f;
        float lift = 0f, mouth = 0.3f + jaw / 40f;

        // ---- 攻撃のアニメーション（ホストが攻撃を始めた合図で始める）
        if (s.BossActionId.Value != m_BossLastId)
        {
            m_BossLastId = s.BossActionId.Value;
            m_BossKind = s.BossAction.Value;
            m_BossT = 0f;
        }
        if (m_BossT >= 0f)
        {
            m_BossT += dt;
            float t = m_BossT;
            switch (m_BossKind)
            {
                case RobotState.BossSwipe:
                {
                    float wu = RobotState.BossSwipeWindup;
                    if (t < wu)
                    {
                        float e = t / wu;
                        armR = Mathf.Lerp(-18f, -150f, e * e); foreR = -30f * e;
                        armL = -18f + 25f * e;                    // 反対の腕は後ろへ引いて勢いをつける
                        torsoX = 18f - 10f * e; headX = -12f * e; jaw = 4f + 18f * e; roll = -6f * e;
                    }
                    else if (t < wu + 0.35f)
                    {
                        float e = (t - wu) / 0.35f;
                        armR = Mathf.Lerp(-150f, 20f, e); foreR = Mathf.Lerp(-30f, 10f, e);
                        armL = Mathf.Lerp(7f, -30f, e);
                        torsoX = Mathf.Lerp(8f, 32f, e); headX = Mathf.Lerp(-12f, 10f, e); jaw = 22f; roll = Mathf.Lerp(-6f, 8f, e);
                    }
                    else if (t < wu + 1.2f)
                    {
                        float e = (t - wu - 0.35f) / 0.85f;
                        armR = Mathf.Lerp(20f, -18f, e); foreR = Mathf.Lerp(10f, 0f, e);
                        armL = Mathf.Lerp(-30f, -18f, e);
                        torsoX = Mathf.Lerp(32f, 18f, e); headX = Mathf.Lerp(10f, 0f, e); jaw = Mathf.Lerp(22f, 4f, e); roll = Mathf.Lerp(8f, 0f, e);
                    }
                    else m_BossT = -1f;
                    break;
                }
                case RobotState.BossBarrage:
                {
                    // のけぞってから、1発ごとに頭とあごを前へ突き出す（ホストの発射間隔 0.9 秒後から 0.55 秒ごと）
                    float rear = Mathf.Clamp01(t / 0.8f);
                    torsoX = 18f - 10f * rear; headX = -20f * rear; jaw = 10f + 10f * rear;
                    armR = armL = -30f * rear;
                    if (t > 0.9f)
                    {
                        float since = (t - 0.9f) % 0.55f;
                        float jolt = Mathf.Exp(-since * 9f);
                        headX = -20f + 26f * jolt; jaw = 14f + 22f * jolt; torsoX = 8f + 6f * jolt;
                        mouth = 0.6f + jolt;
                    }
                    if (s.BossAction.Value != RobotState.BossBarrage && t > 1f) m_BossT = -1f;
                    break;
                }
                case RobotState.BossMega:
                {
                    // 大技：大きくのけぞって両腕を広げ、口に光をためる → 前へ突き出して光を放つ
                    float ct = RobotState.MegaChargeTime;
                    if (s.BossAction.Value != RobotState.BossMega && t < ct) { m_BossT = -1f; break; }   // 止められた
                    if (t < ct)
                    {
                        float e = Mathf.Clamp01(t / 1.2f), c = t / ct;
                        torsoX = 18f - 26f * e; headX = -32f * e + Mathf.Sin(time * 25f) * 1.5f * c; jaw = 6f + 28f * e;
                        armR = armL = -18f - 95f * e; foreR = foreL = -45f * e; roll = Mathf.Sin(time * 9f) * 2.5f * c;
                        mouth = 0.6f + c * 1.6f;
                        m_BossCharge = c;
                        if (Random.value < dt * (4f + c * 14f)) Trail(m_BossJaw.position + Random.insideUnitSphere * 3f, Prims.Hex("#ff6ab0"), 1.2f);
                    }
                    else if (t < ct + 0.35f)
                    {
                        float e = (t - ct) / 0.35f;
                        torsoX = Mathf.Lerp(-8f, 38f, e); headX = Mathf.Lerp(-32f, 22f, e); jaw = 34f; mouth = 2.2f;
                        armR = armL = Mathf.Lerp(-113f, 10f, e); foreR = foreL = Mathf.Lerp(-45f, 0f, e);
                        m_BossCharge = 1f - e;
                    }
                    else if (t < ct + 1.5f)
                    {
                        float e = (t - ct - 0.35f) / 1.15f;
                        torsoX = Mathf.Lerp(38f, 18f, e); headX = Mathf.Lerp(22f, 0f, e); jaw = Mathf.Lerp(34f, 4f, e);
                        armR = armL = Mathf.Lerp(10f, -18f, e); m_BossCharge = 0f;
                    }
                    else { m_BossT = -1f; m_BossCharge = 0f; }
                    if (t >= ct && t - dt < ct) SpawnLineBeam(m_BossJaw.position, new Vector3(s.BossTarget.Value.x, 0f, s.BossTarget.Value.y), 6f, Prims.Hex("#ff4fa0"));
                    break;
                }
                case RobotState.BossStomp:
                {
                    // 両腕を上げて体を反らし、片足を高く上げてから踏み下ろす
                    if (t < 1.4f)
                    {
                        float e = Mathf.Sin(t / 1.4f * Mathf.PI * 0.5f);
                        lift = 32f * e; torsoX = 18f - 12f * e; armR = armL = -18f - 70f * e; headX = -16f * e; jaw = 4f + 20f * e; roll = 5f * e;
                    }
                    else if (t < 1.6f)
                    {
                        float e = (t - 1.4f) / 0.2f;
                        lift = Mathf.Lerp(32f, 0f, e); torsoX = Mathf.Lerp(6f, 30f, e); armR = armL = Mathf.Lerp(-88f, 0f, e); headX = Mathf.Lerp(-16f, 12f, e); roll = Mathf.Lerp(5f, 0f, e);
                        bob -= 1.2f * e;
                    }
                    else if (t < 2.6f)
                    {
                        float e = (t - 1.6f) / 1.0f;
                        torsoX = Mathf.Lerp(30f, 18f, e); armR = armL = Mathf.Lerp(0f, -18f, e); headX = Mathf.Lerp(12f, 0f, e); bob -= 1.2f * (1f - e);
                    }
                    else m_BossT = -1f;
                    if (t >= 1.55f && t - dt < 1.55f && Vector2.Distance(m_BossPos, m_Pos) < 160f) Shake = Mathf.Max(Shake, 1.4f);
                    break;
                }
            }
        }

        // ---- チャンスタイム：大技を止められて、前に崩れ落ちてふらふらしている
        bool stunned = s.BossStun.Value > 0f;
        m_BossStunAmt = Mathf.MoveTowards(m_BossStunAmt, stunned ? 1f : 0f, dt * (stunned ? 2.5f : 1f));
        if (stunned && m_BossT >= 0f && m_BossKind != RobotState.BossMega) m_BossT = -1f;
        if (m_BossStunAmt > 0f)
        {
            float a = Smooth(m_BossStunAmt);
            float sway = Mathf.Sin(time * 1.6f);
            torsoX = Mathf.Lerp(torsoX, 58f + sway * 3f, a);
            headX = Mathf.Lerp(headX, 30f, a);
            jaw = Mathf.Lerp(jaw, 22f, a);
            armL = Mathf.Lerp(armL, 35f + sway * 6f, a); armR = Mathf.Lerp(armR, 35f - sway * 6f, a);
            foreL = Mathf.Lerp(foreL, 20f, a); foreR = Mathf.Lerp(foreR, 20f, a);
            roll = Mathf.Lerp(roll, sway * 7f, a);
            bob -= 5f * a;
            mouth *= 1f - a;
            if (stunned)
            {
                m_StunStarT -= dt;
                if (m_StunStarT <= 0f)
                {
                    m_StunStarT = 0.25f;
                    float ang = time * 4f;
                    Trail(m_BossHead.position + new Vector3(Mathf.Cos(ang) * 6f, 6f, Mathf.Sin(ang) * 6f), Prims.Hex("#ffe14a"), 1.6f);
                }
            }
        }

        // ---- 攻撃を受けたら、体をのけぞらせる
        float hpNow = s.BossHp.Value;
        if (hpNow < m_BossLastHp)
        {
            float big = m_BossLastHp - hpNow >= 5f ? 1f : 0.35f;   // パンチは大きく、砲撃は小さく
            m_BossFlinch = Mathf.Max(m_BossFlinch, big);
            m_BossFlinchSide = Random.value < 0.5f ? -1f : 1f;
            m_BossFlash = 1f;
        }
        m_BossLastHp = hpNow;
        m_BossFlinch = Mathf.Max(0f, m_BossFlinch - dt * 2.5f);
        float f = m_BossFlinch * m_BossFlinch;
        torsoX -= 14f * f;
        headX -= 18f * f;
        roll += 6f * f * m_BossFlinchSide;
        jaw += 18f * f;

        // ---- 反映
        m_BossBody.localPosition = new Vector3(0, bob, 0);
        m_BossBody.localRotation = Quaternion.Euler(0, 0, roll);
        m_BossTorso.localRotation = Quaternion.Euler(torsoX, 0, 0);
        m_BossHead.localRotation = Quaternion.Euler(headX, m_BossLook, 0);
        m_BossJaw.localRotation = Quaternion.Euler(jaw, 0, 0);
        m_BossArm[0].localRotation = Quaternion.Euler(armL, 0, 0);
        m_BossArm[1].localRotation = Quaternion.Euler(armR, 0, 0);
        m_BossForearm[0].localRotation = Quaternion.Euler(foreL, 0, 0);
        m_BossForearm[1].localRotation = Quaternion.Euler(foreR, 0, 0);
        if (lift > 0f)
        {
            m_BossHip[0].localRotation = Quaternion.Euler(-lift, 0, 0);
            m_BossKnee[0].localRotation = Quaternion.Euler(lift * 1.2f, 0, 0);
        }

        // しっぽ：根元から先へ波が伝わるように揺らす（歩くと大きく）
        float tailAmp = 7f + 7f * w;
        for (int i = 0; i < m_BossTailSeg.Length; i++)
        {
            float wave = Mathf.Sin(time * (0.9f + w * 0.8f) - i * 0.8f);
            m_BossTailSeg[i].localRotation = Quaternion.Euler(i == 0 ? -4f : 12f + Mathf.Sin(time * 0.6f - i) * 3f, wave * tailAmp, 0);
        }

        // ---- コアの脈動と、ダメージを受けたときの白い点滅
        m_BossFlash = Mathf.Max(0f, m_BossFlash - dt * 4f);
        float pulse = 0.75f + Mathf.Sin(time * 2.4f) * 0.25f;
        Prims.SetColor(m_BossCore, Color.Lerp(Prims.Hex("#ff3d7f") * pulse, Color.white, m_BossFlash));
        if (m_BossCharge > 0f) Prims.SetColor(m_BossCore, Color.Lerp(Prims.Hex("#ff3d7f"), Color.white, m_BossCharge * (0.6f + 0.4f * Mathf.Sin(time * 30f))));
        if (m_BossStunAmt > 0f) Prims.SetColor(m_BossCore, Color.Lerp(Prims.Hex("#ff3d7f") * 0.4f, Prims.Hex("#ffe14a"), 0.5f + 0.5f * Mathf.Sin(time * 8f)));
        m_BossCore.transform.localScale = Vector3.one * (4.2f + m_BossFlash * 0.8f + m_BossCharge * 2.5f);
        UpdateZones(s);
        Prims.SetColor(m_BossMouth, Prims.Hex("#ff3d7f") * Mathf.Clamp01(mouth));
    }

    // ---------------------------------------------------------------- 演出

    public void SpawnBolt(Vector3 from, float yaw)
    {
        m_Recoil = 1f;
        // 太めの光の弾：外側の水色の光と、少し長くはみ出す白い芯
        var b = Prims.Box(m_FxRoot, from, new Vector3(1.6f, 1.6f, 8f), Prims.Hex("#8ff6ff"), true, "Bolt");
        b.transform.rotation = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
        var core = Prims.Box(b.transform, Vector3.zero, Vector3.one, Color.white, true, "Bolt core");
        core.transform.localScale = new Vector3(0.55f, 0.55f, 1.15f);
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

    // ---------------------------------------------------------------- エネルギーセルの強化

    int m_LastBuffId = -1;
    float m_BuffFlash, m_BoostSparkT;

    void UpdateBuffs(float dt)
    {
        var s = m_State;
        if (m_LastBuffId < 0) m_LastBuffId = s.BuffId.Value;
        if (s.BuffId.Value != m_LastBuffId)
        {
            // セルが入った：胸からその色の光がはじける
            m_LastBuffId = s.BuffId.Value;
            m_BuffFlash = 1f;
            var c = EnergyCells.Color(s.BuffKind.Value);
            if (m_ChestLight != null) Boom(m_ChestLight.transform.position, 1.3f, c);
        }
        if (m_BuffFlash > 0f && m_ChargeT < 0f && m_ChestLight != null)
        {
            m_BuffFlash = Mathf.Max(0f, m_BuffFlash - dt * 1.2f);
            Prims.SetColor(m_ChestLight, Color.Lerp(m_ChestBase, EnergyCells.Color(s.BuffKind.Value), m_BuffFlash));
        }
        // パンチ強化中：両方の拳から赤い火花
        if (s.PunchBoost.Value > 0f)
        {
            m_BoostSparkT -= dt;
            if (m_BoostSparkT <= 0f)
            {
                m_BoostSparkT = 0.35f;
                for (int i = 0; i < 2; i++) Boom(m_Hand[i].position, 0.3f, EnergyCells.Color(EnergyCells.Red));
            }
        }
    }

    // ---------------------------------------------------------------- 敵の攻撃の範囲（地面の赤い印）

    Transform m_SwipeZone, m_SwipeFill, m_CircleZone, m_CircleFill;
    Renderer m_SwipeEdgeR, m_CircleEdgeR;

    /// <summary>
    /// 半径 1 の扇形（deg 度、正面が +Z）を作る。inner は内側の半径（0 なら中まで塗る、0.94 などなら縁だけ）。
    /// edges = true なら扇の左右の辺も線で描く。
    /// </summary>
    static Mesh SectorMesh(float inner, float deg, int seg, bool edges)
    {
        var v = new List<Vector3>();
        var t = new List<int>();
        float half = deg * 0.5f;
        for (int i = 0; i <= seg; i++)
        {
            float a = (-half + deg * i / seg) * Mathf.Deg2Rad;
            var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            v.Add(d * inner); v.Add(d);
        }
        for (int i = 0; i < seg; i++)
        {
            int b = i * 2;
            t.AddRange(new[] { b, b + 1, b + 3, b, b + 3, b + 2 });
        }
        if (edges)
        {
            foreach (float sgn in new[] { -1f, 1f })
            {
                float a = sgn * half * Mathf.Deg2Rad;
                var d = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var n = new Vector3(Mathf.Cos(a), 0f, -Mathf.Sin(a)) * 0.025f;
                int b = v.Count;
                v.Add(-n); v.Add(d - n); v.Add(d + n); v.Add(n);
                t.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3, b, b + 2, b + 1, b, b + 3, b + 2 });   // 両面
            }
        }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    Transform MeshObject(Transform parent, Mesh mesh, Color c, string name, out Renderer r)
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = Prims.Mat(c, true);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r = mr;
        return g.transform;
    }

    void BuildZones()
    {
        var fillC = Prims.Hex("#5a0d14");
        var edgeC = Prims.Hex("#ff3b3b");
        float arc = RobotState.SwipeArc * 2f;
        m_SwipeZone = Prims.Pivot(m_FxRoot, Vector3.zero, "振り下ろしの範囲");
        m_SwipeFill = MeshObject(m_SwipeZone, SectorMesh(0f, arc, 24, false), fillC, "塗り", out _);
        MeshObject(m_SwipeZone, SectorMesh(0.95f, arc, 24, true), edgeC, "縁", out m_SwipeEdgeR).localPosition = new Vector3(0f, 0.02f, 0f);
        m_CircleZone = Prims.Pivot(m_FxRoot, Vector3.zero, "円の範囲");
        m_CircleFill = MeshObject(m_CircleZone, SectorMesh(0f, 360f, 48, false), fillC, "塗り", out _);
        MeshObject(m_CircleZone, SectorMesh(0.96f, 360f, 48, false), edgeC, "縁", out m_CircleEdgeR).localPosition = new Vector3(0f, 0.02f, 0f);
        m_SwipeZone.gameObject.SetActive(false);
        m_CircleZone.gameObject.SetActive(false);
    }

    /// <summary>攻撃が来る範囲を地面に出す。真ん中から赤く埋まっていき、埋まりきると攻撃が来る</summary>
    void UpdateZones(RobotState s)
    {
        bool swipe = false, circle = false;
        float t = m_BossT;
        if (s.BossAlive && t >= 0f && s.BossStun.Value <= 0f)
        {
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 12f);
            var edge = Prims.Hex("#ff3b3b") * pulse;
            switch (m_BossKind)
            {
                case RobotState.BossSwipe when t < RobotState.BossSwipeWindup:
                {
                    swipe = true;
                    float r = RobotState.SwipeReach;
                    m_SwipeZone.SetPositionAndRotation(new Vector3(m_BossPos.x, 0.12f, m_BossPos.y), Quaternion.Euler(0f, m_BossYaw * Mathf.Rad2Deg, 0f));
                    m_SwipeZone.localScale = Vector3.one * r;
                    float e = t / RobotState.BossSwipeWindup;
                    m_SwipeFill.localScale = new Vector3(e, 1f, e);
                    Prims.SetColor(m_SwipeEdgeR, edge);
                    break;
                }
                case RobotState.BossStomp when t < RobotState.StompWindup:
                {
                    circle = true;
                    m_CircleZone.position = new Vector3(m_BossPos.x, 0.12f, m_BossPos.y);
                    m_CircleZone.localScale = Vector3.one * RobotState.StompRange;
                    float e = t / RobotState.StompWindup;
                    m_CircleFill.localScale = new Vector3(e, 1f, e);
                    Prims.SetColor(m_CircleEdgeR, edge);
                    break;
                }
                case RobotState.BossMega when t < RobotState.MegaChargeTime && s.BossAction.Value == RobotState.BossMega:
                {
                    circle = true;
                    var tg = s.BossTarget.Value;
                    m_CircleZone.position = new Vector3(tg.x, 0.12f, tg.y);
                    m_CircleZone.localScale = Vector3.one * RobotState.MegaRadius;
                    float e = t / RobotState.MegaChargeTime;
                    m_CircleFill.localScale = new Vector3(e, 1f, e);
                    Prims.SetColor(m_CircleEdgeR, Color.Lerp(edge, Color.white, e > 0.8f && Mathf.Sin(Time.time * 30f) > 0f ? 0.6f : 0f));
                    break;
                }
            }
        }
        if (m_SwipeZone.gameObject.activeSelf != swipe) m_SwipeZone.gameObject.SetActive(swipe);
        if (m_CircleZone.gameObject.activeSelf != circle) m_CircleZone.gameObject.SetActive(circle);
    }

    /// <summary>2点を結ぶ太い光（大技の光線など）。細くなって消える</summary>
    void SpawnLineBeam(Vector3 from, Vector3 to, float width, Color color)
    {
        var d = to - from;
        var rot = Quaternion.LookRotation(d.normalized);
        var mid = from + d * 0.5f;
        var core = Prims.Box(m_FxRoot, mid, new Vector3(width * 0.4f, width * 0.4f, d.magnitude), Color.white, true, "光線").transform;
        var glow = Prims.Box(m_FxRoot, mid, new Vector3(width, width, d.magnitude), color, true, "光線の光").transform;
        core.rotation = rot; glow.rotation = rot;
        m_Beams.Add(new Beam { Core = core, Glow = glow, Life = 0.9f, Max = 0.9f, Width = width });
        Shake = Mathf.Max(Shake, 1.4f);
    }

    /// <summary>揺れを起こさない小さな光の粒（飛んでいく拳の噴射・ためている光など）</summary>
    void Trail(Vector3 at, Color color, float size)
    {
        var f = Prims.Box(m_FxRoot, at, Vector3.one * Random.Range(0.4f, 0.9f) * size, color, true, "Trail");
        var v = Random.insideUnitSphere * 3f;
        m_Fx.Add(new Fx { T = f.transform, V = v, Life = Random.Range(0.3f, 0.6f) });
    }

    // ---------------------------------------------------------------- ロケットパンチ

    readonly Transform[] m_Rocket = new Transform[2];
    readonly Transform[] m_RocketFlame = new Transform[2];
    readonly Vector3[] m_RocketShown = new Vector3[2];
    readonly Quaternion[] m_RocketRot = new Quaternion[2];
    readonly bool[] m_RocketWasAway = new bool[2];
    float m_RocketTrailT;

    /// <summary>飛んでいるロケットパンチの拳（カメラが後ろから追いかけるのに使う）。飛んでいなければ null</summary>
    public Transform RocketFist(int k) => m_Rocket[k] != null && m_Rocket[k].gameObject.activeSelf ? m_Rocket[k] : null;

    void BuildRockets()
    {
        float S = RobotState.TitanScale;
        var white = Prims.Hex("#e6e9ee");
        var frame = Prims.Hex("#4a5058");
        var yellow = Prims.Hex("#ffc21a");
        for (int k = 0; k < 2; k++)
        {
            var r = Prims.Pivot(m_FxRoot, Vector3.zero, k == 0 ? "ロケットパンチ（左）" : "ロケットパンチ（右）");
            // 前腕（後ろ）→ 拳（前）。+Z が進む向き
            Prims.Box(r, new Vector3(0, 0, -1.3f) * S, new Vector3(2.0f, 1.9f, 2.6f) * S, white, false, "前腕");
            Prims.Box(r, new Vector3((k == 0 ? -1f : 1f) * 1.15f, 0, -1.2f) * S, new Vector3(0.3f, 2.2f, 2.6f) * S, yellow, false, "装甲板");
            Prims.Box(r, new Vector3(0, 0, 0.6f) * S, new Vector3(1.8f, 1.6f, 1.5f) * S, white, false, "拳");
            for (int f = 0; f < 4; f++)
                Prims.Box(r, new Vector3(-0.6f + f * 0.4f, 0.35f, 1.38f) * S, new Vector3(0.34f, 0.5f, 0.2f) * S, frame, false, "指の節");
            Prims.Box(r, new Vector3(0, 0, -2.7f) * S, new Vector3(1.4f, 1.4f, 0.4f) * S, frame, false, "噴射口");
            m_RocketFlame[k] = Prims.Box(r, new Vector3(0, 0, -3.6f) * S, new Vector3(1.0f, 1.0f, 1.8f) * S, Prims.Hex("#ffb02e"), true, "噴射の炎").transform;
            Prims.Box(m_RocketFlame[k], new Vector3(0, 0, -0.2f), new Vector3(0.55f, 0.55f, 1.2f), Color.white, true, "炎の芯");
            r.gameObject.SetActive(false);
            m_Rocket[k] = r;
        }
    }

    void UpdateRocketVisuals(float dt)
    {
        var s = m_State;
        m_RocketTrailT -= dt;
        bool trail = m_RocketTrailT <= 0f;
        if (trail) m_RocketTrailT = 0.05f;
        for (int k = 0; k < 2; k++)
        {
            bool away = s.ArmAway(k);                                   // 飛んでいる・もうない
            bool flying = s.RocketState(k) == RobotState.RocketFlying;
            // 飛ばした腕は前腕から先がなくなる（試合が終わるまで戻らない）。飛んでいるあいだだけ拳を見せる
            if (m_Elbow[k].gameObject.activeSelf == away) m_Elbow[k].gameObject.SetActive(!away);
            if (m_Rocket[k].gameObject.activeSelf != flying) m_Rocket[k].gameObject.SetActive(flying);
            if (!away) m_RocketWasAway[k] = false;
            if (!flying) continue;
            var target = s.RocketPos(k);
            var rot = Quaternion.Euler(-s.RocketPitch(k) * Mathf.Rad2Deg, s.RocketYaw(k) * Mathf.Rad2Deg, 0f);
            if (!m_RocketWasAway[k])
            {
                // 発射した瞬間：腕の位置から飛び出す
                m_RocketWasAway[k] = true;
                m_RocketShown[k] = m_Elbow[k].position;
                m_RocketRot[k] = rot;
                Boom(m_Elbow[k].position, 1.0f, Prims.Hex("#ffb02e"));
            }
            float kk = 1f - Mathf.Exp(-14f * dt);
            m_RocketShown[k] = Vector3.Lerp(m_RocketShown[k], target, kk);
            m_RocketRot[k] = Quaternion.Slerp(m_RocketRot[k], rot, kk);
            m_Rocket[k].SetPositionAndRotation(m_RocketShown[k], m_RocketRot[k]);
            float fl = 1f + Mathf.Sin(Time.time * 50f + k) * 0.25f;
            m_RocketFlame[k].localScale = new Vector3(1.0f, 1.0f, 1.8f * fl) * RobotState.TitanScale;
            if (trail) Trail(m_RocketFlame[k].position - m_RocketFlame[k].forward * 2f, Prims.Hex("#ffb02e"), 0.9f);
        }
    }

    public void Hit()
    {
        // ガードしていると、のけぞりが小さい
        bool guard = m_State != null && (m_State.GuardL.Value || m_State.GuardR.Value);
        m_HitReact = guard ? 0.4f : 1f;
        m_HitSide = Random.value < 0.5f ? -1f : 1f;
        Shake = Mathf.Max(Shake, 0.7f);
        HitFlash = 1f;
    }

    // ---------------------------------------------------------------- 必殺技

    /// <summary>胸から前へまっすぐ伸びる太いビーム。だんだん細くなって消える</summary>
    public void SpawnBeam(Vector3 from, float yaw, float length)
    {
        if (m_ChestLight != null) from = m_ChestLight.transform.position;
        var rot = Quaternion.Euler(0, yaw * Mathf.Rad2Deg, 0);
        var dir = rot * Vector3.forward;
        var mid = from + dir * (length * 0.5f);
        var core = Prims.Box(m_FxRoot, mid, new Vector3(2.4f, 2.4f, length), Color.white, true, "必殺ビーム").transform;
        var glow = Prims.Box(m_FxRoot, mid, new Vector3(6f, 6f, length), Prims.Hex("#ffd23f"), true, "必殺ビームの光").transform;
        core.rotation = rot; glow.rotation = rot;
        m_Beams.Add(new Beam { Core = core, Glow = glow, Life = 1.3f, Max = 1.3f });
        // 胸の前と、ビームの通り道のあちこちで爆発
        Boom(from + dir * 3f, 1.6f, Prims.Hex("#fff3b0"));
        for (float d = 25f; d < length; d += 30f)
            Boom(new Vector3(from.x + dir.x * d, 2f, from.z + dir.z * d), 1.2f, Prims.Hex("#ffb02e"));
        Shake = Mathf.Max(Shake, 1.6f);
        m_Recoil = 1f;
        m_HitReact = Mathf.Max(m_HitReact, 0.5f);   // 反動で上半身がのけぞる
    }

    void UpdateSpecial(float dt)
    {
        var s = m_State;
        if (s.SpecialId.Value != m_LastSpecialId)
        {
            m_LastSpecialId = s.SpecialId.Value;
            if (s.SpecialCharging.Value) m_ChargeT = 0f;
        }
        if (m_ChargeT >= 0f)
        {
            m_ChargeT += dt;
            float c = Mathf.Clamp01(m_ChargeT / RobotState.SpecialChargeTime);
            float flick = 0.7f + 0.3f * Mathf.Sin(Time.time * (12f + c * 30f));
            if (m_ChestLight != null)
            {
                Prims.SetColor(m_ChestLight, Color.Lerp(m_ChestBase, Color.white, c * flick));
                m_ChestLight.transform.localScale = new Vector3(1.0f + c * 1.6f, 0.5f + c * 1.2f, 0.08f);
            }
            Shake = Mathf.Max(Shake, c * 0.35f);
            if (Random.value < dt * (4f + c * 14f) && m_ChestLight != null)
                Boom(m_ChestLight.transform.position + Random.insideUnitSphere * 2.5f, 0.25f, Prims.Hex("#fff3b0"));
            if (!s.SpecialCharging.Value || m_ChargeT > RobotState.SpecialChargeTime + 1f)
            {
                m_ChargeT = -1f;
                if (m_ChestLight != null)
                {
                    Prims.SetColor(m_ChestLight, m_ChestBase);
                    m_ChestLight.transform.localScale = new Vector3(1.0f, 0.5f, 0.08f);
                }
            }
        }
        else if (m_ChestLight != null && s.Special.Value >= RobotState.SpecialMax)
        {
            // ゲージ満タン：胸の灯りがゆっくり点滅して知らせる
            float p = 0.5f + 0.5f * Mathf.Sin(Time.time * 5f);
            Prims.SetColor(m_ChestLight, Color.Lerp(m_ChestBase, Color.white, p * 0.6f));
        }
        else if (m_ChestLight != null) Prims.SetColor(m_ChestLight, m_ChestBase);

        for (int i = m_Beams.Count - 1; i >= 0; i--)
        {
            var b = m_Beams[i];
            b.Life -= dt;
            float t = Mathf.Clamp01(b.Life / b.Max);
            float w = t * t;
            float cw = b.Width * 0.4f * w + 0.05f, gw = b.Width * w + 0.05f;
            var cs = b.Core.localScale; b.Core.localScale = new Vector3(cw, cw, cs.z);
            var gs = b.Glow.localScale; b.Glow.localScale = new Vector3(gw * (1f + 0.1f * Mathf.Sin(Time.time * 40f)), gw, gs.z);
            if (b.Life <= 0f)
            {
                Destroy(b.Core.gameObject); Destroy(b.Glow.gameObject);
                m_Beams.RemoveAt(i);
            }
        }
    }

    // ---------------------------------------------------------------- ビルの崩壊

    void UpdateBuildings(float dt)
    {
        var objs = ExteriorWorld.BuildingObjects;
        var list = ExteriorWorld.Buildings;
        int n = Mathf.Min(objs.Count, list.Count);
        if (m_BldFall == null || m_BldFall.Length != n)
        {
            m_BldFall = new float[n];
            for (int i = 0; i < n; i++) m_BldFall[i] = -1f;
        }
        for (int i = 0; i < n; i++)
        {
            var t = objs[i];
            if (t == null) continue;
            var b = list[i];
            bool alive = m_State.BuildingAlive(i) || m_State.BuildingHp.Count <= i;
            if (alive)
            {
                if (m_BldFall[i] >= 0f)
                {
                    // リセットで元どおりに建て直す
                    m_BldFall[i] = -1f;
                    t.gameObject.SetActive(true);
                    t.SetPositionAndRotation(new Vector3(b.Pos.x, b.Size.y * 0.5f, b.Pos.y), Quaternion.Euler(0, b.RotY, 0));
                }
                continue;
            }
            if (m_BldFall[i] < 0f)
            {
                m_BldFall[i] = 0f;
                Boom(new Vector3(b.Pos.x, b.Size.y * 0.6f, b.Pos.y), 1.4f, Prims.Hex("#9a8f86"));
            }
            if (m_BldFall[i] > 1.6f) { if (t.gameObject.activeSelf) t.gameObject.SetActive(false); continue; }
            m_BldFall[i] += dt;
            float f = Mathf.Clamp01(m_BldFall[i] / 1.6f);
            float e = f * f;
            // ロボットから遠ざかる向きへ傾きながら、地面に沈んでいく
            var away = b.Pos - m_Pos;
            float side = Vector2.Dot(away, new Vector2(Mathf.Cos(b.RotY * Mathf.Deg2Rad), -Mathf.Sin(b.RotY * Mathf.Deg2Rad))) >= 0f ? -1f : 1f;
            float shiver = Mathf.Sin(m_BldFall[i] * 40f) * (1f - f) * 0.4f;
            t.SetPositionAndRotation(
                new Vector3(b.Pos.x + shiver, b.Size.y * 0.5f - b.Size.y * 0.95f * e, b.Pos.y),
                Quaternion.Euler(0, b.RotY, 0) * Quaternion.Euler(0, 0, side * 18f * e));
            if (Random.value < dt * 6f * (1f - f))
                Boom(new Vector3(b.Pos.x + Random.Range(-b.Size.x, b.Size.x) * 0.5f, 1f, b.Pos.y + Random.Range(-b.Size.z, b.Size.z) * 0.5f), 0.7f, Prims.Hex("#8a7f76"));
        }
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
