using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー「Robot → Web版をビルド」。ブラウザで遊べる版（WebGL）を Builds/Web に書き出す。
/// できたフォルダをまるごと itch.io などにアップロードすると、URL を開くだけで遊べる。
/// （Unity Hub で「Web Build Support」モジュールを入れておく必要がある）
/// </summary>
public static class WebBuild
{
    const string OutDir = "Builds/Web";

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

        // どこにアップロードしても動くように：gzip 圧縮＋ブラウザ側で展開できるようにする
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
            EditorUtility.RevealInFinder(Path.GetFullPath(OutDir));
            EditorUtility.DisplayDialog("Web版をビルド",
                $"できました：{OutDir}\n\nこのフォルダの中身を zip にして itch.io（HTML のゲーム）にアップロードすると、ブラウザで遊べます。\n" +
                "※ そのまま index.html をダブルクリックしても動きません（Web サーバーが必要）。", "OK");
        }
        else
        {
            Debug.LogError($"Web版のビルドに失敗しました：{report.summary.result}（エラーはコンソールを確認）");
        }
    }
}
