using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

public sealed class OverrunPistolSpriteImport : AssetPostprocessor
{
    public const string Folder = "Assets/Overrun/Art/Resources/OverrunArt/PistolAnimations/";
    public const string RifleFolder = "Assets/Overrun/Art/Resources/OverrunArt/RifleAnimations/";
    [InitializeOnLoadMethod]
    static void ScheduleNewSheets() { EditorApplication.delayCall += ImportNewRifleSheets; }
    public static void ImportNewRifleSheets()
    {
        // New textures can be seen before this postprocessor's first compilation.
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) { EditorApplication.delayCall += ImportNewRifleSheets; return; }
        if (EditorApplication.isPlayingOrWillChangePlaymode || !Directory.Exists(RifleFolder)) return;
        foreach (var path in Directory.GetFiles(RifleFolder, "*.png").Select(p => p.Replace('\\', '/')))
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer && (importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple))
            {
                Configure(importer, path, true);
                importer.SaveAndReimport();
            }
        }
    }
    void OnPreprocessTexture()
    {
        bool rifle = assetPath.StartsWith(RifleFolder, StringComparison.Ordinal);
        if ((!rifle && !assetPath.StartsWith(Folder, StringComparison.Ordinal)) || !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
        Configure((TextureImporter)assetImporter, assetPath, rifle);
    }
    static void Configure(TextureImporter importer, string path, bool rifle)
    {
        importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 16; importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point; importer.wrapMode = TextureWrapMode.Clamp; importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed; importer.crunchedCompression = false; importer.alphaIsTransparency = true;
        importer.maxTextureSize = 1024;
        var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect; settings.spriteGenerateFallbackPhysicsShape = false; importer.SetTextureSettings(settings);
        var factory = new SpriteDataProviderFactories(); factory.Init(); var provider = factory.GetSpriteEditorDataProviderFromObject(importer); provider.InitSpriteEditorDataProvider();
        var previous = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        var texture = new Texture2D(2, 2);
        try
        {
            if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path))) throw new InvalidOperationException("Cannot read supplied weapon sheet.");
            string name = Path.GetFileNameWithoutExtension(path); int count = int.Parse(Regex.Match(name, @"Sheet(\d+)$").Groups[1].Value);
            if (texture.width % count != 0) throw new InvalidOperationException("Nonintegral weapon frame width: " + name);
            int width = texture.width / count;
            var facing = name.Contains("side-left") ? OverrunPistolFacing.Left : name.Contains("_side_") ? OverrunPistolFacing.Right : name.Contains("_up_") ? OverrunPistolFacing.Up : OverrunPistolFacing.Down;
            var pose = name.IndexOf("reload", StringComparison.OrdinalIgnoreCase) >= 0 ? OverrunPistolPose.Reload : name.Contains("Death") ? OverrunPistolPose.Death : name.Contains("shoot") ? OverrunPistolPose.Shoot : OverrunPistolPose.IdleRun;
            Vector2 pivot = (rifle ? OverrunRifleClips.Pivot(facing, pose, width, texture.height) : OverrunPistolClips.Pivot(facing, pose, width, texture.height)) / new Vector2(width, texture.height);
            var rects = new SpriteRect[count];
            for (int i = 0; i < count; i++)
            {
                string frame = name + "_" + i.ToString("D2");
                rects[i] = new SpriteRect { name = frame, rect = new Rect(i * width, 0, width, texture.height), pivot = pivot,
                    alignment = SpriteAlignment.Custom, spriteID = previous.TryGetValue(frame, out var id) ? id : StableId(path + ":" + frame) };
            }
            provider.SetSpriteRects(rects);
            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (names != null) names.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
        }
        finally { UnityEngine.Object.DestroyImmediate(texture); }
    }
    static GUID StableId(string value)
    {
        using (var hash = MD5.Create()) return new GUID(BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant());
    }
}
