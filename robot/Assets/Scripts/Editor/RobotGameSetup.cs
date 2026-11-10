using System.IO;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// メニュー「Robot/ゲームのシーンを作成」で、ゲームに必要なものを全部自動で用意する。
/// - マテリアル、ネットワーク用のプレハブ（RobotState / CrewPlayer）
/// - シーン（NetworkManager + GameBootstrap + ライト）
/// - ビルド設定への登録、バックグラウンドでも動く設定
/// 何度実行しても同じ結果になる（上書き）。
/// </summary>
public static class RobotGameSetup
{
    const string Root = "Assets/Game";
    const string ScenePath = Root + "/Scenes/RobotGame.unity";

    [MenuItem("Robot/ゲームのシーンを作成")]
    public static void Setup()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        ArtImporter.Reimport(); // 内部のイラストをスプライトとして取り込み直しておく
        EnsureFolder(Root);
        EnsureFolder(Root + "/Materials");
        EnsureFolder(Root + "/Prefabs");
        EnsureFolder(Root + "/Scenes");

        var lit = CreateMaterial("Lit", "Universal Render Pipeline/Lit", "Standard");
        var unlit = CreateMaterial("Unlit", "Universal Render Pipeline/Unlit", "Unlit/Color");
        var robotPrefab = CreateNetworkPrefab<RobotState>("RobotState");
        var crewPrefab = CreateNetworkPrefab<CrewPlayer>("CrewPlayer");

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var lightGo = new GameObject("Sun");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.color = new Color(1f, 0.92f, 0.8f);
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(55f, -35f, 0f);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.46f, 0.5f);
        RenderSettings.skybox = null;

        var nmGo = new GameObject("NetworkManager");
        var nm = nmGo.AddComponent<NetworkManager>();
        var transport = nmGo.AddComponent<UnityTransport>();
        if (nm.NetworkConfig == null) nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = transport;
        nm.NetworkConfig.EnableSceneManagement = false; // シーンは全員同じなので同期しない
        nm.NetworkConfig.ConnectionApproval = false;
        nm.NetworkConfig.PlayerPrefab = null;           // 操縦士はホストが手動で出す

        var gameGo = new GameObject("Game");
        var game = gameGo.AddComponent<GameBootstrap>();
        game.networkManager = nm;
        game.robotPrefab = robotPrefab;
        game.crewPrefab = crewPrefab;
        game.litMaterial = lit;
        game.unlitMaterial = unlit;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        PlayerSettings.runInBackground = true; // 2つ起動して試すとき、裏側のウィンドウも止まらないように
        AssetDatabase.SaveAssets();

#if HAS_NETCODE
        const string howTo = "▶ 再生して「ホストとして始める」を押すと遊べます。\n" +
            "2人で試すときは、ビルドしたものとエディタを同時に起動し、片方をホスト、もう片方を 127.0.0.1 で参加にしてください。";
#else
        const string howTo = "▶ 再生して「スタート」を押すと遊べます（1人用）。";
#endif
        EditorUtility.DisplayDialog("タイタンクルー", "シーンを作成しました。\n\n" + ScenePath + "\n\n" + howTo, "OK");
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static Material CreateMaterial(string name, string shaderName, string fallback)
    {
        var path = $"{Root}/Materials/{name}.mat";
        var shader = Shader.Find(shaderName) ?? Shader.Find(fallback);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else mat.shader = shader;
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.3f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.15f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static GameObject CreateNetworkPrefab<T>(string name) where T : NetworkBehaviour
    {
        var path = $"{Root}/Prefabs/{name}.prefab";
        var go = new GameObject(name);
        go.AddComponent<NetworkObject>();
        go.AddComponent<T>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        // ネットワーク上でプレハブを見分ける番号（GlobalObjectIdHash）が入っているか確認する。
        // 通常は Netcode が自動で入れるが、入っていなければ GUID から作って入れておく。
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var so = new SerializedObject(prefab.GetComponent<NetworkObject>());
        var hash = so.FindProperty("GlobalObjectIdHash");
        if (hash != null && hash.uintValue == 0)
        {
            hash.uintValue = Fnv1a(AssetDatabase.AssetPathToGUID(path));
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
        }
        return prefab;
    }

    static uint Fnv1a(string s)
    {
        uint h = 2166136261;
        foreach (char c in s) { h ^= c; h *= 16777619; }
        return h;
    }
}
