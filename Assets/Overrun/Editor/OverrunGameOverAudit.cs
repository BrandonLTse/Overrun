using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

[InitializeOnLoad]
public static class OverrunGameOverAudit
{
    const string Request = "Temp/OverrunGameOverAudit.request", Phase = "OverrunGameOverAudit.phase", SavedReport = "OverrunGameOverAudit.report";
    static double started, frozenAt;
    static int captureFrame, originalBest;
    static bool originalHadBest;
    static StringBuilder report;
    static float frozenTime, frozenCountdown;
    static Vector3 deathPosition, enemyPosition, shotPosition;
    static Enemy enemy;
    static OverrunBullet shot;
    static Mouse mouse, previousMouse;
    static OverrunGameOverAudit() { EditorApplication.update += Tick; }
    [MenuItem("Overrun/Validate Game Over and Best Round")]
    public static void RequestAudit() { Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify lethal damage, frozen game-over state, saved highest round and both exit buttons."); }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 9)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            report = new StringBuilder(SessionState.GetString(SavedReport, ""));
            if (OverrunRunRecord.BestRound == originalBest && PlayerPrefs.HasKey(OverrunRunRecord.BestRoundKey) == originalHadBest && Mathf.Approximately(Time.timeScale, 1) && !AudioListener.pause)
                report.AppendLine("Audit teardown restored the user's original best-round preference, clock and audio.\nPASS");
            else report.AppendLine("FAIL: Audit teardown changed the user's record, clock or audio.");
            Finish(); return;
        }
        if (phase == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            started = EditorApplication.timeSinceStartup; SessionState.SetInt(Phase, 1); EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - started > 120) { Fail(new TimeoutException("Game-over audit timed out.")); return; }
        var menus = OverrunMenus.Instance; var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>();
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>(); var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
        if (!EditorApplication.isPlaying || !menus || !player || !level || level.Map == null || !director || EditorApplication.timeSinceStartup - started < 3) return;
        try
        {
            if (phase == 1)
            {
                if (!OverrunMovementAudit.EnsurePlaying()) return;
                report = new StringBuilder(); director.enabled = false; OverrunMovementAudit.Verify(report);
                originalHadBest = PlayerPrefs.HasKey(OverrunRunRecord.BestRoundKey); originalBest = OverrunRunRecord.BestRound;
                PlayerPrefs.SetInt(OverrunRunRecord.BestRoundKey, 2); PlayerPrefs.Save(); ReachThirdRound(director, player);
                Require(OverrunRunRecord.BestRound == 3 && PlayerPrefs.GetInt(OverrunRunRecord.BestRoundKey) == 3, "Entering a new best round did not save it.");
                OverrunRunRecord.Record(1); Require(OverrunRunRecord.BestRound == 3, "A lower round replaced the record.");
                PrepareDeath(player, director); menus.EndRun(); player.Damage(1);
                Require(player.Health == 0 && menus.RoundReached == 3 && menus.BestRound == 3 && menus.State == OverrunMenus.ScreenState.GameOver && Time.timeScale == 0 && AudioListener.pause, "Death did not produce the frozen game-over screen and round statistics.");
                var audio = OverrunAudio.Ensure(); Require(audio.Accepted(OverrunSound.GameOver) == 1 && audio.LastSource(OverrunSound.GameOver).ignoreListenerPause, "Game-over sound duplicated or cannot play under pause.");
                VerifyBlockedInput(player, menus);
                frozenTime = Time.time; frozenCountdown = director.Countdown; frozenAt = EditorApplication.timeSinceStartup;
                enemyPosition = enemy.transform.position; shotPosition = shot.transform.position;
                Directory.CreateDirectory("Logs"); ScreenCapture.CaptureScreenshot("Logs/OverrunGameOver.png"); captureFrame = Time.frameCount;
                report.AppendLine("Real progression reaches round 3, saves a new record over round 2 and rejects lower results. Lethal damage leaves zero health at the death location, cancels reload and enters game over exactly once with a pause-safe sound.");
                SessionState.SetInt(Phase, 2); return;
            }
            if (phase == 2)
            {
                if (Time.frameCount <= captureFrame + 3 || EditorApplication.timeSinceStartup - frozenAt < .3) return;
                Require(Time.time == frozenTime && director.Countdown == frozenCountdown && enemy.transform.position == enemyPosition && shot.transform.position == shotPosition && player.transform.position == deathPosition, "Game-over frames advanced simulation or respawned the player.");
                Require(!player.TryHeal(15), "A bandage revived the dead player.");
                Press(Key.Enter); VerifyFreshRun(player, director, level); Require(menus.BestRound == 3, "New run cleared the record.");
                report.AppendLine("Across real game-over frames, player, enemy, projectile, clock and wave countdown remain frozen. Escape cannot resume and gameplay input/healing stay blocked. Enter on Start New Run restores a clean run while retaining the record.");
                SessionState.SetInt(Phase, 3); return;
            }
            if (phase == 3)
            {
                if (OverrunMenus.InputBlocked) return;
                SetMouse(player, true); player.SendMessage("Update"); Require(!UnityEngine.Object.FindObjectsByType<OverrunBullet>().Any(b => b.InFlight), "Held game-over click fired into the new run.");
                SetMouse(player, false); player.SendMessage("Update"); typeof(OverrunPlayer).GetField("fireTimer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 0f);
                SetMouse(player, true); player.SendMessage("Update"); Require(player.GetComponent<OverrunWeaponSystem>().PistolRounds == 11, "Firing did not return after releasing the held click.");
                player.Damage(float.MaxValue); Require(menus.RoundReached == 1 && menus.BestRound == 3 && menus.State == OverrunMenus.ScreenState.GameOver, "A shorter run changed the highest round.");
                Press(Key.S); Press(Key.Enter);
                Require(menus.State == OverrunMenus.ScreenState.MainMenu && Time.timeScale == 0 && AudioListener.pause && director.State == WaveDirector.Phase.Dormant && director.AliveCount == 0 && player.Health == player.maxHealth && OverrunBandagePickup.ActiveCount == 0 && !UnityEngine.Object.FindObjectsByType<OverrunBullet>().Any() && !UnityEngine.Object.FindObjectsByType<OverrunWeaponDeathVisual>().Any(), "Return to Main Menu did not clear the ended run.");
                Require(menus.BestRound == 3, "Returning to the main menu cleared the record.");
                ScreenCapture.CaptureScreenshot("Logs/OverrunCleanMainMenu.png"); captureFrame = Time.frameCount;
                report.AppendLine("Held fire is suppressed after starting over, then fresh clicks work. A shorter run keeps the saved best. Keyboard navigation to Return to Main Menu clears combat/run state and preserves the record; the clean title screen is captured.");
                SessionState.SetInt(Phase, 4); return;
            }
            if (phase == 4)
            {
                if (Time.frameCount <= captureFrame + 3) return;
                Press(Key.Enter); VerifyFreshRun(player, director, level);
                Require(menus.BestRound == 3 && UnityEngine.Object.FindObjectsByType<OverrunPlayer>().Length == 1 && UnityEngine.Object.FindObjectsByType<OverrunMenus>().Length == 1, "Starting from the title lost the record or duplicated systems.");
                CleanupInput(); SessionState.SetString(SavedReport, report.ToString()); SessionState.SetInt(Phase, 9); EditorApplication.isPlaying = false;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }
    static void ReachThirdRound(WaveDirector director, OverrunPlayer player)
    {
        director.Advance(director.initialGrace + .1f);
        while (director.WaveNumber < 3)
        {
            for (int i = 0; i < 150 && director.State != WaveDirector.Phase.Reward; i++)
            {
                director.Advance(2); foreach (var actor in director.GetComponentsInChildren<Enemy>().Where(e => e.IsAlive)) actor.TakeDamage(10000);
            }
            var modules = player.GetComponent<OverrunUpgradeSystem>(); Require(modules.Choosing && modules.Choose(0), "Could not clear and reward a real wave.");
            director.Advance(director.betweenWaves + .1f);
        }
        player.GetComponent<OverrunUpgradeSystem>().ResetRun();
    }
    static void PrepareDeath(OverrunPlayer player, WaveDirector director)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); Require(inventory.RifleUnlocked && inventory.TryEquip(2) && inventory.TryConsumeShot() && inventory.TryReload(), "Death fixture could not start an AR reload.");
        var ranks = (int[])typeof(OverrunUpgradeSystem).GetField("ranks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(player.GetComponent<OverrunUpgradeSystem>()); ranks[0] = 2;
        OverrunBandagePickup.Spawn(new Vector2(10000, 10000));
        director.Advance(.5f); enemy = director.GetComponentsInChildren<Enemy>().FirstOrDefault(e => e.IsAlive);
        Require(enemy, "The death fixture needs a real director-owned zombie.");
        shot = new GameObject("Game-over frozen shot").AddComponent<OverrunBullet>(); shot.transform.position = new Vector3(10000, 10010); shot.Init(2, Vector2.right);
        deathPosition = player.transform.position; player.Damage(float.MaxValue);
        Require(!inventory.IsReloading && inventory.RifleRounds == 29 && player.transform.position == deathPosition, "Lethal hit reloaded ammo or moved the player.");
    }
    static void VerifyBlockedInput(OverrunPlayer player, OverrunMenus menus)
    {
        previousMouse = Mouse.current; mouse = InputSystem.AddDevice<Mouse>("Game-over held click"); mouse.MakeCurrent(); SetMouse(player, true);
        var previousKeyboard = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>("Game-over blocked keys");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Digit1, Key.R)); InputSystem.Update(); player.SendMessage("Update"); player.SendMessage("FixedUpdate");
            Require(player.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero && player.GetComponent<OverrunWeaponSystem>().Equipped == OverrunWeapon.AssaultRifle && !player.GetComponent<OverrunWeaponSystem>().IsReloading, "Game-over input moved, switched or reloaded the player.");
        }
        finally { InputSystem.RemoveDevice(keyboard); if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent(); }
        menus.Resume(); menus.Pause(); Press(Key.Escape); Require(menus.State == OverrunMenus.ScreenState.GameOver, "Game over could be resumed or paused.");
    }
    static void VerifyFreshRun(OverrunPlayer player, WaveDirector director, LevelGenerator level)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); var modules = player.GetComponent<OverrunUpgradeSystem>();
        Require(OverrunMenus.Instance.State == OverrunMenus.ScreenState.Playing && Time.timeScale == 1 && !AudioListener.pause && player.Health == player.maxHealth && Vector2.Distance(player.transform.position, level.PlayerStart) < .01f && inventory.Equipped == OverrunWeapon.Pistol && !inventory.RifleUnlocked && inventory.PistolRounds == 12 && inventory.RifleRounds == 30 && !inventory.IsReloading && Enumerable.Range(0, 4).All(i => modules.Rank((OverrunUpgrade)i) == 0) && !modules.Choosing && director.State == WaveDirector.Phase.Breather && director.Countdown == director.initialGrace && director.WaveNumber == 0 && director.AliveCount == 0 && OverrunBandagePickup.ActiveCount == 0 && !UnityEngine.Object.FindObjectsByType<OverrunBullet>().Any(), "New-run state was not fully reset.");
        Require(!enemy || !enemy.IsAlive, "New run retained a living zombie from game over.");
    }
    static void SetMouse(OverrunPlayer player, bool down)
    {
        var point = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.right * 8);
        InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = down ? (ushort)1 : (ushort)0 }); InputSystem.Update();
    }
    static void Press(Key key)
    {
        var previous = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>("Game-over menu keys");
        try { keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); InputSystem.Update(); OverrunMenus.Instance.SendMessage("Update"); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); }
        finally { InputSystem.RemoveDevice(keyboard); if (previous != null && previous.added) previous.MakeCurrent(); }
    }
    static void CleanupInput() { if (mouse != null && mouse.added) InputSystem.RemoveDevice(mouse); mouse = null; if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent(); }
    static void Fail(Exception ex) { CleanupInput(); if (report == null) report = new StringBuilder(); report.AppendLine("FAIL: " + ex); Debug.LogException(ex); Finish(); EditorApplication.isPlaying = false; }
    static void Finish()
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunGameOverAudit.txt", report.ToString());
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); SessionState.EraseString(SavedReport); Debug.Log("[Overrun Game Over Audit] " + report);
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
