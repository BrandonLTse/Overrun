using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[InitializeOnLoad]
public static class OverrunWaveAudit
{
    const string Request = "Temp/OverrunWaveAudit.request", Phase = "OverrunWaveAudit.phase";
    static double started;
    static int capturedFrame;
    static StringBuilder report;
    static OverrunWaveAudit() { EditorApplication.update += Tick; }
    [MenuItem("Overrun/Validate Waves and Upgrade Modules")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify wave caps, safe entries, one reward per clear and all four upgrade mechanics.");
    }

    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            started = EditorApplication.timeSinceStartup; SessionState.SetInt(Phase, 1);
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - started > 90) { Finish("FAIL: Wave audit timed out."); return; }
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3) return;
        if (!OverrunMovementAudit.EnsurePlaying()) return;
        try
        {
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
            var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>();
            var modules = player.GetComponent<OverrunUpgradeSystem>();
            if (phase == 1)
            {
                director.enabled = false; report = new StringBuilder();
                OverrunMovementAudit.Verify(report);
                VerifyFirstWave(director, modules, report);
                Directory.CreateDirectory("Logs"); ScreenCapture.CaptureScreenshot("Logs/OverrunWaveChoices.png");
                capturedFrame = Time.frameCount; SessionState.SetInt(Phase, 2); return;
            }
            if (Time.frameCount <= capturedFrame + 3) return; // Allow the Game view to render OnGUI and save the capture.
            VerifyDraftAndProgression(director, player, modules, report);
            VerifyModules(player, modules, report);
            report.AppendLine("PASS"); Finish(report.ToString());
        }
        catch (Exception ex)
        {
            if (report == null) report = new StringBuilder();
            report.AppendLine("FAIL: " + ex); Debug.LogException(ex); Finish(report.ToString());
        }
    }

    static void Finish(string text)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunWaveAudit.txt", text);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); Debug.Log("[Overrun Wave Audit] " + text); EditorApplication.isPlaying = false;
    }

    static void VerifyFirstWave(WaveDirector director, OverrunUpgradeSystem modules, StringBuilder report)
    {
        director.Begin();
        var service = FlowFieldService.Instance;
        service.Field.Rebuild(service.Map.WorldToCell(director.player.position));
        director.Advance(director.initialGrace - .1f);
        Require(director.WaveNumber == 0 && director.AliveCount == 0, "Opening grace spawned enemies too soon.");
        director.Advance(.2f);
        Require(director.WaveNumber == 1 && director.WaveBudget == 8 && director.AliveCap == 4, "Opening wave tuning is incorrect.");
        director.Advance(20);
        Require(director.AliveCount == 1, "A long frame dumped a catch-up swarm.");
        foreach (var enemy in Living(director)) VerifySafeSpawn(enemy, director);
        for (int i = 0; i < 16; i++) director.Advance(2);
        Require(director.AliveCount == 4 && director.RemainingBudget == 4, "Early alive cap or finite budget failed.");
        Require(Living(director).All(e => e.GetComponent<OverrunSmallZombieAnimator>() && !e.GetComponent<OverrunRunnerZombieAnimator>() && !e.GetComponent<OverrunBruteZombieAnimator>()), "Runner or brute appeared in wave one.");
        var first = Living(director)[0]; first.TakeDamage(10000);
        Require(!modules.Choosing, "Wave cleared before its entry budget was exhausted.");
        director.Advance(2); Require(director.AliveCount == 4 && director.RemainingBudget == 3, "A released enemy slot did not refill within budget.");
        ClearWave(director);
        Require(director.State == WaveDirector.Phase.Reward && modules.Choosing && director.AliveCount == 0 && director.RemainingBudget == 0,
            "Final kill did not open exactly one reward.");
        director.Advance(100);
        Require(director.State == WaveDirector.Phase.Reward && director.WaveNumber == 1 && director.AliveCount == 0, "Wave advanced while waiting for a choice.");
        report.AppendLine("Opening grace, eight finite shamblers, four-alive cap, no catch-up burst, fair spawn rules and a reward only after the final kill passed.");
    }

    static Enemy[] Living(WaveDirector director) { return director.GetComponentsInChildren<Enemy>().Where(e => e.IsAlive).ToArray(); }
    static void ClearWave(WaveDirector director)
    {
        for (int i = 0; i < 160 && director.State != WaveDirector.Phase.Reward; i++)
        {
            director.Advance(2);
            foreach (var enemy in Living(director)) { VerifySafeSpawn(enemy, director); enemy.TakeDamage(10000); }
        }
        Require(director.State == WaveDirector.Phase.Reward, "Finite wave could not finish.");
    }

    static void VerifySafeSpawn(Enemy enemy, WaveDirector director)
    {
        Vector2 position = enemy.transform.position, center = director.cam.transform.position;
        float height = director.cam.orthographicSize + director.viewMargin, width = height * director.cam.aspect;
        Require(Mathf.Abs(position.x - center.x) >= width || Mathf.Abs(position.y - center.y) >= height, "Enemy appeared on screen.");
        Require(Physics2D.Linecast(director.player.position, position, director.wallMask), "Enemy appeared in direct line of sight.");
        var service = FlowFieldService.Instance;
        Require(service.Field.DistanceAt(service.Map.WorldToCell(position)) >= director.minPathDist && !Physics2D.OverlapCircle(position, .36f, director.wallMask), "Unsafe/unreachable spawn.");
    }

    static void VerifyDraftAndProgression(WaveDirector director, OverrunPlayer player, OverrunUpgradeSystem modules, StringBuilder report)
    {
        Require(modules.Choices.Count == 3 && modules.Choices.Select(c => c.kind).Distinct().Count() == 3, "Draft contains duplicate or missing cards.");
        Require(!modules.Choose(-1) && !modules.Choose(3), "Invalid choice was accepted.");
        // WASD must continue to work while the overlay is open.
        var previous = Keyboard.current;
        var keyboard = InputSystem.AddDevice<Keyboard>("Wave reward movement audit");
        var body = player.GetComponent<Rigidbody2D>();
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W)); InputSystem.Update();
            player.SendMessage("Update"); player.SendMessage("FixedUpdate");
            Require(body.linearVelocity.y > 0 && modules.Choosing, "Upgrade screen disabled WASD.");
        }
        finally
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); player.SendMessage("Update"); body.linearVelocity = Vector2.zero;
            InputSystem.RemoveDevice(keyboard); if (previous != null && previous.added) previous.MakeCurrent();
        }
        var selected = modules.Choices[0];
        float damage = player.damage, speed = player.speed, fireRate = player.fireRate;
        Require(player.ChooseUpgrade(1) && modules.Rank(selected.kind) == selected.rank, "Choice did not apply its module.");
        Require(!player.ChooseUpgrade(1) && director.State == WaveDirector.Phase.Breather, "A clear granted multiple upgrades.");
        Require(player.damage == damage && player.speed == speed && player.fireRate == fireRate, "Module selection modified raw base stats.");
        director.Advance(director.betweenWaves - .1f); Require(director.WaveNumber == 1 && director.AliveCount == 0, "No preparation time after choosing.");
        director.Advance(.2f); Require(director.WaveNumber == 2 && director.WaveBudget == 12 && director.AliveCap == 5, "Wave two did not escalate gradually.");
        SetRanks(modules, 3, 3, 3, 3); ClearWave(director);
        Require(modules.Choices.Count == 3 && modules.Choices.All(c => c.rank == 0), "Maxed modules prevented a late-run choice.");
        int fortify = modules.Choices.ToList().FindIndex(c => c.kind == OverrunUpgrade.Fortify);
        Require(modules.Choose(fortify), "Tactical supply selection failed.");
        director.Advance(director.betweenWaves + .1f);
        Require(director.WaveNumber == 3 && modules.GuardCharges == 4, "Next-wave tactical supply did not activate.");
        modules.OnWaveStarted(); Require(modules.GuardCharges == 2, "One-wave supply did not expire.");
        report.AppendLine("Three unique cards, keyboard choice, live WASD, one reward per clear, no raw-stat choice, five-second breather, gradual escalation and maxed-build supplies passed.");
    }

    static void SetRanks(OverrunUpgradeSystem modules, params int[] values)
    {
        var ranks = (int[])typeof(OverrunUpgradeSystem).GetField("ranks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(modules);
        Array.Copy(values, ranks, 4);
    }
    static float Hp(Enemy enemy) { return (float)typeof(Enemy).GetField("hp", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemy); }
    static Enemy Dummy(Transform parent, Vector2 position)
    {
        var go = new GameObject("Upgrade target"); go.transform.SetParent(parent, false); go.transform.position = position;
        var body = go.AddComponent<Rigidbody2D>(); body.gravityScale = 0; body.freezeRotation = true;
        go.AddComponent<CircleCollider2D>().radius = .3f;
        var enemy = go.AddComponent<Enemy>();
        var definition = ScriptableObject.CreateInstance<EnemyDefinition>(); definition.maxHealth = 10; definition.speed = 0;
        enemy.Init(definition, () => go.SetActive(false));
        return enemy;
    }
    static OverrunBullet Shot(Vector2 from, Vector2 direction, OverrunUpgradeSystem modules, int pierces = 0, float range = 24, bool procs = true)
    {
        var go = new GameObject("Module audit tracer"); go.transform.position = from;
        var shot = go.AddComponent<OverrunBullet>(); shot.Init(2, direction, null, modules, pierces, range, procs); return shot;
    }

    static void VerifyModules(OverrunPlayer player, OverrunUpgradeSystem modules, StringBuilder report)
    {
        var root = new GameObject("Upgrade audit fixtures");
        var healthField = typeof(OverrunPlayer).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic);
        float health = player.Health;
        Vector2 origin = new Vector2(10000, 10000);
        try
        {
            modules.ResetRun(); SetRanks(modules, 1, 0, 0, 0);
            var wall = new GameObject("Ricochet wall"); wall.transform.SetParent(root.transform);
            wall.layer = LayerMask.NameToLayer("Walls"); wall.transform.position = origin + Vector2.right * 2;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(.1f, 4); Physics2D.SyncTransforms();
            var shot = Shot(origin, Vector2.right, modules); shot.Simulate(.08f);
            Require(shot.InFlight && shot.transform.position.x < origin.x, "Bank Shot did not reflect off the wall.");
            shot.Simulate(1); shot.Simulate(.1f);
            SetRanks(modules, 3, 0, 0, 0);
            var seeker = Dummy(root.transform, origin + new Vector2(0, 2)); Physics2D.SyncTransforms();
            shot = Shot(origin, Vector2.right, modules); shot.Simulate(.15f);
            Require(Hp(seeker) < 10 && !shot.InFlight, "Tier-three ricochet did not steer toward a visible target.");
            seeker.gameObject.SetActive(false); wall.SetActive(false); OverrunBullet.ClearActive();
            report.AppendLine("Bank Shot physically reflects, and its third tier steers a reflected shot into a visible enemy.");

            SetRanks(modules, 0, 1, 0, 0); origin += Vector2.up * 20;
            Enemy first = Dummy(root.transform, origin), second = Dummy(root.transform, origin + Vector2.right * 2), blocked = Dummy(root.transform, origin + Vector2.up * 2);
            var cover = new GameObject("Arc cover"); cover.transform.SetParent(root.transform); cover.transform.position = origin + Vector2.up;
            cover.layer = LayerMask.NameToLayer("Walls"); cover.AddComponent<BoxCollider2D>().size = new Vector2(6, .2f); Physics2D.SyncTransforms();
            shot = Shot(origin + Vector2.left * 3, Vector2.right, modules); shot.Simulate(.1f);
            Require(Mathf.Approximately(Hp(first), 8) && Mathf.Abs(Hp(second) - 8.8f) < .01f && Hp(blocked) == 10, "Arc Rounds damaged incorrectly or chained through cover.");
            first.gameObject.SetActive(false); second.gameObject.SetActive(false); blocked.gameObject.SetActive(false); cover.SetActive(false); OverrunBullet.ClearActive();
            report.AppendLine("Arc Rounds jumps to a nearby zombie, respects cover and does not recursively trigger itself.");

            modules.ResetRun(); SetRanks(modules, 0, 0, 1, 0); origin += Vector2.up * 20;
            for (int i = 1; i <= 4; i++)
            {
                OverrunBullet.ClearActive(); modules.FireWeapon(origin, origin + Vector2.right, 2, Vector2.right, null);
                int count = UnityEngine.Object.FindObjectsByType<OverrunBullet>().Count(b => b.InFlight);
                Require(count == (i == 4 ? 3 : 1), "Breach Shot burst cadence is incorrect.");
            }
            OverrunBullet.ClearActive(); SetRanks(modules, 0, 3, 3, 0); origin += Vector2.up * 20;
            first = Dummy(root.transform, origin); second = Dummy(root.transform, origin + Vector2.right * 2); var unchained = Dummy(root.transform, origin + Vector2.up * 2);
            Physics2D.SyncTransforms(); shot = Shot(origin + Vector2.left * 2, Vector2.right, modules, 1, 8, false); shot.Simulate(.1f);
            Require(Hp(first) == 8 && Hp(second) == 8 && Hp(unchained) == 10 && !shot.InFlight, "Piercing shrapnel damaged incorrectly or spawned recursive lightning.");
            first.gameObject.SetActive(false); second.gameObject.SetActive(false); unchained.gameObject.SetActive(false); OverrunBullet.ClearActive();
            report.AppendLine("Breach Shot bursts on every fourth trigger; tier-three shrapnel pierces one zombie and cannot recursively proc lightning.");

            modules.ResetRun(); SetRanks(modules, 0, 0, 0, 1); modules.OnWaveStarted();
            var close = Dummy(root.transform, (Vector2)player.transform.position + Vector2.right * 2); Physics2D.SyncTransforms();
            player.Damage(8); Require(player.Health == health && modules.GuardCharges == 0 && Hp(close) == 7, "Shock Guard failed to absorb/pulse.");
            player.Damage(8); Require(player.Health == health - 8, "Shock Guard absorbed hits without a charge.");
            SetRanks(modules, 0, 0, 0, 3); modules.OnWaveStarted();
            player.Damage(8);
            Require(modules.GuardCharges == 1 && (float)typeof(Enemy).GetField("stunTime", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(close) > 0,
                "Tier-three guard did not stagger its targets.");
            modules.OnWaveStarted(); Require(modules.GuardCharges == 2, "Guard did not refresh per wave.");
            report.AppendLine("Shock Guard blocks only charged hits, damages/pushes nearby enemies, refreshes each wave and gains stun at tier three.");
        }
        finally
        {
            healthField.SetValue(player, health); OverrunBullet.ClearActive();
            root.SetActive(false); UnityEngine.Object.Destroy(root); modules.ResetRun(); Physics2D.SyncTransforms();
        }
    }

    static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
