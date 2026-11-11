using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// メニュー「Robot → Web版をビルド」。ブラウザで遊べる版（WebGL）を、サーバーの server/public に書き出す。
/// server フォルダをまるごとサーバーにアップロードして起動すると、その URL を開くだけで遊べて、部屋番号でマルチもできる。
/// （Unity Hub で「Web Build Support」モジュールを入れておく必要がある）
/// </summary>
public static class WebBuild
{
    const string OutDir = "../server/public";   // Unity プロジェクト（robot/）から見た場所

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

        // gzip で小さくする（server.js が Content-Encoding を付けて配るので、ブラウザがそのまま展開する）
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = false;
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
                "できました：server/public\n\n" +
                "・自分のPCで試す：server フォルダで npm install → npm start を実行して http://localhost:8080 を開く\n" +
                "・みんなで遊ぶ：server フォルダをまるごとサーバーにアップロードして npm start で起動する（README 参照）", "OK");
        }
        else
        {
            Debug.LogError($"Web版のビルドに失敗しました：{report.summary.result}（エラーはコンソールを確認）");
        }
    }
}
