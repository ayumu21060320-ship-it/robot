using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 巨大ロボット1体ぶんの状態。計算はすべてホスト（サーバー）が行い、
/// 結果を NetworkVariable / NetworkList で全員に配る。
/// 操縦士の入力は CrewPlayer の Stick / ActHeld / ActCount から読む。
/// </summary>
public class RobotState : NetworkBehaviour
{
    public static RobotState Instance { get; private set; }

    public const int Playing = 0, Won = 1, Lost = 2;
    public const float MaxHull = 100f, MaxPower = 100f;

    /// <summary>タイタン（味方の巨大ロボット）の大きさ。見た目（RobotVisual）と当たり判定の両方に掛ける</summary>
    public const float TitanScale = 1.8f;

    // ---- 巨大ロボットらしい重い動き（ゆっくり加速・ゆっくり止まる）
    const float WalkSpeedMax = 3.4f;     // 両脚レバー全開での歩く速さ（m/秒）
    const float TurnSpeedMax = 0.22f;    // 旋回の速さ（ラジアン/秒）
    const float WalkAccel = 0.9f;        // 速さが変わる早さ（m/秒²）
    const float LeverRate = 0.45f;       // レバーを倒す速さ（1秒で 45%）
    const float ArmAimRate = 0.55f, HeadAimRate = 0.7f;
    public const float PunchTime = 1.4f; // パンチの腕振り全体の時間（見た目と合わせる）
    const float PunchHitDelay = 0.6f;    // 振りかぶってから当たるまで
    const float PunchCooldown = 2.2f, PunchCooldownBroken = 4.5f;
    const float PunchDamage = 12f;

    // ---- 巨大な敵（1体だけ）
    public const float BossMaxHp = 320f;
    public const float BossRadius = 11f;           // 当たり判定の半径（m）
    public static readonly Vector2 BossStart = new Vector2(0f, 150f);
    const float BossWalkSpeed = 2.3f, BossTurnSpeed = 0.5f, BossReach = 38f;
    public const float BossSwipeWindup = 1.8f;    // 腕を振りかぶっている時間（見た目と合わせる）
    public const int BossIdle = 0, BossSwipe = 1, BossBarrage = 2, BossStomp = 3;

    // ---- 全員に配る状態
    public NetworkVariable<Vector2> Pos = new NetworkVariable<Vector2>();
    public NetworkVariable<float> Yaw = new NetworkVariable<float>();          // ラジアン。0 で +z 向き、増えると右回り
    public NetworkVariable<float> Hull = new NetworkVariable<float>(MaxHull);
    public NetworkVariable<float> Power = new NetworkVariable<float>(MaxPower);
    public NetworkVariable<float> LegL = new NetworkVariable<float>();         // -0.5 ～ 1
    public NetworkVariable<float> LegR = new NetworkVariable<float>();
    public NetworkVariable<float> AimL = new NetworkVariable<float>();         // ラジアン。+ で右へ
    public NetworkVariable<float> AimR = new NetworkVariable<float>();
    public NetworkVariable<float> AimHead = new NetworkVariable<float>();
    public NetworkVariable<int> PunchL = new NetworkVariable<int>();           // パンチするたびに +1（見た目の合図）
    public NetworkVariable<int> PunchR = new NetworkVariable<int>();
    public NetworkVariable<int> GameState = new NetworkVariable<int>(Playing);
    public NetworkVariable<int> Score = new NetworkVariable<int>();
    public NetworkVariable<float> Elapsed = new NetworkVariable<float>();
    public NetworkVariable<float> MoveSpeed = new NetworkVariable<float>();     // 今の歩く速さ（脚のアニメーション用）
    public NetworkVariable<float> TurnRate = new NetworkVariable<float>();

    // ---- 巨大な敵
    public NetworkVariable<Vector2> BossPos = new NetworkVariable<Vector2>();
    public NetworkVariable<float> BossYaw = new NetworkVariable<float>();
    public NetworkVariable<float> BossHp = new NetworkVariable<float>(BossMaxHp);
    public NetworkVariable<float> BossSpeed = new NetworkVariable<float>();
    public NetworkVariable<int> BossAction = new NetworkVariable<int>();       // 今の攻撃の種類
    public NetworkVariable<int> BossActionId = new NetworkVariable<int>();     // 攻撃を始めるたびに +1（見た目の合図）

    public NetworkList<int> Occupant;   // 席ごとの操縦士（clientId + 1、空席は 0）
    public NetworkList<int> FireHp;     // 席ごとの火災（0 = 燃えていない、残り消火回数）
    public NetworkList<int> TaskBroken; // 整備タスクごとに 1 = 故障中

    RobotVisual m_Visual;

    // ---- ホストだけが持つ計算用の情報
    readonly float[] m_Cooldown = new float[Stations.Count];
    readonly float[] m_PunchHitAt = { -1f, -1f };
    readonly Dictionary<ulong, int> m_LastAct = new Dictionary<ulong, int>();
    readonly List<Bolt> m_Bolts = new List<Bolt>();
    readonly List<Shot> m_Shots = new List<Shot>();
    float m_TaskBreakT;
    float m_Speed, m_Turn;
    // 敵の行動
    float m_BossCd, m_BossActT;
    int m_BossShotsLeft;
    float m_BossShotT;
    bool m_BossHitDone;
    struct Bolt { public Vector2 P; public Vector2 D; public float Life; }
    struct Shot { public Vector3 P; public Vector3 V; public float Life; }

    void Awake()
    {
        Occupant = new NetworkList<int>();
        FireHp = new NetworkList<int>();
        TaskBroken = new NetworkList<int>();
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (IsServer) ResetGame();
        m_Visual = gameObject.AddComponent<RobotVisual>();
        m_Visual.Init(this);
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
    }

    public bool TaskOk(int i) => i >= TaskBroken.Count || TaskBroken[i] == 0;

    public int TasksBroken
    {
        get { int n = 0; for (int i = 0; i < TaskBroken.Count; i++) if (TaskBroken[i] != 0) n++; return n; }
    }

    public bool BossAlive => BossHp.Value > 0f;

    // ================================================================ 操縦士からの依頼（RPC）

    [Rpc(SendTo.Server)]
    public void RequestStationRpc(int index, RpcParams rpcParams = default)
    {
        ulong id = rpcParams.Receive.SenderClientId;
        var p = GetPlayer(id);
        if (p == null || GameState.Value != Playing) return;
        if (index < 0 || index >= Stations.Count) return;
        if (Occupant[index] != 0 || FireHp[index] > 0 || p.Station.Value >= 0) return;
        if (Vector2.Distance(p.Pos.Value, Stations.All[index].Pos) > CrewPlayer.UseRange + 0.6f) return;
        Occupant[index] = (int)id + 1;
        p.Station.Value = index;
        m_LastAct[id] = p.ActCount.Value;
    }

    [Rpc(SendTo.Server)]
    public void LeaveStationRpc(RpcParams rpcParams = default)
    {
        var p = GetPlayer(rpcParams.Receive.SenderClientId);
        if (p != null) Unseat(p);
    }

    [Rpc(SendTo.Server)]
    public void RepairRpc(int index, RpcParams rpcParams = default)
    {
        var p = GetPlayer(rpcParams.Receive.SenderClientId);
        if (p == null || index < 0 || index >= Stations.Count || FireHp[index] <= 0) return;
        if (Vector2.Distance(p.Pos.Value, Stations.All[index].Pos) > CrewPlayer.UseRange + 0.6f) return;
        FireHp[index] = FireHp[index] - 1;
    }

    /// <summary>整備タスクのミニゲームをやり終えた</summary>
    [Rpc(SendTo.Server)]
    public void CompleteTaskRpc(int index, RpcParams rpcParams = default)
    {
        var p = GetPlayer(rpcParams.Receive.SenderClientId);
        if (p == null || index < 0 || index >= TaskBroken.Count || TaskBroken[index] == 0) return;
        if (Vector2.Distance(p.Pos.Value, Tasks.All[index].Pos) > CrewPlayer.UseRange + 0.8f) return;
        TaskBroken[index] = 0;
        Score.Value += 50;
    }

    [Rpc(SendTo.Server)]
    public void RestartRpc()
    {
        if (GameState.Value != Playing) ResetGame();
    }

    // ================================================================ 見た目の合図（全員へ）

    [Rpc(SendTo.ClientsAndHost)]
    void BoltFxRpc(Vector3 from, float yaw) { if (m_Visual) m_Visual.SpawnBolt(from, yaw); }

    [Rpc(SendTo.ClientsAndHost)]
    void ShotFxRpc(Vector3 from, Vector3 velocity) { if (m_Visual) m_Visual.SpawnShot(from, velocity); }

    [Rpc(SendTo.ClientsAndHost)]
    void BoomFxRpc(Vector3 at, float size, Color color) { if (m_Visual) m_Visual.Boom(at, size, color); }

    [Rpc(SendTo.ClientsAndHost)]
    void HitFxRpc() { if (m_Visual) m_Visual.Hit(); }

    // ================================================================ ホストの計算

    void ResetGame()
    {
        Pos.Value = Vector2.zero;
        Yaw.Value = 0f;
        Hull.Value = MaxHull;
        Power.Value = MaxPower;
        LegL.Value = LegR.Value = 0f;
        AimL.Value = AimR.Value = AimHead.Value = 0f;
        GameState.Value = Playing;
        Score.Value = 0;
        Elapsed.Value = 0f;
        m_Speed = m_Turn = 0f;
        MoveSpeed.Value = TurnRate.Value = 0f;

        BossPos.Value = BossStart;
        BossYaw.Value = Mathf.PI;          // ロボットのほう（-z）を向いて始まる
        BossHp.Value = BossMaxHp;
        BossSpeed.Value = 0f;
        BossAction.Value = BossIdle;
        m_BossCd = 6f;
        m_BossActT = 0f;

        Occupant.Clear();
        FireHp.Clear();
        for (int i = 0; i < Stations.Count; i++) { Occupant.Add(0); FireHp.Add(0); m_Cooldown[i] = 0f; }
        TaskBroken.Clear();
        for (int i = 0; i < Tasks.Count; i++) TaskBroken.Add(0);
        m_TaskBreakT = 15f;
        m_Bolts.Clear();
        m_Shots.Clear();
        m_PunchHitAt[0] = m_PunchHitAt[1] = -1f;
        foreach (var p in CrewPlayer.All) p.Station.Value = -1;
    }

    CrewPlayer GetPlayer(ulong clientId)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var c) || c.PlayerObject == null) return null;
        return c.PlayerObject.GetComponent<CrewPlayer>();
    }

    void Unseat(CrewPlayer p)
    {
        int st = p.Station.Value;
        if (st >= 0 && st < Occupant.Count && Occupant[st] == (int)p.OwnerClientId + 1) Occupant[st] = 0;
        p.Station.Value = -1;
    }

    Vector2 LocalToWorld(float lx, float lz)
    {
        float yaw = Yaw.Value, c = Mathf.Cos(yaw), s = Mathf.Sin(yaw);
        return Pos.Value + new Vector2(c, -s) * lx + new Vector2(s, c) * lz;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer || GameState.Value != Playing) return;
        float dt = Time.deltaTime;
        Elapsed.Value += dt;

        float power = Power.Value;
        float hull = Hull.Value;
        float legL = LegL.Value, legR = LegR.Value;
        float aimL = AimL.Value, aimR = AimR.Value, aimH = AimHead.Value;

        // ---- 操縦席ごとの入力
        for (int i = 0; i < Stations.Count; i++)
        {
            m_Cooldown[i] -= dt;
            int occ = Occupant[i];
            if (occ == 0) continue;
            ulong id = (ulong)(occ - 1);
            var p = GetPlayer(id);
            if (p == null || p.Station.Value != i) { Occupant[i] = 0; continue; }

            Vector2 stick = p.Stick.Value;
            int count = p.ActCount.Value;
            int last = m_LastAct.TryGetValue(id, out var l) ? l : count;
            bool pressed = count != last;
            m_LastAct[id] = count;
            bool held = p.ActHeld.Value;

            var def = Stations.All[i];
            switch (def.Type)
            {
                case StationType.Leg:
                {
                    float lever = i == Stations.LeftLeg ? legL : legR;
                    // 油圧が壊れていると、レバーを全部は倒せない
                    float max = TaskOk(i == Stations.LeftLeg ? Tasks.GaugeL : Tasks.GaugeR) ? 1f : 0.4f;
                    lever = Mathf.Clamp(lever + stick.y * LeverRate * dt, -0.5f, max);
                    if (Mathf.Abs(stick.y) < 0.1f && Mathf.Abs(lever) < 0.05f) lever = 0f;
                    if (pressed) lever = 0f;
                    if (i == Stations.LeftLeg) legL = lever; else legR = lever;
                    break;
                }
                case StationType.Arm:
                {
                    int k = i == Stations.LeftArm ? 0 : 1;
                    float aim = Mathf.Clamp((k == 0 ? aimL : aimR) + stick.x * ArmAimRate * dt, -0.8f, 0.8f);
                    if (k == 0) aimL = aim; else aimR = aim;
                    if (pressed && m_Cooldown[i] <= 0f && power >= 4f)
                    {
                        // 配線が壊れていると、次のパンチまでが長い
                        m_Cooldown[i] = TaskOk(k == 0 ? Tasks.WiresL : Tasks.WiresR) ? PunchCooldown : PunchCooldownBroken;
                        power -= 4f;
                        m_PunchHitAt[k] = PunchHitDelay;
                        if (k == 0) PunchL.Value++; else PunchR.Value++;
                    }
                    break;
                }
                case StationType.Head:
                {
                    aimH = Mathf.Clamp(aimH + stick.x * HeadAimRate * dt, -1.7f, 1.7f);
                    // ヒューズが切れていると撃てない
                    if (held && m_Cooldown[i] <= 0f && power >= 1.2f && TaskOk(Tasks.Fuse))
                    {
                        m_Cooldown[i] = 0.3f;
                        power -= 1.2f;
                        FireBolt(aimH);
                    }
                    break;
                }
                case StationType.Core:
                {
                    if (pressed) power += 4f;
                    power += 2.5f * dt;
                    break;
                }
            }
        }

        // ---- 火災（その席は使えず、装甲が少しずつ減る）
        for (int i = 0; i < Stations.Count; i++)
        {
            if (FireHp[i] <= 0) continue;
            hull -= 0.5f * dt;
            if (i == Stations.LeftLeg) legL = Mathf.MoveTowards(legL, 0f, 0.5f * dt);
            if (i == Stations.RightLeg) legR = Mathf.MoveTowards(legR, 0f, 0.5f * dt);
        }

        // ---- 整備タスクの故障
        m_TaskBreakT -= dt;
        if (m_TaskBreakT <= 0f)
        {
            BreakRandomTask();
            m_TaskBreakT = Random.Range(16f, 28f);
        }
        // 油圧の故障で、すでに倒れているレバーも戻される
        if (!TaskOk(Tasks.GaugeL)) legL = Mathf.Min(legL, 0.4f);
        if (!TaskOk(Tasks.GaugeR)) legR = Mathf.Min(legR, 0.4f);
        if (!TaskOk(Tasks.Valve)) power -= 1.5f * dt;   // 冷却が止まって動力が漏れる
        if (!TaskOk(Tasks.Vent)) hull -= 0.4f * dt;     // 熱がこもって装甲が傷む

        // ---- 歩行（左右の脚レバーの平均で前進、差で旋回）
        // 重いので、レバーを倒してもすぐには速くならず、戻してもすぐには止まらない
        float pf = power > 0f ? 1f : 0f;
        float targetSpeed = (legL + legR) * 0.5f * WalkSpeedMax * pf;
        float targetTurn = (legL - legR) * TurnSpeedMax * pf;
        // ジャイロが壊れていると、歩いているあいだ左右にふらつく
        if (!TaskOk(Tasks.Gyro) && Mathf.Abs(targetSpeed) > 0.3f) targetTurn += Mathf.Sin(Elapsed.Value * 0.9f) * 0.12f;
        m_Speed = Mathf.MoveTowards(m_Speed, targetSpeed, WalkAccel * dt);
        m_Turn = Mathf.MoveTowards(m_Turn, targetTurn, 0.15f * dt);
        float yaw = Yaw.Value + m_Turn * dt;
        Yaw.Value = yaw;
        var pos = Pos.Value;
        var np = pos + new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * m_Speed * dt;
        bool Blocked(Vector2 q) => ExteriorWorld.Blocked(q, BossPos.Value, BossAlive);
        if (!Blocked(np)) pos = np;
        else if (!Blocked(new Vector2(np.x, pos.y))) pos.x = np.x;
        else if (!Blocked(new Vector2(pos.x, np.y))) pos.y = np.y;
        else m_Speed = 0f; // ぶつかったら止まる
        Pos.Value = pos;
        MoveSpeed.Value = m_Speed;
        TurnRate.Value = m_Turn;
        power -= ((Mathf.Abs(legL) + Mathf.Abs(legR)) * 0.9f * pf + 0.12f) * dt;

        LegL.Value = legL; LegR.Value = legR;
        AimL.Value = aimL; AimR.Value = aimR; AimHead.Value = aimH;
        Power.Value = Mathf.Clamp(power, 0f, MaxPower);
        Hull.Value = Mathf.Clamp(hull, 0f, MaxHull);

        // ---- パンチの当たり判定（腕を振り切ったタイミング）
        for (int k = 0; k < 2; k++)
        {
            if (m_PunchHitAt[k] < 0f) continue;
            m_PunchHitAt[k] -= dt;
            if (m_PunchHitAt[k] <= 0f) { m_PunchHitAt[k] = -1f; PunchHit(k == 0 ? -1 : 1, k == 0 ? aimL : aimR); }
        }

        UpdateBolts(dt);
        UpdateBoss(dt);
        UpdateShots(dt);

        if (Hull.Value <= 0f) GameState.Value = Lost;
    }

    void FireBolt(float aimH)
    {
        float yaw = Yaw.Value + aimH;
        if (!TaskOk(Tasks.Scope)) yaw += Random.Range(-0.3f, 0.3f); // 照準がずれている
        // 頭の右側の砲身の先（RobotVisual の頭の組み立てと合わせてある）
        float c = Mathf.Cos(aimH), sn = Mathf.Sin(aimH);
        var start = LocalToWorld((1.05f * c + 1.2f * sn) * TitanScale, (0.25f - 1.05f * sn + 1.2f * c) * TitanScale);
        m_Bolts.Add(new Bolt { P = start, D = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)), Life = 2.4f });
        BoltFxRpc(new Vector3(start.x, 18.0f * TitanScale, start.y), yaw);
    }

    void PunchHit(int side, float aim)
    {
        // 肩の関節から、腕を前に伸ばしたときの拳の位置（大きさに合わせて拡大）
        float reach = 8.4f * TitanScale, shoulder = 3.75f * TitanScale;
        var fist = LocalToWorld(side * shoulder + Mathf.Sin(aim) * reach, Mathf.Cos(aim) * reach);
        if (BossAlive && Vector2.Distance(BossPos.Value, fist) < BossRadius + 4f)
        {
            DamageBoss(PunchDamage);
            BoomFxRpc(new Vector3(fist.x, 15.4f * TitanScale, fist.y), 1.6f, new Color(1f, 0.82f, 0.25f));
        }
    }

    void UpdateBolts(float dt)
    {
        for (int b = m_Bolts.Count - 1; b >= 0; b--)
        {
            var bolt = m_Bolts[b];
            bolt.P += bolt.D * 75f * dt;
            bolt.Life -= dt;
            if (BossAlive && Vector2.Distance(BossPos.Value, bolt.P) < BossRadius)
            {
                DamageBoss(1f);
                bolt.Life = 0f;
                BoomFxRpc(new Vector3(bolt.P.x, 26f, bolt.P.y), 0.5f, new Color(0.56f, 0.96f, 1f));
            }
            foreach (var bd in ExteriorWorld.Buildings)
                if (bolt.Life > 0f && Vector2.Distance(bd.Pos, bolt.P) < bd.Radius && bd.Size.y > 25f) bolt.Life = 0f;
            if (bolt.Life <= 0f) m_Bolts.RemoveAt(b); else m_Bolts[b] = bolt;
        }
    }

    // ================================================================ 巨大な敵の行動

    /// <summary>
    /// 敵はゆっくりタイタンに近づき、近くまで来たら止まって攻撃する。
    /// - 振り下ろし（近いとき）：長く振りかぶってから腕を振り下ろす。離れればよけられる
    /// - 光弾の連射（遠いとき）：口から光の弾を4発
    /// - 足踏み（たまに）：地面を揺らして装甲を少し削る
    /// </summary>
    void UpdateBoss(float dt)
    {
        if (!BossAlive) return;
        var bp = BossPos.Value;
        var to = Pos.Value - bp;
        float dist = to.magnitude;
        float want = Mathf.Atan2(to.x, to.y);

        // 行動中
        if (BossAction.Value != BossIdle)
        {
            m_BossActT += dt;
            BossSpeed.Value = 0f;
            BossYaw.Value = Mathf.MoveTowardsAngle(BossYaw.Value * Mathf.Rad2Deg, want * Mathf.Rad2Deg, 10f * dt) * Mathf.Deg2Rad;
            switch (BossAction.Value)
            {
                case BossSwipe:
                    if (!m_BossHitDone && m_BossActT >= BossSwipeWindup)
                    {
                        m_BossHitDone = true;
                        if (dist < BossReach) { RobotHit(14f); if (Random.value < 0.5f) StartFire(); }
                        var tip = bp + new Vector2(Mathf.Sin(BossYaw.Value), Mathf.Cos(BossYaw.Value)) * 22f;
                        BoomFxRpc(new Vector3(tip.x, 8f, tip.y), 2.2f, new Color(1f, 0.4f, 0.6f));
                    }
                    if (m_BossActT >= BossSwipeWindup + 1.2f) EndBossAction();
                    break;
                case BossBarrage:
                    m_BossShotT -= dt;
                    if (m_BossShotsLeft > 0 && m_BossShotT <= 0f)
                    {
                        m_BossShotsLeft--;
                        m_BossShotT = 0.55f;
                        var mouth = new Vector3(bp.x, 36f, bp.y) + new Vector3(Mathf.Sin(BossYaw.Value), 0, Mathf.Cos(BossYaw.Value)) * 8f;
                        var target = new Vector3(Pos.Value.x + Random.Range(-4f, 4f), 22f, Pos.Value.y + Random.Range(-4f, 4f));
                        var v = (target - mouth).normalized * 30f;
                        m_Shots.Add(new Shot { P = mouth, V = v, Life = 6f });
                        ShotFxRpc(mouth, v);
                    }
                    if (m_BossShotsLeft <= 0 && m_BossShotT <= -0.8f) EndBossAction();
                    break;
                case BossStomp:
                    if (!m_BossHitDone && m_BossActT >= 1.4f)
                    {
                        m_BossHitDone = true;
                        BoomFxRpc(new Vector3(bp.x, 1f, bp.y), 3.5f, new Color(0.7f, 0.6f, 0.5f));
                        if (dist < 70f) RobotHit(6f);
                    }
                    if (m_BossActT >= 2.6f) EndBossAction();
                    break;
            }
            return;
        }

        // 移動：タイタンのほうを向いて、ゆっくり歩いて近づく
        BossYaw.Value = Mathf.MoveTowardsAngle(BossYaw.Value * Mathf.Rad2Deg, want * Mathf.Rad2Deg, BossTurnSpeed * Mathf.Rad2Deg * dt) * Mathf.Deg2Rad;
        float speed = dist > BossReach - 6f ? BossWalkSpeed : 0f;
        var fwd = new Vector2(Mathf.Sin(BossYaw.Value), Mathf.Cos(BossYaw.Value));
        var nb = bp + fwd * speed * dt;
        if (Vector2.Distance(nb, Pos.Value) > BossRadius + ExteriorWorld.RobotRadius && Mathf.Abs(nb.x) < ExteriorWorld.Bounds && Mathf.Abs(nb.y) < ExteriorWorld.Bounds) bp = nb;
        BossPos.Value = bp;
        BossSpeed.Value = speed;

        // 攻撃を選ぶ
        m_BossCd -= dt;
        if (m_BossCd > 0f || dist > 120f) return;
        int kind = dist < BossReach ? (Random.value < 0.7f ? BossSwipe : BossStomp) : (Random.value < 0.75f ? BossBarrage : BossStomp);
        BossAction.Value = kind;
        BossActionId.Value++;
        m_BossActT = 0f;
        m_BossHitDone = false;
        m_BossShotsLeft = 4;
        m_BossShotT = 0.9f;
    }

    void EndBossAction()
    {
        BossAction.Value = BossIdle;
        m_BossCd = Random.Range(4.5f, 7.5f);
    }

    void DamageBoss(float dmg)
    {
        if (!BossAlive) return;
        BossHp.Value = Mathf.Max(0f, BossHp.Value - dmg);
        Score.Value += Mathf.RoundToInt(dmg * 10f);
        if (BossHp.Value > 0f) return;
        var bp = BossPos.Value;
        BoomFxRpc(new Vector3(bp.x, 20f, bp.y), 6f, new Color(1f, 0.24f, 0.5f));
        Score.Value += 3000 + Mathf.Max(0, 3000 - Mathf.FloorToInt(Elapsed.Value) * 5);
        GameState.Value = Won;
    }

    void UpdateShots(float dt)
    {
        var rp = Pos.Value;
        for (int s = m_Shots.Count - 1; s >= 0; s--)
        {
            var shot = m_Shots[s];
            shot.P += shot.V * dt;
            shot.Life -= dt;
            if (Vector2.Distance(new Vector2(shot.P.x, shot.P.z), rp) < ExteriorWorld.RobotRadius && shot.P.y < 19f * TitanScale)
            {
                shot.Life = 0f;
                RobotHit(5f);
            }
            if (shot.P.y < 0f) shot.Life = 0f;
            if (shot.Life <= 0f) m_Shots.RemoveAt(s); else m_Shots[s] = shot;
        }
    }

    void RobotHit(float dmg)
    {
        Hull.Value = Mathf.Max(0f, Hull.Value - dmg);
        HitFxRpc();
        if (Random.value < 0.33f) StartFire();
        if (Random.value < 0.2f) BreakRandomTask();
    }

    void StartFire()
    {
        var free = new List<int>();
        for (int i = 0; i < Stations.Count; i++) if (FireHp[i] <= 0) free.Add(i);
        if (free.Count == 0) return;
        int st = free[Random.Range(0, free.Count)];
        FireHp[st] = 5;
        int occ = Occupant[st];
        if (occ != 0)
        {
            var p = GetPlayer((ulong)(occ - 1));
            if (p != null) Unseat(p); else Occupant[st] = 0;
        }
    }

    void BreakRandomTask()
    {
        var ok = new List<int>();
        for (int i = 0; i < TaskBroken.Count; i++) if (TaskBroken[i] == 0) ok.Add(i);
        if (ok.Count == 0) return;
        TaskBroken[ok[Random.Range(0, ok.Count)]] = 1;
    }

}
