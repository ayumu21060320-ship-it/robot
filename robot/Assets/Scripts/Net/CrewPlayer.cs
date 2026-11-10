using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ロボットの中を歩く操縦士（1人のプレイヤー）。
/// 位置と入力は本人（オーナー）が書き込み、どの席に座っているかはホストが決める。
/// </summary>
public class CrewPlayer : NetworkBehaviour
{
    public static CrewPlayer Local { get; private set; }
    public static readonly List<CrewPlayer> All = new List<CrewPlayer>();
    public static readonly Color[] Colors = { Prims.Hex("#3fb6ff"), Prims.Hex("#ff5a8a"), Prims.Hex("#7dff6a"), Prims.Hex("#c08bff") };

    const float Speed = 6f;        // 足場を歩く速さ
    const float ClimbSpeed = 4.5f; // はしごを登る速さ
    const float Gravity = 30f;
    public const float Radius = 0.35f;
    public const float UseRange = 1.6f;

    // 本人が書く
    public NetworkVariable<Vector2> Pos = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<Vector2> Stick = new NetworkVariable<Vector2>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<bool> ActHeld = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public NetworkVariable<int> ActCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // ホストが書く
    public NetworkVariable<int> Station = new NetworkVariable<int>(-1);
    public NetworkVariable<int> Slot = new NetworkVariable<int>(0);

    public Vector2 DisplayPos { get; private set; }
    /// <summary>本人の近くにある席（なければ -1）。HUD の案内表示に使う。</summary>
    public int NearStation { get; private set; } = -1;
    /// <summary>本人の近くにある整備タスク（なければ -1）。</summary>
    public int NearTask { get; private set; } = -1;
    public Color Color => Colors[Mathf.Clamp(Slot.Value, 0, Colors.Length - 1)];
    public string Label => "P" + (Slot.Value + 1);

    Transform m_Avatar;
    SpriteRenderer m_Body, m_Detail, m_Shadow;
    float m_Walk;
    bool m_FacingRight = true;
    int m_Ladder = -1;   // 登っているはしご（本人だけが使う）
    float m_FallSpeed;

    // 絵の足元が、128px の絵の下端から 8px 上にある。立ち位置に足元が来るよう上にずらす
    const float SpriteLift = 1f - 8f / Art.PixelsPerUnit;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        if (IsServer) Slot.Value = FreeSlot();
        if (IsOwner)
        {
            Local = this;
            Pos.Value = InteriorMap.Spawn + new Vector2(((int)(OwnerClientId % 4) - 1.5f) * 1.2f, 0f);
        }
        DisplayPos = Pos.Value;
        BuildAvatar();
        Slot.OnValueChanged += OnSlotChanged;
        ApplyColor();
    }

    public override void OnNetworkDespawn()
    {
        Slot.OnValueChanged -= OnSlotChanged;
        All.Remove(this);
        if (Local == this) Local = null;
    }

    void OnSlotChanged(int prev, int next) => ApplyColor();

    int FreeSlot()
    {
        var used = new bool[Colors.Length];
        foreach (var p in All)
            if (p != this && p.Slot.Value >= 0 && p.Slot.Value < used.Length) used[p.Slot.Value] = true;
        for (int i = 0; i < used.Length; i++) if (!used[i]) return i;
        return 0;
    }

    void BuildAvatar()
    {
        m_Avatar = new GameObject("Avatar").transform;
        m_Avatar.SetParent(transform, false);
        m_Shadow = Art.Place(transform, "shadow", new Vector3(0f, 0.02f, 0.05f), InteriorMap.OrderPilot - 1, null, "Shadow");
        m_Shadow.transform.localScale = new Vector3(0.6f, 0.5f, 1f);
        m_Body = Art.Place(m_Avatar, "pilot_body_0", new Vector3(0f, 0.05f, SpriteLift), InteriorMap.OrderPilot, null, "Body");
        m_Detail = Art.Place(m_Avatar, "pilot_detail_0", new Vector3(0f, 0.06f, SpriteLift), InteriorMap.OrderPilot + 1, null, "Detail");
    }

    void ApplyColor()
    {
        if (m_Body) m_Body.color = Color;
    }

    void Update()
    {
        if (!IsSpawned) return;
        if (IsOwner) OwnerUpdate();

        var target = Pos.Value;
        var prev = DisplayPos;
        if (IsOwner || Vector2.Distance(DisplayPos, target) > 4f) DisplayPos = target;
        else DisplayPos = Vector2.Lerp(DisplayPos, target, 1f - Mathf.Exp(-15f * Time.deltaTime));

        var moved = DisplayPos - prev;
        bool walking = Mathf.Abs(moved.x) > 0.0005f || Mathf.Abs(moved.y) > 0.0005f;
        // 絵は右向き。左へ動いたら左右反転。席に座っているときは操作盤のほうを向く
        if (moved.x > 0.001f) m_FacingRight = true;
        else if (moved.x < -0.001f) m_FacingRight = false;
        if (Station.Value >= 0)
        {
            float ox = Stations.All[Station.Value].ConsoleOffset.x;
            if (ox > 0.01f) m_FacingRight = true; else if (ox < -0.01f) m_FacingRight = false;
        }
        m_Walk = walking ? m_Walk + Time.deltaTime * 14f : 0f;

        transform.position = InteriorMap.ToWorld(DisplayPos);
        if (m_Avatar)
        {
            int frame = walking ? 1 + (int)(m_Walk / Mathf.PI) % 2 : 0;
            m_Body.sprite = Art.Get("pilot_body_" + frame);
            m_Detail.sprite = Art.Get("pilot_detail_" + frame);
            m_Body.flipX = m_Detail.flipX = !m_FacingRight;
            // 落ちているあいだと、はしごの途中では影を消す
            m_Shadow.enabled = InteriorMap.OnFloor(DisplayPos.x, DisplayPos.y, 0.05f, out _);
            m_Avatar.localPosition = new Vector3(0f, 0f, m_Shadow.enabled ? Mathf.Abs(Mathf.Sin(m_Walk)) * 0.08f : 0f);
        }
    }

    /// <summary>
    /// 横から見た断面図での移動。足場の上を A/D で歩き、はしごの前で W/S を押すと登り降りする。
    /// 足場のないところでは落ちる。
    /// </summary>
    Vector2 MoveSideView(Vector2 p, Vector2 mv, float dt)
    {
        if (m_Ladder >= 0)
        {
            var l = InteriorMap.Ladders[m_Ladder];
            p.x = l.X;
            p.y = Mathf.Clamp(p.y + mv.y * ClimbSpeed * dt, l.Y0, l.Y1);
            bool atFloor = InteriorMap.OnFloor(p.x, p.y, 0.3f, out float fy);
            // 足場の高さで左右に入力すると、はしごから降りて歩き出す
            if (atFloor && Mathf.Abs(mv.x) > 0.5f && Mathf.Abs(mv.y) < 0.5f) { p.y = fy; m_Ladder = -1; }
            // 下の端・上の端まで行ったら足場に立つ
            else if ((mv.y < -0.1f && p.y <= l.Y0 + 0.001f) || (mv.y > 0.1f && p.y >= l.Y1 - 0.001f)) m_Ladder = -1;
            return p;
        }

        // はしごにつかまる（上なら W、下なら S）
        if (Mathf.Abs(mv.y) > 0.5f && Mathf.Abs(mv.y) >= Mathf.Abs(mv.x))
        {
            int li = InteriorMap.LadderNear(p.x, p.y, mv.y > 0f);
            if (li >= 0) { m_Ladder = li; m_FallSpeed = 0f; p.x = InteriorMap.Ladders[li].X; return p; }
        }

        // 左右に歩く（体の中の空洞から出ないように）
        float nx = p.x + mv.x * Speed * dt;
        if (InteriorMap.InCavity(nx + Mathf.Sign(mv.x) * Radius, p.y)) p.x = nx;

        // 重力
        if (InteriorMap.OnFloor(p.x, p.y, 0.01f, out float floor)) { p.y = floor; m_FallSpeed = 0f; }
        else
        {
            m_FallSpeed += Gravity * dt;
            float ny = p.y - m_FallSpeed * dt;
            if (InteriorMap.FloorBetween(p.x, p.y, ny, out float land)) { p.y = land; m_FallSpeed = 0f; }
            else p.y = ny;
            // 万一体の外まで落ちたらスタート地点へ戻す
            if (p.y < InteriorMap.Bounds.yMin - 5f) { p = InteriorMap.Spawn; m_FallSpeed = 0f; }
        }
        return p;
    }

    void OwnerUpdate()
    {
        var kb = Keyboard.current;
        var gp = Gamepad.current;

        var mv = Vector2.zero;
        bool use = false, actHeld = false, actPressed = false;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1;
            use |= kb.eKey.wasPressedThisFrame;
            actHeld |= kb.spaceKey.isPressed;
            actPressed |= kb.spaceKey.wasPressedThisFrame;
        }
        if (gp != null)
        {
            var s = gp.leftStick.ReadValue();
            if (s.magnitude > 0.2f) mv += s;
            use |= gp.buttonEast.wasPressedThisFrame;
            actHeld |= gp.buttonSouth.isPressed;
            actPressed |= gp.buttonSouth.wasPressedThisFrame;
        }
        mv = Vector2.ClampMagnitude(mv, 1f);

        var robot = RobotState.Instance;
        bool playing = robot != null && robot.GameState.Value == RobotState.Playing;
        int st = Station.Value;

        // ミニゲーム中は歩かない（ミニゲームの操作は TaskUI が読む）
        if (TaskUI.IsOpen)
        {
            if (Stick.Value != Vector2.zero) Stick.Value = Vector2.zero;
            if (ActHeld.Value) ActHeld.Value = false;
            return;
        }
        if (TaskUI.ClosedFrame == Time.frameCount) use = actPressed = false;

        if (st < 0)
        {
            Pos.Value = MoveSideView(Pos.Value, mv, Time.deltaTime);
            if (Stick.Value != Vector2.zero) Stick.Value = Vector2.zero;
            NearStation = InteriorMap.NearestStation(Pos.Value, UseRange);
            NearTask = NearStation < 0 ? Tasks.Nearest(Pos.Value, UseRange) : -1;
            if (robot != null && NearStation >= 0 && robot.FireHp.Count == Stations.Count)
            {
                bool onFire = robot.FireHp[NearStation] > 0;
                if (onFire && (actPressed || use)) robot.RepairRpc(NearStation);
                else if (!onFire && use && playing) robot.RequestStationRpc(NearStation);
            }
            else if (robot != null && NearTask >= 0 && playing && !robot.TaskOk(NearTask) && (use || actPressed))
            {
                TaskUI.Instance.Open(NearTask);
            }
        }
        else
        {
            var sp = Stations.All[st].Pos;
            if (Pos.Value != sp) Pos.Value = sp;
            m_Ladder = -1;
            m_FallSpeed = 0f;
            if (Stick.Value != mv) Stick.Value = mv;
            NearStation = st;
            NearTask = -1;
            if (use && robot != null) robot.LeaveStationRpc();
        }

        if (ActHeld.Value != actHeld) ActHeld.Value = actHeld;
        if (actPressed) ActCount.Value = ActCount.Value + 1;
    }
}
