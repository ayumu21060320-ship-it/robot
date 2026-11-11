using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ロボットが歩く外の世界。ビルの配置は乱数の種を固定して、
/// ホストと参加者で同じ配置になるようにしている（通信しなくても一致する）。
/// </summary>
public static class ExteriorWorld
{
    public const float Bounds = 195f;
    /// <summary>タイタンの当たり判定の半径（大きくなったぶん広い）</summary>
    public const float RobotRadius = 8f;

    public struct Building
    {
        public Vector2 Pos;
        public float Radius;
        public Vector3 Size;
        public float RotY;
    }

    static List<Building> s_Buildings;
    public static List<Building> Buildings { get { Generate(); return s_Buildings; } }
    /// <summary>ビルの見た目（Buildings と同じ順番）。崩れるアニメーションに使う</summary>
    public static readonly List<Transform> BuildingObjects = new List<Transform>();


    static void Generate()
    {
        if (s_Buildings != null) return;
        s_Buildings = new List<Building>();
        var rnd = new System.Random(7);
        float R() => (float)rnd.NextDouble();
        int tries = 0;
        while (s_Buildings.Count < 50 && tries++ < 3000)
        {
            var p = new Vector2((R() * 2 - 1) * 180f, (R() * 2 - 1) * 180f);
            if (p.magnitude < 32f) continue;
            bool bad = false;
            // 敵が立つ場所と、タイタンのスタート地点から敵までのまっすぐな道はあける
            if (Vector2.Distance(RobotState.BossStart, p) < 34f) bad = true;
            float t = Mathf.Clamp01(Vector2.Dot(p, RobotState.BossStart) / RobotState.BossStart.sqrMagnitude);
            if (Vector2.Distance(p, RobotState.BossStart * t) < 30f) bad = true;
            float w = 6 + R() * 9, d = 6 + R() * 9, ht = 6 + R() * 20;
            float rad = Mathf.Max(w, d) * 0.6f;
            foreach (var b in s_Buildings) if (Vector2.Distance(b.Pos, p) < b.Radius + rad + 8f) bad = true;
            if (bad) continue;
            s_Buildings.Add(new Building { Pos = p, Radius = rad, Size = new Vector3(w, ht, d), RotY = R() * 30f });
        }
    }

    /// <summary>タイタンがこの位置に立てるか（巨大な敵・外周の壁）。ビルは踏み倒せるので止まらない</summary>
    public static bool Blocked(Vector2 p, Vector2 bossPos, bool bossAlive)
    {
        if (Mathf.Abs(p.x) > Bounds || Mathf.Abs(p.y) > Bounds) return true;
        if (bossAlive && Vector2.Distance(bossPos, p) < RobotState.BossRadius + RobotRadius) return true;
        return false;
    }

    public static Transform Build(Transform parent)
    {
        var root = new GameObject("Exterior").transform;
        root.SetParent(parent, false);

        Prims.Box(root, new Vector3(0, -0.5f, 0), new Vector3(520f, 1f, 520f), Prims.Hex("#4b4f45"), false, "地面");
        var line = Prims.Hex("#5a5c4f");
        for (int i = -200; i <= 200; i += 20)
        {
            Prims.Box(root, new Vector3(i, 0.01f, 0), new Vector3(0.4f, 0.02f, 400f), line);
            Prims.Box(root, new Vector3(0, 0.01f, i), new Vector3(400f, 0.02f, 0.4f), line);
        }

        // 外周の目印の柱
        var post = Prims.Hex("#ffb000");
        for (int i = -195; i <= 195; i += 39)
        {
            Prims.Box(root, new Vector3(i, 3f, -200f), new Vector3(1.2f, 6f, 1.2f), post);
            Prims.Box(root, new Vector3(i, 3f, 200f), new Vector3(1.2f, 6f, 1.2f), post);
            Prims.Box(root, new Vector3(-200f, 3f, i), new Vector3(1.2f, 6f, 1.2f), post);
            Prims.Box(root, new Vector3(200f, 3f, i), new Vector3(1.2f, 6f, 1.2f), post);
        }

        // 遠くの岩山
        var rnd = new System.Random(11);
        var rock = Prims.Hex("#2a343a");
        for (int i = 0; i < 36; i++)
        {
            float a = i / 36f * Mathf.PI * 2f, dist = 270f + (float)rnd.NextDouble() * 40f, h = 30f + (float)rnd.NextDouble() * 60f;
            var m = Prims.Box(root, new Vector3(Mathf.Cos(a) * dist, h * 0.4f, Mathf.Sin(a) * dist), new Vector3(40f, h, 40f), rock);
            m.transform.rotation = Quaternion.Euler(0, (float)rnd.NextDouble() * 90f, 20f);
        }

        // ビル
        var colors = new[] { Prims.Hex("#6f6a60"), Prims.Hex("#5c6468"), Prims.Hex("#7b6f5c") };
        var window = Prims.Hex("#ffcf7a");
        int ci = 0;
        BuildingObjects.Clear();
        foreach (var b in Buildings)
        {
            var g = Prims.Box(root, new Vector3(b.Pos.x, b.Size.y * 0.5f, b.Pos.y), b.Size, colors[ci++ % 3], false, "ビル");
            BuildingObjects.Add(g.transform);
            g.transform.rotation = Quaternion.Euler(0, b.RotY, 0);
            // 窓の光
            for (float y = 3f; y < b.Size.y - 1f; y += 4f)
                Prims.Box(g.transform, new Vector3(0, y / b.Size.y - 0.5f, 0.505f), new Vector3(0.7f, 0.8f / b.Size.y, 0.01f), window, true);
        }

        return root;
    }
}
