using UnityEngine;

public enum StationType { Leg, Arm, Head, Core }

/// <summary>ロボット内部にある操縦席（操作盤）1つぶんの定義。</summary>
public struct StationDef
{
    public string Name;
    public StationType Type;
    /// <summary>左 = -1、右 = +1、中央 = 0</summary>
    public int Side;
    /// <summary>内部マップ上で操縦士が立つ位置（メートル。x = 右、y = 上）</summary>
    public Vector2 Pos;
    /// <summary>操作盤を置く向き（立ち位置からのずれ）</summary>
    public Vector2 ConsoleOffset;
    public Color Color;
}

public static class Stations
{
    public const int LeftLeg = 0, RightLeg = 1, LeftArm = 2, RightArm = 3, Head = 4, Core = 5;
    public const int Count = 6;

    public static readonly StationDef[] All =
    {
        new StationDef { Name = "左脚", Type = StationType.Leg,  Side = -1, Pos = new Vector2(10.5f, -4.5f) * InteriorMap.Scale, ConsoleOffset = new Vector2(0, -1.5f), Color = Prims.Hex("#4fd2ff") },
        new StationDef { Name = "右脚", Type = StationType.Leg,  Side =  1, Pos = new Vector2(29.5f, -4.5f) * InteriorMap.Scale, ConsoleOffset = new Vector2(0, -1.5f), Color = Prims.Hex("#4fd2ff") },
        new StationDef { Name = "左腕", Type = StationType.Arm,  Side = -1, Pos = new Vector2(3f, 17f) * InteriorMap.Scale,      ConsoleOffset = new Vector2(-1.6f, 0), Color = Prims.Hex("#ff9a3c") },
        new StationDef { Name = "右腕", Type = StationType.Arm,  Side =  1, Pos = new Vector2(37f, 17f) * InteriorMap.Scale,     ConsoleOffset = new Vector2(1.6f, 0),  Color = Prims.Hex("#ff9a3c") },
        new StationDef { Name = "頭部砲", Type = StationType.Head, Side = 0, Pos = new Vector2(20f, 30f) * InteriorMap.Scale,     ConsoleOffset = new Vector2(0, 1.6f),  Color = Prims.Hex("#b8ff5a") },
        new StationDef { Name = "動力炉", Type = StationType.Core, Side = 0, Pos = new Vector2(20f, 8.3f) * InteriorMap.Scale,    ConsoleOffset = new Vector2(0, -1.4f), Color = Prims.Hex("#ffd23f") },
    };
}
