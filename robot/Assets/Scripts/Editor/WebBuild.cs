using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー「Robot → Web版をビルド」。ブラウザで遊べる版（WebGL）を robot/Builds/Web に書き出す。
/// 中身を GitHub Pages 用のリポジトリにアップロードすると、URL を開くだけで遊べる（将棋のゲームと同じ方法）。
/// 自分のサーバー（server/）で配るときは、npm start がこのフォルダをそのまま配る。
/// （Unity Hub で「Web Build Support」モジュールを入れておく必要がある）
/// </summary>
public static class WebBuild
{
    const string OutDir = "Builds/Web";   // Unity プロジェクト（robot/）から見た場所

    [MenuItem("Robot/Web版をビルド")]
    public static void Build()
    {
        if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL))
        {
            EditorUtility.DisplayDialog("Web版をビルド",
                "Web（WebGL）のビルド機能が入っていません。\nUnity Hub → インストール → このバージョンの歯車 → モジュールを加える → 「Web Build Support」を入れてください。", "OK");
            return;
        }

        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length == 0)
        {
            EditorUtility.DisplayDialog("Web版をビルド", "ビルドするシーンがありません。先にメニュー「Robot → ゲームのシーンを作成」を実行してください。", "OK");
            return;
        }

        // GitHub Pages は Brotli に対応しないので Gzip。サーバー側の設定がなくても動くように、ブラウザ側で展開する（Decompression Fallback）
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.runInBackground = true;          // ほかのウィンドウを触っていても止まらない
        PlayerSettings.WebGL.template = "APPLICATION:Default";

        var paths = new string[scenes.Length];
        for (int i = 0; i < scenes.Length; i++) paths[i] = scenes[i].path;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = paths,
            locationPathName = OutDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });

        if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
        {
            // ブラウザどうしのマルチに使う PeerJS（robot/WebExtras）を、ゲームの横に置く
            foreach (var f in new[] { "peerjs.min.js", "PEERJS_LICENSE.txt" })
            {
                string src = Path.Combine("WebExtras", f);
                if (File.Exists(src)) File.Copy(src, Path.Combine(OutDir, f), true);
            }
            EditorUtility.RevealInFinder(Path.GetFullPath(OutDir));
            EditorUtility.DisplayDialog("Web版をビルド",
                "できました：robot/Builds/Web\n\n" +
                "このフォルダの中身（index.html・Build・TemplateData・peerjs.min.js など）を、GitHub Pages 用のリポジトリにアップロードしてください（README の「Web版を公開する」）。\n" +
                "※ index.html をダブルクリックしても動きません。", "OK");
        }
        else
        {
            Debug.LogError($"Web版のビルドに失敗しました：{report.summary.result}（エラーはコンソールを確認）");
        }
    }
}
