using UnityEditor;
using UnityEngine;

// Runtime sprite rectangles use the original TSX pixel coordinates. Never resize
// or compress these atlases: neither dimension needs to be a power of two.
public sealed class OverrunTiledTextureImport : AssetPostprocessor
{
    internal const string Folder = "Assets/Overrun/Art/Resources/OverrunArt/OfficeMap1/";

    void OnPreprocessTexture()
    {
        if (assetPath.StartsWith(Folder, System.StringComparison.Ordinal) &&
            assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            Configure((TextureImporter)assetImporter);
    }

    internal static void Configure(TextureImporter importer)
    {
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.maxTextureSize = 2048;
        importer.isReadable = false;
        importer.alphaIsTransparency = true;
        foreach (string platform in new[] { "DefaultTexturePlatform", "Standalone", "WebGL" })
        {
            var settings = importer.GetPlatformTextureSettings(platform);
            settings.maxTextureSize = 2048;
            settings.textureCompression = TextureImporterCompression.Uncompressed;
            settings.crunchedCompression = false;
            settings.format = TextureImporterFormat.Automatic;
            importer.SetPlatformTextureSettings(settings);
        }
    }
}
