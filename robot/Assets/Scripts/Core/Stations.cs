using UnityEngine;

public enum StationType { Leg, Arm, Head, Core }

/// <summary>ロボット内部にある操縦席（操作盤）1つぶんの定義。</summary>
public struct StationDef
{
    public string Name;
    public StationType Type;
    /// <summary>左 = -1、右 = +1、中央 = 0</summary>
    public int Side;
    /// <summary>内部の断面図で操縦士が立つ位置（メートル。x = 右、y = 足元の高さ）</summary>
    public Vector2 Pos;
    /// <summary>操作盤の絵を置く位置（立ち位置からのずれ。x の向きで操縦士の向きも決まる）</summary>
    public Vector2 ConsoleOffset;
    public Color Color;
}

public static class Stations
{
    public const int LeftLeg = 0, RightLeg = 1, LeftArm = 2, RightArm = 3, Head = 4, Core = 5;
    public const int Count = 6;

    public static readonly StationDef[] All =
    {
        new StationDef { Name = "左脚", Type = StationType.Leg,  Side = -1, Pos = new Vector2(13f, 4.6f) * InteriorMap.Scale, ConsoleOffset = new Vector2(-1.5f, 0.95f), Color = Prims.Hex("#4fd2ff") },
        new StationDef { Name = "右脚", Type = StationType.Leg,  Side =  1, Pos = new Vector2(27f, 4.6f) * InteriorMap.Scale, ConsoleOffset = new Vector2(1.5f, 0.95f), Color = Prims.Hex("#4fd2ff") },
        new StationDef { Name = "左腕", Type = StationType.Arm,  Side = -1, Pos = new Vector2(3.9f, 17.6f) * InteriorMap.Scale,      ConsoleOffset = new Vector2(-1.4f, 0.95f), Color = Prims.Hex("#ff9a3c") },
        new StationDef { Name = "右腕", Type = StationType.Arm,  Side =  1, Pos = new Vector2(36.1f, 17.6f) * InteriorMap.Scale,     ConsoleOffset = new Vector2(1.4f, 0.95f),  Color = Prims.Hex("#ff9a3c") },
        new StationDef { Name = "頭部砲", Type = StationType.Head, Side = 0, Pos = new Vector2(21.8f, 49.5f) * InteriorMap.Scale,     ConsoleOffset = new Vector2(1.5f, 0.95f),  Color = Prims.Hex("#b8ff5a") },
        new StationDef { Name = "動力炉", Type = StationType.Core, Side = 0, Pos = new Vector2(14.2f, 35.6f) * InteriorMap.Scale,    ConsoleOffset = new Vector2(1.2f, 0.95f), Color = Prims.Hex("#ffd23f") },
    };
}
