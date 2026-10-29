using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;

[InitializeOnLoad]
public static class OverrunNavigationAudit
{
    const string Request = "Temp/OverrunNavigationAudit.request", Phase = "OverrunNavigationAudit.phase";
    static double started;
    static OverrunNavigationAudit() { EditorApplication.update += Tick; }
    [MenuItem("Overrun/Validate Corner Navigation and Ground Collision")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify physical corner routes, crowds, accurate ground footprints, wall edges and depth sorting.");
    }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SessionState.GetInt(Phase, 0) == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            SessionState.SetInt(Phase, 1); started = EditorApplication.timeSinceStartup;
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - started > 120) { Finish("FAIL: Navigation audit timed out."); return; }
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3 || !OverrunMovementAudit.EnsurePlaying()) return;
        var report = new StringBuilder();
        try
        {
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>(); director.enabled = false;
            OverrunMovementAudit.Verify(report);
            VerifyRoutes(director, report);
            VerifyWalls(level, report);
            VerifyPropsAndCapture(director, report);
            report.AppendLine("PASS");
        }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); Debug.LogException(ex); }
        Finish(report.ToString());
    }
    static void Finish(string report)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunNavigationAudit.txt", report);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); Debug.Log("[Overrun Navigation Audit] " + report); EditorApplication.isPlaying = false;
    }

    static BoxCollider2D Wall(Transform root, Vector2 center, Vector2 size)
    {
        var go = new GameObject("Corner fixture wall"); go.transform.SetParent(root, false); go.transform.localPosition = center;
        go.layer = LayerMask.NameToLayer("Walls"); var box = go.AddComponent<BoxCollider2D>(); box.size = size; return box;
    }
    static Enemy Agent(WaveDirector director, int type, Vector2 position, Transform parent)
    {
        var enemy = UnityEngine.Object.Instantiate(director.enemies[type].prefab, parent);
        enemy.transform.position = position; enemy.gameObject.SetActive(true); enemy.Init(director.enemies[type], () => enemy.gameObject.SetActive(false));
        return enemy;
    }
    static void VerifyRoutes(WaveDirector director, StringBuilder report)
    {
        var service = FlowFieldService.Instance; var savedMap = service.Map;
        Vector3 playerPosition = director.player.position;
        var playerBody = director.player.GetComponent<Rigidbody2D>(); bool playerSimulation = playerBody.simulated;
        var mode = Physics2D.simulationMode; int direct = service.directChaseCells;
        var root = new GameObject("Physical corner fixtures"); root.transform.position = new Vector3(10000, 10000, 0);
        try
        {
            playerBody.simulated = false; Physics2D.simulationMode = SimulationMode2D.Script;
            for (int fixture = 0; fixture < 6; fixture++)
            {
                var layout = new GameObject("Route " + fixture); layout.transform.SetParent(root.transform, false);
                Vector2 start, target;
                if (fixture == 0) { Wall(layout.transform, new Vector2(12, 10), new Vector2(4, 4)); start = new Vector2(8, 10); target = new Vector2(16, 10); }
                else if (fixture == 1)
                {
                    Wall(layout.transform, new Vector2(12, 10), new Vector2(.25f, 6)); Wall(layout.transform, new Vector2(14, 7), new Vector2(4, .25f));
                    start = new Vector2(10.5f, 8.2f); target = new Vector2(14.5f, 8.2f);
                }
                else if (fixture == 2)
                {
                    Wall(layout.transform, new Vector2(12, 10.5f), new Vector2(.16f, .75f));
                    start = new Vector2(11.35f, 10.5f); target = new Vector2(12.65f, 10.5f);
                }
                else if (fixture == 3) { Wall(layout.transform, new Vector2(12, 10), new Vector2(4, 4)); start = new Vector2(9.63f, 11.99f); target = new Vector2(16, 10); }
                else
                {
                    Wall(layout.transform, new Vector2(12.25f, 10.25f), Vector2.one * .6f);
                    start = new Vector2(12.95f, 10.95f); target = new Vector2(16.5f, 13.5f);
                    if (fixture == 5) { var swap = start; start = target; target = swap; }
                }
                Physics2D.SyncTransforms();
                var map = new GridMap(root.transform.position, 24, 20, 1, director.wallMask); service.Init(map);
                director.player.position = root.transform.position + (Vector3)target; Physics2D.SyncTransforms();
                service.RebuildAtPlayer();
                service.directChaseCells = fixture == 2 ? 10 : direct;
                if (fixture == 2) Require(!map.CanTraverse(new Vector2Int(11, 10), new Vector2Int(12, 10)), "A thin wall between clear cell centres was ignored.");
                foreach (int type in new[] { 0, 1, 2 })
                {
                    var enemy = Agent(director, type, (Vector2)root.transform.position + start, layout.transform);
                    bool arrived = false; var body = enemy.GetComponent<Rigidbody2D>();
                    for (int tick = 0; tick < 1600; tick++)
                    {
                        Vector2 before = body.position; enemy.SendMessage("FixedUpdate"); Physics2D.Simulate(.02f);
                        Require(Vector2.Distance(before, body.position) <= director.enemies[type].speed * .02f + .12f, "Navigation teleported an enemy.");
                        if (Vector2.Distance(body.position, director.player.position) < .55f) { arrived = true; break; }
                    }
                    Require(arrived, $"{director.enemies[type].name} stuck in physical corner fixture {fixture} at {body.position - (Vector2)root.transform.position}.");
                    enemy.Despawn(); UnityEngine.Object.Destroy(enemy.gameObject);
                }
                layout.SetActive(false); UnityEngine.Object.Destroy(layout); Physics2D.SyncTransforms();
            }
            report.AppendLine("All three zombie types physically reach the target around box corners, L-walls, thin intervening walls, contact-skin starts, blocked-centre recovery and a wall-hugging player (18 routes, no teleports).");

            var crowdRoot = new GameObject("Corner crowd"); crowdRoot.transform.SetParent(root.transform, false);
            Wall(crowdRoot.transform, new Vector2(12, 10), new Vector2(4, 4)); Physics2D.SyncTransforms();
            var crowdMap = new GridMap(root.transform.position, 24, 20, 1, director.wallMask); service.Init(crowdMap);
            director.player.position = root.transform.position + new Vector3(18, 10, 0); Physics2D.SyncTransforms();
            service.Field.Rebuild(crowdMap.WorldToCell(director.player.position)); service.directChaseCells = direct;
            var crowd = new Enemy[6];
            for (int i = 0; i < crowd.Length; i++) crowd[i] = Agent(director, i % 3, (Vector2)root.transform.position + new Vector2(6 + (i % 3) * .9f, 9 + (i / 3) * .9f), crowdRoot.transform);
            for (int tick = 0; tick < 2400 && crowd.Any(e => e.IsAlive); tick++)
            {
                foreach (var enemy in crowd) if (enemy.IsAlive) enemy.SendMessage("FixedUpdate");
                Physics2D.Simulate(.02f);
                foreach (var enemy in crowd) if (enemy.IsAlive && Vector2.Distance(enemy.GetComponent<Rigidbody2D>().position, director.player.position) < .8f) enemy.Despawn();
            }
            Require(crowd.All(e => !e.IsAlive), "A mixed six-zombie crowd remained pinned at the corner.");
            report.AppendLine("Six mixed shamblers/runners/brutes clear a shared physical corner with enemy-enemy collisions enabled.");
        }
        finally
        {
            root.SetActive(false); UnityEngine.Object.Destroy(root);
            director.player.position = playerPosition; playerBody.position = playerPosition; playerBody.simulated = playerSimulation;
            service.directChaseCells = direct; service.Init(savedMap); service.Field.Rebuild(savedMap.WorldToCell(playerPosition));
            Physics2D.simulationMode = mode; Physics2D.SyncTransforms();
        }
    }

    static void VerifyWalls(LevelGenerator level, StringBuilder report)
    {
        int checkedCells = 0;
        foreach (var wall in level.GetComponentsInChildren<OverrunWallDepth>())
        {
            var map = wall.GetComponent<Tilemap>(); int tiles = 0; wall.Refresh();
            foreach (var cell in map.cellBounds.allPositionsWithin)
            {
                if (!map.HasTile(cell)) continue; tiles++;
                Vector2 center = map.GetCellCenterWorld(cell);
                Require(Physics2D.OverlapPoint(center, level.wallMask), "Visible wall tile has no physical blocker.");
                foreach (var direction in new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right })
                {
                    Vector2 outside = center + direction * .65f;
                    if (Physics2D.OverlapPoint(outside, level.wallMask)) continue;
                    var hit = Physics2D.Raycast(outside, -direction, .7f, level.wallMask);
                    Require(hit && Mathf.Abs(hit.distance - .15f) < .025f, "Wall collision edge differs from its visible full-cell edge.");
                }
            }
            Require(tiles == wall.RenderedTiles, "Wall row rendering lost or added a tile.");
            foreach (Transform child in wall.transform)
            {
                var row = child.GetComponent<Tilemap>(); if (!row) continue;
                float y = row.GetCellCenterWorld(new Vector3Int(0, row.cellBounds.yMin, 0)).y;
                Require(child.GetComponent<TilemapRenderer>().sortingOrder == OverrunDepthSort.OrderAt(y), "Wall row has incorrect ground depth.");
                Require(!child.GetComponent<Collider2D>(), "A wall visual copy added extra collision.");
            }
            checkedCells += tiles;
        }
        Require(checkedCells > 100, "No substantial procedural wall geometry was checked.");
        report.AppendLine($"{checkedCells} wall/closure cells match the physical tile edges within contact tolerance; depth-render copies add no collision.");
    }

    static void VerifyPropsAndCapture(WaveDirector director, StringBuilder report)
    {
        // Stay outside the live building in X while using its normal Y range;
        // Unity's finite sorting-order range cannot represent Y=10,000.
        var root = new GameObject("Ground footprint preview"); root.transform.position = new Vector3(200, 0, 0);
        var player = director.player.GetComponent<OverrunPlayer>(); var playerSort = player.GetComponent<OverrunDepthSort>();
        GameObject rearActor = null, frontActor = null;
        Vector3 saved = player.transform.position;
        RenderTexture target = null; Texture2D capture = null; var previous = RenderTexture.active;
        try
        {
            Require(playerSort && player.GetComponent<SortingGroup>(), "Player is not ground-depth sorted as a complete body/weapon group.");
            for (int i = 0; i < OverrunProceduralRoom.PropCount; i++)
            {
                var prop = OverrunProceduralRoom.CreateProp(root.transform, i, new Vector2(2 + i % 5 * 4, i < 5 ? 7 : 2));
                var collider = prop.GetComponent<PolygonCollider2D>(); var art = prop.GetComponent<SpriteRenderer>().bounds;
                Physics2D.SyncTransforms(); var bounds = collider.bounds;
                Require(collider.pathCount == 1 && collider.points.Length == 8 && collider.sharedMaterial.friction == 0, "Prop has an extra or friction-heavy collider.");
                Require(art.Contains(new Vector3(bounds.min.x, bounds.min.y, art.center.z)) && art.Contains(new Vector3(bounds.max.x, bounds.max.y, art.center.z)), "Ground collider extends outside the sprite.");
                Require(bounds.size.y < art.size.y, "Tall prop is using a full-height collider.");
                if (prop.name == "Barrel_red_1") Require(Mathf.Abs(collider.bounds.center.x - (prop.transform.position.x - 1.5f / 16)) < .005f, "Barrel collision still includes its transparent right padding.");
                var sort = prop.GetComponent<OverrunDepthSort>(); sort.Refresh(); int order = prop.GetComponent<SortingGroup>().sortingOrder;
                player.transform.position = new Vector3(bounds.center.x, bounds.max.y + .46f, 0); playerSort.Refresh();
                Require(player.GetComponent<SortingGroup>().sortingOrder < order, "Actor behind tall prop is drawn in front.");
                player.transform.position = new Vector3(bounds.center.x, bounds.min.y - .46f, 0); playerSort.Refresh();
                Require(player.GetComponent<SortingGroup>().sortingOrder > order, "Actor in front of prop is drawn behind.");
                var points = collider.GetPath(0); var line = OverrunCombatVfx.Line(prop.transform, "Ground collision outline", .025f, new Color(.3f, 1, .6f), 30000);
                line.positionCount = points.Length + 1;
                for (int p = 0; p <= points.Length; p++) line.SetPosition(p, prop.transform.TransformPoint(points[p % points.Length]));
                // Keep diagnostic outlines outside the prop's sorting group.
                line.transform.SetParent(root.transform, true);
                if (i == 7)
                {
                    foreach (bool behind in new[] { true, false })
                    {
                        var actor = UnityEngine.Object.Instantiate(player.gameObject, root.transform);
                        actor.name = behind ? "Actor behind fridge" : "Actor in front of fridge";
                        if (behind) rearActor = actor; else frontActor = actor;
                        actor.GetComponent<OverrunPlayer>().enabled = false; actor.GetComponent<Rigidbody2D>().simulated = false;
                        actor.transform.position = new Vector3(bounds.center.x + (behind ? -.18f : .18f), behind ? bounds.max.y + .46f : bounds.min.y - .46f, 0);
                        actor.GetComponent<OverrunDepthSort>().Refresh();
                    }
                }
            }
            foreach (var definition in director.enemies) Require(definition.prefab.GetComponent<OverrunDepthSort>() && definition.prefab.GetComponent<CircleCollider2D>().sharedMaterial.friction == 0, "A zombie type lacks sorting or sliding contact material.");
            player.transform.position = saved; player.GetComponent<Rigidbody2D>().position = saved; playerSort.Refresh();
            var camera = new GameObject("Ground collision preview camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false);
            camera.transform.localPosition = new Vector3(10, 5.5f, -10); camera.orthographic = true; camera.orthographicSize = 5.5f;
            camera.backgroundColor = new Color(.045f, .065f, .08f); camera.clearFlags = CameraClearFlags.SolidColor;
            target = new RenderTexture(1200, 660, 24); target.Create(); camera.targetTexture = target;
            capture = new Texture2D(1200, 660, TextureFormat.RGBA32, false);
            rearActor.SetActive(false); frontActor.SetActive(false); ReadCamera(camera, capture);
            // Compare the opaque inner fridge face, not one pixel that might
            // coincide with transparent padding at the character's hair/feet.
            Vector3 lower = camera.WorldToViewportPoint(root.transform.position + new Vector3(9.65f, 2.05f, 0));
            Vector3 upper = camera.WorldToViewportPoint(root.transform.position + new Vector3(10.35f, 3.65f, 0));
            var region = new RectInt((int)(lower.x * capture.width), (int)(lower.y * capture.height),
                (int)((upper.x - lower.x) * capture.width), (int)((upper.y - lower.y) * capture.height));
            var basePixels = capture.GetPixels32();
            rearActor.SetActive(true); rearActor.GetComponent<OverrunDepthSort>().Refresh(); ReadCamera(camera, capture);
            Require(ChangedPixels(basePixels, capture, region) == 0, "Rendered rear character overwrites the opaque fridge face.");
            rearActor.SetActive(false); frontActor.SetActive(true); frontActor.GetComponent<OverrunDepthSort>().Refresh(); ReadCamera(camera, capture);
            Require(ChangedPixels(basePixels, capture, region) > 10, "Rendered front character is hidden by the fridge.");
            rearActor.SetActive(true); rearActor.GetComponent<OverrunDepthSort>().Refresh(); ReadCamera(camera, capture);
            File.WriteAllBytes("Logs/OverrunCollisionFootprints.png", capture.EncodeToPNG());
            report.AppendLine("All ten props have single beveled ground footprints inside their art; barrel padding offset and front/behind depth ordering passed. Rendered pixel checks prove the fridge occludes a rear character but not a front character. Body/weapon and all zombie types share ground sorting.");
        }
        finally
        {
            player.transform.position = saved; player.GetComponent<Rigidbody2D>().position = saved; if (playerSort) playerSort.Refresh();
            root.SetActive(false); UnityEngine.Object.Destroy(root); RenderTexture.active = previous;
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); } if (capture) UnityEngine.Object.Destroy(capture);
            Physics2D.SyncTransforms();
        }
    }
    static void ReadCamera(Camera camera, Texture2D capture)
    {
        // This audit renders synchronously before Unity's normal post-LateUpdate
        // sorting pass; flush newly created/toggled groups before each capture.
        SortingGroup.UpdateAllSortingGroups();
        camera.Render(); RenderTexture.active = camera.targetTexture;
        capture.ReadPixels(new Rect(0, 0, capture.width, capture.height), 0, 0); capture.Apply();
    }
    static int ChangedPixels(Color32[] baseline, Texture2D capture, RectInt region)
    {
        var pixels = capture.GetPixels32(); int changed = 0;
        for (int y = region.yMin; y < region.yMax; y++) for (int x = region.xMin; x < region.xMax; x++)
        {
            int index = y * capture.width + x; var a = baseline[index]; var b = pixels[index];
            if (a.r != b.r || a.g != b.g || a.b != b.b || a.a != b.a) changed++;
        }
        return changed;
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
