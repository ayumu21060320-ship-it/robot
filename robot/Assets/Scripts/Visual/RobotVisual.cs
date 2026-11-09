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

    const float DeckY = 10.5f;

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

    void Build()
    {
        var steel = Prims.Hex("#7a868b");
        var dark = Prims.Hex("#2a3236");
        var amber = Prims.Hex("#ffb000");
        var glow = Prims.Hex("#5fe8ff");

        Root = new GameObject("Robot").transform;
        m_Body = Prims.Pivot(Root, Vector3.zero, "Body");
        m_FxRoot = new GameObject("RobotFx").transform;

        Prims.Box(m_Body, new Vector3(0, 8.4f, -0.3f), new Vector3(7.2f, 1.8f, 4.4f), dark, false, "腰");

        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            var hip = Prims.Pivot(m_Body, new Vector3(s * 3f, 8.4f, -0.3f), k == 0 ? "左脚" : "右脚");
            Prims.Box(hip, new Vector3(0, -2.1f, 0), new Vector3(2.3f, 4.2f, 2.3f), steel);
            Prims.Box(hip, new Vector3(s * 0.9f, -2.1f, 1.2f), new Vector3(1.2f, 2.6f, 0.3f), amber);
            var knee = Prims.Pivot(hip, new Vector3(0, -4.2f, 0), "膝");
            Prims.Box(knee, Vector3.zero, new Vector3(2.6f, 1.2f, 2.6f), dark);
            Prims.Box(knee, new Vector3(0, -1.9f, 0), new Vector3(2f, 3.6f, 2f), steel);
            var foot = Prims.Pivot(knee, new Vector3(0, -3.8f, 0), "足");
            Prims.Box(foot, new Vector3(0, 0, 0.5f), new Vector3(2.8f, 0.8f, 4f), dark);
            Prims.Box(foot, new Vector3(0, 0.2f, 2.4f), new Vector3(2.9f, 0.3f, 0.6f), amber);
            m_Hip[k] = hip; m_Knee[k] = knee; m_Foot[k] = foot;
        }

        // 胴体（中に操縦士たちがいる）
        Prims.Box(m_Body, new Vector3(0, 9.4f, 0), new Vector3(10f, 2.2f, 9.2f), steel, false, "胴体");
        Prims.Box(m_Body, new Vector3(0, 8.45f, 0), new Vector3(10.3f, 0.5f, 9.5f), amber);
        Prims.Box(m_Body, new Vector3(0, DeckY + 1.6f, -0.4f), new Vector3(9.2f, 3.2f, 8.2f), steel, false, "胸");
        Prims.Box(m_Body, new Vector3(0, DeckY + 1.9f, 3.75f), new Vector3(6f, 1.2f, 0.1f), Prims.Hex("#ffd23f"), true, "胸の灯り");
        for (int i = -1; i <= 1; i += 2)
            Prims.Shape(PrimitiveType.Cylinder, m_Body, new Vector3(i * 3.4f, DeckY + 3.8f, -3.9f), new Vector3(1f, 1.5f, 1f), dark, false, "排気筒");

        // 頭
        Prims.Shape(PrimitiveType.Cylinder, m_Body, new Vector3(0, DeckY + 3.4f, 1.6f), new Vector3(1.6f, 0.5f, 1.6f), dark);
        m_Head = Prims.Pivot(m_Body, new Vector3(0, DeckY + 4.8f, 1.8f), "頭");
        Prims.Box(m_Head, Vector3.zero, new Vector3(3.4f, 2.2f, 2.6f), steel);
        Prims.Box(m_Head, new Vector3(0, 1.2f, 0), new Vector3(3.6f, 0.4f, 2.8f), amber);
        Prims.Box(m_Head, new Vector3(0, 0.15f, 1.33f), new Vector3(2.8f, 0.55f, 0.12f), glow, true, "目");
        var barrel = Prims.Shape(PrimitiveType.Cylinder, m_Head, new Vector3(0, -0.5f, 2.2f), new Vector3(0.7f, 1.3f, 0.7f), dark, false, "砲身");
        barrel.transform.localRotation = Quaternion.Euler(90f, 0, 0);
        m_Sight = Prims.Box(m_Head, new Vector3(0, -0.5f, 37f), new Vector3(0.12f, 0.12f, 70f), glow, true, "照準線");

        // 肩と腕
        for (int k = 0; k < 2; k++)
        {
            float s = k == 0 ? -1f : 1f;
            Prims.Box(m_Body, new Vector3(s * 6.3f, DeckY + 1.2f, 1.2f), new Vector3(2.8f, 2.4f, 3.4f), amber, false, "肩");
            var arm = Prims.Pivot(m_Body, new Vector3(s * 6.3f, DeckY + 0.7f, 1.2f), k == 0 ? "左腕" : "右腕");
            Prims.Box(arm, new Vector3(0, -2.2f, 0), new Vector3(1.9f, 4f, 1.9f), steel);
            Prims.Box(arm, new Vector3(0, -4.4f, 0), new Vector3(2.2f, 1f, 2.2f), dark);
            Prims.Box(arm, new Vector3(0, -6.3f, 0), new Vector3(2.3f, 3.6f, 2.3f), steel);
            Prims.Box(arm, new Vector3(0, -9.1f, 0), new Vector3(2.9f, 2.5f, 2.9f), dark, false, "拳");
            Prims.Box(arm, new Vector3(0, -8.2f, 1.3f), new Vector3(3f, 0.5f, 0.4f), amber);
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
