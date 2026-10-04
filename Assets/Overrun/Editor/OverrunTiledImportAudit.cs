using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

// Explicit menu action, also callable by placing a one-shot request in Temp.
// Builds an isolated room, checks every cell against TMX/TSX, and renders it.
[InitializeOnLoad]
public static class OverrunTiledImportAudit
{
    const string Request = "Temp/OverrunTiledAudit.request";
    const string ResourceFolder = "OverrunArt/OfficeMap1/";
    static bool resumePlay;

    static OverrunTiledImportAudit() { EditorApplication.update += CheckRequest; }

    static void CheckRequest()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            resumePlay = true;
            EditorApplication.isPlaying = false;
            return;
        }
        File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        try { RepairAndValidate(); }
        catch (Exception ex)
        {
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/OverrunTiledAudit.txt", "FAIL\n" + ex);
            Debug.LogException(ex);
        }
        if (resumePlay) { resumePlay = false; EditorApplication.isPlaying = true; }
    }

    [MenuItem("Overrun/Repair and Validate Tiled Room Import")]
    public static void RepairAndValidate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play Mode before running the Tiled import audit.");
        var report = new StringBuilder();
        foreach (string name in new[] { "Buildings_gray_TileSet", "Office_Furniture_16x16_Shadow" })
        {
            string path = OverrunTiledTextureImport.Folder + name + ".png";
            var before = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            report.AppendLine($"{name}: before {before.width}x{before.height}");
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            OverrunTiledTextureImport.Configure(importer);
            importer.SaveAndReimport();
            var after = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            report.AppendLine($"{name}: after {after.width}x{after.height}; NPOT={importer.npotScale}");
        }

        GameObject root = null;
        GameObject cameraObject = null;
        RenderTexture target = null;
        Texture2D capture = null;
        var previousTarget = RenderTexture.active;
        try
        {
            root = OverrunTiledRoomFactory.TryBuild(ResourceFolder + "OfficeMap1Data", out int width, out int height);
            Require(root != null, "Room factory failed.");
            root.hideFlags = HideFlags.HideAndDontSave;
            root.transform.position = new Vector3(10000, 10000, 0);
            foreach (var collider in root.GetComponentsInChildren<Collider2D>(true)) collider.enabled = false;
            root.SetActive(true);
            var maps = root.GetComponentsInChildren<Tilemap>(true);
            var document = new XmlDocument();
            document.LoadXml(Resources.Load<TextAsset>(ResourceFolder + "OfficeMap1Data").text);
            int layerOrder = 0, populated = 0, checkedCells = 0;
            foreach (XmlNode layer in document.SelectNodes("/map//layer"))
            {
                string name = layer.Attributes["name"].Value;
                var layerMaps = maps.Where(m => name == "DoorClosures" ? m.name.StartsWith("Door Closure ") : m.name == name).ToArray();
                Require(layerMaps.Length > 0, "Missing layer: " + name);
                foreach (var map in layerMaps)
                    Require(map.GetComponent<TilemapRenderer>().sortingOrder == layerOrder, "Incorrect layer order: " + name);
                var values = layer.SelectSingleNode("data").InnerText.Split(new[] { ',', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                Require(values.Length == width * height, "Unexpected layer dimensions: " + name);
                for (int i = 0; i < values.Length; i++)
                {
                    int gid = int.Parse(values[i]);
                    var cell = new Vector3Int(i % width, height - 1 - i / width, 0);
                    var present = layerMaps.Where(m => m.HasTile(cell)).ToArray();
                    checkedCells++;
                    if (gid == 0) { Require(present.Length == 0, "Unexpected tile: " + name + " " + cell); continue; }
                    Require(present.Length == 1, "Missing or duplicate tile: " + name + " " + cell);
                    var tile = present[0].GetTile<Tile>(cell);
                    Require(tile.name == "Runtime Tiled Tile " + gid, "Wrong GID: " + name + " " + cell);
                    var sprite = tile.sprite;
                    bool furniture = gid >= 170;
                    int local = gid - (furniture ? 170 : 1);
                    int columns = furniture ? 35 : 13;
                    int imageHeight = furniture ? 384 : 208;
                    var expected = new Rect((local % columns) * 16, imageHeight - (local / columns + 1) * 16, 16, 16);
                    Require(sprite.rect == expected, $"Wrong sprite rectangle for {gid}: {sprite.rect}, expected {expected}");
                    Require(sprite.texture.width == (furniture ? 560 : 208) && sprite.texture.height == imageHeight, "Resized atlas");
                    Require(sprite.texture.name == (furniture ? "Office_Furniture_16x16_Shadow" : "Buildings_gray_TileSet"), "Wrong texture");
                    populated++;
                }
                report.AppendLine("Layer verified: " + name);
                layerOrder++;
            }
            Require(maps.Single(m => m.name == "ShortObjects").GetComponent<OverrunShootThrough>() != null, "Short objects must allow shooting over them.");
            Require(maps.Single(m => m.name == "Shelves").GetComponent<OverrunShootThrough>() == null, "Tall objects must block shots.");

            cameraObject = new GameObject("Tiled Audit Camera") { hideFlags = HideFlags.HideAndDontSave };
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = height / 2f;
            camera.aspect = (float)width / height;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.magenta;
            camera.transform.position = root.transform.position + new Vector3(width / 2f, height / 2f, -10);
            target = new RenderTexture(width * 16, height * 16, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            capture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            capture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            capture.Apply();
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/OverrunTiledAudit.png", capture.EncodeToPNG());
            report.AppendLine($"PASS: {checkedCells} cells checked across {layerOrder} layers; {populated} tile sprites verified.");
            File.WriteAllText("Logs/OverrunTiledAudit.txt", report.ToString());
            Debug.Log("[Overrun Tiled Audit] " + report);
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (cameraObject) UnityEngine.Object.DestroyImmediate(cameraObject);
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (capture) UnityEngine.Object.DestroyImmediate(capture);
            if (root)
            {
                var tiles = root.GetComponentsInChildren<Tilemap>(true)
                    .SelectMany(m => m.GetTilesBlock(m.cellBounds)).OfType<Tile>().Distinct().ToArray();
                var sprites = tiles.Select(t => t.sprite).Distinct().ToArray();
                UnityEngine.Object.DestroyImmediate(root);
                foreach (var tile in tiles) UnityEngine.Object.DestroyImmediate(tile);
                foreach (var sprite in sprites) UnityEngine.Object.DestroyImmediate(sprite);
            }
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
