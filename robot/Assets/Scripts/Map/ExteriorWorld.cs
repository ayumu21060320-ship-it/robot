using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ロボットが歩く外の世界。ビルの配置は乱数の種を固定して、
/// ホストと参加者で同じ配置になるようにしている（通信しなくても一致する）。
/// </summary>
public static class ExteriorWorld
{
    public const float Bounds = 195f;
    public const float RobotRadius = 5f;
    public const float HiveRadius = 9f;
    public const int HiveMaxHp = 40;

    public static readonly Vector2[] HivePos = { new Vector2(-70f, 105f), new Vector2(100f, 55f), new Vector2(25f, 175f) };

    public struct Building
    {
        public Vector2 Pos;
        public float Radius;
        public Vector3 Size;
        public float RotY;
    }

    static List<Building> s_Buildings;
    public static List<Building> Buildings { get { Generate(); return s_Buildings; } }

    public static GameObject[] HiveObjects = new GameObject[3];
    public static Transform[] HiveCores = new Transform[3];

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
            foreach (var h in HivePos) if (Vector2.Distance(h, p) < 28f) bad = true;
            float w = 6 + R() * 9, d = 6 + R() * 9, ht = 6 + R() * 20;
            float rad = Mathf.Max(w, d) * 0.6f;
            foreach (var b in s_Buildings) if (Vector2.Distance(b.Pos, p) < b.Radius + rad + 8f) bad = true;
            if (bad) continue;
            s_Buildings.Add(new Building { Pos = p, Radius = rad, Size = new Vector3(w, ht, d), RotY = R() * 30f });
        }
    }

    /// <summary>ロボットがこの位置に立てるか（ビル・巣・外周の壁）</summary>
    public static bool Blocked(Vector2 p, System.Func<int, bool> hiveAlive)
    {
        if (Mathf.Abs(p.x) > Bounds || Mathf.Abs(p.y) > Bounds) return true;
        foreach (var b in Buildings)
            if (Vector2.Distance(b.Pos, p) < b.Radius + RobotRadius) return true;
        for (int i = 0; i < HivePos.Length; i++)
            if (hiveAlive(i) && Vector2.Distance(HivePos[i], p) < HiveRadius + RobotRadius) return true;
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
        foreach (var b in Buildings)
        {
            var g = Prims.Box(root, new Vector3(b.Pos.x, b.Size.y * 0.5f, b.Pos.y), b.Size, colors[ci++ % 3], false, "ビル");
            g.transform.rotation = Quaternion.Euler(0, b.RotY, 0);
            // 窓の光
            for (float y = 3f; y < b.Size.y - 1f; y += 4f)
                Prims.Box(g.transform, new Vector3(0, y / b.Size.y - 0.5f, 0.505f), new Vector3(0.7f, 0.8f / b.Size.y, 0.01f), window, true);
        }

        // 敵の巣
        var shell = Prims.Hex("#2d1a33");
        var vein = Prims.Hex("#ff3d7f");
        for (int i = 0; i < HivePos.Length; i++)
        {
            var h = new GameObject("敵の巣 " + (i + 1)).transform;
            h.SetParent(root, false);
            h.position = new Vector3(HivePos[i].x, 0, HivePos[i].y);
            Prims.Shape(PrimitiveType.Cylinder, h, new Vector3(0, 1.5f, 0), new Vector3(18f, 1.5f, 18f), shell);
            Prims.Shape(PrimitiveType.Cylinder, h, new Vector3(0, 8f, 0), new Vector3(9f, 6f, 9f), shell);
            for (int k = 0; k < 6; k++)
            {
                float a = k / 6f * Mathf.PI * 2f;
                var sp = Prims.Box(h, new Vector3(Mathf.Cos(a) * 6.5f, 6f, Mathf.Sin(a) * 6.5f), new Vector3(1.6f, 10f, 1.6f), shell);
                sp.transform.localRotation = Quaternion.Euler(Mathf.Sin(a) * 20f, 0, -Mathf.Cos(a) * 20f);
                Prims.Box(h, new Vector3(Mathf.Cos(a) * 4.7f, 8f, Mathf.Sin(a) * 4.7f), new Vector3(0.4f, 11f, 0.4f), vein, true);
            }
            var core = Prims.Shape(PrimitiveType.Sphere, h, new Vector3(0, 16.5f, 0), Vector3.one * 6.5f, vein, true, "コア");
            HiveObjects[i] = h.gameObject;
            HiveCores[i] = core.transform;
        }
        return root;
    }
}
