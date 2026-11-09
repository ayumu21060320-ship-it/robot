using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 仮の見た目をキューブや円柱で組み立てるための小さなヘルパー。
/// あとで本番のモデルに差し替えるまでの「仮組み」用。
/// </summary>
public static class Prims
{
    static Material s_Lit;
    static Material s_Unlit;
    static readonly Dictionary<(Color, bool), Material> s_Cache = new Dictionary<(Color, bool), Material>();
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public static void Init(Material lit, Material unlit)
    {
        s_Lit = lit;
        s_Unlit = unlit;
        s_Cache.Clear();
    }

    /// <summary>色ごとに共有するマテリアル。glow=true は光って見える（ライトの影響を受けない）。</summary>
    public static Material Mat(Color c, bool glow = false)
    {
        if (s_Cache.TryGetValue((c, glow), out var m)) return m;
        m = new Material(glow ? s_Unlit : s_Lit);
        m.SetColor(BaseColor, c);
        s_Cache[(c, glow)] = m;
        return m;
    }

    public static GameObject Shape(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color c, bool glow = false, string name = null)
    {
        var g = GameObject.CreatePrimitive(type);
        if (name != null) g.name = name;
        var col = g.GetComponent<Collider>();
        if (col) Object.Destroy(col);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        g.transform.localScale = scale;
        var r = g.GetComponent<MeshRenderer>();
        r.sharedMaterial = Mat(c, glow);
        if (glow) r.shadowCastingMode = ShadowCastingMode.Off;
        return g;
    }

    public static GameObject Box(Transform parent, Vector3 localPos, Vector3 size, Color c, bool glow = false, string name = null)
        => Shape(PrimitiveType.Cube, parent, localPos, size, c, glow, name);

    public static Transform Pivot(Transform parent, Vector3 localPos, string name = "Pivot")
    {
        var g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.localPosition = localPos;
        return g.transform;
    }

    /// <summary>この Renderer だけ色を変えたいとき用（マテリアルを複製する）。</summary>
    public static void SetColor(Renderer r, Color c)
    {
        r.material.SetColor(BaseColor, c);
    }

    /// <summary>"#rrggbb" を Color に変換（静的初期化からも安全に呼べるよう純粋なC#で解析）。</summary>
    public static Color Hex(string hex)
    {
        hex = hex.TrimStart('#');
        int v = System.Convert.ToInt32(hex.Substring(0, 6), 16);
        return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
    }
}
