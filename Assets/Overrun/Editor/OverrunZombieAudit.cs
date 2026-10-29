using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Pool;

[InitializeOnLoad]
public static class OverrunZombieAudit
{
    const string SmallRequest = "Temp/OverrunZombieAudit.request", RunnerRequest = "Temp/OverrunRunnerAudit.request", BruteRequest = "Temp/OverrunBruteAudit.request", Phase = "OverrunZombieAudit.phase";
    static bool Brute => File.Exists(BruteRequest);
    static bool Runner => File.Exists(RunnerRequest);
    static string Request => Brute ? BruteRequest : Runner ? RunnerRequest : SmallRequest;
    static string Prefix => Brute ? "OverrunBrute" : Runner ? "OverrunRunner" : "OverrunZombie";
    static string ArtPrefix => Brute ? "Zombie_Big_" : Runner ? "Zombie_Axe_" : "Zombie_Small_";
    static int EnemyIndex => Brute ? 2 : Runner ? 1 : 0;
    static Sprite[] Frames(OverrunSmallZombieAnimator.Facing direction, OverrunSmallZombieAnimator.Pose pose) =>
        Brute ? OverrunBruteZombieAnimator.Frames(direction, pose) : Runner ? OverrunRunnerZombieAnimator.Frames(direction, pose) : OverrunSmallZombieAnimator.Frames(direction, pose);
    static double started;
    static OverrunZombieAudit() { EditorApplication.update += Tick; }

    [MenuItem("Overrun/Validate Small Zombie Animations")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(SmallRequest, "Verify small zombie sprite imports, animation, combat, death and pooling.");
    }

    [MenuItem("Overrun/Validate Runner Zombie Animations")]
    public static void RequestRunnerAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(RunnerRequest, "Verify axe runner sprite imports, animation, combat, death, pooling and wave unlock.");
    }

    [MenuItem("Overrun/Validate Brute Zombie Animations")]
    public static void RequestBruteAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(BruteRequest, "Verify big zombie imports, animation, combat, death, pooling and wave-six unlock.");
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
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (EditorApplication.timeSinceStartup - started > 60) { Finish("FAIL: Zombie audit Play Mode initialization timed out."); return; }
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3) return;
        if (!OverrunMovementAudit.EnsurePlaying()) return;
        var report = new StringBuilder();
        try
        {
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>(); director.enabled = false;
            OverrunMovementAudit.Verify(report);
            VerifyImports(report);
            VerifyZombie(director, report);
            if (Runner || Brute) VerifyWaveGate(director, report);
            Capture(director, report);
            report.AppendLine("PASS");
        }
        catch (Exception ex) { report.AppendLine("FAIL: " + ex); Debug.LogException(ex); }
        Finish(report.ToString());
    }

    static void Finish(string report)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/" + Prefix + "Audit.txt", report);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); Debug.Log("[Overrun Zombie Audit] " + report); EditorApplication.isPlaying = false;
    }

    static void VerifyImports(StringBuilder report)
    {
        string folder = Brute ? OverrunZombieSpriteImport.BigFolder : Runner ? OverrunZombieSpriteImport.AxeFolder : OverrunZombieSpriteImport.Folder;
        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { folder.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        Require(paths.Length == (Runner ? 19 : 20), "Not all supplied sheets were imported.");
        int total = 0;
        foreach (string path in paths)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            Require(texture.width == width && texture.height == height, "Texture was resized: " + path);
            Require(importer.filterMode == FilterMode.Point && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.npotScale == TextureImporterNPOTScale.None,
                "Pixel art import changed: " + path);
            int count = int.Parse(Regex.Match(Path.GetFileNameWithoutExtension(path), @"Sheet(\d+)$").Groups[1].Value);
            var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            Require(sprites.Length == count, "Wrong sprite count: " + path);
            for (int i = 0; i < count; i++)
            {
                var sprite = sprites[i]; var rect = sprite.rect;
                Require(sprite.pixelsPerUnit == 16 && Mathf.Approximately(sprite.pivot.y, 8), "Unstable scale/feet pivot: " + sprite.name);
                Require(rect.xMin >= i * (width / count) && rect.xMax <= (i + 1) * (width / count) && rect.yMin >= 0 && rect.yMax <= height,
                    "Frame overlaps another cell: " + sprite.name);
                Require(sprite.name == Path.GetFileNameWithoutExtension(path) + "_" + i.ToString("D2"), "Incorrect frame order/name: " + sprite.name);
                if (Runner || Brute) Require(rect == new Rect(i * (width / count), 0, width / count, height), "A supplied attack/death frame was cropped.");
                else if (path.Contains("Second-Attack")) Require(rect.width <= 27 && rect.height <= 24, "Second attack still contains large translated padding.");
            }
            total += sprites.Length;
        }
        Require(total == (Brute ? 178 : Runner ? 139 : 134), "Unexpected animation frame total.");
        foreach (OverrunSmallZombieAnimator.Facing direction in Enum.GetValues(typeof(OverrunSmallZombieAnimator.Facing)))
        foreach (var pose in new[] { OverrunSmallZombieAnimator.Pose.Idle, OverrunSmallZombieAnimator.Pose.Walk, OverrunSmallZombieAnimator.Pose.FirstAttack, OverrunSmallZombieAnimator.Pose.SecondAttack })
            Require(Frames(direction, pose).All(s => s.name.StartsWith(ArtPrefix, StringComparison.Ordinal)), "Runtime clip lookup returned incorrect art.");
        if (Brute)
        {
            foreach (OverrunSmallZombieAnimator.Facing direction in Enum.GetValues(typeof(OverrunSmallZombieAnimator.Facing)))
            foreach (var pose in new[] { OverrunSmallZombieAnimator.Pose.FirstDeath, OverrunSmallZombieAnimator.Pose.SecondDeath })
                Require(Frames(direction, pose).All(s => s.name.StartsWith(ArtPrefix, StringComparison.Ordinal)), "Brute death lookup borrowed unrelated art.");
        }
        if (Runner)
        {
            foreach (OverrunSmallZombieAnimator.Facing direction in Enum.GetValues(typeof(OverrunSmallZombieAnimator.Facing)))
            foreach (var pose in new[] { OverrunSmallZombieAnimator.Pose.FirstDeath, OverrunSmallZombieAnimator.Pose.SecondDeath })
                Require(OverrunRunnerZombieAnimator.Frames(direction, pose).All(s => s.name.StartsWith("Zombie_Axe_", StringComparison.Ordinal)), "Runner death fallback borrowed unrelated art.");
            Require(OverrunRunnerZombieAnimator.Frames(OverrunSmallZombieAnimator.Facing.Right, OverrunSmallZombieAnimator.Pose.SecondDeath) ==
                OverrunRunnerZombieAnimator.Frames(OverrunSmallZombieAnimator.Facing.Right, OverrunSmallZombieAnimator.Pose.FirstDeath), "Missing right second-death fallback failed.");
        }
        report.AppendLine($"{paths.Length} original-size sheets, {total} real sprite subassets, 16 PPU, point/no-mips/clamp and directional runtime lookups passed.");
    }

    static void VerifyZombie(WaveDirector director, StringBuilder report)
    {
        Require(director.enemies[0].prefab.GetComponent<OverrunSmallZombieAnimator>(), "Regular shambler is not using the supplied art.");
        Require(!director.enemies[0].prefab.GetComponent<OverrunCharacterAnimator>(), "Legacy animator still overwrites regular zombie frames.");
        Require(director.enemies[1].prefab.GetComponent<OverrunRunnerZombieAnimator>() && director.enemies[2].prefab.GetComponent<OverrunBruteZombieAnimator>(), "Runner/brute visual assignments are incorrect.");
        Require(!director.enemies[1].prefab.GetComponent<OverrunCharacterAnimator>(), "Legacy animator still overwrites runner frames.");
        Require(!director.enemies[2].prefab.GetComponent<OverrunCharacterAnimator>(), "Legacy animator still overwrites brute frames.");
        Require(director.enemies[1].speed == 3.6f && director.enemies[1].maxHealth == 2 && director.enemies[1].contactDamage == 12 && director.enemies[1].cost == 2 && director.runnerFromWave == 3,
            "Runner gameplay tuning changed with its visuals.");
        Require(director.enemies[2].speed == 1.35f && director.enemies[2].maxHealth == 12 && director.enemies[2].contactDamage == 20 && director.enemies[2].cost == 8 && director.bruteFromWave == 6,
            "Brute gameplay tuning changed with its visuals.");
        var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>();
        var healthField = typeof(OverrunPlayer).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic);
        float health = (float)healthField.GetValue(player);
        var definition = ScriptableObject.CreateInstance<EnemyDefinition>(); definition.maxHealth = 80; definition.speed = Brute ? 1.35f : Runner ? 3.6f : 2; definition.contactDamage = Brute ? 20 : Runner ? 12 : 7; definition.cost = Brute ? 8 : Runner ? 2 : 1;
        var pool = new ObjectPool<Enemy>(() => UnityEngine.Object.Instantiate(director.enemies[EnemyIndex].prefab),
            e => e.gameObject.SetActive(true), e => e.gameObject.SetActive(false), e => UnityEngine.Object.Destroy(e.gameObject));
        Enemy enemy = null;
        int deaths = 0;
        try
        {
            enemy = pool.Get();
            Enemy held = enemy;
            Action release = () => { deaths++; pool.Release(held); };
            enemy.Init(definition, release);
            var animation = enemy.GetComponent<OverrunSmallZombieAnimator>();
            var collider = enemy.GetComponent<CircleCollider2D>();
            enemy.transform.position = new Vector3(10000, 10000, 0);
            var directions = new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up };
            for (int i = 0; i < directions.Length; i++)
            {
                animation.SetMotion(directions[i]); animation.Advance(.13f);
                Require((int)animation.Direction == i && animation.CurrentPose == OverrunSmallZombieAnimator.Pose.Walk, "Incorrect facing/walk selection.");
                Require(animation.Visual.sprite.name.StartsWith(ArtPrefix, StringComparison.Ordinal), "Wrong enemy art selected.");
                var frame = animation.Visual.sprite; animation.Advance(.12f);
                Require(animation.Visual.sprite != frame, "Walking clip is not advancing.");
                animation.SetMotion(Vector2.zero); frame = animation.Visual.sprite; animation.Advance(.18f);
                Require(animation.CurrentPose == OverrunSmallZombieAnimator.Pose.Idle && animation.Visual.sprite != frame, "Idle clip is not advancing.");
                Require(Mathf.Approximately(collider.radius, .36f) && enemy.transform.localScale == Vector3.one && animation.Visual.transform.localScale == Vector3.one * 2,
                    "Animation resized the physics collider.");
            }
            report.AppendLine("All four directions animate idle/walk; character scale is stable and the .36-radius collider stays unchanged.");

            for (int secondary = 0; secondary < 2; secondary++)
            {
                enemy.Init(definition, release);
                enemy.transform.position = player.transform.position + Vector3.right * .8f;
                Physics2D.SyncTransforms();
                float before = (float)healthField.GetValue(player);
                Require(animation.BeginAttack(player, secondary == 1), "Attack did not start.");
                enemy.SendMessage("FixedUpdate");
                Require(enemy.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero, "Zombie walks during its attack windup.");
                float strike = Brute ? (secondary == 1 ? 3f / 12 : 3f / 10) : Runner ? (secondary == 1 ? 4f / 16 : 3f / 14) : (secondary == 1 ? 3f / 16 : 2f / 8);
                animation.Advance(strike - .01f);
                Require((float)healthField.GetValue(player) == before, "Attack damage landed before its hit frame.");
                animation.Advance(.02f);
                Require((float)healthField.GetValue(player) == before - definition.contactDamage, "Attack did not land once on its hit frame.");
                animation.Advance(.1f);
                Require((float)healthField.GetValue(player) == before - definition.contactDamage, "Attack caused repeated damage.");
                animation.Advance(2); Require(!animation.IsAttacking, "Attack never returned to locomotion.");
            }
            enemy.Init(definition, release); enemy.transform.position = new Vector3(10000, 10000, 0); Physics2D.SyncTransforms();
            float untouched = (float)healthField.GetValue(player);
            animation.BeginAttack(player, false); animation.Advance(.5f);
            Require((float)healthField.GetValue(player) == untouched, "A dodged/out-of-range attack still damaged the player.");
            report.AppendLine("Both attacks wind up, hit once on their strike frame, return to locomotion, and miss when the player is out of reach.");

            enemy.Init(definition, release); enemy.transform.position = new Vector3(10000, 10000, 0);
            animation.SetMotion(Vector2.left);
            enemy.TakeDamage(1, Vector2.right);
            Require(animation.Visual.color.r > 1, "Tracer hit flash no longer works on the new art.");
            enemy.SendMessage("FixedUpdate"); Require(enemy.GetComponent<Rigidbody2D>().linearVelocity.x > 0, "Knockback disappeared.");
            int corpseCount = UnityEngine.Object.FindObjectsByType<OverrunZombieCorpse>().Length;
            enemy.TakeDamage(100); enemy.TakeDamage(100);
            Require(deaths == 1 && !enemy.gameObject.activeSelf, "Dead zombie did not release exactly once.");
            var corpses = UnityEngine.Object.FindObjectsByType<OverrunZombieCorpse>();
            Require(corpses.Length == corpseCount + 1, "Death animation was missing or duplicated.");
            var corpse = corpses.First(c => c.transform.position.x > 9990);
            Require(corpse.GetComponentsInChildren<Collider2D>().Length == 0 && corpse.GetComponent<SpriteRenderer>().sprite.name.Contains("Side-left"), "Corpse has collision or incorrect directional art.");
            Require(corpse.GetComponent<SpriteRenderer>().sprite.name.StartsWith(ArtPrefix, StringComparison.Ordinal), "Corpse used a different zombie's frames.");
            Sprite first = corpse.GetComponent<SpriteRenderer>().sprite;
            corpse.Advance(.21f); Require(corpse.GetComponent<SpriteRenderer>().sprite != first, "Death frames are not advancing.");
            var reused = pool.Get(); Require(reused == enemy, "Enemy pool did not reuse its object.");
            reused.Init(definition, release);
            Require(animation.CurrentPose == OverrunSmallZombieAnimator.Pose.Idle && animation.Direction == OverrunSmallZombieAnimator.Facing.Down &&
                animation.Visual.color == Color.white && reused.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero && reused.IsAlive,
                "Pooled zombie retained its attack/death/hit state.");
            corpse.Advance(2); Require(!corpse.gameObject.activeSelf && reused.gameObject.activeSelf, "Corpse cleanup affected the live pooled enemy.");
            report.AppendLine("Tracer flash/knockback, single death/drop path, independent noncolliding death animation, corpse cleanup and enemy pool reuse passed.");
        }
        finally
        {
            healthField.SetValue(player, health);
            if (enemy && enemy.gameObject.activeSelf) { enemy.gameObject.SetActive(false); UnityEngine.Object.Destroy(enemy.gameObject); }
            pool.Dispose(); UnityEngine.Object.Destroy(definition); Physics2D.SyncTransforms();
        }
    }

    static void VerifyWaveGate(WaveDirector director, StringBuilder report)
    {
        var modules = director.player.GetComponent<OverrunUpgradeSystem>();
        var randomState = UnityEngine.Random.state;
        // Fixed seed and strongly biased weights exercise the real spawn path,
        // without waiting for normal random selection during a live run.
        float regularWeight = director.enemies[0].weight, runnerWeight = director.enemies[1].weight;
        float bruteWeight = director.enemies[2].weight;
        var livingField = typeof(WaveDirector).GetField("live", BindingFlags.Instance | BindingFlags.NonPublic);
        try
        {
            UnityEngine.Random.InitState(42017);
            director.enemies[0].weight = .01f; director.enemies[1].weight = 10000;
            if (Brute) { director.enemies[1].weight = 1; director.enemies[2].weight = 10000; }
            director.Begin();
            var service = FlowFieldService.Instance; service.Field.Rebuild(service.Map.WorldToCell(director.player.position));
            int unlock = Brute ? 6 : 3;
            for (int wave = 1; wave <= unlock; wave++)
            {
                director.Advance(wave == 1 ? director.initialGrace + .1f : director.betweenWaves + .1f);
                bool sawTarget = false;
                for (int tick = 0; tick < 100 && director.State != WaveDirector.Phase.Reward; tick++)
                {
                    director.Advance(2);
                    var living = ((System.Collections.Generic.HashSet<Enemy>)livingField.GetValue(director)).ToArray();
                    foreach (var enemy in living)
                    {
                        bool runner = enemy.GetComponent<OverrunRunnerZombieAnimator>();
                        if (wave < 3) Require(!runner, "Runner appeared before wave three.");
                        bool brute = enemy.GetComponent<OverrunBruteZombieAnimator>();
                        if (wave < 6) Require(!brute, "Brute appeared before wave six.");
                        sawTarget |= Brute ? brute : runner;
                        enemy.TakeDamage(10000);
                    }
                }
                Require(director.State == WaveDirector.Phase.Reward && modules.Choosing, "Wave did not reach its reward.");
                if (wave == unlock) Require(sawTarget, "Target zombie never entered its unlock wave.");
                Require(modules.Choose(0), "Wave reward choice failed.");
            }
            report.AppendLine(Brute ? "Real wave spawning excludes brutes through wave five, introduces big zombies in wave six, and still offers a reward after every clear." :
                "Real wave spawning excludes runners in waves one/two, introduces axe runners in wave three, and still offers a reward after every clear.");
        }
        finally
        {
            director.enemies[0].weight = regularWeight; director.enemies[1].weight = runnerWeight;
            director.enemies[2].weight = bruteWeight;
            UnityEngine.Random.state = randomState;
        }
    }

    static void Capture(WaveDirector director, StringBuilder report)
    {
        var root = new GameObject("Zombie Preview Samples");
        var camera = Camera.main; var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>();
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        RenderTexture target = null; Texture2D capture = null;
        try
        {
            var offsets = new[] { new Vector2(-4, 2.5f), new Vector2(-4, -2), new Vector2(4, -2), new Vector2(4, 2.5f) };
            var motions = new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up };
            for (int i = 0; i < offsets.Length; i++)
            {
                int index = EnemyIndex;
                var zombie = UnityEngine.Object.Instantiate(director.enemies[index].prefab, root.transform);
                zombie.transform.position = player.transform.position + (Vector3)offsets[i]; zombie.gameObject.SetActive(true);
                zombie.Init(director.enemies[index], null); zombie.GetComponent<Rigidbody2D>().simulated = false;
                var animation = zombie.GetComponent<OverrunSmallZombieAnimator>(); animation.SetMotion(motions[i]); animation.Advance(.18f);
            }
            target = new RenderTexture(960, 540, 24); target.Create(); camera.targetTexture = target;
            camera.Render(); RenderTexture.active = target;
            capture = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            capture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); capture.Apply();
            Directory.CreateDirectory("Logs"); File.WriteAllBytes("Logs/" + Prefix + "Preview.png", capture.EncodeToPNG());
            report.AppendLine("Four directional " + (Brute ? "big zombies" : Runner ? "axe runners" : "regular zombies") + " rendered in the procedural building; other assignments preserved.");
        }
        finally
        {
            root.SetActive(false); UnityEngine.Object.Destroy(root);
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); }
            if (capture) UnityEngine.Object.Destroy(capture);
        }
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
