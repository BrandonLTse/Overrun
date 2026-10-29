using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Tilemaps;

// One-shot Play Mode integration check; requested explicitly through Temp.
[InitializeOnLoad]
public static class OverrunMovementAudit
{
    const string Request = "Temp/OverrunMovementAudit.request";
    const string Phase = "OverrunMovementAudit.phase";
    static double enteredPlay;

    static OverrunMovementAudit() { EditorApplication.update += Tick; }

    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 0)
        {
            SessionState.SetInt(Phase, 1);
            if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
            return;
        }
        if (phase == 1)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            // ProjectSettings are cached by an open editor; update the live
            // serialized settings as well as the on-disk asset before restarting.
            EnsureWallsLayer();
            EditorApplication.isPaused = false;
            SessionState.SetInt(Phase, 2);
            enteredPlay = EditorApplication.timeSinceStartup;
            EditorApplication.isPlaying = true;
            return;
        }
        if (!EditorApplication.isPlaying) return;
        EditorApplication.isPaused = false;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - enteredPlay < 3) return;
        var currentLevel = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if ((currentLevel == null || currentLevel.Map == null) && EditorApplication.timeSinceStartup - enteredPlay < 15) return;
        if (!EnsurePlaying()) return;
        File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase);
        var report = new StringBuilder();
        try { Verify(report); report.AppendLine("PASS"); }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); Debug.LogException(ex); }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/OverrunMovementAudit.txt", report.ToString());
        Debug.Log("[Overrun Movement Audit] " + report);
    }

    static void EnsureWallsLayer()
    {
        if (LayerMask.NameToLayer("Walls") >= 0) return;
        var settings = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = settings.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var layer = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(layer.stringValue)) continue;
            layer.stringValue = "Walls";
            settings.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return;
        }
        throw new InvalidOperationException("No free layer slot for Walls.");
    }

    // Gameplay audits explicitly enter a run; the prototype no longer auto-starts.
    public static bool EnsurePlaying()
    {
        var menus = OverrunMenus.Instance;
        if (menus && menus.State == OverrunMenus.ScreenState.MainMenu) { menus.StartRun(); return false; }
        if (menus && menus.State == OverrunMenus.ScreenState.Paused) { menus.Resume(); return false; }
        return !OverrunMenus.InputBlocked;
    }

    public static void Verify(StringBuilder report)
    {
        var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>();
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        report.AppendLine($"Playing={EditorApplication.isPlaying}, paused={EditorApplication.isPaused}, timeScale={Time.timeScale}, player={player != null}, level={level != null}, map={level != null && level.Map != null}");
        Require(player && level && level.Map != null, "Playable room was not initialized.");
        var body = player.GetComponent<Rigidbody2D>();
        int walls = LayerMask.GetMask("Walls");
        Require(walls != 0, "Walls layer is missing.");
        Physics2D.SyncTransforms();
        var startCell = level.Map.WorldToCell(level.PlayerStart);
        Require(level.Map.IsWalkable(startCell), "Spawn is blocked in navigation grid.");
        Require(!Physics2D.OverlapCircle(level.PlayerStart, .4f, walls), "Spawn overlaps scenery.");
        var proceduralRooms = level.GetComponentsInChildren<OverrunProceduralRoom>();
        bool procedural = proceduralRooms.Length > 0;
        if (!procedural) Require(!level.Map.IsWalkable(new Vector2Int(32, 24)), "Desk/chair at the old starting cell is incorrectly walkable.");
        var visual = player.GetComponentInChildren<SpriteRenderer>();
        Require(visual && visual.sprite, "Player sprite missing after Play Mode restart.");
        report.AppendLine($"Safe spawn: {level.PlayerStart}; generation={(procedural ? "procedural" : "Tiled")}.");

        var camera = Camera.main;
        Require(camera && Mathf.Approximately(camera.orthographicSize, 7.5f), "Camera is not at the original zoom.");
        var follow = camera.GetComponent<OverrunCamera>();
        Require(follow && follow.target == player.transform, "Camera does not follow the player.");

        Vector2 open = Vector2.zero;
        bool found = false;
        if (procedural)
        {
            var room = proceduralRooms[0];
            open = (Vector2)room.transform.position + new Vector2(room.width / 2, room.height / 2);
            found = true; // The reserved cross provides four clear movement paths.
        }
        for (int y = 4; y < level.Map.Height - 4 && !found; y++)
            for (int x = 4; x < level.Map.Width - 4 && !found; x++)
            {
                var cell = new Vector2Int(x, y);
                var center = level.Map.CellCenter(cell);
                if (level.Map.IsWalkable(cell) && !Physics2D.OverlapBox(center, Vector2.one * 7f, 0, walls)) { open = center; found = true; }
            }
        Require(found, "No open area for movement test.");
        var previousKeyboard = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>("Overrun movement verification");
        var oldMode = Physics2D.simulationMode;
        var oldInterpolation = body.interpolation;
        var savedPosition = body.position;
        var otherBodies = UnityEngine.Object.FindObjectsByType<Rigidbody2D>();
        var simulated = new bool[otherBodies.Length];
        try
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            body.interpolation = RigidbodyInterpolation2D.None;
            for (int i = 0; i < otherBodies.Length; i++)
            {
                simulated[i] = otherBodies[i].simulated;
                if (otherBodies[i] != body && otherBodies[i].bodyType != RigidbodyType2D.Static) otherBodies[i].simulated = false;
            }
            var keys = new[] { Key.W, Key.A, Key.S, Key.D };
            var directions = new[] { Vector2.up, Vector2.left, Vector2.down, Vector2.right };
            for (int i = 0; i < keys.Length; i++)
            {
                Place(body, open);
                Press(keyboard, keys[i]);
                Step(player, 15);
                Vector2 delta = body.position - open;
                Require(Vector2.Dot(delta, directions[i]) > 1.9f, "Movement failed for " + keys[i] + ": " + delta);
                report.AppendLine($"{keys[i]} moved {delta.magnitude:F2} units in the correct direction.");
            }
            Press(keyboard);
            var stop = body.position;
            Step(player, 5);
            Require(Vector2.Distance(body.position, stop) < .01f, "Player keeps moving when input is released.");

            var wallTest = procedural ? (Vector2)proceduralRooms[0].transform.position + new Vector2(2.5f, 3.5f) : new Vector2(2.5f, 24.5f);
            Place(body, wallTest);
            Press(keyboard, Key.A);
            Step(player, 40);
            float wallStop = procedural ? proceduralRooms[0].transform.position.x + 1.45f : 1.5f;
            Require(Mathf.Abs(body.position.x - wallStop) < .16f, "Player did not stop at the left wall: " + body.position);
            report.AppendLine("Solid wall stops movement.");

            // Find a furniture cell with a clear three-cell approach from the left.
            var maps = level.GetComponentsInChildren<Tilemap>();
            bool checkedFurniture = false;
            if (procedural)
            {
                foreach (var collider in level.GetComponentsInChildren<Collider2D>())
                {
                    if (!collider.GetComponent<SpriteRenderer>()) continue;
                    var bounds = collider.bounds;
                    var approach = new Vector2(bounds.min.x - 2.5f, bounds.center.y);
                    if (Physics2D.OverlapBox(new Vector2(bounds.min.x - 1.6f, bounds.center.y), new Vector2(3f, 1f), 0, walls)) continue;
                    Place(body, approach);
                    Press(keyboard, Key.D);
                    Step(player, 35);
                    Require(body.position.x < bounds.min.x - .35f && body.position.x > bounds.min.x - .65f,
                        "Player did not stop at prop footprint: " + collider.name + " " + body.position);
                    Require(!Physics2D.OverlapCircle(body.position, .35f, walls), "Player ended inside a prop.");
                    checkedFurniture = true;
                    report.AppendLine("Prop base collider stops movement without trapping the player.");
                    break;
                }
            }
            foreach (var map in maps)
            {
                if (map.name != "ShortObjects") continue;
                foreach (var p in map.cellBounds.allPositionsWithin)
                {
                    if (!map.HasTile(p)) continue;
                    var target = new Vector2Int(p.x, p.y);
                    bool clear = true;
                    for (int n = 1; n <= 3; n++) clear &= level.Map.IsWalkable(target + Vector2Int.left * n);
                    if (!clear) continue;
                    var center = level.Map.CellCenter(target);
                    Place(body, center + Vector2.left * 3);
                    Press(keyboard, Key.D);
                    Step(player, 30);
                    Require(body.position.x < center.x - .7f, "Player passed through furniture.");
                    Require(!Physics2D.OverlapCircle(body.position, .35f, walls), "Player ended inside furniture.");
                    report.AppendLine("Furniture stops movement without trapping the player inside it.");
                    checkedFurniture = true;
                    break;
                }
                if (checkedFurniture) break;
            }
            Require(checkedFurniture, "No furniture approach was tested.");
            player.ResetRun();
            Require(Vector2.Distance(body.position, level.PlayerStart) < .01f, "Run reset is outside the safe spawn.");
            follow.SendMessage("LateUpdate");
            Require(Vector2.Distance(camera.transform.position, player.transform.position) < .01f, "Camera does not track player position.");
            report.AppendLine("Run reset restores the player on clear floor; close camera follows player.");
        }
        finally
        {
            Press(keyboard);
            player.SendMessage("Update");
            body.linearVelocity = Vector2.zero;
            Place(body, savedPosition);
            body.interpolation = oldInterpolation;
            for (int i = 0; i < otherBodies.Length; i++) if (otherBodies[i]) otherBodies[i].simulated = simulated[i];
            Physics2D.simulationMode = oldMode;
            InputSystem.RemoveDevice(keyboard);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            follow.SendMessage("LateUpdate");
        }
    }

    static void Place(Rigidbody2D body, Vector2 position)
    {
        body.linearVelocity = Vector2.zero;
        body.position = position;
        body.transform.position = position;
        Physics2D.SyncTransforms();
    }

    static void Press(Keyboard keyboard, params Key[] keys)
    {
        keyboard.MakeCurrent();
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
    }

    static void Step(OverrunPlayer player, int count)
    {
        player.SendMessage("Update");
        for (int n = 0; n < count; n++)
        {
            player.SendMessage("FixedUpdate");
            Physics2D.Simulate(.02f);
        }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
