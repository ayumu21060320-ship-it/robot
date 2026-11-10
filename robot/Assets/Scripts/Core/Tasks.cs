using UnityEngine;

/// <summary>整備タスクのミニゲームの種類</summary>
public enum TaskKind { Valve, Wires, Gauge, Fuse, Scope, Vent, Gyro }

/// <summary>ロボット内部の整備タスク1つぶんの定義。</summary>
public struct TaskDef
{
    public string Name;
    public string Room;
    public TaskKind Kind;
    /// <summary>作業するときに立つ位置（メートル）</summary>
    public Vector2 Pos;
    /// <summary>装置の絵を置くずれ（メートル）。足場の上、立ち位置の後ろの壁ぎわ</summary>
    public Vector2 PanelOffset;
    /// <summary>壊れているあいだに起きること（画面の一覧に出す）</summary>
    public string Effect;
}

/// <summary>
/// 手足の操縦とは別に、ロボットのあちこちにある装置が時々壊れる。
/// 壊れた装置のところへ行ってミニゲームをこなすと直る。放っておくとロボットに悪い影響が出る。
/// 効果の中身は RobotState、ミニゲームは TaskUI。
/// </summary>
public static class Tasks
{
    public const int Valve = 0, WiresL = 1, WiresR = 2, GaugeL = 3, GaugeR = 4, Fuse = 5, Scope = 6, Vent = 7, Gyro = 8;
    public const int Count = 9;

    static Vector2 P(float x, float z) => new Vector2(x, z) * InteriorMap.Scale;

    public static readonly TaskDef[] All =
    {
        new TaskDef { Name = "冷却バルブ",     Room = "胸",     Kind = TaskKind.Valve, Pos = P(11.4f, 41.5f),   PanelOffset = new Vector2(0f, 1.05f),  Effect = "動力が早く減る" },
        new TaskDef { Name = "左腕の配線",     Room = "左肩",   Kind = TaskKind.Wires, Pos = P(4.9f, 41.5f),    PanelOffset = new Vector2(0f, 1.05f),   Effect = "左腕のパンチが遅い" },
        new TaskDef { Name = "右腕の配線",     Room = "右肩",   Kind = TaskKind.Wires, Pos = P(35.1f, 41.5f),   PanelOffset = new Vector2(0f, 1.05f),   Effect = "右腕のパンチが遅い" },
        new TaskDef { Name = "左脚の油圧",     Room = "左もも", Kind = TaskKind.Gauge, Pos = P(16.7f, 20.6f),   PanelOffset = new Vector2(0f, 1.05f),  Effect = "左脚が全力で動かない" },
        new TaskDef { Name = "右脚の油圧",     Room = "右もも", Kind = TaskKind.Gauge, Pos = P(23.3f, 20.6f),   PanelOffset = new Vector2(0f, 1.05f),   Effect = "右脚が全力で動かない" },
        new TaskDef { Name = "ヒューズ",       Room = "腰",     Kind = TaskKind.Fuse,  Pos = P(26f, 27.4f),   PanelOffset = new Vector2(0f, 1.05f),   Effect = "頭部砲が撃てない" },
        new TaskDef { Name = "照準の調整",     Room = "頭",     Kind = TaskKind.Scope, Pos = P(17.0f, 49.5f),   PanelOffset = new Vector2(0f, 1.05f),  Effect = "砲撃が狙いからずれる" },
        new TaskDef { Name = "排熱ダクト",     Room = "腰",     Kind = TaskKind.Vent,  Pos = P(14f, 27.4f),     PanelOffset = new Vector2(0f, 1.05f),  Effect = "装甲がじわじわ減る" },
        new TaskDef { Name = "姿勢制御ジャイロ", Room = "左足", Kind = TaskKind.Gyro,  Pos = P(10.4f, -10.8f),   PanelOffset = new Vector2(0f, 1.05f),  Effect = "歩くとふらついて曲がる" },
    };

    public static string SpriteFor(TaskKind k) => k switch
    {
        TaskKind.Valve => "task_valve",
        TaskKind.Wires => "task_wires",
        TaskKind.Gauge => "task_gauge",
        TaskKind.Fuse => "task_fuse",
        TaskKind.Scope => "task_scope",
        TaskKind.Vent => "task_vent",
        _ => "task_gyro",
    };

    public static int Nearest(Vector2 p, float maxDist)
    {
        int best = -1;
        float bd = maxDist;
        for (int i = 0; i < Count; i++)
        {
            float d = Vector2.Distance(p, All[i].Pos);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }
}
