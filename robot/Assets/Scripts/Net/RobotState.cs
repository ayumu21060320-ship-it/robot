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
    public const int MaxDrones = 12;
    public const float MaxHull = 100f, MaxPower = 100f;

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
    public NetworkVariable<int> Kills = new NetworkVariable<int>();
    public NetworkVariable<float> Elapsed = new NetworkVariable<float>();

    public NetworkList<int> Occupant;   // 席ごとの操縦士（clientId + 1、空席は 0）
    public NetworkList<int> FireHp;     // 席ごとの火災（0 = 燃えていない、残り消火回数）
    public NetworkList<int> HiveHp;
    public NetworkList<Vector4> Drones; // x, y, z, hp（hp <= 0 は空き枠）
    public NetworkList<int> TaskBroken; // 整備タスクごとに 1 = 故障中

    RobotVisual m_Visual;

    // ---- ホストだけが持つ計算用の情報
    readonly float[] m_Cooldown = new float[Stations.Count];
    readonly float[] m_PunchHitAt = { -1f, -1f };
    readonly Dictionary<ulong, int> m_LastAct = new Dictionary<ulong, int>();
    readonly float[] m_HiveSpawnT = new float[3];
    readonly DroneSim[] m_Drones = new DroneSim[MaxDrones];
    readonly List<Bolt> m_Bolts = new List<Bolt>();
    readonly List<Shot> m_Shots = new List<Shot>();
    float m_DroneSyncT;
    float m_TaskBreakT;

    class DroneSim { public Vector3 P; public int Hp; public float FireT; public float Orbit; public int Hive; public float Seed; }
    struct Bolt { public Vector2 P; public Vector2 D; public float Life; }
    struct Shot { public Vector3 P; public Vector3 V; public float Life; }

    void Awake()
    {
        Occupant = new NetworkList<int>();
        FireHp = new NetworkList<int>();
        HiveHp = new NetworkList<int>();
        Drones = new NetworkList<Vector4>();
        TaskBroken = new NetworkList<int>();
        for (int i = 0; i < MaxDrones; i++) m_Drones[i] = new DroneSim();
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

    public bool HiveAlive(int i) => i < HiveHp.Count && HiveHp[i] > 0;

    public int HivesLeft
    {
        get { int n = 0; for (int i = 0; i < HiveHp.Count; i++) if (HiveHp[i] > 0) n++; return n; }
    }

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
        Kills.Value = 0;
        Elapsed.Value = 0f;

        Occupant.Clear();
        FireHp.Clear();
        for (int i = 0; i < Stations.Count; i++) { Occupant.Add(0); FireHp.Add(0); m_Cooldown[i] = 0f; }
        HiveHp.Clear();
        for (int i = 0; i < ExteriorWorld.HivePos.Length; i++) { HiveHp.Add(ExteriorWorld.HiveMaxHp); m_HiveSpawnT[i] = 2f; }
        TaskBroken.Clear();
        for (int i = 0; i < Tasks.Count; i++) TaskBroken.Add(0);
        m_TaskBreakT = 15f;
        Drones.Clear();
        for (int i = 0; i < MaxDrones; i++) { m_Drones[i].Hp = 0; Drones.Add(Vector4.zero); }
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
                    lever = Mathf.Clamp(lever + stick.y * 0.9f * dt, -0.5f, max);
                    if (Mathf.Abs(stick.y) < 0.1f && Mathf.Abs(lever) < 0.05f) lever = 0f;
                    if (pressed) lever = 0f;
                    if (i == Stations.LeftLeg) legL = lever; else legR = lever;
                    break;
                }
                case StationType.Arm:
                {
                    int k = i == Stations.LeftArm ? 0 : 1;
                    float aim = Mathf.Clamp((k == 0 ? aimL : aimR) + stick.x * 1.5f * dt, -0.8f, 0.8f);
                    if (k == 0) aimL = aim; else aimR = aim;
                    if (pressed && m_Cooldown[i] <= 0f && power >= 3f)
                    {
                        // 配線が壊れていると、次のパンチまでが長い
                        m_Cooldown[i] = TaskOk(k == 0 ? Tasks.WiresL : Tasks.WiresR) ? 0.7f : 2.2f;
                        power -= 3f;
                        m_PunchHitAt[k] = 0.15f;
                        if (k == 0) PunchL.Value++; else PunchR.Value++;
                    }
                    break;
                }
                case StationType.Head:
                {
                    aimH = Mathf.Clamp(aimH + stick.x * 1.6f * dt, -1.7f, 1.7f);
                    // ヒューズが切れていると撃てない
                    if (held && m_Cooldown[i] <= 0f && power >= 1.2f && TaskOk(Tasks.Fuse))
                    {
                        m_Cooldown[i] = 0.2f;
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
        float pf = power > 0f ? 1f : 0f;
        float speed = (legL + legR) * 0.5f * 8f * pf;
        float turn = (legL - legR) * 0.55f * pf;
        // ジャイロが壊れていると、歩いているあいだ左右にふらつく
        if (!TaskOk(Tasks.Gyro) && Mathf.Abs(speed) > 0.5f) turn += Mathf.Sin(Elapsed.Value * 1.7f) * 0.35f;
        float yaw = Yaw.Value + turn * dt;
        Yaw.Value = yaw;
        var pos = Pos.Value;
        var np = pos + new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)) * speed * dt;
        if (!ExteriorWorld.Blocked(np, HiveAlive)) pos = np;
        else if (!ExteriorWorld.Blocked(new Vector2(np.x, pos.y), HiveAlive)) pos.x = np.x;
        else if (!ExteriorWorld.Blocked(new Vector2(pos.x, np.y), HiveAlive)) pos.y = np.y;
        Pos.Value = pos;
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
        UpdateHives(dt);
        UpdateDrones(dt);
        UpdateShots(dt);

        if (Hull.Value <= 0f) GameState.Value = Lost;
    }

    void FireBolt(float aimH)
    {
        float yaw = Yaw.Value + aimH;
        if (!TaskOk(Tasks.Scope)) yaw += Random.Range(-0.3f, 0.3f); // 照準がずれている
        // 頭（胴体の前寄り）から砲身の先までの位置。RobotVisual の頭の組み立てと合わせてある
        var start = LocalToWorld(Mathf.Sin(aimH) * 4f, 1.8f + Mathf.Cos(aimH) * 4f);
        m_Bolts.Add(new Bolt { P = start, D = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw)), Life = 1.4f });
        BoltFxRpc(new Vector3(start.x, 14.8f, start.y), yaw);
    }

    void PunchHit(int side, float aim)
    {
        const float reach = 10.2f;
        var fist = LocalToWorld(side * 6.3f + Mathf.Sin(aim) * reach, 1.2f + Mathf.Cos(aim) * reach);
        bool any = false;
        for (int i = 0; i < MaxDrones; i++)
        {
            var d = m_Drones[i];
            if (d.Hp > 0 && Vector2.Distance(new Vector2(d.P.x, d.P.z), fist) < 6.5f) { DamageDrone(i, 4); any = true; }
        }
        for (int h = 0; h < HiveHp.Count; h++)
            if (HiveAlive(h) && Vector2.Distance(ExteriorWorld.HivePos[h], fist) < ExteriorWorld.HiveRadius + 2f) { DamageHive(h, 5); any = true; }
        if (any) BoomFxRpc(new Vector3(fist.x, 9f, fist.y), 0.8f, new Color(1f, 0.82f, 0.25f));
    }

    void UpdateBolts(float dt)
    {
        for (int b = m_Bolts.Count - 1; b >= 0; b--)
        {
            var bolt = m_Bolts[b];
            bolt.P += bolt.D * 75f * dt;
            bolt.Life -= dt;
            for (int i = 0; i < MaxDrones && bolt.Life > 0f; i++)
            {
                var d = m_Drones[i];
                if (d.Hp > 0 && Vector2.Distance(new Vector2(d.P.x, d.P.z), bolt.P) < 2.4f) { DamageDrone(i, 1); bolt.Life = 0f; }
            }
            for (int h = 0; h < HiveHp.Count && bolt.Life > 0f; h++)
                if (HiveAlive(h) && Vector2.Distance(ExteriorWorld.HivePos[h], bolt.P) < ExteriorWorld.HiveRadius - 1f) { DamageHive(h, 1); bolt.Life = 0f; }
            foreach (var bd in ExteriorWorld.Buildings)
                if (bolt.Life > 0f && Vector2.Distance(bd.Pos, bolt.P) < bd.Radius) bolt.Life = 0f;
            if (bolt.Life <= 0f) m_Bolts.RemoveAt(b); else m_Bolts[b] = bolt;
        }
    }

    void UpdateHives(float dt)
    {
        for (int h = 0; h < HiveHp.Count; h++)
        {
            if (!HiveAlive(h)) continue;
            if (Vector2.Distance(ExteriorWorld.HivePos[h], Pos.Value) > 115f) continue;
            m_HiveSpawnT[h] -= dt;
            if (m_HiveSpawnT[h] > 0f) continue;
            int mine = 0, free = -1;
            for (int i = 0; i < MaxDrones; i++)
            {
                if (m_Drones[i].Hp > 0 && m_Drones[i].Hive == h) mine++;
                if (m_Drones[i].Hp <= 0 && free < 0) free = i;
            }
            if (mine >= 3 || free < 0) continue;
            m_HiveSpawnT[h] = 6.5f;
            var d = m_Drones[free];
            d.P = new Vector3(ExteriorWorld.HivePos[h].x + Random.Range(-4f, 4f), 16f, ExteriorWorld.HivePos[h].y + Random.Range(-4f, 4f));
            d.Hp = 3;
            d.FireT = 2.5f + Random.value * 2f;
            d.Orbit = Random.value < 0.5f ? 1f : -1f;
            d.Hive = h;
            d.Seed = Random.value * 6f;
        }
    }

    void UpdateDrones(float dt)
    {
        var rp = Pos.Value;
        for (int i = 0; i < MaxDrones; i++)
        {
            var d = m_Drones[i];
            if (d.Hp <= 0) continue;
            var to = new Vector2(rp.x - d.P.x, rp.y - d.P.z);
            float dist = Mathf.Max(0.01f, to.magnitude);
            var n = to / dist;
            Vector2 move;
            if (dist > 26f) move = n * 9f;
            else move = new Vector2(-n.y, n.x) * d.Orbit * 6f + (dist < 18f ? -n * 5f : Vector2.zero);
            for (int j = 0; j < MaxDrones; j++)
            {
                if (j == i || m_Drones[j].Hp <= 0) continue;
                var away = new Vector2(d.P.x - m_Drones[j].P.x, d.P.z - m_Drones[j].P.z);
                float ad = away.magnitude;
                if (ad < 4f && ad > 0.01f) move += away / ad * 3f;
            }
            d.P.x += move.x * dt;
            d.P.z += move.y * dt;
            d.P.y += (5f + Mathf.Sin(Elapsed.Value * 2f + d.Seed) * 1.2f - d.P.y) * Mathf.Min(1f, dt * 2f);

            d.FireT -= dt;
            if (d.FireT <= 0f && dist < 58f)
            {
                d.FireT = 3f + Random.value * 2.4f;
                var target = new Vector3(rp.x, 10.5f, rp.y);
                var v = (target - d.P).normalized * 24f;
                m_Shots.Add(new Shot { P = d.P, V = v, Life = 4f });
                ShotFxRpc(d.P, v);
            }
        }

        // 位置の同期は 1 秒に 15 回で十分（見た目側でなめらかにする）
        m_DroneSyncT -= dt;
        if (m_DroneSyncT <= 0f)
        {
            m_DroneSyncT = 1f / 15f;
            for (int i = 0; i < MaxDrones; i++)
            {
                var d = m_Drones[i];
                var v = new Vector4(d.P.x, d.P.y, d.P.z, d.Hp);
                if (Drones[i] != v) Drones[i] = v;
            }
        }
    }

    void UpdateShots(float dt)
    {
        var rp = Pos.Value;
        for (int s = m_Shots.Count - 1; s >= 0; s--)
        {
            var shot = m_Shots[s];
            shot.P += shot.V * dt;
            shot.Life -= dt;
            if (Vector2.Distance(new Vector2(shot.P.x, shot.P.z), rp) < 5.4f && shot.P.y < 15f)
            {
                shot.Life = 0f;
                RobotHit(4f);
            }
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

    void DamageDrone(int i, int dmg)
    {
        var d = m_Drones[i];
        d.Hp -= dmg;
        if (d.Hp > 0) return;
        d.Hp = 0;
        Drones[i] = new Vector4(d.P.x, d.P.y, d.P.z, 0);
        Score.Value += 100;
        Kills.Value += 1;
        BoomFxRpc(d.P, 1f, new Color(1f, 0.24f, 0.5f));
    }

    void DamageHive(int h, int dmg)
    {
        HiveHp[h] = Mathf.Max(0, HiveHp[h] - dmg);
        if (HiveHp[h] > 0) return;
        var hp = ExteriorWorld.HivePos[h];
        BoomFxRpc(new Vector3(hp.x, 12f, hp.y), 3f, new Color(1f, 0.24f, 0.5f));
        Score.Value += 1000;
        for (int i = 0; i < MaxDrones; i++) if (m_Drones[i].Hive == h) m_Drones[i].Hive = -1;
        if (HivesLeft == 0)
        {
            Score.Value += Mathf.Max(0, 3000 - Mathf.FloorToInt(Elapsed.Value) * 5);
            GameState.Value = Won;
        }
    }
}
