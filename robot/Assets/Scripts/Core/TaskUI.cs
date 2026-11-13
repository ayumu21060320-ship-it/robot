using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 整備タスクのミニゲーム。画面の左半分（ロボット内部）の上に重ねて出す。
/// 自分の画面の中だけで遊び、やり終えたら RobotState.CompleteTaskRpc でホストに伝える。
/// 操作はキーボード（A/D/W/S/Space）が基本で、配線とヒューズはマウスのクリックでもできる。
/// </summary>
public class TaskUI : MonoBehaviour
{
    public static TaskUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance.m_Task >= 0;

    int m_Task = -1;
    int m_OpenedFrame = -1;
    /// <summary>閉じたフレーム。同じ E キーでまた開いてしまわないように使う。</summary>
    public static int ClosedFrame { get; private set; } = -1;
    public static int CurrentTask => Instance != null ? Instance.m_Task : -1;
    TaskKind m_Kind;
    float m_DoneT = -1f;   // やり終えてから閉じるまでの演出
    float m_FailT;         // 失敗したときの赤い点滅
    float m_T;

    // バルブ
    float m_ValveAngle, m_ValveShown;
    int m_ValveLast;       // 最後に押したのが A(-1) か D(+1) か
    // 配線
    static readonly Color[] WireColors = { new Color(0.86f, 0.24f, 0.2f), new Color(0.24f, 0.47f, 0.86f), new Color(0.94f, 0.78f, 0.16f), new Color(0.31f, 0.78f, 0.35f) };
    static readonly string[] WireNames = { "赤", "青", "黄", "緑" };
    int[] m_WireLeft = new int[4], m_WireRight = new int[4];
    bool[] m_WireDone = new bool[4];
    int m_WireCursor, m_WirePicked = -1;
    // 油圧ゲージ・照準
    int m_Hits;
    float m_ZoneCenter, m_Speed, m_Phase;
    // ヒューズ
    static readonly int[] FuseAmps = { 10, 15, 20, 30 };
    int[] m_Fuses = new int[4];
    int m_FuseTarget, m_FuseCursor, m_FuseRound;
    // 排熱ダクト
    int m_Debris;
    Vector2[] m_DebrisPos = new Vector2[12];
    // ジャイロ
    float m_GyroX, m_GyroV, m_GyroTime;

    // 修理はさっと終わるように簡単め（ロボットの操縦がメイン）
    const int GaugeHits = 2, ScopeHits = 2, FuseRounds = 1, DebrisCount = 6;
    const float ValveTurns = 1f, GyroNeed = 2f;
    const float GaugeZone = 22f, ScopeZone = 26f, GyroZone = 0.34f;   // 当たりの範囲の半分（度／割合）

    void Awake() => Instance = this;

    public void Open(int task)
    {
        m_Task = task;
        m_OpenedFrame = Time.frameCount;
        m_Kind = Tasks.All[task].Kind;
        m_DoneT = -1f;
        m_FailT = 0f;
        m_T = 0f;
        m_Hits = 0;
        m_ValveAngle = m_ValveShown = 0f;
        m_ValveLast = 0;
        switch (m_Kind)
        {
            case TaskKind.Wires:
                Shuffle(m_WireLeft); Shuffle(m_WireRight);
                for (int i = 0; i < 4; i++) m_WireDone[i] = false;
                m_WireCursor = 0; m_WirePicked = -1;
                break;
            case TaskKind.Gauge:
                m_ZoneCenter = Random.Range(-50f, 50f); m_Speed = 1.6f; m_Phase = Random.value * 6f;
                break;
            case TaskKind.Scope:
                m_ZoneCenter = Random.Range(0f, 360f); m_Speed = 90f; m_Phase = Random.Range(0f, 360f);
                break;
            case TaskKind.Fuse:
                m_FuseRound = 0; NewFuseRound();
                break;
            case TaskKind.Vent:
                m_Debris = DebrisCount;
                for (int i = 0; i < DebrisCount; i++) m_DebrisPos[i] = new Vector2(Random.Range(0.1f, 0.9f), Random.Range(0.15f, 0.85f));
                break;
            case TaskKind.Gyro:
                m_GyroX = Random.value < 0.5f ? -0.6f : 0.6f; m_GyroV = 0f; m_GyroTime = 0f;
                break;
        }
    }

    public void Close()
    {
        if (m_Task < 0) return;
        m_Task = -1;
        ClosedFrame = Time.frameCount;
    }

    static void Shuffle(int[] a)
    {
        for (int i = 0; i < a.Length; i++) a[i] = i;
        for (int i = a.Length - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (a[i], a[j]) = (a[j], a[i]); }
    }

    void NewFuseRound()
    {
        Shuffle(m_Fuses);
        m_FuseTarget = Random.Range(0, 4);
        m_FuseCursor = 0;
    }

    // ================================================================ 入力と進み具合

    void Update()
    {
        if (!IsOpen) return;
        var rs = RobotState.Instance;
        // ゲームが終わった、または誰かが先に直したら閉じる
        if (rs == null || rs.GameState.Value != RobotState.Playing || (m_DoneT < 0f && rs.TaskOk(m_Task))) { Close(); return; }

        if (Time.frameCount == m_OpenedFrame) return; // 開いたときのキーをミニゲームの操作に数えない
        float dt = Time.deltaTime;
        m_T += dt;
        m_FailT = Mathf.Max(0f, m_FailT - dt);
        if (m_DoneT >= 0f)
        {
            m_DoneT += dt;
            if (m_DoneT > 0.6f) Close();
            return;
        }

        var kb = Keyboard.current;
        var gp = Gamepad.current;
        bool Pressed(params Key[] keys) { if (kb == null) return false; foreach (var k in keys) if (kb[k].wasPressedThisFrame) return true; return false; }
        bool left = Pressed(Key.A, Key.LeftArrow) || (gp != null && gp.dpad.left.wasPressedThisFrame);
        bool right = Pressed(Key.D, Key.RightArrow) || (gp != null && gp.dpad.right.wasPressedThisFrame);
        bool up = Pressed(Key.W, Key.UpArrow) || (gp != null && gp.dpad.up.wasPressedThisFrame);
        bool down = Pressed(Key.S, Key.DownArrow) || (gp != null && gp.dpad.down.wasPressedThisFrame);
        bool act = Pressed(Key.Space, Key.Enter) || (gp != null && gp.buttonSouth.wasPressedThisFrame);
        bool close = Pressed(Key.E, Key.Escape) || (gp != null && gp.buttonEast.wasPressedThisFrame);
        if (close) { Close(); return; }

        switch (m_Kind)
        {
            case TaskKind.Valve:
                // A と D を交互に押すと回る
                if (left && m_ValveLast != -1) { m_ValveAngle += 36f; m_ValveLast = -1; }
                if (right && m_ValveLast != 1) { m_ValveAngle += 36f; m_ValveLast = 1; }
                m_ValveShown = Mathf.Lerp(m_ValveShown, m_ValveAngle, 1f - Mathf.Exp(-12f * dt));
                if (m_ValveAngle >= ValveTurns * 360f) Succeed();
                break;

            case TaskKind.Wires:
                if (up) m_WireCursor = (m_WireCursor + 3) % 4;
                if (down) m_WireCursor = (m_WireCursor + 1) % 4;
                if (act) ChooseWire(m_WireCursor);
                break;

            case TaskKind.Gauge:
                if (act)
                {
                    if (Mathf.Abs(GaugeNeedle() - m_ZoneCenter) < GaugeZone) { m_Hits++; m_ZoneCenter = Random.Range(-50f, 50f); m_Speed += 0.2f; }
                    else m_FailT = 0.3f;   // 外れても減らない
                    if (m_Hits >= GaugeHits) Succeed();
                }
                break;

            case TaskKind.Scope:
                if (act)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(ScopeMarker(), m_ZoneCenter)) < ScopeZone) { m_Hits++; m_ZoneCenter = Random.Range(0f, 360f); m_Speed += 15f; }
                    else m_FailT = 0.3f;
                    if (m_Hits >= ScopeHits) Succeed();
                }
                break;

            case TaskKind.Fuse:
                if (left) m_FuseCursor = (m_FuseCursor + 3) % 4;
                if (right) m_FuseCursor = (m_FuseCursor + 1) % 4;
                if (act) ChooseFuse(m_FuseCursor);
                break;

            case TaskKind.Vent:
                if (act && m_Debris > 0) { m_Debris--; if (m_Debris == 0) Succeed(); }
                break;

            case TaskKind.Gyro:
            {
                // ゆらゆら動く印を、A/D で真ん中の枠に入れておく
                float push = 0f;
                if (kb != null) { if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) push -= 1f; if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) push += 1f; }
                if (gp != null) push += gp.leftStick.ReadValue().x;
                m_GyroV += (Mathf.Sin(m_T * 1.3f) * 0.7f + Mathf.Sin(m_T * 3.1f) * 0.35f + push * 2.6f) * dt;
                m_GyroV *= Mathf.Exp(-1.5f * dt);
                m_GyroX = Mathf.Clamp(m_GyroX + m_GyroV * dt, -1f, 1f);
                if (Mathf.Abs(m_GyroX) >= 1f) m_GyroV = 0f;
                if (Mathf.Abs(m_GyroX) < GyroZone) m_GyroTime += dt;
                if (m_GyroTime >= GyroNeed) Succeed();
                break;
            }
        }
    }

    float GaugeNeedle() => Mathf.Sin(m_T * m_Speed + m_Phase) * 80f;
    float ScopeMarker() => Mathf.Repeat(m_Phase + m_T * m_Speed, 360f);

    void ChooseWire(int row)
    {
        if (m_WirePicked < 0)
        {
            if (!m_WireDone[m_WireLeft[row]]) { m_WirePicked = row; m_WireCursor = 0; }
            return;
        }
        int color = m_WireLeft[m_WirePicked];
        if (m_WireRight[row] == color)
        {
            m_WireDone[color] = true;
            m_WirePicked = -1;
            bool all = true;
            foreach (var d in m_WireDone) all &= d;
            if (all) Succeed();
        }
        else
        {
            m_WirePicked = -1;
            m_FailT = 0.3f;
        }
    }

    void ChooseFuse(int slot)
    {
        m_FuseCursor = slot;
        if (m_Fuses[slot] == m_FuseTarget)
        {
            m_FuseRound++;
            if (m_FuseRound >= FuseRounds) Succeed(); else NewFuseRound();
        }
        else m_FailT = 0.3f;
    }

    void Succeed()
    {
        m_DoneT = 0f;
        var rs = RobotState.Instance;
        if (rs != null) rs.CompleteTaskRpc(m_Task);
    }

    // ================================================================ 表示

    GUIStyle m_Title, m_Text, m_Center;
    float m_Scale;

    static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    static void Frame(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    /// <summary>a から b へ太さ w の線を引く</summary>
    static void Line(Vector2 a, Vector2 b, float w, Color c)
    {
        var m = GUI.matrix;
        float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(ang, a);
        Fill(new Rect(a.x, a.y - w * 0.5f, (b - a).magnitude, w), c);
        GUI.matrix = m;
    }

    void OnGUI()
    {
        if (!IsOpen) return;
        UIDraw.ApplyFont();
        GUI.depth = -10;
        float s = Mathf.Max(0.7f, Screen.height / 900f);
        if (m_Title == null || !Mathf.Approximately(s, m_Scale))
        {
            m_Scale = s;
            m_Title = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * s), fontStyle = FontStyle.Bold, richText = true };
            m_Title.normal.textColor = Prims.Hex("#ffb000");
            m_Text = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(15 * s), wordWrap = true, richText = true };
            m_Text.normal.textColor = new Color(0.85f, 0.9f, 0.92f);
            m_Center = new GUIStyle(m_Text) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        }

        var def = Tasks.All[m_Task];
        float half = Screen.width * 0.5f;
        float w = Mathf.Min(half - 40f * s, 520f * s), h = w * 0.82f + 90f * s;
        var panel = new Rect((half - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        Fill(new Rect(0, 0, half, Screen.height), new Color(0, 0, 0, 0.45f));
        Fill(panel, new Color(0.11f, 0.13f, 0.15f, 0.97f));
        Frame(panel, m_FailT > 0f ? new Color(0.9f, 0.25f, 0.2f) : m_DoneT >= 0f ? new Color(0.4f, 0.9f, 0.4f) : Prims.Hex("#ffb000"), 4f * s);

        GUI.Label(new Rect(panel.x + 18 * s, panel.y + 12 * s, w - 36 * s, 30 * s), $"{def.Name}<size={Mathf.RoundToInt(14 * s)}><color=#93a6ae>　{def.Room}</color></size>", m_Title);
        GUI.Label(new Rect(panel.x + 18 * s, panel.y + 44 * s, w - 36 * s, 40 * s), Instruction() + "　<color=#93a6ae>E：閉じる</color>", m_Text);
        var area = new Rect(panel.x + 18 * s, panel.y + 86 * s, w - 36 * s, h - 104 * s);
        Fill(area, new Color(0.07f, 0.08f, 0.09f));

        switch (m_Kind)
        {
            case TaskKind.Valve: DrawValve(area); break;
            case TaskKind.Wires: DrawWires(area, s); break;
            case TaskKind.Gauge: DrawGauge(area, s); break;
            case TaskKind.Scope: DrawScope(area, s); break;
            case TaskKind.Fuse: DrawFuse(area, s); break;
            case TaskKind.Vent: DrawVent(area, s); break;
            case TaskKind.Gyro: DrawGyro(area, s); break;
        }

        if (m_DoneT >= 0f)
        {
            Fill(area, new Color(0.1f, 0.4f, 0.15f, 0.55f));
            var big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(30 * s) };
            big.normal.textColor = Color.white;
            GUI.Label(area, "修理完了！", big);
        }
    }

    string Instruction() => m_Kind switch
    {
        TaskKind.Valve => $"A と D を交互に押してバルブを回す（{Mathf.Min(ValveTurns, m_ValveAngle / 360f):0.0} / {ValveTurns:0} 回転）",
        TaskKind.Wires => m_WirePicked < 0 ? "W/S で左の線を選んで Space（クリックでもOK）" : $"<b>{WireNames[m_WireLeft[m_WirePicked]]}</b> の線をつなぐ先を W/S で選んで Space",
        TaskKind.Gauge => $"針が緑の範囲に入ったら Space（{m_Hits} / {GaugeHits}）",
        TaskKind.Scope => $"回る印が緑の目標に重なったら Space（{m_Hits} / {ScopeHits}）",
        TaskKind.Fuse => $"指定と同じ数字のヒューズを A/D で選んで Space（{m_FuseRound} / {FuseRounds}）",
        TaskKind.Vent => $"Space を連打してダクトのゴミを取る（残り {m_Debris}）",
        _ => $"A/D で印を真ん中の枠に入れておく（{Mathf.Min(GyroNeed, m_GyroTime):0.0} / {GyroNeed:0} 秒）",
    };

    void DrawValve(Rect a)
    {
        float size = Mathf.Min(a.width, a.height) * 0.85f;
        var r = new Rect(a.center.x - size * 0.5f, a.center.y - size * 0.5f, size, size);
        var m = GUI.matrix;
        GUIUtility.RotateAroundPivot(m_ValveShown, a.center);
        GUI.DrawTexture(r, Art.Get("valve_wheel").texture);
        GUI.matrix = m;
        // 進み具合のバー
        float p = Mathf.Clamp01(m_ValveAngle / (ValveTurns * 360f));
        Fill(new Rect(a.x + 10, a.yMax - 16, a.width - 20, 8), new Color(1, 1, 1, 0.15f));
        Fill(new Rect(a.x + 10, a.yMax - 16, (a.width - 20) * p, 8), Prims.Hex("#5fe8ff"));
    }

    void DrawWires(Rect a, float s)
    {
        float rowH = a.height / 4f;
        float lx = a.x + 30 * s, rx = a.xMax - 30 * s;
        for (int row = 0; row < 4; row++)
        {
            float y = a.y + rowH * (row + 0.5f);
            // 左の端子
            int lc = m_WireLeft[row];
            var lr = new Rect(a.x + 6 * s, y - 12 * s, 40 * s, 24 * s);
            Fill(lr, WireColors[lc]);
            if (m_WirePicked < 0 && row == m_WireCursor && !m_WireDone[lc]) Frame(lr, Color.white, 3 * s);
            if (m_WirePicked == row) Frame(lr, Prims.Hex("#ffb000"), 4 * s);
            if (GUI.Button(lr, GUIContent.none, GUIStyle.none) && m_DoneT < 0f) { if (m_WirePicked >= 0) m_WirePicked = -1; ChooseWire(row); }
            // 右の端子
            int rc = m_WireRight[row];
            var rr = new Rect(a.xMax - 46 * s, y - 12 * s, 40 * s, 24 * s);
            Fill(rr, WireColors[rc]);
            if (m_WirePicked >= 0 && row == m_WireCursor) Frame(rr, Color.white, 3 * s);
            if (GUI.Button(rr, GUIContent.none, GUIStyle.none) && m_DoneT < 0f && m_WirePicked >= 0) ChooseWire(row);
        }
        // つないだ線
        for (int c = 0; c < 4; c++)
        {
            if (!m_WireDone[c]) continue;
            int lrow = System.Array.IndexOf(m_WireLeft, c), rrow = System.Array.IndexOf(m_WireRight, c);
            Line(new Vector2(lx + 16 * s, a.y + rowH * (lrow + 0.5f)), new Vector2(rx - 16 * s, a.y + rowH * (rrow + 0.5f)), 10 * s, WireColors[c]);
        }
        if (m_WirePicked >= 0)
        {
            var from = new Vector2(lx + 16 * s, a.y + rowH * (m_WirePicked + 0.5f));
            var to = new Vector2(rx - 16 * s, a.y + rowH * (m_WireCursor + 0.5f));
            var col = WireColors[m_WireLeft[m_WirePicked]];
            col.a = 0.6f;
            Line(from, to, 8 * s, col);
        }
    }

    void DrawGauge(Rect a, float s)
    {
        var c = new Vector2(a.center.x, a.yMax - 30 * s);
        float r = Mathf.Min(a.width * 0.45f, a.height - 50 * s);
        // 目盛りと緑の範囲（-80° ～ 80°）
        for (float deg = -80f; deg <= 80.1f; deg += 4f)
        {
            bool zone = Mathf.Abs(deg - m_ZoneCenter) < GaugeZone;
            var dir = new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), -Mathf.Cos(deg * Mathf.Deg2Rad));
            Line(c + dir * r * 0.78f, c + dir * r, zone ? 10 * s : 4 * s, zone ? new Color(0.3f, 0.85f, 0.35f) : new Color(1, 1, 1, 0.35f));
        }
        float n = GaugeNeedle();
        var nd = new Vector2(Mathf.Sin(n * Mathf.Deg2Rad), -Mathf.Cos(n * Mathf.Deg2Rad));
        Line(c, c + nd * r * 0.95f, 6 * s, Prims.Hex("#ff5a4a"));
        Fill(new Rect(c.x - 10 * s, c.y - 10 * s, 20 * s, 20 * s), Prims.Hex("#c8d0d6"));
    }

    void DrawScope(Rect a, float s)
    {
        var c = a.center;
        float r = Mathf.Min(a.width, a.height) * 0.38f;
        for (float deg = 0; deg < 360f; deg += 6f)
        {
            var dir = new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), -Mathf.Cos(deg * Mathf.Deg2Rad));
            bool zone = Mathf.Abs(Mathf.DeltaAngle(deg, m_ZoneCenter)) < ScopeZone;
            Line(c + dir * r * 0.9f, c + dir * r, zone ? 10 * s : 3 * s, zone ? new Color(0.5f, 1f, 0.35f) : new Color(0.5f, 1f, 0.35f, 0.25f));
        }
        Line(c + new Vector2(-r * 0.6f, 0), c + new Vector2(r * 0.6f, 0), 2 * s, new Color(0.5f, 1f, 0.35f, 0.4f));
        Line(c + new Vector2(0, -r * 0.6f), c + new Vector2(0, r * 0.6f), 2 * s, new Color(0.5f, 1f, 0.35f, 0.4f));
        float m = ScopeMarker();
        var md = new Vector2(Mathf.Sin(m * Mathf.Deg2Rad), -Mathf.Cos(m * Mathf.Deg2Rad));
        var mp = c + md * r * 1.12f;
        Fill(new Rect(mp.x - 9 * s, mp.y - 9 * s, 18 * s, 18 * s), Prims.Hex("#ffb000"));
    }

    void DrawFuse(Rect a, float s)
    {
        var big = new GUIStyle(m_Center) { fontSize = Mathf.RoundToInt(22 * s) };
        big.normal.textColor = Color.white;
        GUI.Label(new Rect(a.x, a.y + 8 * s, a.width, 34 * s), $"必要なヒューズ： <color=#ffb000>{FuseAmps[m_FuseTarget]}A</color>", big);
        float slotW = a.width / 4f;
        for (int i = 0; i < 4; i++)
        {
            var r = new Rect(a.x + slotW * i + slotW * 0.2f, a.y + 56 * s, slotW * 0.6f, a.height - 72 * s);
            Fill(r, new Color(0.88f, 0.88f, 0.84f));
            Fill(new Rect(r.x, r.y, r.width, 14 * s), new Color(0.7f, 0.72f, 0.74f));
            Fill(new Rect(r.x, r.yMax - 14 * s, r.width, 14 * s), new Color(0.7f, 0.72f, 0.74f));
            var t = new GUIStyle(big) { fontSize = Mathf.RoundToInt(20 * s) };
            t.normal.textColor = new Color(0.15f, 0.17f, 0.2f);
            GUI.Label(r, FuseAmps[m_Fuses[i]] + "A", t);
            if (i == m_FuseCursor) Frame(r, Prims.Hex("#ffb000"), 4 * s);
            if (GUI.Button(r, GUIContent.none, GUIStyle.none) && m_DoneT < 0f) ChooseFuse(i);
        }
    }

    void DrawVent(Rect a, float s)
    {
        for (float y = a.y + 10 * s; y < a.yMax - 10 * s; y += 18 * s)
            Fill(new Rect(a.x + 10 * s, y, a.width - 20 * s, 8 * s), new Color(0.22f, 0.25f, 0.28f));
        for (int i = 0; i < m_Debris; i++)
        {
            var p = new Vector2(a.x + a.width * m_DebrisPos[i].x, a.y + a.height * m_DebrisPos[i].y);
            float sz = (18 + (i * 7) % 16) * s;
            Fill(new Rect(p.x - sz * 0.5f, p.y - sz * 0.5f, sz, sz), i % 3 == 0 ? new Color(0.45f, 0.36f, 0.25f) : new Color(0.55f, 0.55f, 0.5f));
        }
    }

    void DrawGyro(Rect a, float s)
    {
        float cy = a.center.y;
        Fill(new Rect(a.x + 10 * s, cy - 3 * s, a.width - 20 * s, 6 * s), new Color(1, 1, 1, 0.2f));
        float zoneW = (a.width - 20 * s) * GyroZone;
        Fill(new Rect(a.center.x - zoneW * 0.5f, cy - 40 * s, zoneW, 80 * s), new Color(0.3f, 0.85f, 0.35f, 0.35f));
        float x = a.center.x + m_GyroX * (a.width * 0.5f - 24 * s);
        Fill(new Rect(x - 12 * s, cy - 30 * s, 24 * s, 60 * s), Prims.Hex("#5fe8ff"));
        float p = Mathf.Clamp01(m_GyroTime / GyroNeed);
        Fill(new Rect(a.x + 10, a.yMax - 16, a.width - 20, 8), new Color(1, 1, 1, 0.15f));
        Fill(new Rect(a.x + 10, a.yMax - 16, (a.width - 20) * p, 8), Prims.Hex("#7dff6a"));
    }
}
