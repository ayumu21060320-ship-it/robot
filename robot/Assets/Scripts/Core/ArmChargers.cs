using UnityEngine;

/// <summary>
/// 腕の充電器：肩の足場（胸の上の段）の左右に1台ずつ。
/// パンチやガードで減った腕のエネルギーを、そばで Space を連打して充電する（腕の席とは別の仕事）。
/// </summary>
public static class ArmChargers
{
    public const float UseRange = 1.6f;

    static Vector2 P(float x, float z) => new Vector2(x, z) * InteriorMap.Scale;

    /// <summary>立つ位置（0 = 左腕用、1 = 右腕用）</summary>
    public static readonly Vector2[] Pos = { P(13.4f, 41.5f), P(26.6f, 41.5f) };

    public static int Nearest(Vector2 p, float maxDist)
    {
        int best = -1;
        float bd = maxDist;
        for (int i = 0; i < Pos.Length; i++)
        {
            float d = Vector2.Distance(p, Pos[i]);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }
}
