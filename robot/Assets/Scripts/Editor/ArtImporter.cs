using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Assets/Resources/Art に置いた PNG を、ロボット内部用のスプライトとして取り込む設定にする。
/// （1メートル = 64ピクセル、ぼかさない、圧縮しない、床と壁は繰り返し並べられるように）
/// </summary>
public class ArtImporter : AssetPostprocessor
{
    const string Folder = "Assets/Resources/Art/";

    void OnPreprocessTexture()
    {
        if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
        var ti = (TextureImporter)assetImporter;
        string name = Path.GetFileNameWithoutExtension(assetPath);

        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        bool bigLayer = name.StartsWith("robot_"); // ロボット全体の大きな絵（robot_layers.py）
        ti.spritePixelsPerUnit = bigLayer ? InteriorMap.LayerPixelsPerUnit : Art.PixelsPerUnit;
        if (bigLayer) ti.maxTextureSize = 4096;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        // 大きな絵は高画質の圧縮（メモリを抑える）、小さな絵は圧縮しない
        ti.textureCompression = bigLayer ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
        ti.filterMode = FilterMode.Bilinear;
        ti.wrapMode = name == "floor" || name == "wall_face" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; // 繰り返し・伸ばし表示に必要
        settings.spriteAlignment = (int)SpriteAlignment.Center;
        ti.SetTextureSettings(settings);

        ti.spriteBorder = name == "wall_cap" ? new Vector4(8, 8, 8, 8)
                        : name == "piston" ? new Vector4(0, 24, 0, 24)
                        : Vector4.zero;
    }

    [MenuItem("Robot/イラストを読み込み直す")]
    public static void Reimport()
    {
        AssetDatabase.ImportAsset("Assets/Resources/Art", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
    }
}
