using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[InitializeOnLoad]
public static class OverrunCombatAudit
{
    const string Request = "Temp/OverrunCombatAudit.request";
    const string Phase = "OverrunCombatAudit.phase";
    static double started;
    static OverrunCombatAudit() { EditorApplication.update += Tick; }

    [MenuItem("Overrun/Validate Tracer Combat")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp");
        File.WriteAllText(Request, "Verify tracer visuals, swept collision, hit feedback and player movement.");
    }

    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (SessionState.GetInt(Phase, 0) == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            SessionState.SetInt(Phase, 1); started = EditorApplication.timeSinceStartup;
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true;
            return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (EditorApplication.timeSinceStartup - started > 60) { Finish("FAIL: Play Mode initialization timed out."); return; }
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3) return;
        if (!OverrunMovementAudit.EnsurePlaying()) return;
        var report = new StringBuilder();
        try
        {
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
            if (director) director.enabled = false;
            OverrunMovementAudit.Verify(report);
            VerifyProjectiles(report);
            VerifyEnemyFeedback(report);
            VerifyFiringAndCapture(report);
            report.AppendLine("PASS");
        }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); Debug.LogException(ex); }
        Finish(report.ToString());
    }

    static void Finish(string report)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunCombatAudit.txt", report);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase);
        Debug.Log("[Overrun Combat Audit] " + report);
        EditorApplication.isPlaying = false;
    }

    static OverrunBullet Shot(Vector2 origin, Vector2 direction, float damage = 2)
    {
        var shot = new GameObject("Audit Tracer").AddComponent<OverrunBullet>();
        shot.transform.position = origin; shot.Init(damage, direction);
        return shot;
    }

    static void VerifyProjectiles(StringBuilder report)
    {
        var root = new GameObject("Audit Collision Fixtures");
        root.transform.position = new Vector3(10000, 10000, 0);
        var origin = (Vector2)root.transform.position;
        try
        {
            var owner = Box(root.transform, "Owner", new Vector2(.25f, 0), new Vector2(.8f, .8f));
            var low = Box(root.transform, "Low cover", new Vector2(1.5f, 0), new Vector2(.4f, 1)); low.gameObject.AddComponent<OverrunShootThrough>();
            var trigger = Box(root.transform, "Pickup trigger", new Vector2(2.5f, 0), new Vector2(.4f, 1)); trigger.isTrigger = true;
            var wall = Box(root.transform, "Thin wall", new Vector2(4, 0), new Vector2(.1f, 1));
            Physics2D.SyncTransforms();
            var shot = Shot(origin, Vector2.right);
            shot.Init(2, Vector2.right, new[] { owner });
            shot.Simulate(.2f); // Would travel 16.8 units through a .1-unit wall without a sweep.
            Require(!shot.InFlight && shot.transform.position.x > origin.x + 3.8f && shot.transform.position.x < wall.bounds.min.x,
                "Fast shot hit its owner/low cover/trigger, or tunneled through the wall.");
            Require(!shot.GetComponent<SpriteRenderer>(), "Bullet still renders as a floating PNG.");
            foreach (var line in shot.GetComponentsInChildren<LineRenderer>())
                Require(line.GetPosition(1).x <= wall.bounds.min.x && line.sharedMaterial.shader.name == "Overrun/TracerGlow" && line.sharedMaterial.shader.isSupported,
                    "Tracer extends through cover or its glow shader is unsupported.");
            shot.Simulate(.1f); Require(!shot.gameObject.activeSelf, "Impact afterglow did not finish.");
            UnityEngine.Object.Destroy(shot.gameObject);
            Vector2 muzzle = OverrunBullet.Fire(origin, origin + Vector2.right * 5, 2, Vector2.right, new[] { owner });
            Require(muzzle.x <= wall.bounds.min.x + .001f, "Point-blank shot spawned on the other side of the wall.");
            report.AppendLine("Swept shots cross low cover and pickups, ignore the owner, and stop at a thin wall even with a 200ms frame. Muzzle clipping is safe.");

            for (int i = 0; i < 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                Vector2 start = origin + Vector2.up * (20 + i * 10);
                shot = Shot(start, direction);
                shot.Simulate(1f / 60);
                Require(Vector2.Distance(shot.transform.position, start + direction * (OverrunBullet.Speed / 60)) < .003f, "Incorrect travel direction.");
                var line = shot.GetComponentsInChildren<LineRenderer>()[0];
                Require(Vector2.Dot(((Vector2)line.GetPosition(1) - (Vector2)line.GetPosition(0)).normalized, direction) > .99f, "Tracer does not align with aim.");
                shot.Simulate(1); Require(!shot.InFlight, "Miss exceeded its maximum range.");
                shot.Simulate(.1f); Require(!shot.gameObject.activeSelf, "Miss did not return to the pool.");
                UnityEngine.Object.Destroy(shot.gameObject);
            }
            report.AppendLine("All eight aim directions, range limit, fading and projectile retirement passed.");
        }
        finally { root.SetActive(false); UnityEngine.Object.Destroy(root); Physics2D.SyncTransforms(); }
    }

    static BoxCollider2D Box(Transform parent, string name, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
        var collider = go.AddComponent<BoxCollider2D>(); collider.size = size; return collider;
    }

    static void VerifyEnemyFeedback(StringBuilder report)
    {
        var go = new GameObject("Audit Enemy");
        go.transform.position = new Vector3(10000, 10200, 0);
        var body = go.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.freezeRotation = true;
        var visual = go.AddComponent<SpriteRenderer>(); visual.sprite = OverrunVisuals.Character(1);
        go.AddComponent<CircleCollider2D>().radius = .36f;
        var enemy = go.AddComponent<Enemy>();
        var definition = ScriptableObject.CreateInstance<EnemyDefinition>(); definition.maxHealth = 5; definition.speed = 0; definition.cost = 1;
        int deaths = 0;
        OverrunBullet shot = null;
        try
        {
            enemy.Init(definition, () => { deaths++; go.SetActive(false); }); Physics2D.SyncTransforms();
            shot = Shot((Vector2)go.transform.position + Vector2.left * 3, Vector2.right);
            shot.Simulate(.1f);
            Require(!shot.InFlight && visual.color.r > 1, "Enemy did not receive a hit flash.");
            enemy.SendMessage("FixedUpdate");
            float normalKick = body.linearVelocity.x;
            Require(normalKick > 3, "Enemy did not react to the impact direction.");
            shot.Simulate(.02f);
            enemy.TakeDamage(2); Require(deaths == 0, "Projectile dealt damage more than once.");
            enemy.TakeDamage(1); Require(deaths == 1, "Enemy damage amount changed.");
            Require(visual.color == Color.white, "Pooled enemy kept its hit-flash color.");
            go.SetActive(true); definition.cost = 8;
            enemy.Init(definition, () => { deaths++; go.SetActive(false); });
            Require(body.linearVelocity == Vector2.zero && visual.color == Color.white, "Enemy feedback leaked into a reused enemy.");
            enemy.TakeDamage(1, Vector2.right); enemy.SendMessage("FixedUpdate");
            Require(body.linearVelocity.x > 0 && body.linearVelocity.x < normalKick * .5f, "Brutes do not resist knockback.");
            report.AppendLine("Enemy hit flash, directional knockback, brute resistance, single-hit damage, death and pool reset passed.");
        }
        finally
        {
            if (shot) UnityEngine.Object.Destroy(shot.gameObject);
            go.SetActive(false); UnityEngine.Object.Destroy(go); UnityEngine.Object.Destroy(definition);
            Physics2D.SyncTransforms();
        }
    }

    static void VerifyFiringAndCapture(StringBuilder report)
    {
        var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>();
        var camera = Camera.main;
        var weapon = player.GetComponentInChildren<OverrunWeaponFeedback>();
        Require(weapon && camera, "Player's separate weapon/muzzle is missing.");
        var previousMouse = Mouse.current;
        var previousKeyboard = Keyboard.current;
        var mouse = InputSystem.AddDevice<Mouse>("Combat audit mouse");
        var keyboard = InputSystem.AddDevice<Keyboard>("Combat audit keyboard");
        RenderTexture target = null; Texture2D capture = null;
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        try
        {
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Vector2 cursor = camera.WorldToScreenPoint(player.transform.position + Vector3.right * 8);
            // Entering a run deliberately gates held menu clicks and briefly
            // cools down the weapon. This fixture starts after that input boundary.
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }); InputSystem.Update();
            player.SendMessage("Update");
            typeof(OverrunPlayer).GetField("fireTimer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 0f);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor, buttons = 1 }); InputSystem.Update();
            player.SendMessage("Update");
            Require(Vector2.Dot(weapon.transform.up, Vector2.right) > .99f, "Weapon did not point at the mouse.");
            var shots = UnityEngine.Object.FindObjectsByType<OverrunBullet>();
            bool flying = false;
            foreach (var shot in shots) if (shot.InFlight) { shot.Simulate(.03f); flying |= shot.InFlight; }
            Require(flying, "Mouse firing did not create a light tracer.");
            Require(UnityEngine.Object.FindObjectsByType<OverrunShotBurst>().Length > 0, "Muzzle flash was not emitted.");
            camera.GetComponent<OverrunCamera>().SendMessage("LateUpdate");
            float kick = Vector2.Distance(camera.transform.position, player.transform.position);
            Require(kick > 0 && kick <= .08f && Mathf.Approximately(camera.orthographicSize, 7.5f), "Camera kick/zoom is incorrect.");
            target = new RenderTexture(960, 540, 24); target.Create();
            camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
            capture = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            capture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); capture.Apply();
            Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/OverrunCombatPreview.png", capture.EncodeToPNG());
            report.AppendLine("Actual mouse firing, rotating muzzle, muzzle flash, bounded camera kick and original zoom passed. Combat preview rendered.");
        }
        finally
        {
            InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.Update(); player.SendMessage("Update");
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (capture) UnityEngine.Object.Destroy(capture);
        }
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
