using UnityEngine;

/// <summary>
/// エネルギーセル：ロボットの中の遠い場所にときどき出てくる、光る電池。
/// 拾って体の真ん中の動力炉まで運び、入れるとロボットが強化される（やってもやらなくてもよい仕事）。
/// 色で強化の種類が決まる。計算は RobotState、見た目は GameBootstrap（中の絵）と RobotVisual（外の光）。
/// </summary>
public static class EnergyCells
{
    public const int Green = 0, Red = 1, Blue = 2, Yellow = 3;
    public const int KindCount = 4;

    public const int MaxActive = 2;              // 同時に出ているセルの数
    public const float PickRange = 1.5f;         // 拾える距離（メートル）
    public const float DeliverRange = 2.4f;      // 動力炉に入れられる距離
    public const float CarrySpeed = 0.8f;        // 持っているあいだの歩く速さの倍率
    public const float FirstSpawn = 12f;         // 試合が始まってから最初に出るまで（秒）
    public const float SpawnMin = 18f, SpawnMax = 28f;

    public const float HullHeal = 25f;           // 緑：装甲の回復量
    public const float PunchBoostTime = 20f;     // 赤：パンチ強化の時間（秒）
    public const float PunchBoostMul = 1.5f;     // 赤：パンチのダメージ倍率
    public const float SpecialGain = 40f;        // 青：必殺ゲージ

    static Vector2 P(float x, float z) => new Vector2(x, z) * InteriorMap.Scale;

    /// <summary>セルが出てくる場所（足場の上）。操縦席・整備装置・ダクトと重ならない遠い場所</summary>
    public static readonly Vector2[] Spots =
    {
        P(5.6f, 17.6f),    // 左の手のひら
        P(34.4f, 17.6f),   // 右の手のひら
        P(20.2f, 49.5f),   // 頭
        P(15.4f, -10.8f),  // 左足
        P(24.8f, -10.8f),  // 右足
        P(15.0f, 4.6f),    // 左すね
        P(25.0f, 4.6f),    // 右すね
    };
    public static int Count => Spots.Length;

    /// <summary>セルを入れる場所（動力炉の足元）</summary>
    public static Vector2 ReactorPos => InteriorMap.ReactorPos;

    public static string Name(int kind) => kind switch
    {
        Green => "緑",
        Red => "赤",
        Blue => "青",
        _ => "黄",
    };

    public static string Effect(int kind) => kind switch
    {
        Green => $"装甲 +{HullHeal:0}",
        Red => $"パンチ強化（{PunchBoostTime:0}秒）",
        Blue => $"必殺ゲージ +{SpecialGain:0}",
        _ => "動力 満タン",
    };

    public static Color Color(int kind) => kind switch
    {
        Green => Prims.Hex("#5dff7a"),
        Red => Prims.Hex("#ff4a4a"),
        Blue => Prims.Hex("#4fb6ff"),
        _ => Prims.Hex("#ffd23f"),
    };

    public static string Hex(int kind) => kind switch
    {
        Green => "#5dff7a",
        Red => "#ff6a6a",
        Blue => "#6fc4ff",
        _ => "#ffd23f",
    };
}
