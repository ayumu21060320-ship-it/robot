using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ゲーム全体の入口。シーンに1つだけ置く（メニュー「Robot/ゲームのシーンを作成」で自動配置）。
/// - 外の世界とロボット内部を組み立てる
/// - 画面を左右に分ける2台のカメラを用意する（左 = 内部、右 = 外）
/// - ホスト / 参加 の接続
/// - 画面表示（HUD）
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [Header("メニュー「Robot/ゲームのシーンを作成」で自動で入ります")]
    public NetworkManager networkManager;
    public GameObject robotPrefab;
    public GameObject crewPrefab;
    public Material litMaterial;
    public Material unlitMaterial;

    public const int MaxPlayers = 4;
    const ushort DefaultPort = 7777;

    Camera m_InteriorCam, m_ExteriorCam;
    InteriorMap.View m_Interior;
    Vector3 m_ExtCamPos, m_ExtCamLook;
    float m_IntSize = 9f;

    string m_Address = "127.0.0.1";
    string m_Port = DefaultPort.ToString();
    string m_Status = "";
    string m_LanAddresses;

    void Awake()
    {
        Application.targetFrameRate = 60;
        Prims.Init(litMaterial, unlitMaterial);
        ExteriorWorld.Build(transform);
        m_Interior = InteriorMap.Build(transform);
        SetupCameras();
        m_LanAddresses = FindLanAddresses();

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = Prims.Hex("#2b3a42");
        RenderSettings.fogStartDistance = 110f;
        RenderSettings.fogEndDistance = 320f;
    }

    void Start()
    {
        networkManager.OnServerStarted += OnServerStarted;
        networkManager.OnClientConnectedCallback += OnClientConnected;
        networkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    void OnDestroy()
    {
        if (!networkManager) return;
        networkManager.OnServerStarted -= OnServerStarted;
        networkManager.OnClientConnectedCallback -= OnClientConnected;
        networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    // ================================================================ カメラ

    void SetupCameras()
    {
        var ig = new GameObject("内部カメラ（左）");
        m_InteriorCam = ig.AddComponent<Camera>();
        m_InteriorCam.orthographic = true;
        m_InteriorCam.orthographicSize = m_IntSize;
        m_InteriorCam.rect = new Rect(0f, 0f, 0.5f, 1f);
        m_InteriorCam.clearFlags = CameraClearFlags.SolidColor;
        m_InteriorCam.backgroundColor = Prims.Hex("#07090b");
        m_InteriorCam.nearClipPlane = 1f;
        m_InteriorCam.farClipPlane = 60f;
        ig.transform.SetPositionAndRotation(InteriorMap.ToWorld(InteriorMap.Spawn, 30f), Quaternion.Euler(90f, 0f, 0f));

        var eg = new GameObject("外カメラ（右）");
        m_ExteriorCam = eg.AddComponent<Camera>();
        m_ExteriorCam.fieldOfView = 55f;
        m_ExteriorCam.rect = new Rect(0.5f, 0f, 0.5f, 1f);
        m_ExteriorCam.clearFlags = CameraClearFlags.SolidColor;
        m_ExteriorCam.backgroundColor = Prims.Hex("#2b3a42");
        m_ExteriorCam.nearClipPlane = 0.3f;
        m_ExteriorCam.farClipPlane = 600f;
        eg.AddComponent<AudioListener>();
        m_ExtCamPos = new Vector3(0, 25, -35);
        m_ExtCamLook = new Vector3(0, 8, 0);
    }

    void LateUpdate()
    {
        float dt = Time.deltaTime;

        // 左：自分の操縦士を真上から追いかける。Tab を押している間は内部全体を表示
        bool overview = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
        var local = CrewPlayer.Local;
        Vector2 focus = overview || local == null ? InteriorMap.Center : local.DisplayPos;
        float size = overview || local == null ? 23f : 9f;
        m_IntSize = Mathf.Lerp(m_IntSize, size, 1f - Mathf.Exp(-8f * dt));
        m_InteriorCam.orthographicSize = m_IntSize;
        var want = InteriorMap.ToWorld(focus, 30f);
        m_InteriorCam.transform.position = Vector3.Lerp(m_InteriorCam.transform.position, want, 1f - Mathf.Exp(-10f * dt));

        // 右：ロボットの斜め後ろから
        var robot = RobotVisual.Instance;
        Vector3 pos, look;
        if (robot != null)
        {
            pos = robot.Root.TransformPoint(new Vector3(0f, 24f, -32f));
            look = robot.Root.TransformPoint(new Vector3(0f, 9f, 14f));
        }
        else
        {
            float a = Time.time * 0.1f;
            pos = new Vector3(Mathf.Sin(a) * 60f, 30f, Mathf.Cos(a) * 60f);
            look = new Vector3(0, 8, 0);
        }
        float k = 1f - Mathf.Exp(-4f * dt);
        m_ExtCamPos = Vector3.Lerp(m_ExtCamPos, pos, k);
        m_ExtCamLook = Vector3.Lerp(m_ExtCamLook, look, k);
        var shake = Random.insideUnitSphere * RobotVisual.Shake * 0.6f;
        m_ExteriorCam.transform.position = m_ExtCamPos + shake;
        m_ExteriorCam.transform.LookAt(m_ExtCamLook);
    }

    // ================================================================ 内部の見た目の更新・キー操作

    void Update()
    {
        var rs = RobotState.Instance;
        for (int i = 0; i < Stations.Count; i++)
        {
            bool fire = rs != null && rs.FireHp.Count == Stations.Count && rs.FireHp[i] > 0;
            bool occupied = rs != null && rs.Occupant.Count == Stations.Count && rs.Occupant[i] != 0;
            if (m_Interior.Fires[i].activeSelf != fire) m_Interior.Fires[i].SetActive(fire);
            if (fire)
                for (int f = 0; f < 3; f++)
                    m_Interior.FireFlames[i * 3 + f].localScale = new Vector3(0.4f, Random.Range(0.5f, 1.3f), 0.4f);
            var c = Stations.All[i].Color;
            float pulse = 0.45f + Mathf.Sin(Time.time * 3f + i) * 0.12f;
            Prims.SetColor(m_Interior.Pads[i], fire ? Prims.Hex("#401008") : occupied ? c : c * pulse);
        }
        if (rs != null)
        {
            float p = rs.Power.Value / RobotState.MaxPower;
            var rc = p < 0.2f && Mathf.Sin(Time.time * 10f) > 0f ? Prims.Hex("#ff6a4a") : Color.Lerp(Prims.Hex("#4a3a10"), Prims.Hex("#ffd23f"), p);
            Prims.SetColor(m_Interior.Reactor, rc);

            // ホストは R で再出撃
            if (networkManager.IsServer && rs.GameState.Value != RobotState.Playing &&
                Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
                rs.RestartRpc();
        }
    }

    // ================================================================ 接続

    void RegisterPrefabs()
    {
        if (!networkManager.NetworkConfig.Prefabs.Contains(robotPrefab)) networkManager.AddNetworkPrefab(robotPrefab);
        if (!networkManager.NetworkConfig.Prefabs.Contains(crewPrefab)) networkManager.AddNetworkPrefab(crewPrefab);
    }

    bool TryGetPort(out ushort port)
    {
        if (ushort.TryParse(m_Port, out port)) return true;
        m_Status = "ポート番号は 1～65535 の数字で入力してください。";
        return false;
    }

    void StartHost()
    {
        if (!TryGetPort(out var port)) return;
        RegisterPrefabs();
        var t = (UnityTransport)networkManager.NetworkConfig.NetworkTransport;
        t.SetConnectionData("127.0.0.1", port, "0.0.0.0");
        m_Status = networkManager.StartHost() ? "" : $"ホストを始められませんでした。ポート {port} がほかのアプリで使われていないか確認してください。";
    }

    void StartClient()
    {
        if (!TryGetPort(out var port)) return;
        string addr = m_Address.Trim();
        if (!IPAddress.TryParse(addr, out _))
        {
            m_Status = "IPアドレスは 192.168.1.10 のような数字の形で入力してください。";
            return;
        }
        RegisterPrefabs();
        var t = (UnityTransport)networkManager.NetworkConfig.NetworkTransport;
        t.SetConnectionData(addr, port);
        m_Status = networkManager.StartClient() ? $"{addr}:{port} に接続しています…" : "接続を始められませんでした。";
    }

    void OnServerStarted() => EnsureRobot();

    void EnsureRobot()
    {
        if (RobotState.Instance != null) return;
        var go = Instantiate(robotPrefab);
        go.GetComponent<NetworkObject>().Spawn();
    }

    void OnClientConnected(ulong clientId)
    {
        if (networkManager.IsServer)
        {
            EnsureRobot();
            if (networkManager.ConnectedClientsList.Count > MaxPlayers)
            {
                networkManager.DisconnectClient(clientId, "満員です（最大4人）");
                return;
            }
            var go = Instantiate(crewPrefab);
            go.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);
        }
        if (clientId == networkManager.LocalClientId) m_Status = "";
    }

    void OnClientDisconnected(ulong clientId)
    {
        if (networkManager.IsServer && clientId != NetworkManager.ServerClientId) return;
        if (!networkManager.IsServer)
        {
            string reason = networkManager.DisconnectReason;
            m_Status = string.IsNullOrEmpty(reason) ? "接続が切れました。アドレスとポート、ホストが起動しているかを確認してください。" : "接続が切れました：" + reason;
        }
    }

    static string FindLanAddresses()
    {
        var list = new List<string>();
        try
        {
            foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)) list.Add(a.ToString());
        }
        catch (System.Exception) { }
        return list.Count > 0 ? string.Join("  /  ", list) : "（取得できませんでした）";
    }

    // ================================================================ 画面表示（仮のUI。あとで uGUI / UI Toolkit に置き換え予定）

    GUIStyle m_Label, m_Small, m_Title, m_Big, m_Box, m_Button, m_Field, m_Tag;
    float m_Scale;

    void EnsureStyles()
    {
        float s = Mathf.Max(0.7f, Screen.height / 900f);
        if (m_Label != null && Mathf.Approximately(s, m_Scale)) return;
        m_Scale = s;
        m_Label = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(16 * s), wordWrap = true, richText = true };
        m_Label.normal.textColor = new Color(0.91f, 0.93f, 0.94f);
        m_Small = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(13 * s) };
        m_Small.normal.textColor = new Color(0.66f, 0.74f, 0.77f);
        m_Title = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(44 * s), fontStyle = FontStyle.Bold };
        m_Title.normal.textColor = Prims.Hex("#ffb000");
        m_Big = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(34 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        m_Box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(12, 12, 10, 10) };
        m_Button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(18 * s), fontStyle = FontStyle.Bold };
        m_Field = new GUIStyle(GUI.skin.textField) { fontSize = Mathf.RoundToInt(18 * s) };
        m_Tag = new GUIStyle(m_Label) { fontSize = Mathf.RoundToInt(14 * s), fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
    }

    void OnGUI()
    {
        EnsureStyles();
        bool connected = networkManager.IsClient || networkManager.IsServer;
        if (!connected)
        {
            DrawMenu();
            return;
        }
        DrawSplitLine();
        DrawInteriorLabels();
        DrawStatus();
        DrawPrompt();
        DrawEnd();
        if (RobotVisual.HitFlash > 0f) DrawRect(new Rect(Screen.width * 0.5f, 0, Screen.width * 0.5f, Screen.height), new Color(1f, 0.2f, 0.1f, RobotVisual.HitFlash * 0.25f));
    }

    static void DrawRect(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    void DrawMenu()
    {
        float s = m_Scale;
        float w = Mathf.Min(620f * s, Screen.width - 40f), h = 560f * s;
        var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        DrawRect(r, new Color(0.06f, 0.08f, 0.1f, 0.92f));
        GUILayout.BeginArea(new Rect(r.x + 24 * s, r.y + 20 * s, r.width - 48 * s, r.height - 40 * s));
        GUILayout.Label("タイタンクルー", m_Title);
        GUILayout.Label("巨大ロボットの中を走り回り、脚・腕・頭部砲・動力炉の席に乗り込んで、みんなで1体のロボットを動かそう。3つの敵の巣を壊せば勝ち。", m_Label);
        GUILayout.Space(12 * s);

        GUILayout.Label("<b>ホストになる</b>（部屋を作る人）", m_Label);
        GUILayout.BeginHorizontal();
        GUILayout.Label("ポート", m_Label, GUILayout.Width(80 * s));
        m_Port = GUILayout.TextField(m_Port, 5, m_Field, GUILayout.Width(110 * s));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("ホストとして始める", m_Button, GUILayout.Width(240 * s), GUILayout.Height(40 * s))) StartHost();
        GUILayout.EndHorizontal();
        GUILayout.Label("このPCのアドレス： " + m_LanAddresses, m_Small);
        GUILayout.Space(14 * s);

        GUILayout.Label("<b>参加する</b>（ホストのアドレスを入力）", m_Label);
        GUILayout.BeginHorizontal();
        GUILayout.Label("アドレス", m_Label, GUILayout.Width(80 * s));
        m_Address = GUILayout.TextField(m_Address, 45, m_Field, GUILayout.Width(220 * s));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("参加する", m_Button, GUILayout.Width(240 * s), GUILayout.Height(40 * s))) StartClient();
        GUILayout.EndHorizontal();
        GUILayout.Space(10 * s);

        if (!string.IsNullOrEmpty(m_Status)) GUILayout.Label("<color=#ffb000>" + m_Status + "</color>", m_Label);
        GUILayout.FlexibleSpace();
        GUILayout.Label("操作：WASD / 矢印で移動　E で席に乗る・降りる　Space でアクション　Tab で内部の全体図\n同じPCで試すときはアドレス 127.0.0.1 のまま。ネット越しに遊ぶ招待コード方式（Relay）は次の段階で追加します。", m_Small);
        GUILayout.EndArea();
    }

    void DrawSplitLine()
    {
        DrawRect(new Rect(Screen.width * 0.5f - 2f, 0, 4f, Screen.height), Prims.Hex("#ffb000"));
        float s = m_Scale;
        GUI.Label(new Rect(12 * s, 8 * s, 400 * s, 24 * s), "ロボット内部　<color=#93a6ae>Tab：全体図</color>", m_Small);
    }

    void DrawInteriorLabels()
    {
        var rs = RobotState.Instance;
        float s = m_Scale;
        for (int i = 0; i < Stations.Count; i++)
        {
            var def = Stations.All[i];
            var sp = m_InteriorCam.WorldToScreenPoint(InteriorMap.ToWorld(def.Pos + def.ConsoleOffset * 1.6f, 1f));
            if (sp.z < 0 || sp.x < 0 || sp.x > Screen.width * 0.5f) continue;
            float w = 110 * s, h = 26 * s;
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h * 0.5f, w, h);
            bool fire = rs != null && rs.FireHp.Count == Stations.Count && rs.FireHp[i] > 0;
            DrawRect(r, fire ? new Color(0.6f, 0.1f, 0.05f, 0.9f) : new Color(0.04f, 0.06f, 0.07f, 0.8f));
            m_Tag.normal.textColor = fire ? Color.white : def.Color;
            GUI.Label(r, fire ? def.Name + " 火災！" : def.Name, m_Tag);
        }
        foreach (var p in CrewPlayer.All)
        {
            var sp = m_InteriorCam.WorldToScreenPoint(InteriorMap.ToWorld(p.DisplayPos, 2.2f));
            if (sp.x < 0 || sp.x > Screen.width * 0.5f) continue;
            float w = 40 * s, h = 20 * s;
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h - 14 * s, w, h);
            DrawRect(r, p.Color);
            m_Tag.normal.textColor = new Color(0.05f, 0.06f, 0.07f);
            GUI.Label(r, p.Label, m_Tag);
        }
    }

    void DrawBar(Rect r, float v, Color c, string label)
    {
        DrawRect(r, new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect(r.x, r.y, r.width * Mathf.Clamp01(v), r.height), c);
        GUI.Label(new Rect(r.x + 6, r.y, r.width, r.height), label, m_Small);
    }

    void DrawStatus()
    {
        var rs = RobotState.Instance;
        if (rs == null) return;
        float s = m_Scale;
        float x = Screen.width * 0.5f + 14 * s, y = 12 * s, w = 280 * s;
        DrawRect(new Rect(x - 8 * s, y - 6 * s, w + 16 * s, 118 * s), new Color(0.06f, 0.08f, 0.1f, 0.85f));
        DrawBar(new Rect(x, y, w, 22 * s), rs.Hull.Value / RobotState.MaxHull, Prims.Hex("#ff6a4a"), $"装甲  {Mathf.CeilToInt(rs.Hull.Value)}");
        DrawBar(new Rect(x, y + 30 * s, w, 22 * s), rs.Power.Value / RobotState.MaxPower, Prims.Hex("#d9a920"), $"動力  {Mathf.CeilToInt(rs.Power.Value)}");
        int t = Mathf.FloorToInt(rs.Elapsed.Value);
        GUI.Label(new Rect(x, y + 60 * s, w, 24 * s), $"敵の巣 残り <color=#ff6aa0>{rs.HivesLeft}</color>　スコア <color=#ffb000>{rs.Score.Value}</color>　{t / 60}:{t % 60:00}", m_Label);
        // 一番近い巣の方角
        int near = -1; float nd = float.MaxValue;
        for (int i = 0; i < ExteriorWorld.HivePos.Length; i++)
        {
            if (!rs.HiveAlive(i)) continue;
            float d = Vector2.Distance(ExteriorWorld.HivePos[i], rs.Pos.Value);
            if (d < nd) { nd = d; near = i; }
        }
        if (near >= 0)
        {
            var to = ExteriorWorld.HivePos[near] - rs.Pos.Value;
            float rel = Mathf.DeltaAngle(rs.Yaw.Value * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
            string dir = Mathf.Abs(rel) < 20f ? "正面" : rel > 0 ? (rel < 100f ? "右前" : "右後ろ") : (rel > -100f ? "左前" : "左後ろ");
            GUI.Label(new Rect(x, y + 86 * s, w, 24 * s), $"<color=#93a6ae>近い巣：{dir} {Mathf.RoundToInt(nd)}m</color>", m_Small);
        }

        if (GUI.Button(new Rect(Screen.width - 110 * s, 10 * s, 96 * s, 30 * s), "切断", m_Button)) networkManager.Shutdown();
    }

    void DrawPrompt()
    {
        var me = CrewPlayer.Local;
        var rs = RobotState.Instance;
        if (me == null || rs == null) return;
        float s = m_Scale;
        string where, hint;
        int st = me.Station.Value;
        if (st >= 0)
        {
            var def = Stations.All[st];
            where = $"{def.Name} を操縦中";
            switch (def.Type)
            {
                case StationType.Leg:
                    float lever = st == Stations.LeftLeg ? rs.LegL.Value : rs.LegR.Value;
                    hint = $"W/S でレバー（{Mathf.RoundToInt(lever * 100)}%）　Space で止める\n両脚そろえると前進、差をつけると曲がる"; break;
                case StationType.Arm: hint = "A/D で狙いを左右に振る　Space でパンチ"; break;
                case StationType.Head: hint = "A/D で砲塔を回す　Space 長押しで連射"; break;
                default: hint = "Space 連打で発電（座っているだけでも少し回復）"; break;
            }
            hint += "　E で降りる";
        }
        else if (me.NearStation >= 0)
        {
            var def = Stations.All[me.NearStation];
            bool fire = rs.FireHp.Count == Stations.Count && rs.FireHp[me.NearStation] > 0;
            bool taken = rs.Occupant.Count == Stations.Count && rs.Occupant[me.NearStation] != 0;
            where = def.Name;
            hint = fire ? $"<color=#ff8a6a>火災！ Space 連打で消火（あと {rs.FireHp[me.NearStation]} 回）</color>"
                 : taken ? "ほかの乗組員が操縦中" : "E で席に乗る";
        }
        else
        {
            where = InteriorMap.AreaName(me.Pos.Value);
            hint = "WASD で移動。光っている操作盤に近づこう";
        }
        float w = Mathf.Min(Screen.width * 0.5f - 24 * s, 520 * s), h = 76 * s;
        var r = new Rect(12 * s, Screen.height - h - 12 * s, w, h);
        DrawRect(r, new Color(0.06f, 0.08f, 0.1f, 0.88f));
        DrawRect(new Rect(r.x, r.y, 6 * s, r.height), me.Color);
        GUI.Label(new Rect(r.x + 16 * s, r.y + 6 * s, r.width - 24 * s, 26 * s), $"<b>{me.Label}　{where}</b>", m_Label);
        GUI.Label(new Rect(r.x + 16 * s, r.y + 32 * s, r.width - 24 * s, 44 * s), hint, m_Small);
    }

    void DrawEnd()
    {
        var rs = RobotState.Instance;
        if (rs == null || rs.GameState.Value == RobotState.Playing) return;
        bool won = rs.GameState.Value == RobotState.Won;
        DrawRect(new Rect(0, 0, Screen.width, Screen.height), new Color(0, 0, 0, 0.55f));
        float s = m_Scale;
        m_Big.normal.textColor = won ? Prims.Hex("#ffb000") : Prims.Hex("#ff6a4a");
        GUI.Label(new Rect(0, Screen.height * 0.36f, Screen.width, 60 * s), won ? "任務完了" : "タイタン大破", m_Big);
        int t = Mathf.FloorToInt(rs.Elapsed.Value);
        var info = $"スコア {rs.Score.Value}　撃墜 {rs.Kills.Value}　時間 {t / 60}:{t % 60:00}\n" +
                   (networkManager.IsServer ? "R キーでもう一度出撃" : "ホストが再出撃するのを待っています");
        var style = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0, Screen.height * 0.36f + 70 * s, Screen.width, 60 * s), info, style);
    }
}
