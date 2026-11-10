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

    const float Speed = 6f;
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
    public Color Color => Colors[Mathf.Clamp(Slot.Value, 0, Colors.Length - 1)];
    public string Label => "P" + (Slot.Value + 1);

    Transform m_Avatar;
    Renderer m_Body;
    Transform m_LegL, m_LegR;
    float m_Walk, m_Facing;

    public override void OnNetworkSpawn()
    {
        All.Add(this);
        if (IsServer) Slot.Value = FreeSlot();
        if (IsOwner)
        {
            Local = this;
            Pos.Value = InteriorMap.Spawn + new Vector2(((int)(OwnerClientId % 4) - 1.5f) * 1.4f, 0f);
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
        var body = Prims.Shape(PrimitiveType.Capsule, m_Avatar, new Vector3(0, 0.75f, 0), new Vector3(0.75f, 0.5f, 0.75f), Color.white);
        m_Body = body.GetComponent<Renderer>();
        Prims.Box(m_Avatar, new Vector3(0, 0.95f, 0.3f), new Vector3(0.5f, 0.22f, 0.15f), Prims.Hex("#9fe9ff"), true, "Visor");
        Prims.Box(m_Avatar, new Vector3(0, 0.7f, -0.38f), new Vector3(0.45f, 0.45f, 0.2f), Prims.Hex("#2a3236"), false, "Pack");
        m_LegL = Prims.Box(m_Avatar, new Vector3(-0.17f, 0.15f, 0), new Vector3(0.2f, 0.3f, 0.22f), Prims.Hex("#2a3236")).transform;
        m_LegR = Prims.Box(m_Avatar, new Vector3(0.17f, 0.15f, 0), new Vector3(0.2f, 0.3f, 0.22f), Prims.Hex("#2a3236")).transform;
    }

    void ApplyColor()
    {
        if (m_Body) Prims.SetColor(m_Body, Color);
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
        bool walking = moved.sqrMagnitude > 0.00001f;
        if (walking) m_Facing = Mathf.Atan2(moved.x, moved.y) * Mathf.Rad2Deg;
        if (Station.Value >= 0) m_Facing = Mathf.Atan2(Stations.All[Station.Value].ConsoleOffset.x, Stations.All[Station.Value].ConsoleOffset.y) * Mathf.Rad2Deg;
        m_Walk = walking ? m_Walk + Time.deltaTime * 14f : Mathf.MoveTowards(m_Walk, Mathf.Round(m_Walk / Mathf.PI) * Mathf.PI, Time.deltaTime * 10f);

        transform.position = InteriorMap.ToWorld(DisplayPos);
        if (m_Avatar)
        {
            m_Avatar.localRotation = Quaternion.Euler(0, m_Facing, 0);
            m_Avatar.localPosition = new Vector3(0, Mathf.Abs(Mathf.Sin(m_Walk)) * 0.12f, 0);
            m_LegL.localRotation = Quaternion.Euler(Mathf.Sin(m_Walk) * 35f, 0, 0);
            m_LegR.localRotation = Quaternion.Euler(-Mathf.Sin(m_Walk) * 35f, 0, 0);
        }
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

        if (st < 0)
        {
            Pos.Value = InteriorMap.Move(Pos.Value, mv * Speed * Time.deltaTime, Radius);
            if (Stick.Value != Vector2.zero) Stick.Value = Vector2.zero;
            NearStation = InteriorMap.NearestStation(Pos.Value, UseRange);
            if (robot != null && NearStation >= 0 && robot.FireHp.Count == Stations.Count)
            {
                bool onFire = robot.FireHp[NearStation] > 0;
                if (onFire && (actPressed || use)) robot.RepairRpc(NearStation);
                else if (!onFire && use && playing) robot.RequestStationRpc(NearStation);
            }
        }
        else
        {
            var sp = Stations.All[st].Pos;
            if (Pos.Value != sp) Pos.Value = sp;
            if (Stick.Value != mv) Stick.Value = mv;
            NearStation = st;
            if (use && robot != null) robot.LeaveStationRpc();
        }

        if (ActHeld.Value != actHeld) ActHeld.Value = actHeld;
        if (actPressed) ActCount.Value = ActCount.Value + 1;
    }
}
