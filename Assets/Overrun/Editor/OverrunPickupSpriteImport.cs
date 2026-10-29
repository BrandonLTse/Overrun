using System;
using UnityEditor;
using UnityEngine;

/// Import only the three supplied copies; never alter the source asset pack.
public sealed class OverrunPickupSpriteImport : AssetPostprocessor
{
    public const string Folder = "Assets/Overrun/Art/Resources/OverrunArt/Pickables/";
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(Folder, StringComparison.Ordinal) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 16; importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.crunchedCompression = false;
        importer.alphaIsTransparency = true; importer.maxTextureSize = 1024;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteAlignment = (int)SpriteAlignment.Center; settings.spritePivot = new Vector2(.5f, .5f);
        settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);
    }
}
