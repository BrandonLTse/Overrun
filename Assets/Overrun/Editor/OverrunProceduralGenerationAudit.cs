using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// Extends the existing one-shot audit workflow with procedural layout coverage.
/// Drop a request in Temp or use the menu. Never runs just because Unity opens.
[InitializeOnLoad]
public static class OverrunProceduralGenerationAudit
{
    const string Request = "Temp/OverrunProceduralAudit.request";
    const string Phase = "OverrunProceduralAudit.phase";
    static double started;

    static OverrunProceduralGenerationAudit() { EditorApplication.update += Tick; }

    [MenuItem("Overrun/Validate Procedural Building")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Request, "Verify procedural generation, movement, visible colliders and spawn safety.");
    }

    public static void RunBatch() { SessionState.SetBool("OverrunProceduralAudit.batch", true); RequestAudit(); }

    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            SessionState.SetInt(Phase, 1);
            EditorApplication.isPaused = false;
            started = EditorApplication.timeSinceStartup;
            Debug.Log("[Overrun Procedural Audit] Starting a fresh Play Mode check.");
            EditorApplication.isPlaying = true;
            return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup; // Domain reload resets static fields.
        EditorApplication.QueuePlayerLoopUpdate();
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (EditorApplication.timeSinceStartup - started > 60)
        {
            Finish($"FAIL: Play Mode initialization timed out. Playing={EditorApplication.isPlaying}; level={level != null}; map={level && level.Map != null}; bootstrap={UnityEngine.Object.FindAnyObjectByType<OverrunRuntimeBootstrap>() != null}.", false);
            return;
        }
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3) return;
        if (!OverrunMovementAudit.EnsurePlaying()) return;
        var report = new StringBuilder();
        bool passed = false;
        try
        {
            VerifyImports(report);
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
            if (director) director.enabled = false;
            OverrunMovementAudit.Verify(report);
            CapturePreview(level, report);
            VerifyLayouts(level, report);
            report.AppendLine("PASS");
            passed = true;
        }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); Debug.LogException(ex); }
        Finish(report.ToString(), passed);
    }

    static void Finish(string report, bool passed)
    {
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/OverrunProceduralAudit.txt", report);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase);
        Debug.Log("[Overrun Procedural Audit] " + report);
        if (SessionState.GetBool("OverrunProceduralAudit.batch", false))
        {
            SessionState.EraseBool("OverrunProceduralAudit.batch");
            EditorApplication.Exit(passed ? 0 : 1);
        }
        else EditorApplication.isPlaying = false;
    }

    static void VerifyImports(StringBuilder report)
    {
        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { OverrunProceduralPropImport.Folder.TrimEnd('/') });
        Require(guids.Length == 10, "Expected all ten supplied prop textures.");
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            Require(importer.filterMode == FilterMode.Point && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp,
                "Incorrect pixel-art filtering: " + path);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            Require(texture.width == width && texture.height == height && sprite && Mathf.Approximately(sprite.pixelsPerUnit, 16),
                "Sprite dimensions or PPU changed: " + path);
            Require(sprite.pivot == new Vector2(width * .5f, 0), "Sprite pivot must be bottom-center: " + path);
        }
        report.AppendLine("All ten source PNGs imported at original dimensions, 16 PPU, point/clamp, no mipmaps.");
    }

    static void CapturePreview(LevelGenerator level, StringBuilder report)
    {
        var go = new GameObject("Procedural Audit Camera");
        RenderTexture target = null;
        Texture2D image = null;
        var previous = RenderTexture.active;
        try
        {
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .04f, .045f);
            int width = level.Map.Width, height = level.Map.Height;
            camera.orthographicSize = height * .5f;
            camera.transform.position = new Vector3(width * .5f, height * .5f, -10);
            target = new RenderTexture(width * 16, height * 16, 24);
            target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            image = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
            image.Apply();
            Directory.CreateDirectory("Logs");
            File.WriteAllBytes("Logs/OverrunProceduralBuilding.png", image.EncodeToPNG());
            report.AppendLine("Full-building render captured to Logs/OverrunProceduralBuilding.png; gameplay camera zoom remains 7.5.");
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(go);
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (image) UnityEngine.Object.Destroy(image);
        }
    }

    static void VerifyLayouts(LevelGenerator liveLevel, StringBuilder report)
    {
        Require(liveLevel.GetComponentsInChildren<OverrunProceduralRoom>().Length == 9, "Default run did not create nine procedural rooms.");
        // Test isolated generators away from the live player/enemies, using the
        // same physics scan and flow-field implementation as an actual run.
        var template = OverrunProceduralRoom.CreateTemplate(32, 24, 14, 24);
        GameObject root = null;
        var seenProps = new System.Collections.Generic.HashSet<string>();
        try
        {
            root = new GameObject("Procedural Audit Layouts");
            var test = root.AddComponent<LevelGenerator>();
            test.templates = new[] { template.GetComponent<RoomTemplate>() };
            test.wallMask = LayerMask.GetMask("Walls");
            test.minReachableFraction = 1f;
            // Temporarily disable the live rooms to keep scans isolated at origin.
            liveLevel.gameObject.SetActive(false);
            var sizes = new[] { new Vector2Int(3, 3), new Vector2Int(2, 3), new Vector2Int(3, 2), new Vector2Int(1, 1) };
            foreach (var size in sizes)
            {
                test.gridW = size.x; test.gridH = size.y;
                string fingerprint = null;
                for (int seed = 1; seed <= 8; seed++)
                {
                    test.seed = seed;
                    test.Generate();
                    Require(test.ConnectivityValid, "Connectivity failed for seed " + seed);
                    var seen = test.Map.ReachableFrom(test.Map.WorldToCell(test.PlayerStart), out int reached);
                    Require(reached == test.Map.WalkableCount, "Unreachable walkable pocket for seed " + seed);
                    var field = new FlowField(test.Map); field.Rebuild(test.Map.WorldToCell(test.PlayerStart));
                    foreach (var point in test.SpawnPoints)
                    {
                        var cell = test.Map.WorldToCell(point.transform.position);
                        Require(seen[test.Map.Index(cell)] && field.DistanceAt(cell) != FlowField.Unreachable, "Unreachable spawn.");
                        Require(!Physics2D.OverlapCircle(point.transform.position, .45f, test.wallMask), "Spawn lacks player/enemy clearance.");
                    }
                    var rooms = test.GetComponentsInChildren<OverrunProceduralRoom>();
                    Require(test.SpawnPoints.Count == rooms.Length * 4, "Missing edge spawns.");
                    Require(rooms.All(r => r.PlacedCount > 0), "Obstacle sampling fell back to an empty room.");
                    foreach (var collider in test.GetComponentsInChildren<Collider2D>())
                    {
                        if (collider.GetComponent<SpriteRenderer>())
                        {
                            var renderer = collider.GetComponent<SpriteRenderer>();
                            Require(renderer && renderer.enabled && renderer.sprite && renderer.color.a > 0, "Invisible prop collider: " + collider.name);
                            var art = renderer.bounds; var bounds = collider.bounds;
                            Require(art.Contains(new Vector3(bounds.min.x, bounds.min.y, art.center.z)) &&
                                art.Contains(new Vector3(bounds.max.x, bounds.max.y, art.center.z)), "Collider extends outside sprite: " + collider.name);
                            seenProps.Add(collider.name);
                        }
                        else
                        {
                            var map = collider.GetComponent<Tilemap>(); var renderer = collider.GetComponent<TilemapRenderer>();
                            var rows = collider.GetComponent<OverrunWallDepth>();
                            Require(map && renderer && (renderer.enabled || (rows && rows.RenderedTiles > 0)) && map.GetUsedTilesCount() > 0, "Invisible wall collider.");
                        }
                    }
                    VerifyBoundariesAndDoors(test, report);
                    string next = Fingerprint(test);
                    if (seed == 1)
                    {
                        fingerprint = next;
                        test.Generate();
                        string repeated = Fingerprint(test);
                        Require(repeated == fingerprint, "Same seed did not reproduce obstacle placements.");
                    }
                    if (seed == 2) Require(next != fingerprint, "Different seeds produced identical placements.");
                }
                report.AppendLine($"{size.x}x{size.y}: eight seeds passed full connectivity, visible collider, doorway and spawn checks.");
            }
            Require(seenProps.Count == 10, "Not all ten supplied prop types were sampled.");
            report.AppendLine("All ten prop types exercised; deterministic repeats and different-seed variation verified.");
        }
        finally
        {
            if (root) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            template.SetActive(false); UnityEngine.Object.Destroy(template);
            liveLevel.gameObject.SetActive(true);
            Physics2D.SyncTransforms();
        }
    }

    static string Fingerprint(LevelGenerator level) => string.Join(";", level.GetComponentsInChildren<PolygonCollider2D>().Select(c => c.name + c.transform.position));

    static void VerifyBoundariesAndDoors(LevelGenerator level, StringBuilder report)
    {
        var map = level.Map;
        for (int x = 0; x < map.Width; x++)
            Require(!map.IsWalkable(new Vector2Int(x, 0)) && !map.IsWalkable(new Vector2Int(x, map.Height - 1)), "Outer horizontal wall has a hole.");
        for (int y = 0; y < map.Height; y++)
            Require(!map.IsWalkable(new Vector2Int(0, y)) && !map.IsWalkable(new Vector2Int(map.Width - 1, y)), "Outer vertical wall has a hole.");
        foreach (var room in level.GetComponentsInChildren<RoomTemplate>())
        {
            int degree = 0;
            var origin = map.WorldToCell(room.transform.position);
            var doors = new[] { room.blockNorth, room.blockEast, room.blockSouth, room.blockWest };
            var starts = new[] { new Vector2Int(level.roomW / 2 - 2, level.roomH - 1), new Vector2Int(level.roomW - 1, level.roomH / 2 - 2),
                new Vector2Int(level.roomW / 2 - 2, 0), new Vector2Int(0, level.roomH / 2 - 2) };
            for (int i = 0; i < doors.Length; i++)
            {
                bool open = !doors[i].activeSelf;
                if (open) degree++;
                for (int n = 0; n < 4; n++)
                {
                    var cell = origin + starts[i] + (i % 2 == 0 ? Vector2Int.right : Vector2Int.up) * n;
                    Require(map.IsWalkable(cell) == open, "Door collider does not match its visible closure state.");
                }
            }
            if (level.gridW > 1 && level.gridH > 1) Require(degree >= 2, "Room has fewer than two connections.");
        }
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
