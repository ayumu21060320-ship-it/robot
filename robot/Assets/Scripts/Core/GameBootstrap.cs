using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// ゲーム全体の入口。シーンに1つだけ置く（メニュー「Robot/ゲームのシーンを作成」で自動配置）。
/// - 外の世界とロボット内部を組み立てる
/// - 画面を左右に分ける2台のカメラを用意する（左 = 内部、右 = 外）
/// - 部屋を作る / 部屋番号で参加する（OnlineRooms）。オンラインは試合前に待機室に集まる
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

    public const int MaxPlayers = 6;

    Camera m_InteriorCam, m_ExteriorCam;
    InteriorMap.View m_Interior;
    Vector3 m_ExtCamPos, m_ExtCamLook;
    float m_IntSize = 9f;
    bool m_Piloting;      // ロケットパンチを操縦中（外の画面を全画面にしている）
    bool m_Spectating;    // ロケットパンチで飛んでいって観戦中（外の画面を全画面にしている）
    bool m_FullExterior;

    string m_RoomInput = "";
    string m_UrlInput;
    string m_Status = "";
    bool m_Busy;   // 部屋を作っている・入ろうとしているあいだ

    void Awake()
    {
        Application.targetFrameRate = 60;
        Prims.Init(litMaterial, unlitMaterial);
        ExteriorWorld.Build(transform);
        m_Interior = InteriorMap.Build(transform);
        SetupCameras();
        gameObject.AddComponent<TaskUI>();
        gameObject.AddComponent<StationUI>();

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
        var rsCam = RobotState.Instance;
        bool inLobby = rsCam != null && rsCam.GameState.Value == RobotState.Lobby;
        bool overview = !inLobby && Keyboard.current != null && Keyboard.current.tabKey.isPressed;
        var local = CrewPlayer.Local;
        Vector2 focus = overview || local == null ? InteriorMap.Center : local.DisplayPos;
        // 全体図はロボット全体が左半分に収まる大きさ
        var all = InteriorMap.Bounds;
        float fit = Mathf.Max(all.height * 0.5f, all.width * 0.5f / Mathf.Max(0.1f, m_InteriorCam.aspect)) + 1f;
        float size = overview || local == null ? fit : 9f;
        m_IntSize = Mathf.Lerp(m_IntSize, size, 1f - Mathf.Exp(-8f * dt));
        m_InteriorCam.orthographicSize = m_IntSize;
        var want = InteriorMap.ToWorld(focus, 30f);
        m_InteriorCam.transform.position = Vector3.Lerp(m_InteriorCam.transform.position, want, 1f - Mathf.Exp(-10f * dt));

        // ロケットパンチを操縦しているあいだ：外の画面を全画面にして、拳のすぐ後ろから見る
        var rsP = RobotState.Instance;
        Transform fist = null;
        if (rsP != null && local != null && RobotVisual.Instance != null && (local.Station.Value == Stations.LeftArm || local.Station.Value == Stations.RightArm))
        {
            int arm = local.Station.Value == Stations.LeftArm ? 0 : 1;
            if (rsP.RocketState(arm) == RobotState.RocketFlying) fist = RobotVisual.Instance.RocketFist(arm);
        }
        bool piloting = fist != null;
        // 観戦中（ロケットパンチで飛んでいった）も、外の画面を全画面にする
        bool spectating = local != null && local.Spectating.Value;
        bool full = piloting || spectating;
        m_Piloting = piloting;
        m_Spectating = spectating;
        if (full != m_FullExterior)
        {
            m_FullExterior = full;
            m_InteriorCam.enabled = !full;
            m_ExteriorCam.rect = full ? new Rect(0f, 0f, 1f, 1f) : new Rect(0.5f, 0f, 0.5f, 1f);
        }
        m_ExteriorCam.fieldOfView = piloting ? 70f : 55f;
        if (piloting)
        {
            var camPos = fist.position - fist.forward * 9f + Vector3.up * 3f;
            m_ExtCamPos = camPos;
            m_ExtCamLook = fist.position + fist.forward * 25f;
            m_ExteriorCam.transform.position = camPos + UnityEngine.Random.insideUnitSphere * 0.15f;
            m_ExteriorCam.transform.LookAt(m_ExtCamLook);
            return;
        }

        // 右：ロボットの斜め後ろから
        var robot = RobotVisual.Instance;
        Vector3 pos, look;
        if (robot != null)
        {
            pos = robot.Root.TransformPoint(new Vector3(0f, 26f, -34f) / RobotState.TitanScale * 1.6f);
            look = robot.Root.TransformPoint(new Vector3(0f, 11f, 14f) / RobotState.TitanScale * 1.6f);
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
        var shake = UnityEngine.Random.insideUnitSphere * RobotVisual.Shake * 0.6f;
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
            var fireSprite = m_Interior.Fires[i];
            if (fireSprite.gameObject.activeSelf != fire) fireSprite.gameObject.SetActive(fire);
            if (fire) fireSprite.sprite = Art.Get(((int)(Time.time * 8f) + i) % 2 == 0 ? "fire_0" : "fire_1");
            // 立ち位置：使用中は席の色で明るく、空いているときはゆっくり点滅、火災中は暗い赤
            var c = Stations.All[i].Color;
            float pulse = 0.55f + Mathf.Sin(Time.time * 3f + i) * 0.15f;
            var padColor = fire ? Prims.Hex("#5a2a20") : occupied ? c : Color.Lerp(Prims.Hex("#6a747c"), c, pulse);
            m_Interior.Pads[i].color = padColor;
        }
        // 壊れた整備装置の上の「！」を点滅させる
        for (int i = 0; i < Tasks.Count; i++)
        {
            bool broken = rs != null && !rs.TaskOk(i);
            var alert = m_Interior.TaskAlerts[i];
            if (alert.gameObject.activeSelf != broken) alert.gameObject.SetActive(broken);
            if (broken) alert.transform.localScale = Vector3.one * (1f + Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 0.25f);
        }
        // エネルギーセル：出ている場所に色つきで光らせ、ふわふわ上下させる
        for (int i = 0; i < EnergyCells.Count; i++)
        {
            int kind = rs != null && rs.CellAt.Count == EnergyCells.Count ? rs.CellAt[i] : -1;
            bool on = kind >= 0 && rs.GameState.Value == RobotState.Playing;
            var body = m_Interior.CellBodies[i];
            var glow = m_Interior.CellGlows[i];
            if (body.gameObject.activeSelf != on) { body.gameObject.SetActive(on); glow.gameObject.SetActive(on); }
            if (!on) continue;
            var c = EnergyCells.Color(kind);
            body.color = c;
            glow.color = new Color(c.r, c.g, c.b, 0.55f + Mathf.Sin(Time.time * 4f + i) * 0.25f);
            var sp = EnergyCells.Spots[i];
            float bob = Mathf.Sin(Time.time * 2.5f + i) * 0.12f;
            body.transform.localPosition = new Vector3(sp.x, 0.026f, sp.y + 0.55f + bob);
            glow.transform.localPosition = new Vector3(sp.x, 0.025f, sp.y + 0.6f + bob);
        }
        // セルを持っている人がいるときは、動力炉の入れ口を光らせて行き先を知らせる
        var meC = CrewPlayer.Local;
        bool carryingC = meC != null && meC.Carry.Value >= 0;
        if (m_Interior.ReactorIntake.gameObject.activeSelf != carryingC) m_Interior.ReactorIntake.gameObject.SetActive(carryingC);
        if (carryingC)
        {
            var c = EnergyCells.Color(meC.Carry.Value);
            m_Interior.ReactorIntake.color = new Color(c.r, c.g, c.b, 0.35f + Mathf.Abs(Mathf.Sin(Time.time * 3f)) * 0.35f);
        }

        for (int k = 0; k < 2; k++)
        {
            bool away = rs != null && rs.ArmAway(k);
            if (m_Interior.ArmCovers[k].gameObject.activeSelf != away) m_Interior.ArmCovers[k].gameObject.SetActive(away);
        }

        if (rs != null)
        {
            float p = rs.Power.Value / RobotState.MaxPower;
            var rc = p < 0.2f && Mathf.Sin(Time.time * 10f) > 0f ? Prims.Hex("#ff6a4a") : Color.Lerp(Prims.Hex("#5a4a2a"), Prims.Hex("#ffd23f"), p);
            m_Interior.ReactorCore.color = rc;

            // ホストは R で再出撃（オンラインなら待機室へ戻る）
            if (networkManager.IsServer && rs.GameState.Value != RobotState.Playing && rs.GameState.Value != RobotState.Lobby &&
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

    /// <summary>1人で遊ぶ（待機室なしで、すぐ試合）</summary>
    void StartSolo()
    {
        RegisterPrefabs();
        RobotState.UseLobby = false;
        OnlineRooms.Clear();
        m_Status = OnlineRooms.StartSolo(networkManager) ? "" : "始められませんでした。";
    }

    /// <summary>部屋を作ってホストになる。lobby = true なら待機室から始める</summary>
    async void CreateRoom(bool lobby)
    {
        if (m_Busy) return;
        m_Busy = true;
        m_Status = "部屋を作っています…";
        SaveUrl();
        RegisterPrefabs();
        RobotState.UseLobby = lobby;
        try
        {
            await OnlineRooms.CreateRoom(networkManager);
            m_Status = "";
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            m_Status = "部屋を作れませんでした：" + e.Message;
            if (networkManager.IsListening) networkManager.Shutdown();
        }
        m_Busy = false;
    }

    /// <summary>部屋番号で部屋に入る</summary>
    async void JoinRoom()
    {
        if (m_Busy) return;
        string code = OnlineRooms.Normalize(m_RoomInput);
        if (code.Length < 4) { m_Status = OnlineRooms.UsesRelay ? "部屋番号（6文字の英数字）を入力してください。" : "部屋番号（4けたの数字）を入力してください。"; return; }
        m_Busy = true;
        m_Status = $"部屋 {code} に入っています…";
        SaveUrl();
        RegisterPrefabs();
        try
        {
            await OnlineRooms.JoinRoom(networkManager, code);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            m_Status = "部屋に入れませんでした：" + e.Message;
            if (networkManager.IsListening) networkManager.Shutdown();
        }
        m_Busy = false;
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
                networkManager.DisconnectClient(clientId, $"満員です（最大{MaxPlayers}人）");
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
        OnlineRooms.Clear();
        if (!networkManager.IsServer)
        {
            string reason = networkManager.DisconnectReason;
            if (string.IsNullOrEmpty(reason)) reason = OnlineRooms.LastError(networkManager);
            if (m_Busy) return;   // 入る途中の失敗は JoinRoom が表示する
            m_Status = string.IsNullOrEmpty(reason) ? "部屋から切断されました（部屋を作った人が抜けると部屋はなくなります）。" : "切断されました：" + reason;
        }
    }

    /// <summary>エディタ・PC 版で入力したサーバーの URL を覚えておく</summary>
    void SaveUrl()
    {
        if (OnlineRooms.UrlFromPage || m_UrlInput == null) return;
        m_UrlInput = OnlineRooms.NormalizeUrl(m_UrlInput);
        OnlineRooms.ServerUrl = m_UrlInput;
    }

    void Leave()
    {
        networkManager.Shutdown();
        OnlineRooms.Clear();
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
        UIDraw.ApplyFont();
        EnsureStyles();
        bool connected = networkManager.IsClient || networkManager.IsServer;
        if (!connected)
        {
            DrawMenu();
            return;
        }
        var rs = RobotState.Instance;
        bool lobby = rs != null && rs.GameState.Value == RobotState.Lobby;
        if (m_Piloting)
        {
            // 操縦の照準は StationUI が出す。ここでは敵の大技などの警告だけ
            DrawStatus();
            DrawBossWarnings(true);
            DrawBuffBanner();
            return;
        }
        if (m_Spectating)
        {
            DrawStatus();
            DrawBossWarnings(true);
            DrawBuffBanner();
            DrawSpectatorBanner();
            DrawEnd();
            return;
        }
        DrawSplitLine();
        DrawInteriorLabels();
        if (lobby)
        {
            DrawLobby();
        }
        else
        {
            DrawMinimap();
            DrawTaskList();
            DrawStatus();
            DrawEnd();
        }
        DrawPrompt();
        DrawTeleportFlash();
        DrawBuffBanner();
        if (!lobby) DrawBossWarnings(false);
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
        float w = Mathf.Min(640f * s, Screen.width - 40f), h = Mathf.Min(600f * s, Screen.height - 40f);
        var r = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        DrawRect(r, new Color(0.06f, 0.08f, 0.1f, 0.92f));
        GUILayout.BeginArea(new Rect(r.x + 24 * s, r.y + 20 * s, r.width - 48 * s, r.height - 40 * s));
        GUILayout.Label("タイタンクルー", m_Title);
        GUILayout.Label("巨大ロボットの中を走り回り、脚・腕・頭部砲・動力炉の席に乗り込んで、1体のロボットを動かそう。巨大な敵を倒せば勝ち。", m_Label);
        GUILayout.Space(12 * s);
        bool old = GUI.enabled;
        GUI.enabled = !m_Busy;

        if (OnlineRooms.Available)
        {
            GUILayout.Label($"<b>みんなで遊ぶ</b>（最大{MaxPlayers}人）", m_Label);
            if (!OnlineRooms.UrlFromPage)
            {
                // エディタ・PC 版：どのサーバーにつなぐか（Web 版は開いたページのサーバーに自動でつなぐ）
                if (m_UrlInput == null) m_UrlInput = OnlineRooms.ServerUrl;
                GUILayout.BeginHorizontal();
                GUILayout.Label("サーバー", m_Label, GUILayout.Width(90 * s));
                m_UrlInput = GUILayout.TextField(m_UrlInput, 120, m_Field, GUILayout.Width(330 * s), GUILayout.Height(32 * s));
                GUILayout.EndHorizontal();
                GUILayout.Label("<color=#93a6ae>自分のPCで server を起動したときは ws://localhost:8080/ws のまま。公開したサーバーなら、そのアドレス（例：my-titan.onrender.com）</color>", m_Small);
                GUILayout.Space(6 * s);
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("部屋を作る", m_Button, GUILayout.Width(240 * s), GUILayout.Height(44 * s))) CreateRoom(true);
            GUILayout.Label(OnlineRooms.UsesRelay ? "6文字の部屋番号がもらえるので、友だちに教えよう" : "4けたの部屋番号がもらえるので、友だちに教えよう", m_Small);
            GUILayout.EndHorizontal();
            GUILayout.Space(10 * s);
            GUILayout.BeginHorizontal();
            GUILayout.Label("部屋番号", m_Label, GUILayout.Width(90 * s));
            m_RoomInput = OnlineRooms.Normalize(GUILayout.TextField(m_RoomInput, 8, m_Field, GUILayout.Width(150 * s), GUILayout.Height(40 * s)));
            GUILayout.Space(10 * s);
            if (GUILayout.Button("参加する", m_Button, GUILayout.Width(160 * s), GUILayout.Height(40 * s))) JoinRoom();
            GUILayout.EndHorizontal();
            GUILayout.Space(16 * s);
        }
        else
        {
            GUILayout.Label("<color=#93a6ae>いまは1人用。部屋番号でみんなと遊ぶには Netcode for GameObjects のパッケージが必要です（README の「オンラインの準備」）。</color>", m_Small);
            GUILayout.Space(10 * s);
        }

        GUILayout.Label("<b>1人で遊ぶ</b>　全部の席を1人で受け持つ。脚のレバーは降りても残る", m_Label);
        if (GUILayout.Button("1人で出撃", m_Button, GUILayout.Width(240 * s), GUILayout.Height(40 * s))) StartSolo();
        GUI.enabled = old;
        GUILayout.Space(10 * s);

        if (!string.IsNullOrEmpty(m_Status)) GUILayout.Label("<color=#ffb000>" + m_Status + "</color>", m_Label);
        GUILayout.FlexibleSpace();
        GUILayout.Label("操作：A/D で足場を歩く　はしごの前で W/S で登り降り　E で席に乗る・降りる　Space でアクション　Tab で内部の全体図", m_Small);
        GUILayout.EndArea();
    }

    // ================================================================ 待機室

    /// <summary>
    /// 試合前の待機室の画面。左上にルール、下に部屋番号と人数、ホストには「試合開始」ボタン。
    /// 右半分（外）には出撃前の格納庫と、集まった乗組員の一覧を出す。
    /// </summary>
    void DrawLobby()
    {
        var rs = RobotState.Instance;
        float s = m_Scale;
        float half = Screen.width * 0.5f;
        int n = CrewPlayer.All.Count;

        // ---- 左上：ルール
        var rules = new GUIStyle(m_Small) { wordWrap = false };
        rules.normal.textColor = new Color(0.86f, 0.9f, 0.92f);
        var rr = new Rect(12 * s, 34 * s, 270 * s, 150 * s);
        DrawRect(rr, new Color(0.04f, 0.06f, 0.07f, 0.7f));
        GUI.Label(new Rect(rr.x + 10 * s, rr.y + 6 * s, rr.width, rr.height),
            "<b>出撃の設定</b>\n" +
            $"最大人数：{MaxPlayers}人\n" +
            $"操縦席：{Stations.Count}（脚2・腕2・頭部砲・動力炉）\n" +
            $"敵の体力：{RobotState.BossMaxHp:0}\n" +
            "敵の遠距離攻撃：なし\n" +
            "途中参加：できる（ロボットの中に出る）", rules);

        // ---- 下：部屋番号・人数・試合開始
        float bw = Mathf.Min(half - 24 * s, 560 * s), bh = 64 * s;
        var bar = new Rect((half - bw) * 0.5f, Screen.height - bh - 104 * s, bw, bh);
        DrawRect(bar, new Color(0.04f, 0.06f, 0.07f, 0.85f));
        string code = OnlineRooms.RoomCode;
        GUI.Label(new Rect(bar.x + 14 * s, bar.y + 4 * s, 120 * s, 20 * s), "部屋番号", m_Small);
        var codeStyle = new GUIStyle(m_Big) { alignment = TextAnchor.MiddleLeft, fontSize = Mathf.RoundToInt(30 * s) };
        codeStyle.normal.textColor = Color.white;
        GUI.Label(new Rect(bar.x + 14 * s, bar.y + 22 * s, 190 * s, 40 * s), string.IsNullOrEmpty(code) ? "－" : code, codeStyle);
        var countStyle = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(28 * s) };
        countStyle.normal.textColor = Prims.Hex("#7dff6a");
        GUI.Label(new Rect(bar.x + 200 * s, bar.y + 8 * s, 110 * s, 48 * s), $"{n}/{MaxPlayers}", countStyle);
        var btn = new Rect(bar.xMax - 200 * s, bar.y + 8 * s, 186 * s, 48 * s);
        if (networkManager.IsServer)
        {
            var bs = new GUIStyle(m_Button) { fontSize = Mathf.RoundToInt(20 * s) };
            var oldc = GUI.backgroundColor;
            GUI.backgroundColor = Prims.Hex("#4cd964");
            if (GUI.Button(btn, "試合開始", bs) && rs != null) rs.StartMatchRpc();
            GUI.backgroundColor = oldc;
        }
        else
        {
            var wait = new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(btn, "ホストが試合を\n始めるのを待っています", wait);
        }

        // ---- 右半分：出撃準備中と、乗組員の一覧
        DrawRect(new Rect(half, 0, half, Screen.height), new Color(0.02f, 0.03f, 0.04f, 0.45f));
        m_Big.normal.textColor = Prims.Hex("#ffb000");
        GUI.Label(new Rect(half, Screen.height * 0.12f, half, 50 * s), "出撃準備中", m_Big);
        var listStyle = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleLeft };
        float lw = 280 * s, ly = Screen.height * 0.12f + 70 * s;
        float lx = half + (half - lw) * 0.5f;
        foreach (var p in CrewPlayer.All)
        {
            var row = new Rect(lx, ly, lw, 34 * s);
            DrawRect(row, new Color(0.06f, 0.08f, 0.1f, 0.85f));
            DrawRect(new Rect(row.x, row.y, 10 * s, row.height), p.Color);
            bool host = p.OwnerClientId == NetworkManager.ServerClientId;
            bool me = p == CrewPlayer.Local;
            GUI.Label(new Rect(row.x + 20 * s, row.y, row.width - 24 * s, row.height),
                $"<b>{p.Label}</b>{(host ? "　<color=#ffb000>ホスト</color>" : "")}{(me ? "　<color=#93a6ae>（あなた）</color>" : "")}", listStyle);
            ly += 40 * s;
        }
        var hint = new GUIStyle(m_Small) { alignment = TextAnchor.UpperCenter };
        GUI.Label(new Rect(half + 20 * s, ly + 10 * s, half - 40 * s, 60 * s),
            networkManager.IsServer ? "全員そろったら「試合開始」。みんなロボットの中心へ転送される" : "部屋番号を友だちに教えて、いっしょに乗り込もう", hint);

        if (GUI.Button(new Rect(Screen.width - 130 * s, 10 * s, 116 * s, 30 * s), "部屋を出る", m_Button)) Leave();
    }

    /// <summary>転送された直後に左半分を白く光らせる</summary>
    /// <summary>
    /// 敵の大技（メガフレア）とチャンスタイムを大きく知らせる。
    /// 大技：逃げろ＋止めるのに必要なダメージのゲージ、チャンスタイム：残り時間
    /// </summary>
    void DrawBossWarnings(bool fullScreen)
    {
        var rs = RobotState.Instance;
        if (rs == null || !rs.BossAlive || rs.GameState.Value != RobotState.Playing) return;
        float s = m_Scale;
        float x0 = fullScreen ? 0f : Screen.width * 0.5f, w = fullScreen ? Screen.width : Screen.width * 0.5f;
        float y = fullScreen ? Screen.height - 150 * s : Screen.height - 170 * s;
        var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(24 * s) };
        if (rs.BossStun.Value > 0f)
        {
            var r = new Rect(x0 + w * 0.5f - 260 * s, y, 520 * s, 64 * s);
            DrawRect(r, new Color(0.15f, 0.12f, 0.02f, 0.85f));
            DrawRect(new Rect(r.x, r.y, r.width, 4 * s), Prims.Hex("#ffe14a"));
            big.normal.textColor = Prims.Hex("#ffe14a");
            GUI.Label(new Rect(r.x, r.y + 2 * s, r.width, 34 * s), $"チャンスタイム！　あと {Mathf.CeilToInt(rs.BossStun.Value)} 秒", big);
            GUI.Label(new Rect(r.x, r.y + 36 * s, r.width, 24 * s), $"<color=#fff3b0>敵が倒れている。いまはダメージ {RobotState.StunDamageMul:0.0} 倍！ 全力で攻撃しよう</color>", new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter });
            return;
        }
        if (rs.BossAction.Value != RobotState.BossMega) return;
        float left = RobotVisual.Instance != null ? Mathf.Max(0f, RobotState.MegaChargeTime - RobotVisual.Instance.BossActionTime) : 0f;
        if (left <= 0f) return;
        var rr = new Rect(x0 + w * 0.5f - 280 * s, y, 560 * s, 84 * s);
        float blink = 0.7f + Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.3f;
        DrawRect(rr, new Color(0.2f, 0.02f, 0.04f, 0.85f));
        DrawRect(new Rect(rr.x, rr.y, rr.width, 4 * s), Prims.Hex("#ff3b3b") * blink);
        big.normal.textColor = Prims.Hex("#ff6a6a") * blink;
        GUI.Label(new Rect(rr.x, rr.y + 2 * s, rr.width, 32 * s), $"⚠ 大技 メガフレア！ 赤い円から逃げろ（{left:0.0} 秒）", big);
        // 止めるのに必要なダメージ
        float br = Mathf.Clamp01(rs.MegaBreak.Value / RobotState.MegaBreakNeed);
        var bar = new Rect(rr.x + 20 * s, rr.y + 44 * s, rr.width - 40 * s, 14 * s);
        DrawRect(bar, new Color(0, 0, 0, 0.6f));
        DrawRect(new Rect(bar.x, bar.y, bar.width * br, bar.height), Prims.Hex("#ffe14a"));
        GUI.Label(new Rect(rr.x, rr.y + 60 * s, rr.width, 22 * s), "<color=#fff3b0>ためているあいだに大ダメージ（パンチ・ロケットパンチ・必殺技）を与えると止められる → チャンスタイム</color>", new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter, wordWrap = false });
    }

    /// <summary>観戦中の案内（画面の下）</summary>
    void DrawSpectatorBanner()
    {
        var rs = RobotState.Instance;
        if (rs == null || rs.GameState.Value != RobotState.Playing) return;
        float s = m_Scale;
        var r = new Rect(Screen.width * 0.5f - 300 * s, Screen.height - 90 * s, 600 * s, 70 * s);
        DrawRect(r, new Color(0.04f, 0.05f, 0.06f, 0.8f));
        var big = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(24 * s) };
        big.normal.textColor = Prims.Hex("#ffb000");
        GUI.Label(new Rect(r.x, r.y + 4 * s, r.width, 34 * s), "観戦中", big);
        GUI.Label(new Rect(r.x, r.y + 38 * s, r.width, 26 * s), "ロケットパンチで腕と一緒に飛んでいった。次の試合から、また乗り込める", new GUIStyle(m_Small) { alignment = TextAnchor.MiddleCenter });
    }

    int m_LastBuffId = -1;
    float m_BuffShownAt = -10f;

    /// <summary>エネルギーセルを入れて強化されたとき、外の画面に大きく知らせる（だれが入れても全員に出る）</summary>
    void DrawBuffBanner()
    {
        var rs = RobotState.Instance;
        if (rs == null) return;
        if (m_LastBuffId < 0) m_LastBuffId = rs.BuffId.Value;   // 途中から入ったときは出さない
        if (rs.BuffId.Value != m_LastBuffId) { m_LastBuffId = rs.BuffId.Value; m_BuffShownAt = Time.time; }
        float t = Time.time - m_BuffShownAt;
        if (t > 2.5f) return;
        float a = t < 2f ? 1f : 1f - (t - 2f) / 0.5f;
        float s = m_Scale;
        int k = rs.BuffKind.Value;
        var c = EnergyCells.Color(k);
        var r = new Rect(Screen.width * 0.5f + 40 * s, Screen.height * 0.3f, Screen.width * 0.5f - 80 * s, 64 * s);
        DrawRect(r, new Color(0.04f, 0.05f, 0.06f, 0.75f * a));
        DrawRect(new Rect(r.x, r.y, r.width, 4 * s), new Color(c.r, c.g, c.b, a));
        var style = new GUIStyle(m_Big) { fontSize = Mathf.RoundToInt(28 * s) };
        style.normal.textColor = new Color(c.r, c.g, c.b, a);
        GUI.Label(r, $"強化！ {EnergyCells.Effect(k)}", style);
    }

    void DrawTeleportFlash()
    {
        float t = Time.time - CrewPlayer.TeleportTime;
        if (t < 0f || t > 0.9f) return;
        float a = 1f - t / 0.9f;
        DrawRect(new Rect(0, 0, Screen.width * 0.5f, Screen.height), new Color(0.75f, 0.95f, 1f, a * 0.85f));
        m_Big.normal.textColor = new Color(0.1f, 0.3f, 0.4f, a);
        GUI.Label(new Rect(0, Screen.height * 0.4f, Screen.width * 0.5f, 60 * m_Scale), "転送！", m_Big);
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
        // ロケットパンチで飛ばした腕：隠した場所の真ん中に知らせを出す
        for (int k = 0; k < 2; k++)
        {
            if (rs == null || !rs.ArmAway(k)) continue;
            var cover = InteriorMap.ArmCover(k);
            var csp = m_InteriorCam.WorldToScreenPoint(InteriorMap.ToWorld(cover.center, 1f));
            if (csp.x < 0 || csp.x > Screen.width * 0.5f) continue;
            var cr = new Rect(csp.x - 90 * s, Screen.height - csp.y - 26 * s, 180 * s, 52 * s);
            m_Tag.normal.textColor = Prims.Hex("#ffb000");
            GUI.Label(cr, rs.ArmLost(k) ? "（腕がない）\nロケットパンチで飛ばした" : "ロケットパンチ\n飛行中", m_Tag);
        }
        for (int i = 0; i < Stations.Count; i++)
        {
            var def = Stations.All[i];
            if (rs != null && ((i == Stations.LeftArm && rs.ArmAway(0)) || (i == Stations.RightArm && rs.ArmAway(1)))) continue;   // 腕がない
            var labelPos = def.Type == StationType.Core ? InteriorMap.ReactorPos + new Vector2(0f, 2f) : def.Pos + def.ConsoleOffset + new Vector2(0f, 1.4f);
            var sp = m_InteriorCam.WorldToScreenPoint(InteriorMap.ToWorld(labelPos, 1f));
            if (sp.z < 0 || sp.x < 0 || sp.x > Screen.width * 0.5f) continue;
            float w = 110 * s, h = 26 * s;
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h * 0.5f, w, h);
            bool fire = rs != null && rs.FireHp.Count == Stations.Count && rs.FireHp[i] > 0;
            DrawRect(r, fire ? new Color(0.6f, 0.1f, 0.05f, 0.9f) : new Color(0.04f, 0.06f, 0.07f, 0.8f));
            m_Tag.normal.textColor = fire ? Color.white : def.Color;
            GUI.Label(r, fire ? def.Name + " 火災！" : def.Name, m_Tag);
        }
        for (int i = 0; i < Tasks.Count; i++)
        {
            if (rs == null || rs.TaskOk(i)) continue;
            var t = Tasks.All[i];
            var sp = m_InteriorCam.WorldToScreenPoint(InteriorMap.ToWorld(t.Pos + t.PanelOffset + new Vector2(0f, 1.9f), 1f));
            if (sp.x < 0 || sp.x > Screen.width * 0.5f) continue;
            float w = 130 * s, h = 24 * s;
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h, w, h);
            DrawRect(r, new Color(0.6f, 0.1f, 0.05f, 0.9f));
            m_Tag.normal.textColor = Color.white;
            GUI.Label(r, t.Name + " 故障", m_Tag);
        }
        foreach (var p in CrewPlayer.All)
        {
            if (p.Spectating.Value) continue;
            var sp = m_InteriorCam.WorldToScreenPoint(InteriorMap.ToWorld(p.DisplayPos + new Vector2(0f, 1.85f), 1f));
            if (sp.x < 0 || sp.x > Screen.width * 0.5f) continue;
            float w = 40 * s, h = 20 * s;
            var r = new Rect(sp.x - w * 0.5f, Screen.height - sp.y - h - 14 * s, w, h);
            DrawRect(r, p.Color);
            m_Tag.normal.textColor = new Color(0.05f, 0.06f, 0.07f);
            GUI.Label(r, p.Label, m_Tag);
        }
    }

    /// <summary>
    /// 左半分の右上に出す、ロボット内部全体のミニマップ。
    /// 部屋・操作盤・火災・乗組員と、いま左の画面に映っている範囲（白い枠）を表示する。
    /// </summary>
    void DrawMinimap()
    {
        var rs = RobotState.Instance;
        float s = m_Scale;
        var b = InteriorMap.Bounds;
        float size = Mathf.Min(220f * s, Screen.width * 0.5f * 0.38f);
        float k = size / Mathf.Max(b.width, b.height);
        float w = b.width * k, h = b.height * k;
        float pad = 8f * s;
        var frame = new Rect(Screen.width * 0.5f - w - pad * 2f - 12f * s, 12f * s, w + pad * 2f, h + pad * 2f + 18f * s);
        DrawRect(frame, new Color(0.04f, 0.06f, 0.07f, 0.85f));
        GUI.Label(new Rect(frame.x + pad, frame.y + 2f * s, frame.width, 18f * s), "<color=#93a6ae>内部マップ</color>", m_Small);
        float ox = frame.x + pad, oy = frame.y + pad + 18f * s;

        // 地図上の点（メートル）→ 画面上の位置。z が上向きなので y は反転する
        Vector2 ToMap(Vector2 p) => new Vector2(ox + (p.x - b.xMin) * k, oy + (b.yMax - p.y) * k);

        // ロボットの体の絵（装甲と内部）をそのまま縮めて描く
        var lr = InteriorMap.Bounds;
        var lmin = ToMap(new Vector2(lr.xMin, lr.yMax));
        var lmax = ToMap(new Vector2(lr.xMax, lr.yMin));
        var lrect = Rect.MinMaxRect(lmin.x, lmin.y, lmax.x, lmax.y);
        GUI.DrawTexture(lrect, Art.Get("robot_armor").texture);
        var oldc = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.9f);
        GUI.DrawTexture(lrect, Art.Get("robot_back").texture);
        GUI.color = oldc;

        // 足場とはしご
        for (int ci = 0; ci < InteriorMap.RobotCatwalks; ci++)
        {
            var c = InteriorMap.Catwalks[ci];
            var a0 = ToMap(new Vector2(c.X0, c.Y)); var a1 = ToMap(new Vector2(c.X1, c.Y));
            DrawRect(new Rect(a0.x, a0.y - 1f, a1.x - a0.x, Mathf.Max(2f, 2f * s)), new Color(0.75f, 0.8f, 0.85f, 0.9f));
        }
        for (int li = 0; li < InteriorMap.RobotLadders; li++)
        {
            var l = InteriorMap.Ladders[li];
            var b0 = ToMap(new Vector2(l.X, l.Y1)); var b1 = ToMap(new Vector2(l.X, l.Y0));
            DrawRect(new Rect(b0.x - 1f, b0.y, Mathf.Max(2f, 2f * s), b1.y - b0.y), new Color(0.95f, 0.71f, 0.19f, 0.9f));
        }
        // ダクト（黄色い点。近くにいるときは行き先と線でつなぐ）
        var meD = CrewPlayer.Local;
        for (int i = 0; i < Ducts.All.Length; i++)
        {
            var c = ToMap(Ducts.All[i].Pos);
            float d = 6f * s;
            DrawRect(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), Prims.Hex("#ffb000"));
        }
        // エネルギーセル（色つきの丸）と、セルを持っているときは動力炉の場所
        if (rs != null && rs.CellAt.Count == EnergyCells.Count && Mathf.Sin(Time.time * 6f) > -0.5f)
        {
            for (int i = 0; i < EnergyCells.Count; i++)
            {
                if (rs.CellAt[i] < 0) continue;
                var c = ToMap(EnergyCells.Spots[i] + new Vector2(0f, 0.5f));
                float d = 8f * s;
                DrawRect(new Rect(c.x - d * 0.5f - 1, c.y - d * 0.5f - 1, d + 2, d + 2), new Color(0, 0, 0, 0.8f));
                DrawRect(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), EnergyCells.Color(rs.CellAt[i]));
            }
        }
        if (meD != null && meD.Carry.Value >= 0)
        {
            var c = ToMap(EnergyCells.ReactorPos + new Vector2(0f, 1f));
            float d = (10f + Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 6f) * s;
            var col = EnergyCells.Color(meD.Carry.Value);
            DrawRect(new Rect(c.x - d * 0.5f, c.y - 1, d, 2), col);
            DrawRect(new Rect(c.x - 1, c.y - d * 0.5f, 2, d), col);
        }
        if (meD != null && meD.NearDuct >= 0)
        {
            var a0 = ToMap(Ducts.All[meD.NearDuct].Pos);
            var a1 = ToMap(Ducts.All[Ducts.All[meD.NearDuct].Link].Pos);
            for (int dot = 0; dot <= 12; dot++)
            {
                var q = Vector2.Lerp(a0, a1, dot / 12f);
                DrawRect(new Rect(q.x - 1.5f, q.y - 1.5f, 3f, 3f), Prims.Hex("#ffb000"));
            }
        }
        if (rs != null && Mathf.Sin(Time.time * 8f) > -0.3f)
        {
            for (int i = 0; i < Tasks.Count; i++)
            {
                if (rs.TaskOk(i)) continue;
                var c = ToMap(Tasks.All[i].Pos);
                float d = 9f * s;
                DrawRect(new Rect(c.x - d * 0.5f - 1.5f, c.y - d * 0.5f - 1.5f, d + 3f, d + 3f), Color.white);
                DrawRect(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), new Color(0.92f, 0.24f, 0.2f));
            }
        }
        foreach (var p in CrewPlayer.All)
        {
            if (p.Spectating.Value) continue;
            var c = ToMap(p.DisplayPos);
            bool me = p == CrewPlayer.Local;
            float d = (me ? 10f + Mathf.Sin(Time.time * 6f) * 2f : 8f) * s;
            if (me) DrawRect(new Rect(c.x - d * 0.5f - 2f, c.y - d * 0.5f - 2f, d + 4f, d + 4f), Color.white);
            DrawRect(new Rect(c.x - d * 0.5f, c.y - d * 0.5f, d, d), p.Color);
        }
    }

    /// <summary>左半分の左上に、故障中の装置の一覧を出す。</summary>
    void DrawTaskList()
    {
        var rs = RobotState.Instance;
        if (rs == null || rs.TaskBroken.Count != Tasks.Count) return;
        int n = rs.TasksBroken;
        float s = m_Scale;
        float x = 12 * s, y = 32 * s, w = Mathf.Min(300 * s, Screen.width * 0.5f * 0.5f);
        float lineH = 36 * s;
        float h = 26 * s + Mathf.Max(1, n) * lineH;
        DrawRect(new Rect(x - 6 * s, y - 4 * s, w + 12 * s, h + 8 * s), new Color(0.04f, 0.06f, 0.07f, 0.82f));
        GUI.Label(new Rect(x, y, w, 22 * s), n > 0 ? $"<color=#ff8a6a><b>故障中の装置 {n}</b></color>" : "<color=#7dff6a><b>すべての装置が正常</b></color>", m_Small);
        float yy = y + 26 * s;
        for (int i = 0; i < Tasks.Count; i++)
        {
            if (rs.TaskOk(i)) continue;
            var t = Tasks.All[i];
            bool working = TaskUI.CurrentTask == i;
            GUI.Label(new Rect(x, yy, w, lineH), $"<b>{t.Name}</b>（{t.Room}）{(working ? "<color=#ffb000> 修理中</color>" : "")}\n<color=#93a6ae>{t.Effect}</color>", m_Small);
            yy += lineH;
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
        bool lostL = rs.ArmLost(0), lostR = rs.ArmLost(1);
        bool armL = rs.TaskOk(Tasks.WiresL) || lostL, armR = rs.TaskOk(Tasks.WiresR) || lostR;   // 腕がないときは配線の表示はしない
        bool legL = rs.LegWorks(0), legR = rs.LegWorks(1);
        bool boost = rs.PunchBoost.Value > 0f;
        int lines = (armL && armR ? 0 : 1) + (legL && legR ? 0 : 1) + (boost ? 1 : 0) + (lostL || lostR ? 1 : 0);
        DrawRect(new Rect(x - 8 * s, y - 6 * s, w + 16 * s, (136 + lines * 22) * s), new Color(0.06f, 0.08f, 0.1f, 0.85f));
        float ly = y + 128 * s;
        if (!armL || !armR)
        {
            string stopped = !armL && !armR ? "両腕" : !armL ? "左腕" : "右腕";
            GUI.Label(new Rect(x, ly, w, 24 * s), $"<color=#ff6a4a><b>⚠ {stopped}が停止中（肩の配線が切れている）</b></color>", m_Small);
            ly += 22 * s;
        }
        if (!legL || !legR)
        {
            string stopped = !legL && !legR ? "両脚" : !legL ? "左脚" : "右脚";
            GUI.Label(new Rect(x, ly, w, 24 * s), $"<color=#ff6a4a><b>⚠ {stopped}が停止中（ももの油圧が切れている）</b></color>", m_Small);
            ly += 22 * s;
        }
        if (lostL || lostR)
        {
            string lost = lostL && lostR ? "両腕" : lostL ? "左腕" : "右腕";
            GUI.Label(new Rect(x, ly, w, 24 * s), $"<color=#ffb000><b>{lost}がない（ロケットパンチで飛ばした）</b></color>", m_Small);
            ly += 22 * s;
        }
        if (boost)
            GUI.Label(new Rect(x, ly, w, 24 * s), $"<color=#ff6a6a><b>パンチ強化中（あと {Mathf.CeilToInt(rs.PunchBoost.Value)} 秒・ダメージ {EnergyCells.PunchBoostMul:0.0} 倍）</b></color>", m_Small);
        DrawBar(new Rect(x, y, w, 22 * s), rs.Hull.Value / RobotState.MaxHull, Prims.Hex("#ff6a4a"), $"装甲  {Mathf.CeilToInt(rs.Hull.Value)}");
        DrawBar(new Rect(x, y + 30 * s, w, 22 * s), rs.Power.Value / RobotState.MaxPower, Prims.Hex("#d9a920"), $"動力  {Mathf.CeilToInt(rs.Power.Value)}");
        // 必殺ゲージ（満タンで点滅。頭部砲の席で F）
        float spc = Mathf.Clamp01(rs.Special.Value / RobotState.SpecialMax);
        float blink = spc >= 1f ? 0.7f + Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.3f : 1f;
        string spLabel = rs.SpecialCharging.Value ? "必殺技 ため中！" : spc >= 1f ? "必殺技OK！ 頭部砲の席で F" : $"必殺  {Mathf.FloorToInt(spc * 100)}%";
        DrawBar(new Rect(x, y + 60 * s, w, 18 * s), spc, Prims.Hex("#ffd23f") * blink, spLabel);
        int t = Mathf.FloorToInt(rs.Elapsed.Value);
        GUI.Label(new Rect(x, y + 82 * s, w, 24 * s), $"スコア <color=#ffb000>{rs.Score.Value}</color>　{t / 60}:{t % 60:00}", m_Label);
        if (rs.GuardL.Value || rs.GuardR.Value)
        {
            string g = rs.GuardL.Value && rs.GuardR.Value ? "両腕でガード中" : rs.GuardL.Value ? "左腕でガード中" : "右腕でガード中";
            GUI.Label(new Rect(x + w - 130 * s, y + 84 * s, 130 * s, 22 * s), $"<color=#4fd2ff><b>{g}</b></color>", m_Small);
        }
        // 巨大な敵がどの方向・どのくらい先にいるか
        if (rs.BossAlive)
        {
            var to = rs.BossPos.Value - rs.Pos.Value;
            float rel = Mathf.DeltaAngle(rs.Yaw.Value * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
            string dir = Mathf.Abs(rel) < 20f ? "正面" : rel > 0 ? (rel < 100f ? "右前" : "右後ろ") : (rel > -100f ? "左前" : "左後ろ");
            GUI.Label(new Rect(x, y + 106 * s, w, 24 * s), $"<color=#93a6ae>敵：{dir} {Mathf.RoundToInt(to.magnitude)}m</color>", m_Small);
        }

        // 巨大な敵の体力：右半分（外の画面）で、敵の頭のすぐ上に出す。画面の外にいるときは右半分の上の端に出す
        if (rs.BossAlive)
        {
            float bw = 130 * s, bh = 7 * s;
            float halfL = Screen.width * 0.5f;
            Vector2 center = new Vector2(Screen.width * 0.75f, 150 * s);
            bool onScreen = false;
            var over = RobotVisual.Instance != null ? RobotVisual.Instance.BossOverhead : null;
            if (over.HasValue)
            {
                var sp = m_ExteriorCam.WorldToScreenPoint(over.Value);
                if (sp.z > 0f && sp.x > halfL && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height)
                {
                    center = new Vector2(sp.x, Screen.height - sp.y);
                    onScreen = true;
                }
            }
            // はみ出さないように右半分の中へおさめる
            center.x = Mathf.Clamp(center.x, halfL + bw * 0.5f + 8 * s, Screen.width - bw * 0.5f - 8 * s);
            center.y = Mathf.Clamp(center.y, 140 * s, Screen.height - 40 * s);
            var br = new Rect(center.x - bw * 0.5f, center.y - bh * 0.5f, bw, bh);
            // 細いゲージだけ（文字は画面の外にいるときだけ小さく出す）
            DrawRect(new Rect(br.x - 2 * s, br.y - 2 * s, br.width + 4 * s, br.height + 4 * s), new Color(0.04f, 0.05f, 0.06f, 0.85f));
            DrawRect(new Rect(br.x, br.y, br.width * Mathf.Clamp01(rs.BossHp.Value / RobotState.BossMaxHp), br.height), Prims.Hex("#ff3d7f"));
            if (!onScreen)
                GUI.Label(new Rect(br.x, br.y - 18 * s, br.width + 40 * s, 18 * s), "<color=#ff6aa0>巨大な敵（画面の外）</color>", m_Small);
            // 振りかぶっているときは、ゲージの上に警告
            if (rs.BossAction.Value == RobotState.BossSwipe)
            {
                var warn = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
                GUI.Label(new Rect(center.x - 200 * s, br.y - 30 * s, 400 * s, 24 * s), "<color=#ff6a4a><b>⚠ 腕を振りかぶっている！ 赤い範囲から出るか両腕でガード</b></color>", warn);
            }
        }

        string leave = string.IsNullOrEmpty(OnlineRooms.RoomCode) ? "タイトルへ" : "部屋を出る";
        if (GUI.Button(new Rect(Screen.width - 130 * s, 10 * s, 116 * s, 30 * s), leave, m_Button)) Leave();
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
            hint = "操作盤で操縦する";
            hint += "　E で降りる";
        }
        else if (me.NearReactor)
        {
            int k = me.Carry.Value;
            where = "動力炉（エネルギーの入れ口）";
            hint = $"E でエネルギーを入れる → <color={EnergyCells.Hex(k)}><b>{EnergyCells.Effect(k)}</b></color>";
        }
        else if (me.NearCell >= 0 && rs.CellAt.Count == EnergyCells.Count && rs.CellAt[me.NearCell] >= 0)
        {
            int k = rs.CellAt[me.NearCell];
            where = $"<color={EnergyCells.Hex(k)}>{EnergyCells.Name(k)}のエネルギーセル</color>";
            hint = $"E で拾う。体の真ん中の動力炉へ運ぶと <color={EnergyCells.Hex(k)}><b>{EnergyCells.Effect(k)}</b></color>";
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
        else if (me.InDuct.Value)
        {
            where = "ダクトの中";
            hint = "<color=#93a6ae>出口まで移動中…</color>";
        }
        else if (me.NearDuct >= 0)
        {
            var d = Ducts.All[me.NearDuct];
            where = $"ダクト（{d.Name}）";
            hint = $"E でダクトに入る → <color=#ffb000>{Ducts.All[d.Link].Name}</color> に出る";
        }
        else if (me.NearTask >= 0)
        {
            var t = Tasks.All[me.NearTask];
            where = t.Name;
            hint = rs.TaskOk(me.NearTask) ? "<color=#93a6ae>正常に動いている</color>"
                 : $"<color=#ff8a6a>故障中（{t.Effect}）</color>　E で修理する";
        }
        else if (rs.GameState.Value == RobotState.Lobby)
        {
            where = "待機室";
            hint = "A/D で歩く・はしごは W/S。全員そろったらホストが「試合開始」を押す";
        }
        else
        {
            where = InteriorMap.AreaName(me.Pos.Value);
            if (me.Carry.Value >= 0)
                hint = $"<color={EnergyCells.Hex(me.Carry.Value)}>{EnergyCells.Name(me.Carry.Value)}のセル</color>を持っている。胸の真ん中の動力炉（光っている所）へ運ぼう → {EnergyCells.Effect(me.Carry.Value)}";
            else hint = rs.TasksBroken > 0 ? "A/D で歩く・はしごは W/S。故障した装置（赤い ！）を直そう" : "A/D で歩く・はしごは W/S。光っている席に近づこう";
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
        GUI.Label(new Rect(0, Screen.height * 0.36f, Screen.width, 60 * s), won ? "巨大な敵を倒した！" : rs.Hull.Value > 0f ? "操縦する人がいなくなった…" : "タイタン大破", m_Big);
        int t = Mathf.FloorToInt(rs.Elapsed.Value);
        var info = $"スコア {rs.Score.Value}　時間 {t / 60}:{t % 60:00}\n" +
                   (networkManager.IsServer ? (RobotState.UseLobby ? "R キーで待機室に戻る" : "R キーでもう一度出撃")
                                            : "ホストが待機室に戻すのを待っています");
        var style = new GUIStyle(m_Label) { alignment = TextAnchor.MiddleCenter };
        GUI.Label(new Rect(0, Screen.height * 0.36f + 70 * s, Screen.width, 60 * s), info, style);
    }
}
