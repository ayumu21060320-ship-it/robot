using UnityEngine;

/// <summary>IMGUI で操作盤やミニゲームを描くための小さな道具（四角・枠・線・円）。</summary>
public static class UIDraw
{
    public static void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = old;
    }

    public static void Frame(Rect r, Color c, float t)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    /// <summary>a から b へ太さ w の線</summary>
    public static void Line(Vector2 a, Vector2 b, float w, Color c)
    {
        var m = GUI.matrix;
        float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
        GUIUtility.RotateAroundPivot(ang, a);
        Fill(new Rect(a.x, a.y - w * 0.5f, (b - a).magnitude, w), c);
        GUI.matrix = m;
    }

    /// <summary>円の輪郭（from～to 度。0 度が真上、時計回り）</summary>
    public static void Arc(Vector2 c, float r, float from, float to, float w, Color col, int segments = 32)
    {
        Vector2 prev = Point(c, r, from);
        for (int i = 1; i <= segments; i++)
        {
            var p = Point(c, r, Mathf.Lerp(from, to, i / (float)segments));
            Line(prev, p, w, col);
            prev = p;
        }
    }

    /// <summary>中心 c から半径 r、角度 deg（0 度が真上、時計回り）の点</summary>
    public static Vector2 Point(Vector2 c, float r, float deg) =>
        c + new Vector2(Mathf.Sin(deg * Mathf.Deg2Rad), -Mathf.Cos(deg * Mathf.Deg2Rad)) * r;

    public static void Dot(Vector2 c, float size, Color col) => Fill(new Rect(c.x - size * 0.5f, c.y - size * 0.5f, size, size), col);
}
