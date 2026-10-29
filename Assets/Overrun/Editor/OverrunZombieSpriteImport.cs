using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

/// Slices the supplied copies into real sprite subassets, without changing PNGs.
public sealed class OverrunZombieSpriteImport : AssetPostprocessor
{
    public const string Folder = "Assets/Overrun/Art/Resources/OverrunArt/ZombieSmall/";
    public const string AxeFolder = "Assets/Overrun/Art/Resources/OverrunArt/ZombieAxe/";
    public const string BigFolder = "Assets/Overrun/Art/Resources/OverrunArt/ZombieBig/";

    void OnPreprocessTexture()
    {
        bool small = assetPath.StartsWith(Folder, StringComparison.Ordinal);
        if ((!small && !assetPath.StartsWith(AxeFolder, StringComparison.Ordinal) && !assetPath.StartsWith(BigFolder, StringComparison.Ordinal)) ||
            !assetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return;
        var importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Multiple;
        importer.spritePixelsPerUnit = 16;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.crunchedCompression = false;
        importer.alphaIsTransparency = true;
        importer.maxTextureSize = 2048;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = SpriteMeshType.FullRect;
        settings.spriteGenerateFallbackPhysicsShape = false;
        importer.SetTextureSettings(settings);

        var factory = new SpriteDataProviderFactories(); factory.Init();
        var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
        provider.InitSpriteEditorDataProvider();
        var old = provider.GetSpriteRects().ToDictionary(r => r.name, r => r.spriteID);
        var source = new Texture2D(2, 2);
        try
        {
            if (!ImageConversion.LoadImage(source, File.ReadAllBytes(assetPath))) throw new InvalidOperationException("Cannot read zombie sheet: " + assetPath);
            string name = Path.GetFileNameWithoutExtension(assetPath);
            int count = int.Parse(Regex.Match(name, @"Sheet(\d+)$").Groups[1].Value);
            if (source.width % count != 0) throw new InvalidOperationException("Non-integral frame width: " + assetPath);
            int width = source.width / count;
            var pixels = source.GetPixels32();
            var rects = new SpriteRect[count];
            for (int i = 0; i < count; i++)
            {
                var rect = new Rect(i * width, 0, width, source.height);
                if (small && name.Contains("Second-Attack"))
                {
                    // These sheets contain translated bodies inside very wide/tall
                    // cells. Trim transparent motion padding and anchor the feet;
                    // no physical dash is implied by this cosmetic melee variant.
                    int minX = width, minY = source.height, maxX = -1, maxY = -1;
                    for (int y = 0; y < source.height; y++) for (int x = 0; x < width; x++)
                    {
                        if (pixels[y * source.width + i * width + x].a == 0) continue;
                        minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                        minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y);
                    }
                    if (maxX < 0) throw new InvalidOperationException("Empty zombie attack frame: " + name + " " + i);
                    rect = new Rect(i * width + minX, minY, maxX - minX + 1, maxY - minY + 1);
                }
                string frameName = name + "_" + i.ToString("D2");
                rects[i] = new SpriteRect
                {
                    name = frameName, rect = rect,
                    alignment = SpriteAlignment.Custom,
                    pivot = new Vector2(.5f, 8f / rect.height),
                    spriteID = old.TryGetValue(frameName, out var id) ? id : StableId(assetPath + ":" + frameName)
                };
            }
            provider.SetSpriteRects(rects);
            var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            if (names != null) names.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)));
            provider.Apply();
        }
        finally { UnityEngine.Object.DestroyImmediate(source); }
    }

    static GUID StableId(string value)
    {
        using (var hash = MD5.Create()) return new GUID(BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "").ToLowerInvariant());
    }
}
