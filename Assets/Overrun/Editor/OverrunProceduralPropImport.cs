using UnityEditor;
using UnityEngine;

/// Applies only to the supplied copies. Source pack PNGs and existing atlases stay untouched.
public sealed class OverrunProceduralPropImport : AssetPostprocessor
{
    public const string Folder = "Assets/Overrun/Art/Resources/OverrunArt/ProceduralProps/";

    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith(Folder, System.StringComparison.Ordinal) &&
            assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            Configure((TextureImporter)assetImporter);
    }

    public static void Configure(TextureImporter importer)
    {
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 16;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Custom;
        settings.spritePivot = new Vector2(.5f, 0);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        importer.SetTextureSettings(settings);
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 2048;
    }
}
