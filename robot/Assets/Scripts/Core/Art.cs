using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ロボット内部の2Dイラスト（Assets/Resources/Art/*.png）を読み込んで、床に寝かせたスプライトとして置くヘルパー。
/// 絵は tools/art/generate_art.py で描き出している。
/// 内部カメラは真上から見下ろしているので、スプライトは寝かせて置き、
/// 前後の重なりは sortingOrder（画面の下にあるものほど手前）で決める。
/// </summary>
public static class Art
{
    public const float PixelsPerUnit = 64f;

    static readonly Dictionary<string, Sprite> s_Cache = new Dictionary<string, Sprite>();
    static readonly Quaternion Flat = Quaternion.Euler(90f, 0f, 0f);

    // 重なり順の目安
    public const int OrderFloor = 0, OrderPad = 2, OrderWallFace = 3, OrderShadow = 5, OrderWallCap = 6, OrderObjects = 100;

    public static Sprite Get(string name)
    {
        if (s_Cache.TryGetValue(name, out var s)) return s;
        s = Resources.Load<Sprite>("Art/" + name);
        if (s == null)
        {
            // スプライトとして取り込まれていなかったときの予備（ArtImporter が動く前など）
            var tex = Resources.Load<Texture2D>("Art/" + name);
            if (tex == null)
            {
                Debug.LogWarning($"Art/{name}.png が見つかりません。白い四角で代わりに表示します。");
                tex = Texture2D.whiteTexture;
            }
            var border = name == "wall_cap" ? new Vector4(8, 8, 8, 8) : Vector4.zero;
            s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), PixelsPerUnit, 0, SpriteMeshType.FullRect, border);
        }
        s_Cache[name] = s;
        return s;
    }

    /// <summary>床に寝かせたスプライトを置く。pos は親から見た位置（x = 右、z = 画面の上）。</summary>
    public static SpriteRenderer Place(Transform parent, string sprite, Vector3 pos, int order, Color? color = null, string name = null)
    {
        var g = new GameObject(name ?? sprite);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localRotation = Flat;
        var r = g.AddComponent<SpriteRenderer>();
        r.sprite = Get(sprite);
        r.sortingOrder = order;
        if (color.HasValue) r.color = color.Value;
        return r;
    }

    /// <summary>決まった大きさ（メートル）に繰り返し並べる（床・壁の面）。</summary>
    public static SpriteRenderer Tiled(Transform parent, string sprite, Vector3 center, Vector2 size, int order, Color? color = null, string name = null)
    {
        var r = Place(parent, sprite, center, order, color, name);
        r.drawMode = SpriteDrawMode.Tiled;
        r.tileMode = SpriteTileMode.Continuous;
        r.size = size;
        return r;
    }

    /// <summary>ふちを保ったまま決まった大きさに伸ばす（壁の上面）。</summary>
    public static SpriteRenderer Sliced(Transform parent, string sprite, Vector3 center, Vector2 size, int order, Color? color = null, string name = null)
    {
        var r = Place(parent, sprite, center, order, color, name);
        r.drawMode = SpriteDrawMode.Sliced;
        r.size = size;
        return r;
    }

    /// <summary>画面の下（z が小さい）ほど手前に描く。baseZ は物の足元の位置。</summary>
    public static int DepthOrder(float baseZ) => OrderObjects + Mathf.RoundToInt((InteriorMap.Bounds.yMax - baseZ) * 10f);
}
