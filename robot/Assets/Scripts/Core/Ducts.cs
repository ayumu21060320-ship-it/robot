using UnityEngine;

/// <summary>ダクトの入り口1つぶん。Link の先のダクトへ一瞬で移動する（行き先は選べない）。</summary>
public struct DuctDef
{
    public string Name;
    /// <summary>入り口の前で立つ位置（メートル）</summary>
    public Vector2 Pos;
    /// <summary>つながっている先のダクト（All の番号）</summary>
    public int Link;
}

/// <summary>
/// 体の中の遠い場所どうしをつなぐダクト。はしごだと時間のかかる道の近道。
/// 2つずつ組になっていて、どちらから入っても相手の側に出る。
/// </summary>
public static class Ducts
{
    /// <summary>ダクトの中を通るのにかかる時間（秒）</summary>
    public const float TravelTime = 0.9f;
    public const float UseRange = 1.6f;

    static Vector2 P(float x, float z) => new Vector2(x, z) * InteriorMap.Scale;

    public static readonly DuctDef[] All =
    {
        new DuctDef { Name = "頭",           Pos = P(18.6f, 49.5f),  Link = 1 },
        new DuctDef { Name = "腰",           Pos = P(17.6f, 27.4f),  Link = 0 },
        new DuctDef { Name = "胸の上（左）", Pos = P(8.6f, 41.5f),   Link = 3 },
        new DuctDef { Name = "左の手のひら", Pos = P(8.4f, 17.6f),   Link = 2 },
        new DuctDef { Name = "胸の上（右）", Pos = P(31.4f, 41.5f),  Link = 5 },
        new DuctDef { Name = "右の手のひら", Pos = P(31.6f, 17.6f),  Link = 4 },
        new DuctDef { Name = "胸の下（左）", Pos = P(22.0f, 35.6f),  Link = 7 },
        new DuctDef { Name = "左すね",       Pos = P(17.6f, 4.6f),   Link = 6 },
        new DuctDef { Name = "胸の下（右）", Pos = P(25.4f, 35.6f),  Link = 9 },
        new DuctDef { Name = "右すね",       Pos = P(22.4f, 4.6f),   Link = 8 },
        new DuctDef { Name = "左足",         Pos = P(17.4f, -10.8f), Link = 11 },
        new DuctDef { Name = "右足",         Pos = P(22.4f, -10.8f), Link = 10 },
    };

    public static int Nearest(Vector2 p, float maxDist)
    {
        int best = -1;
        float bd = maxDist;
        for (int i = 0; i < All.Length; i++)
        {
            float d = Vector2.Distance(p, All[i].Pos);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }
}
