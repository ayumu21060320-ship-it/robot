using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 操縦席に座っているあいだ、画面の左半分に出す操作盤。
/// 操作盤の上でレバーを引いたりダイヤルを回したりした結果を、
/// CrewPlayer がホストへ送る入力（Stick / ActHeld / ActCount）に変える。
///
///   脚　　：スロットルレバー（W/S かドラッグ）・速度計・両脚のレバー・曲がる向き・ブレーキ
///   腕　　：狙いのダイヤル（敵の方向と射程）・パンチレバー（引いて、離して叩き込む）
///   頭部砲：レーダー（敵の位置）・砲塔の向き（A/D かクリック）・Space 長押しで連射
///   動力炉：圧力計の針が緑のときに Space（大きく発電）
/// </summary>
public class StationUI : MonoBehaviour
{
    public static StationUI Instance { get; private set; }

    /// <summary>操作盤が出ているか（座っているか）</summary>
    public bool Active => m_Station >= 0;
    public Vector2 Stick { get; private set; }
    public bool Held { get; private set; }
    int m_Presses;
    /// <summary>このフレームまでにたまった「ボタンを押した回数」を受け取る</summary>
    public int TakePresses() { int p = m_Presses; m_Presses = 0; return p; }

    int m_Station = -1;
    // 脚
    float m_LegTarget = float.NaN;
    bool m_DragLever;
    Rect m_LeverTrack, m_BrakeRect;
    // 腕
    float m_Pull, m_PunchFlash, m_PunchCd;
    bool m_DragPunch, m_MouseGuard;
    Rect m_GuardRect;
    Rect m_PunchTrack, m_DialRect;
    float m_AimTarget = float.NaN;
    // 頭部砲
    Rect m_RadarRect;
    float m_HeadTarget = float.NaN;
    // 動力炉
    float m_CoreT, m_CoreFlash, m_TurbineAngle, m_TurbineSpeed;
    string m_CoreMsg = "";
    bool m_MouseFire;

    const float GoodZone = 0.12f;   // 動力炉の緑の範囲（針の位置 0～1 の真ん中 ± これ）

    void Awake() => Instance = this;

    void ResetPanel()
    {
        m_LegTarget = m_AimTarget = m_HeadTarget = float.NaN;
        m_DragLever = m_DragPunch = m_MouseFire = m_MouseGuard = false;
        m_Pull = 0f;
        m_Presses = 0;
        m_CoreMsg = "";
    }

    void Update()
    {
        var me = CrewPlayer.Local;
        var rs = RobotState.Instance;
        int st = me != null && rs != null && rs.GameState.Value == RobotState.Playing ? me.Station.Value : -1;
        if (st != m_Station) { m_Station = st; ResetPanel(); }
        if (st < 0) { Stick = Vector2.zero; Held = false; return; }

        float dt = Time.deltaTime;
        var kb = Keyboard.current;
        var gp = Gamepad.current;
        float x = 0f, y = 0f;
        bool space = false, spaceDown = false, spaceUp = false;
        if (kb != null)
        {
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            space = kb.spaceKey.isPressed;
            spaceDown = kb.spaceKey.wasPressedThisFrame;
            spaceUp = kb.spaceKey.wasReleasedThisFrame;
        }
        if (gp != null)
        {
            var s = gp.leftStick.ReadValue();
            if (s.magnitude > 0.2f) { x += s.x; y += s.y; }
            space |= gp.buttonSouth.isPressed;
            spaceDown |= gp.buttonSouth.wasPressedThisFrame;
            spaceUp |= gp.buttonSouth.wasReleasedThisFrame;
        }
        x = Mathf.Clamp(x, -1f, 1f);
        y = Mathf.Clamp(y, -1f, 1f);

        var def = Stations.All[st];
        switch (def.Type)
        {
            case StationType.Leg:
            {
                // 油圧が切れているあいだは、脚が動かないので操作盤も効かない
                if (!rs.LegWorks(st == Stations.LeftLeg ? 0 : 1))
                {
                    Stick = Vector2.zero; Held = false; m_LegTarget = float.NaN; m_DragLever = false;
                    break;
                }
                float cur = st == Stations.LeftLeg ? rs.LegL.Value : rs.LegR.Value;
                if (Mathf.Abs(y) > 0.1f) { m_LegTarget = float.NaN; Stick = new Vector2(0f, y); }
                else if (!float.IsNaN(m_LegTarget))
                {
                    // ドラッグで決めた位置へレバーを動かす
                    float diff = m_LegTarget - cur;
                    Stick = new Vector2(0f, Mathf.Abs(diff) < 0.015f ? 0f : Mathf.Clamp(diff * 8f, -1f, 1f));
                    if (Mathf.Abs(diff) < 0.015f && !m_DragLever) m_LegTarget = float.NaN;
                }
                else Stick = Vector2.zero;
                if (spaceDown) { m_Presses++; m_LegTarget = float.NaN; }   // ブレーキ（レバーを 0 へ）
                Held = false;
                break;
            }
            case StationType.Arm:
            {
                // 肩の配線が切れているあいだは、腕が動かないので操作盤も効かない
                if (!rs.TaskOk(st == Stations.LeftArm ? Tasks.WiresL : Tasks.WiresR))
                {
                    Stick = Vector2.zero; Held = false; m_Pull = 0f; m_DragPunch = false; m_MouseGuard = false; m_AimTarget = float.NaN;
                    break;
                }
                int armK = st == Stations.LeftArm ? 0 : 1;
                // ロケットパンチを飛ばしているあいだ：A/D で左右、W/S で上下に拳を操縦する
                if (rs.ArmAway(armK))
                {
                    Stick = new Vector2(x, y);
                    Held = false; m_Pull = 0f; m_DragPunch = false; m_MouseGuard = false; m_AimTarget = float.NaN;
                    break;
                }
                // ロケットパンチの発射：F（ゲームパッドは Y）
                if ((kb != null && kb.fKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame)) TryLaunchRocket(rs, armK);
                // 狙い：A/D か、ダイヤルをクリックした向きへ
                float aim = st == Stations.LeftArm ? rs.AimL.Value : rs.AimR.Value;
                if (Mathf.Abs(x) > 0.1f) { m_AimTarget = float.NaN; Stick = new Vector2(x, 0f); }
                else if (!float.IsNaN(m_AimTarget))
                {
                    float diff = m_AimTarget - aim;
                    Stick = new Vector2(Mathf.Abs(diff) < 0.01f ? 0f : Mathf.Clamp(diff * 6f, -1f, 1f), 0f);
                    if (Mathf.Abs(diff) < 0.01f) m_AimTarget = float.NaN;
                }
                else Stick = Vector2.zero;

                // パンチレバー：Space か S を押しているあいだ引いて、離すと叩き込む（引ききっていないと空振り）
                m_PunchCd = Mathf.Max(0f, m_PunchCd - dt);
                m_PunchFlash = Mathf.Max(0f, m_PunchFlash - dt * 2f);
                // ガード：W（ゲームパッドは LB）か、ガードボタンを押しているあいだ。ガード中はパンチできない
                bool guard = y > 0.5f || m_MouseGuard || (gp != null && gp.leftShoulder.isPressed);
                bool pulling = !guard && (space || y < -0.5f || m_DragPunch) && m_PunchCd <= 0f;
                if (pulling) m_Pull = Mathf.Min(1f, m_Pull + dt / 0.55f);
                bool release = !pulling && m_Pull > 0f;
                if (release)
                {
                    if (m_Pull >= 0.85f)
                    {
                        m_Presses++;
                        m_PunchFlash = 1f;
                        m_PunchCd = 2.2f;
                        m_Pull = 0f;
                    }
                    else m_Pull = Mathf.Max(0f, m_Pull - dt * 3f); // バネで戻る
                }
                if (guard) m_Pull = Mathf.Max(0f, m_Pull - dt * 3f);
                Held = guard;
                break;
            }
            case StationType.Head:
            {
                float aim = rs.AimHead.Value;
                if (Mathf.Abs(x) > 0.1f) { m_HeadTarget = float.NaN; Stick = new Vector2(x, 0f); }
                else if (!float.IsNaN(m_HeadTarget))
                {
                    float diff = m_HeadTarget - aim;
                    Stick = new Vector2(Mathf.Abs(diff) < 0.01f ? 0f : Mathf.Clamp(diff * 5f, -1f, 1f), 0f);
                    if (Mathf.Abs(diff) < 0.01f) m_HeadTarget = float.NaN;
                }
                else Stick = Vector2.zero;
                Held = space || m_MouseFire;
                // 必殺技：F キー（ゲームパッドは Y）
                bool fire = (kb != null && kb.fKey.wasPressedThisFrame) || (gp != null && gp.buttonNorth.wasPressedThisFrame);
                if (fire) TryFireSpecial(rs);
                break;
            }
            case StationType.Core:
            {
                m_CoreT += dt;
                m_CoreFlash = Mathf.Max(0f, m_CoreFlash - dt * 2.5f);
                if (spaceDown) PressCore();
                m_TurbineSpeed = Mathf.MoveTowards(m_TurbineSpeed, 60f, dt * 120f);
                m_TurbineAngle += m_TurbineSpeed * dt;
                Stick = Vector2.zero;
                Held = false;
                break;
            }
        }
    }

    float m_RocketConfirmT = -10f;   // 1回目を押した時刻（3秒以内にもう一度押すと発射）

    /// <summary>ロケットパンチは一度きり（腕も自分も戻らない）なので、2回押したら発射する</summary>
    void TryLaunchRocket(RobotState rs, int k)
    {
        if (rs.ArmAway(k) || rs.Power.Value < 10f) return;
        if (Time.time - m_RocketConfirmT > 3f) { m_RocketConfirmT = Time.time; return; }
        m_RocketConfirmT = -10f;
        rs.LaunchRocketRpc();
    }

    void TryFireSpecial(RobotState rs)
    {
        if (rs.Special.Value < RobotState.SpecialMax || rs.SpecialCharging.Value) return;
        rs.FireSpecialRpc();
    }

    float CoreNeedle() => 0.5f + Mathf.Sin(m_CoreT * 2.4f) * 0.48f;

    void PressCore()
    {
        bool good = Mathf.Abs(CoreNeedle() - 0.5f) < GoodZone;
        m_Presses += good ? 3 : 1;
        m_CoreFlash = 1f;
        m_CoreMsg = good ? "ぴったり！ 動力 +9" : "ずれた… 動力 +3";
        m_TurbineSpeed += good ? 500f : 150f;
    }

    // ================================================================ 表示

    GUIStyle m_Title, m_Text, m_Small, m_Center;
    float m_Scale;

    void Styles(float s)
    {
        if (m_Title != null && Mathf.Approximately(s, m_Scale)) return;
        m_Scale = s;
        m_Title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(21 * s), fontStyle = FontStyle.Bold, richText = true };
        m_Title.normal.textColor = Prims.Hex("#ffb000");
        m_Text = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), wordWrap = true, richText = true };
        m_Text.normal.textColor = new Color(0.86f, 0.9f, 0.92f);
        m_Small = new GUIStyle(m_Text) { fontSize = Mathf.RoundToInt(12.5f * s) };
        m_Small.normal.textColor = new Color(0.62f, 0.7f, 0.74f);
        m_Center = new GUIStyle(m_Text) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
    }

    void OnGUI()
    {
        if (!Active || TaskUI.IsOpen) return;
        UIDraw.ApplyFont();
        var rs = RobotState.Instance;
        if (rs == null) return;
        GUI.depth = -5;
        float s = Mathf.Max(0.7f, Screen.height / 900f);
        Styles(s);

        var def = Stations.All[m_Station];
        // ロケットパンチを操縦しているあいだは、操作盤のかわりに全画面の照準を出す
        if (def.Type == StationType.Arm)
        {
            int k = m_Station == Stations.LeftArm ? 0 : 1;
            if (rs.RocketState(k) == RobotState.RocketFlying) { DrawRocketHud(rs, k, s); return; }
        }
        float half = Screen.width * 0.5f;
        float w = Mathf.Min(half - 36f * s, 520f * s), h = Mathf.Min(w * 0.86f, Screen.height - 220f * s);
        var panel = new Rect((half - w) * 0.5f, Mathf.Max(60f * s, (Screen.height - h) * 0.5f - 40f * s), w, h);
        UIDraw.Fill(panel, new Color(0.1f, 0.12f, 0.14f, 0.97f));
        UIDraw.Frame(panel, def.Color, 4f * s);
        // ボルト
        foreach (var c in new[] { new Vector2(panel.x + 12 * s, panel.y + 12 * s), new Vector2(panel.xMax - 12 * s, panel.y + 12 * s),
                                  new Vector2(panel.x + 12 * s, panel.yMax - 12 * s), new Vector2(panel.xMax - 12 * s, panel.yMax - 12 * s) })
            UIDraw.Dot(c, 7 * s, new Color(0.4f, 0.44f, 0.48f));

        GUI.Label(new Rect(panel.x + 24 * s, panel.y + 12 * s, w - 48 * s, 28 * s), $"{def.Name} 操作盤　<size={Mathf.RoundToInt(13 * s)}><color=#93a6ae>E：席を降りる</color></size>", m_Title);
        var area = new Rect(panel.x + 22 * s, panel.y + 48 * s, w - 44 * s, h - 66 * s);

        switch (def.Type)
        {
            case StationType.Leg: DrawLeg(area, s, rs); break;
            case StationType.Arm: DrawArm(area, s, rs); break;
            case StationType.Head: DrawHead(area, s, rs); break;
            case StationType.Core: DrawCore(area, s, rs); break;
        }
    }

    // ---------------- 脚

    void DrawLeg(Rect a, float s, RobotState rs)
    {
        bool left = m_Station == Stations.LeftLeg;
        float cur = left ? rs.LegL.Value : rs.LegR.Value;
        float other = left ? rs.LegR.Value : rs.LegL.Value;
        bool hydraulicOk = rs.LegWorks(left ? 0 : 1);

        // スロットルレバー（-50% ～ 100%）
        var track = new Rect(a.x + 18 * s, a.y + 10 * s, 70 * s, a.height - 80 * s);
        m_LeverTrack = track;
        UIDraw.Fill(track, new Color(0.05f, 0.06f, 0.07f));
        float Y(float v) => track.yMax - (v + 0.5f) / 1.5f * track.height;
        for (int p = -50; p <= 100; p += 25)
        {
            float yy = Y(p / 100f);
            UIDraw.Fill(new Rect(track.xMax + 4 * s, yy - 1, (p % 50 == 0 ? 14 : 8) * s, 2), new Color(1, 1, 1, 0.5f));
            if (p % 50 == 0) GUI.Label(new Rect(track.xMax + 20 * s, yy - 10 * s, 60 * s, 20 * s), p + "%", m_Small);
        }
        UIDraw.Fill(new Rect(track.x, Y(0f) - 1.5f, track.width, 3f), Prims.Hex("#ffb000"));
        float fillTop = Mathf.Min(Y(cur), Y(0f)), fillBot = Mathf.Max(Y(cur), Y(0f));
        UIDraw.Fill(new Rect(track.x + 10 * s, fillTop, track.width - 20 * s, fillBot - fillTop), cur >= 0f ? Prims.Hex("#4fd2ff") : Prims.Hex("#ff6a4a"));
        if (!float.IsNaN(m_LegTarget)) UIDraw.Frame(new Rect(track.x - 6 * s, Y(m_LegTarget) - 5 * s, track.width + 12 * s, 10 * s), new Color(1, 1, 1, 0.6f), 2f);
        var knob = new Rect(track.x - 10 * s, Y(cur) - 13 * s, track.width + 20 * s, 26 * s);
        UIDraw.Fill(knob, Prims.Hex("#ffb000"));
        UIDraw.Frame(knob, new Color(0.1f, 0.1f, 0.1f), 2f);
        GUI.Label(new Rect(track.x - 10 * s, track.yMax + 6 * s, 140 * s, 22 * s), $"<b>{Mathf.RoundToInt(cur * 100)}%</b>", m_Text);

        // マウスでレバーをつかんで動かす
        var e = Event.current;
        if (hydraulicOk && e.type == EventType.MouseDown && track.Contains(e.mousePosition)) { m_DragLever = true; e.Use(); }
        if (e.type == EventType.MouseUp) m_DragLever = false;
        if (m_DragLever && (e.type == EventType.MouseDrag || e.type == EventType.MouseDown))
            m_LegTarget = Mathf.Clamp((track.yMax - e.mousePosition.y) / track.height * 1.5f - 0.5f, -0.5f, 1f);

        // 右側：速度計・両脚のレバー・曲がる向き
        float rx = a.x + 150 * s, rw = a.xMax - rx;
        var c = new Vector2(rx + rw * 0.5f, a.y + 100 * s);
        float r = Mathf.Min(rw * 0.42f, 85 * s);
        UIDraw.Arc(c, r, -110f, 110f, 6 * s, new Color(1, 1, 1, 0.25f));
        float sp = rs.MoveSpeed.Value / RobotState.WalkSpeedMax;
        UIDraw.Arc(c, r, -110f, Mathf.Lerp(-110f, 110f, Mathf.Clamp01((sp + 0.5f) / 1.5f)), 6 * s, Prims.Hex("#4fd2ff"));
        UIDraw.Line(c, UIDraw.Point(c, r * 0.9f, Mathf.Lerp(-110f, 110f, Mathf.Clamp01((sp + 0.5f) / 1.5f))), 4 * s, Prims.Hex("#ff6a4a"));
        UIDraw.Dot(c, 12 * s, Prims.Hex("#c8d0d6"));
        GUI.Label(new Rect(c.x - 80 * s, c.y + 14 * s, 160 * s, 22 * s), $"速さ {rs.MoveSpeed.Value:0.0} m/秒", m_Center);

        // 両脚のレバー（自分と相方）
        float by = a.y + 210 * s, bh = 70 * s;
        for (int i = 0; i < 2; i++)
        {
            bool mine = (i == 0) == left;
            float v = i == 0 ? rs.LegL.Value : rs.LegR.Value;
            var bar = new Rect(rx + 30 * s + i * 70 * s, by, 26 * s, bh);
            UIDraw.Fill(bar, new Color(0.05f, 0.06f, 0.07f));
            float zero = bar.yMax - bar.height / 3f;
            float top = zero - Mathf.Max(0f, v) * bar.height * 2f / 3f, bot = zero + Mathf.Max(0f, -v) * bar.height * 2f / 3f;
            UIDraw.Fill(new Rect(bar.x, top, bar.width, bot - top), mine ? Prims.Hex("#4fd2ff") : new Color(0.4f, 0.6f, 0.7f));
            if (mine) UIDraw.Frame(bar, Prims.Hex("#ffb000"), 2f);
            GUI.Label(new Rect(bar.x - 14 * s, bar.yMax + 2 * s, 60 * s, 20 * s), (i == 0 ? "左脚" : "右脚"), m_Small);
        }
        float turn = rs.LegL.Value - rs.LegR.Value;
        string dir = Mathf.Abs(turn) < 0.05f ? "まっすぐ" : turn > 0 ? "右へ曲がる →" : "← 左へ曲がる";
        GUI.Label(new Rect(rx + 170 * s, by + 18 * s, rw - 170 * s, 40 * s), $"<b>{dir}</b>", m_Text);

        // ブレーキボタン
        m_BrakeRect = new Rect(rx + 20 * s, a.yMax - 52 * s, rw - 40 * s, 40 * s);
        UIDraw.Fill(m_BrakeRect, new Color(0.55f, 0.12f, 0.08f));
        UIDraw.Frame(m_BrakeRect, Prims.Hex("#ff6a4a"), 2f);
        GUI.Label(m_BrakeRect, "ブレーキ（Space）", m_Center);
        if (hydraulicOk && e.type == EventType.MouseDown && m_BrakeRect.Contains(e.mousePosition)) { m_Presses++; m_LegTarget = float.NaN; e.Use(); }

        string help = "W/S かレバーをドラッグして倒す。両脚そろえると前進、差をつけると曲がる";
        GUI.Label(new Rect(rx, by + bh + 26 * s, rw, 46 * s), help, m_Small);

        // ももの油圧が切れていると脚が動かない：操作盤全体を赤く覆って知らせる
        if (!hydraulicOk)
        {
            float blink = 0.55f + Mathf.Abs(Mathf.Sin(Time.time * 3f)) * 0.25f;
            UIDraw.Fill(a, new Color(0.35f, 0.04f, 0.02f, blink));
            UIDraw.Frame(a, Prims.Hex("#ff6a4a"), 4 * s);
            var big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(22 * s) };
            big.normal.textColor = Color.white;
            string side = left ? "左" : "右";
            GUI.Label(new Rect(a.x, a.center.y - 50 * s, a.width, 40 * s), $"⚠ {side}脚が動かない", big);
            GUI.Label(new Rect(a.x, a.center.y, a.width, 50 * s), $"{side}ももの油圧が切れている。\n{side}もも（腰の下の段）の装置を直そう", m_Center);
        }
    }

    // ---------------- 腕

    void DrawArm(Rect a, float s, RobotState rs)
    {
        bool left = m_Station == Stations.LeftArm;
        float aim = left ? rs.AimL.Value : rs.AimR.Value;
        var e = Event.current;

        // 狙いのダイヤル（上が正面、-0.8 ～ 0.8 ラジアン）。敵の方向も表示
        var c = new Vector2(a.x + a.width * 0.32f, a.y + a.height * 0.55f);
        float r = Mathf.Min(a.width * 0.28f, a.height * 0.45f);
        m_DialRect = new Rect(c.x - r, c.y - r, r * 2f, r);
        float maxDeg = 0.8f * Mathf.Rad2Deg;
        UIDraw.Arc(c, r, -maxDeg, maxDeg, 8 * s, new Color(1, 1, 1, 0.18f));
        for (float d = -40f; d <= 40.1f; d += 20f) UIDraw.Line(UIDraw.Point(c, r * 0.86f, d), UIDraw.Point(c, r, d), 2f, new Color(1, 1, 1, 0.4f));
        // 敵の方向と距離
        var to = rs.BossPos.Value - rs.Pos.Value;
        float rel = Mathf.DeltaAngle(rs.Yaw.Value * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
        float dist = to.magnitude;
        float reach = 8.4f * RobotState.TitanScale + RobotState.BossRadius + 2f;
        bool inRange = rs.BossAlive && dist < reach && Mathf.Abs(Mathf.DeltaAngle(rel, aim * Mathf.Rad2Deg)) < 40f;
        if (rs.BossAlive)
        {
            float shown = Mathf.Clamp(rel, -maxDeg - 15f, maxDeg + 15f);
            UIDraw.Dot(UIDraw.Point(c, r + 14 * s, shown), 14 * s, Prims.Hex("#ff3d7f"));
            GUI.Label(new Rect(c.x - r, c.y + 12 * s, r * 2f, 22 * s), inRange ? "<color=#7dff6a><b>敵が射程内！</b></color>" : $"<color=#93a6ae>敵まで {Mathf.RoundToInt(dist)}m</color>", m_Center);
        }
        UIDraw.Line(c, UIDraw.Point(c, r * 0.95f, aim * Mathf.Rad2Deg), 6 * s, Prims.Hex("#ff9a3c"));
        UIDraw.Dot(c, 16 * s, Prims.Hex("#c8d0d6"));
        GUI.Label(new Rect(c.x - r, c.y - r - 30 * s, r * 2f, 22 * s), "狙い（A/D・クリック）", m_Center);
        if (e.type == EventType.MouseDown && Vector2.Distance(e.mousePosition, c) < r * 1.2f && e.mousePosition.y < c.y)
        {
            var v = e.mousePosition - c;
            m_AimTarget = Mathf.Clamp(Mathf.Atan2(v.x, -v.y), -0.8f, 0.8f);
            e.Use();
        }

        // パンチレバー（下へ引くほどため。離すと叩き込む）
        var track = new Rect(a.x + a.width * 0.72f, a.y + 20 * s, 60 * s, a.height - 110 * s);
        m_PunchTrack = track;
        UIDraw.Fill(track, new Color(0.05f, 0.06f, 0.07f));
        UIDraw.Fill(new Rect(track.x, track.y + track.height * 0.85f, track.width, track.height * 0.15f), new Color(0.3f, 0.75f, 0.35f, 0.4f));
        float ky = track.y + m_Pull * (track.height - 30 * s);
        UIDraw.Line(new Vector2(track.center.x, track.y), new Vector2(track.center.x, ky + 15 * s), 10 * s, new Color(0.5f, 0.55f, 0.6f));
        var knob = new Rect(track.x - 14 * s, ky, track.width + 28 * s, 30 * s);
        UIDraw.Fill(knob, m_PunchCd > 0f ? new Color(0.4f, 0.4f, 0.42f) : Prims.Hex("#ff9a3c"));
        UIDraw.Frame(knob, new Color(0.1f, 0.1f, 0.1f), 2f);
        if (m_PunchFlash > 0f) UIDraw.Frame(new Rect(track.x - 20 * s, track.y - 6 * s, track.width + 40 * s, track.height + 12 * s), new Color(1f, 0.85f, 0.3f, m_PunchFlash), 4 * s);
        string state = m_PunchCd > 0f ? $"冷却中 {m_PunchCd:0.0}秒" : m_Pull >= 0.85f ? "<color=#7dff6a>離して叩き込め！</color>" : m_Pull > 0f ? "引いている…" : "パンチレバー";
        GUI.Label(new Rect(track.x - 60 * s, track.yMax + 6 * s, track.width + 120 * s, 22 * s), $"<b>{state}</b>", m_Center);
        if (e.type == EventType.MouseDown && knob.Contains(e.mousePosition)) { m_DragPunch = true; e.Use(); }
        if (e.type == EventType.MouseUp) m_DragPunch = false;

        // ガードボタン（押しているあいだ）
        bool guarding = left ? rs.GuardL.Value : rs.GuardR.Value;
        bool other = left ? rs.GuardR.Value : rs.GuardL.Value;
        m_GuardRect = new Rect(c.x - r, c.y + 64 * s, r * 2f, 40 * s);
        UIDraw.Fill(m_GuardRect, guarding ? Prims.Hex("#4fd2ff") : new Color(0.16f, 0.3f, 0.4f));
        UIDraw.Frame(m_GuardRect, guarding ? Color.white : Prims.Hex("#4fd2ff"), 2f);
        var gs = new GUIStyle(m_Center);
        gs.normal.textColor = guarding ? new Color(0.04f, 0.1f, 0.14f) : Color.white;
        GUI.Label(m_GuardRect, guarding ? (other ? "両腕でガード中！" : "ガード中") : "ガード（W 長押し）", gs);
        if (e.type == EventType.MouseDown && m_GuardRect.Contains(e.mousePosition)) { m_MouseGuard = true; e.Use(); }
        if (e.type == EventType.MouseUp) m_MouseGuard = false;
        if (rs.BossAction.Value == RobotState.BossSwipe)
        {
            float bl = Mathf.Abs(Mathf.Sin(Time.time * 8f));
            GUI.Label(new Rect(c.x - r - 40 * s, m_GuardRect.y - 25 * s, r * 2f + 80 * s, 22 * s), "<color=#ff6a4a><b>⚠ 敵が振りかぶっている！ ガード！</b></color>", m_Center);
            if (!guarding) UIDraw.Frame(new Rect(m_GuardRect.x - 4 * s, m_GuardRect.y - 4 * s, m_GuardRect.width + 8 * s, m_GuardRect.height + 8 * s), new Color(1f, 0.4f, 0.3f, bl), 3f * s);
        }

        // ロケットパンチの発射ボタン（タイトルの行の右）
        {
            int k = left ? 0 : 1;
            bool ready = rs.Power.Value >= 10f;
            bool confirm = Time.time - m_RocketConfirmT <= 3f;
            var rb = new Rect(a.xMax - 220 * s, a.y - 40 * s, 220 * s, 32 * s);
            float rbl = ready ? 0.8f + Mathf.Abs(Mathf.Sin(Time.time * (confirm ? 12f : 4f))) * 0.2f : 1f;
            UIDraw.Fill(rb, ready ? (confirm ? Prims.Hex("#ff2a2a") : Prims.Hex("#ff5a2a")) * rbl : new Color(0.25f, 0.27f, 0.3f));
            UIDraw.Frame(rb, ready ? Color.white : new Color(0.1f, 0.1f, 0.1f), 2f);
            var rs2 = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(14 * s) };
            rs2.normal.textColor = ready ? Color.white : new Color(0.62f, 0.66f, 0.7f);
            GUI.Label(rb, !ready ? "動力が足りない" : confirm ? "もう一度押すと発射！" : "ロケットパンチ（F）", rs2);
            if (confirm)
                GUI.Label(new Rect(rb.x - 120 * s, rb.yMax + 2 * s, rb.width + 120 * s, 20 * s),
                    "<color=#ff8a6a>一度きり：腕は戻らず、あなたはこの試合を観戦になる</color>", new GUIStyle(m_Small) { alignment = TextAnchor.UpperRight, wordWrap = false });
            if (ready && e.type == EventType.MouseDown && rb.Contains(e.mousePosition)) { TryLaunchRocket(rs, k); e.Use(); }
        }

        string help = "Space（か S・レバーをドラッグ）で引いて、引ききったら離すとパンチ。W 長押しでガード。F を2回でロケットパンチ（一度きりの切り札）";
        GUI.Label(new Rect(a.x, a.yMax - 44 * s, a.width, 44 * s), help, m_Small);

        // 肩の配線が切れていると腕が動かない：操作盤全体を赤く覆って知らせる
        if (!rs.TaskOk(left ? Tasks.WiresL : Tasks.WiresR))
        {
            float blink = 0.55f + Mathf.Abs(Mathf.Sin(Time.time * 3f)) * 0.25f;
            UIDraw.Fill(a, new Color(0.35f, 0.04f, 0.02f, blink));
            UIDraw.Frame(a, Prims.Hex("#ff6a4a"), 4 * s);
            var big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(22 * s) };
            big.normal.textColor = Color.white;
            string side = left ? "左" : "右";
            GUI.Label(new Rect(a.x, a.center.y - 50 * s, a.width, 40 * s), $"⚠ {side}腕が動かない", big);
            GUI.Label(new Rect(a.x, a.center.y, a.width, 50 * s), $"{side}肩の配線が切れている。\n{side}肩（胸の上の段の{(left ? "左" : "右")}端）の装置を直そう", m_Center);
        }
    }

    // ---------------- ロケットパンチの操縦（全画面）

    void DrawRocketHud(RobotState rs, int k, float s)
    {
        float W = Screen.width, H = Screen.height;
        var c = new Vector2(W * 0.5f, H * 0.5f);
        bool flying = rs.RocketState(k) == RobotState.RocketFlying;
        var green = new Color(0.55f, 1f, 0.45f, 0.9f);
        // 照準
        UIDraw.Arc(c, 34 * s, 0f, 360f, 2f, green, 32);
        UIDraw.Line(c + new Vector2(-60 * s, 0), c + new Vector2(-40 * s, 0), 2f, green);
        UIDraw.Line(c + new Vector2(40 * s, 0), c + new Vector2(60 * s, 0), 2f, green);
        UIDraw.Line(c + new Vector2(0, -60 * s), c + new Vector2(0, -40 * s), 2f, green);
        UIDraw.Line(c + new Vector2(0, 40 * s), c + new Vector2(0, 60 * s), 2f, green);
        UIDraw.Dot(c, 5 * s, green);

        // 上：状態と操作
        var top = new Rect(W * 0.5f - 300 * s, 16 * s, 600 * s, 70 * s);
        UIDraw.Fill(top, new Color(0.04f, 0.05f, 0.06f, 0.7f));
        var big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(22 * s) };
        big.normal.textColor = flying ? Prims.Hex("#ffb000") : new Color(0.7f, 0.75f, 0.8f);
        GUI.Label(new Rect(top.x, top.y + 4 * s, top.width, 32 * s), $"ロケットパンチ操縦中　あと {rs.RocketLife(k):0.0} 秒", big);
        GUI.Label(new Rect(top.x, top.y + 38 * s, top.width, 26 * s), "A / D：左右に曲がる　　W / S：上 / 下　　敵に体当たりで大ダメージ", m_Center);

        // 敵の方向（拳の向きから見て）。正面から外れているときは、画面のふちに矢印
        if (rs.BossAlive && flying)
        {
            var p = rs.RocketPos(k);
            var to = rs.BossPos.Value - new Vector2(p.x, p.z);
            float rel = Mathf.DeltaAngle(rs.RocketYaw(k) * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
            string where = Mathf.Abs(rel) < 12f ? "<color=#7dff6a><b>正面！</b></color>" : rel > 0 ? $"右 {Mathf.RoundToInt(rel)}°" : $"左 {Mathf.RoundToInt(-rel)}°";
            GUI.Label(new Rect(c.x - 150 * s, c.y + 70 * s, 300 * s, 26 * s), $"敵：{where}　{Mathf.RoundToInt(to.magnitude)}m", m_Center);
            if (Mathf.Abs(rel) >= 12f)
            {
                float side = Mathf.Sign(rel);
                var ac = new Vector2(c.x + side * Mathf.Min(W * 0.42f, 420 * s), c.y);
                float blink = 0.6f + Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.4f;
                var red = Prims.Hex("#ff3d7f") * blink;
                UIDraw.Line(ac + new Vector2(-side * 24 * s, -22 * s), ac, 6 * s, red);
                UIDraw.Line(ac + new Vector2(-side * 24 * s, 22 * s), ac, 6 * s, red);
            }
        }
    }

    // ---------------- 頭部砲

    void DrawHead(Rect a, float s, RobotState rs)
    {
        var e = Event.current;
        float r = Mathf.Min(a.width * 0.38f, (a.height - 70 * s) * 0.5f);
        var c = new Vector2(a.x + a.width * 0.42f, a.y + r + 6 * s);
        m_RadarRect = new Rect(c.x - r, c.y - r, r * 2f, r * 2f);
        const float range = 160f;
        // レーダーの円と距離の輪（50m ごと）
        UIDraw.Arc(c, r, 0f, 360f, 2f, new Color(0.5f, 1f, 0.35f, 0.25f), 48);
        for (int ring = 1; ring <= 3; ring++) UIDraw.Arc(c, r * ring * 50f / range, 0f, 360f, 1f, new Color(0.5f, 1f, 0.35f, 0.18f), 32);
        UIDraw.Line(new Vector2(c.x - r, c.y), new Vector2(c.x + r, c.y), 1f, new Color(0.5f, 1f, 0.35f, 0.15f));
        UIDraw.Line(new Vector2(c.x, c.y - r), new Vector2(c.x, c.y + r), 1f, new Color(0.5f, 1f, 0.35f, 0.15f));
        // 回るスキャン線
        float sweep = Time.time * 90f % 360f;
        UIDraw.Line(c, UIDraw.Point(c, r, sweep), 2f, new Color(0.5f, 1f, 0.35f, 0.35f));
        // 砲塔の向き
        float aimDeg = rs.AimHead.Value * Mathf.Rad2Deg;
        bool fuseOk = rs.TaskOk(Tasks.Fuse);
        UIDraw.Line(c, UIDraw.Point(c, r, aimDeg), 3 * s, fuseOk ? Prims.Hex("#b8ff5a") : new Color(0.5f, 0.5f, 0.5f));
        // 敵の位置（タイタンの正面が上）
        if (rs.BossAlive)
        {
            var to = rs.BossPos.Value - rs.Pos.Value;
            float rel = Mathf.DeltaAngle(rs.Yaw.Value * Mathf.Rad2Deg, Mathf.Atan2(to.x, to.y) * Mathf.Rad2Deg);
            float d = Mathf.Min(to.magnitude, range);
            float blink = 0.6f + Mathf.Abs(Mathf.Sin(Time.time * 4f)) * 0.4f;
            UIDraw.Dot(UIDraw.Point(c, r * d / range, rel), 16 * s, Prims.Hex("#ff3d7f") * blink);
            bool aimed = Mathf.Abs(Mathf.DeltaAngle(rel, aimDeg)) < 6f;
            GUI.Label(new Rect(c.x + r + 10 * s, c.y - 40 * s, a.xMax - c.x - r - 10 * s, 80 * s),
                $"敵\n{Mathf.RoundToInt(to.magnitude)}m\n{(aimed ? "<color=#7dff6a><b>照準OK</b></color>" : "<color=#93a6ae>ずれている</color>")}", m_Text);
        }
        UIDraw.Dot(c, 12 * s, Prims.Hex("#ffb000"));   // 自分（タイタン）

        if (e.type == EventType.MouseDown && m_RadarRect.Contains(e.mousePosition))
        {
            var v = e.mousePosition - c;
            m_HeadTarget = Mathf.Clamp(Mathf.Atan2(v.x, -v.y), -1.7f, 1.7f);
            m_MouseFire = e.button == 1; // 右クリックを押しているあいだも撃てる
            e.Use();
        }
        if (e.type == EventType.MouseUp) m_MouseFire = false;

        // ---- 必殺技ゲージと発射ボタン（右の列）
        float colX = c.x + r + 10 * s, colW = a.xMax - colX;
        float sp = Mathf.Clamp01(rs.Special.Value / RobotState.SpecialMax);
        bool charging = rs.SpecialCharging.Value, full = sp >= 1f && !charging;
        GUI.Label(new Rect(colX, c.y + 46 * s, colW, 20 * s), "必殺ゲージ", m_Small);
        var gauge = new Rect(colX, c.y + 66 * s, colW - 4 * s, 14 * s);
        UIDraw.Fill(gauge, new Color(0.05f, 0.06f, 0.07f));
        float gb = full ? 0.75f + Mathf.Abs(Mathf.Sin(Time.time * 6f)) * 0.25f : 1f;
        UIDraw.Fill(new Rect(gauge.x, gauge.y, gauge.width * sp, gauge.height), Prims.Hex("#ffd23f") * gb);
        UIDraw.Frame(gauge, new Color(1, 1, 1, 0.4f), 1.5f);
        var btn = new Rect(colX, gauge.yMax + 8 * s, colW - 4 * s, 44 * s);
        UIDraw.Fill(btn, charging ? Prims.Hex("#fff3b0") : full ? Prims.Hex("#ff9a3c") * gb : new Color(0.25f, 0.27f, 0.3f));
        UIDraw.Frame(btn, full ? Color.white : new Color(0.1f, 0.1f, 0.1f), 2f);
        var bs = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(14 * s) };
        bs.normal.textColor = full || charging ? new Color(0.12f, 0.08f, 0.02f) : new Color(0.6f, 0.64f, 0.68f);
        GUI.Label(btn, charging ? "ため中…" : "必殺技（F）", bs);
        if (full && e.type == EventType.MouseDown && btn.Contains(e.mousePosition)) { TryFireSpecial(rs); e.Use(); }
        if (rs.BossAlive)
        {
            var to2 = rs.BossPos.Value - rs.Pos.Value;
            float rel2 = Mathf.DeltaAngle(rs.Yaw.Value * Mathf.Rad2Deg, Mathf.Atan2(to2.x, to2.y) * Mathf.Rad2Deg);
            bool front = Mathf.Abs(rel2) < 8f;
            GUI.Label(new Rect(colX, btn.yMax + 4 * s, colW, 40 * s),
                front ? "<color=#7dff6a><b>胴の正面に敵</b></color>" : "<color=#93a6ae>胴の正面に\n敵がいない</color>", m_Small);
        }

        string help = "A/D かレーダーをクリックして砲塔を回す。Space 長押しで連射";
        help += "\n必殺ビームは胸から胴の正面へ撃つ（脚の係に向きを合わせてもらおう）";
        if (!fuseOk) help = "<color=#ff8a6a>ヒューズが切れていて撃てない（腰の装置を直そう）</color>";
        else if (!rs.TaskOk(Tasks.Scope)) help += "\n<color=#ff8a6a>照準がずれている（頭の装置を直そう）</color>";
        GUI.Label(new Rect(a.x, a.yMax - 58 * s, a.width, 58 * s), help, m_Small);
    }

    // ---------------- 動力炉

    void DrawCore(Rect a, float s, RobotState rs)
    {
        var e = Event.current;
        // 圧力計：針が緑の範囲にあるときに押すと大きく発電
        var c = new Vector2(a.x + a.width * 0.36f, a.y + a.height * 0.5f);
        float r = Mathf.Min(a.width * 0.3f, a.height * 0.42f);
        UIDraw.Arc(c, r, -120f, 120f, 10 * s, new Color(1, 1, 1, 0.15f));
        float zoneA = Mathf.Lerp(-120f, 120f, 0.5f - GoodZone), zoneB = Mathf.Lerp(-120f, 120f, 0.5f + GoodZone);
        UIDraw.Arc(c, r, zoneA, zoneB, 12 * s, new Color(0.3f, 0.85f, 0.35f));
        float needle = Mathf.Lerp(-120f, 120f, CoreNeedle());
        UIDraw.Line(c, UIDraw.Point(c, r * 0.95f, needle), 5 * s, Prims.Hex("#ff5a4a"));
        UIDraw.Dot(c, 16 * s, Prims.Hex("#c8d0d6"));
        if (m_CoreFlash > 0f) UIDraw.Arc(c, r + 12 * s, -120f, 120f, 4 * s, new Color(1f, 0.85f, 0.3f, m_CoreFlash));
        GUI.Label(new Rect(c.x - r, c.y + 20 * s, r * 2f, 24 * s), $"<b>{m_CoreMsg}</b>", m_Center);
        var press = new Rect(c.x - r * 0.6f, c.y + r * 0.55f, r * 1.2f, 40 * s);
        UIDraw.Fill(press, Prims.Hex("#d9a920"));
        UIDraw.Frame(press, new Color(0.1f, 0.1f, 0.1f), 2f);
        GUI.Label(press, "発電（Space）", m_Center);
        if (e.type == EventType.MouseDown && press.Contains(e.mousePosition)) { PressCore(); e.Use(); }

        // 右側：タービンと動力計
        var tc = new Vector2(a.x + a.width * 0.8f, a.y + 70 * s);
        float tr = 46 * s;
        UIDraw.Arc(tc, tr, 0f, 360f, 3f, new Color(1, 1, 1, 0.4f), 32);
        for (int b = 0; b < 6; b++) UIDraw.Line(tc, UIDraw.Point(tc, tr * 0.9f, m_TurbineAngle + b * 60f), 6 * s, Prims.Hex("#ffd23f"));
        UIDraw.Dot(tc, 12 * s, Prims.Hex("#c8d0d6"));
        var bar = new Rect(tc.x - 20 * s, tc.y + tr + 20 * s, 40 * s, a.yMax - (tc.y + tr + 20 * s) - 50 * s);
        UIDraw.Fill(bar, new Color(0.05f, 0.06f, 0.07f));
        float p = rs.Power.Value / RobotState.MaxPower;
        UIDraw.Fill(new Rect(bar.x, bar.yMax - bar.height * p, bar.width, bar.height * p), p < 0.2f ? Prims.Hex("#ff6a4a") : Prims.Hex("#ffd23f"));
        GUI.Label(new Rect(bar.x - 40 * s, bar.yMax + 4 * s, bar.width + 80 * s, 22 * s), $"動力 {Mathf.CeilToInt(rs.Power.Value)}", m_Center);

        string help = "針が緑のときに Space（+9）。ずれると +3。座っているだけでも少しずつ回復";
        if (!rs.TaskOk(Tasks.Valve)) help += "\n<color=#ff8a6a>冷却バルブが故障中：動力が早く減る</color>";
        GUI.Label(new Rect(a.x, a.yMax - 44 * s, a.width * 0.62f, 44 * s), help, m_Small);
    }
}
