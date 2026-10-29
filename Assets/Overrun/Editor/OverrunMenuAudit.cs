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
public static class OverrunMenuAudit
{
    const string Request = "Temp/OverrunMenuAudit.request", Phase = "OverrunMenuAudit.phase", SavedReport = "OverrunMenuAudit.report";
    static double started, frozenAt;
    static int capturedFrame;
    static StringBuilder report;
    static OverrunBullet frozenShot;
    static Enemy frozenEnemy;
    static Vector3 shotPosition, enemyPosition;
    static Sprite enemySprite;
    static float gameTime, countdown;
    static Mouse heldMouse, previousMouse;
    static OverrunMenuAudit() { EditorApplication.update += Tick; }

    [MenuItem("Overrun/Validate Start and Pause Menus")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify start/pause menus, frozen gameplay, input isolation, reward resume and full run reset.");
    }

    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 9)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            report = new StringBuilder(SessionState.GetString(SavedReport, ""));
            if (Mathf.Approximately(Time.timeScale, 1) && !AudioListener.pause && !OverrunMenus.Instance)
                report.AppendLine("Leaving Play Mode restores the clock/audio and releases the menu singleton.\nPASS");
            else report.AppendLine("FAIL: Menu teardown left the clock, audio or singleton dirty.");
            Finish(); return;
        }
        if (phase == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            started = EditorApplication.timeSinceStartup; SessionState.SetInt(Phase, 1);
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - started > 100) { Fail(new TimeoutException("Menu audit initialization timed out.")); return; }
        var menus = OverrunMenus.Instance;
        var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>();
        var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>();
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (!EditorApplication.isPlaying || !menus || !player || !director || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3) return;
        try
        {
            if (phase == 1)
            {
                report = new StringBuilder();
                Require(menus.State == OverrunMenus.ScreenState.MainMenu && Time.timeScale == 0 && AudioListener.pause && director.State == WaveDirector.Phase.Dormant && director.AliveCount == 0,
                    $"Gameplay auto-started behind the title menu: menu={menus.State}, clock={Time.timeScale}, audioPaused={AudioListener.pause}, waveState={director.State}, alive={director.AliveCount}.");
                Press(Key.S); Press(Key.Enter); Require(menus.ShowingControls, "Keyboard How to Play navigation failed.");
                Press(Key.Escape); Require(!menus.ShowingControls && menus.State == OverrunMenus.ScreenState.MainMenu, "Escape did not return from controls.");
                report.AppendLine("Title menu holds gameplay, waves and audio; keyboard controls/help/back work before the run starts.");
                Directory.CreateDirectory("Logs"); ScreenCapture.CaptureScreenshot("Logs/OverrunStartMenu.png");
                capturedFrame = Time.frameCount; SessionState.SetInt(Phase, 2); return;
            }
            if (phase == 2)
            {
                if (Time.frameCount <= capturedFrame + 3) return;
                Press(Key.Enter);
                Require(menus.State == OverrunMenus.ScreenState.Playing && Time.timeScale == 1 && !AudioListener.pause && director.WaveNumber == 0 && director.Countdown == director.initialGrace, "Start did not begin a fresh run with its opening grace.");
                director.enabled = false; SessionState.SetInt(Phase, 20); return;
            }
            if (phase == 20)
            {
                if (OverrunMenus.InputBlocked) return;
                OverrunMovementAudit.Verify(report);
                ScreenCapture.CaptureScreenshot("Logs/OverrunGameplayHud.png"); capturedFrame = Time.frameCount;
                SessionState.SetInt(Phase, 3); return;
            }
            if (phase == 3)
            {
                if (OverrunMenus.InputBlocked || Time.frameCount <= capturedFrame + 3) return;
                frozenShot = new GameObject("Pause audit shot").AddComponent<OverrunBullet>();
                frozenShot.transform.position = new Vector3(10000, 10000); frozenShot.Init(2, Vector2.right);
                frozenEnemy = UnityEngine.Object.Instantiate(director.enemies[0].prefab);
                frozenEnemy.transform.position = new Vector3(10010, 10000); frozenEnemy.gameObject.SetActive(true); frozenEnemy.Init(director.enemies[0], null);
                frozenEnemy.GetComponent<OverrunSmallZombieAnimator>().SetMotion(Vector2.right);
                shotPosition = frozenShot.transform.position; enemyPosition = frozenEnemy.transform.position;
                enemySprite = frozenEnemy.GetComponent<OverrunSmallZombieAnimator>().Visual.sprite;
                director.enabled = true;
                Press(Key.Escape);
                Require(menus.State == OverrunMenus.ScreenState.Paused && Time.timeScale == 0 && AudioListener.pause, "Escape did not freeze gameplay/audio.");
                // Baseline at the actual pause boundary, after input delivery.
                countdown = director.Countdown;
                VerifyBlockedInput(player);
                gameTime = Time.time; frozenAt = EditorApplication.timeSinceStartup;
                ScreenCapture.CaptureScreenshot("Logs/OverrunPauseMenu.png"); capturedFrame = Time.frameCount;
                SessionState.SetInt(Phase, 4); return;
            }
            if (phase == 4)
            {
                if (Time.frameCount <= capturedFrame + 3 || EditorApplication.timeSinceStartup - frozenAt < .3) return;
                Require(Time.time == gameTime && director.Countdown == countdown && frozenShot.transform.position == shotPosition && frozenEnemy.transform.position == enemyPosition &&
                    frozenEnemy.GetComponent<OverrunSmallZombieAnimator>().Visual.sprite == enemySprite,
                    $"Simulation advanced while paused: clock {gameTime}->{Time.time}, countdown {countdown}->{director.Countdown}, shot {shotPosition}->{frozenShot.transform.position}, enemy {enemyPosition}->{frozenEnemy.transform.position}, frame {enemySprite.name}->{frozenEnemy.GetComponent<OverrunSmallZombieAnimator>().Visual.sprite.name}.");
                report.AppendLine("Across real paused frames, the game clock, wave countdown, enemy position/animation and projectile position stay frozen; gameplay input/damage are blocked.");
                previousMouse = Mouse.current; heldMouse = InputSystem.AddDevice<Mouse>("Held menu click mouse"); heldMouse.MakeCurrent();
                Vector2 cursor = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.right * 8);
                InputSystem.QueueStateEvent(heldMouse, new MouseState { position = cursor, buttons = 1 }); InputSystem.Update();
                Press(Key.Escape); Require(menus.State == OverrunMenus.ScreenState.Playing && Time.timeScale == 1 && !AudioListener.pause, "Escape did not resume gameplay/audio.");
                director.enabled = false; SessionState.SetInt(Phase, 5); return;
            }
            if (phase == 5)
            {
                if (OverrunMenus.InputBlocked) return;
                frozenEnemy.gameObject.SetActive(false); UnityEngine.Object.Destroy(frozenEnemy.gameObject);
                OverrunBullet.ClearActive();
                VerifyFireRelease(player);
                VerifyRewardPause(menus, director, player);
                menus.Pause(); ScreenCapture.CaptureScreenshot("Logs/OverrunPauseUpgrades.png"); capturedFrame = Time.frameCount;
                SessionState.SetInt(Phase, 50); return;
            }
            if (phase == 50)
            {
                if (Time.frameCount <= capturedFrame + 3) return;
                menus.Resume();
                VerifyRestart(menus, director, player, level);
                SessionState.SetInt(Phase, 6); return;
            }
            if (phase == 6)
            {
                if (OverrunMenus.InputBlocked) return;
                VerifyReturn(menus, director, player);
                report.AppendLine("Gameplay HUD, title, empty pause panel and pause panel after a real upgrade choice captured from the Game view.");
                SessionState.SetString(SavedReport, report.ToString()); SessionState.SetInt(Phase, 9);
                EditorApplication.isPlaying = false;
            }
        }
        catch (Exception ex) { Fail(ex); }
    }

    static void Press(Key key)
    {
        var previous = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>("Menu audit keyboard");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); InputSystem.Update();
            OverrunMenus.Instance.SendMessage("Update");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        }
        finally { InputSystem.RemoveDevice(keyboard); if (previous != null && previous.added) previous.MakeCurrent(); }
    }

    static void VerifyBlockedInput(OverrunPlayer player)
    {
        var previousKeyboard = Keyboard.current; var previousMouse = Mouse.current;
        var keyboard = InputSystem.AddDevice<Keyboard>("Paused gameplay keyboard"); var mouse = InputSystem.AddDevice<Mouse>("Paused gameplay mouse");
        float savedHealth = player.Health;
        typeof(OverrunPlayer).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 75f);
        float health = player.Health;
        Quaternion rotation = player.GetComponentInChildren<OverrunWeaponFeedback>().transform.rotation;
        int shots = UnityEngine.Object.FindObjectsByType<OverrunBullet>().Length;
        try
        {
            keyboard.MakeCurrent(); mouse.MakeCurrent();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.Digit1));
            InputSystem.QueueStateEvent(mouse, new MouseState { buttons = 1, position = new Vector2(850, 400) }); InputSystem.Update();
            player.SendMessage("Update"); player.SendMessage("FixedUpdate"); player.Damage(10);
            Require(!player.TryHeal(3) && player.GetComponent<Rigidbody2D>().linearVelocity == Vector2.zero && player.Health == health &&
                player.GetComponentInChildren<OverrunWeaponFeedback>().transform.rotation == rotation && UnityEngine.Object.FindObjectsByType<OverrunBullet>().Length == shots, "Paused input moved, aimed, fired, healed or damaged the player.");
        }
        finally
        {
            typeof(OverrunPlayer).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, savedHealth);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.Update();
            InputSystem.RemoveDevice(keyboard); InputSystem.RemoveDevice(mouse);
            if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent(); if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        }
    }

    static void VerifyFireRelease(OverrunPlayer player)
    {
        var mouse = heldMouse;
        try
        {
            mouse.MakeCurrent();
            typeof(OverrunPlayer).GetField("fireTimer", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 0f);
            Vector2 cursor = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.right * 8);
            int before = UnityEngine.Object.FindObjectsByType<OverrunBullet>().Length;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor, buttons = 1 }); InputSystem.Update(); player.SendMessage("Update");
            Require(UnityEngine.Object.FindObjectsByType<OverrunBullet>().Length == before, "A held menu click fired on resume.");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor }); InputSystem.Update(); player.SendMessage("Update");
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor, buttons = 1 }); InputSystem.Update(); player.SendMessage("Update");
            Require(UnityEngine.Object.FindObjectsByType<OverrunBullet>().Length > before, "Firing did not return after releasing the menu click.");
            report.AppendLine("Resume suppresses held mouse fire until release, then firing works again.");
        }
        finally
        {
            InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.Update(); InputSystem.RemoveDevice(mouse);
            heldMouse = null;
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent();
        }
    }

    static void VerifyRewardPause(OverrunMenus menus, WaveDirector director, OverrunPlayer player)
    {
        var modules = player.GetComponent<OverrunUpgradeSystem>(); director.Begin();
        director.Advance(director.initialGrace + .1f);
        for (int i = 0; i < 100 && director.State != WaveDirector.Phase.Reward; i++)
        {
            director.Advance(2);
            foreach (var enemy in director.GetComponentsInChildren<Enemy>().Where(e => e.IsAlive)) enemy.TakeDamage(10000);
        }
        Require(modules.Choosing && modules.Choices.Count == 3, "Could not reach a real post-wave reward.");
        var offers = modules.Choices.ToArray(); menus.Pause();
        Require(!modules.Choose(0) && !player.ChooseUpgrade(1), "Pause allowed an upgrade to be spent behind the menu.");
        menus.Resume();
        Require(modules.Choosing && modules.Choices.Select(c => c.Title).SequenceEqual(offers.Select(c => c.Title)) && director.State == WaveDirector.Phase.Reward, "Pause changed/lost the pending reward.");
        Require(modules.Choose(0) && !modules.Choose(0), "Resumed reward did not allow exactly one choice.");
        report.AppendLine("Pause on a cleared-wave draft blocks selection, preserves the same three cards and resumes with exactly one valid reward.");
    }

    static void VerifyRestart(OverrunMenus menus, WaveDirector director, OverrunPlayer player, LevelGenerator level)
    {
        var map = level.Map; director.Advance(director.betweenWaves + .1f); director.Advance(2);
        Require(director.AliveCount > 0, "Restart fixture did not include a live wave enemy.");
        OverrunBandagePickup.Spawn(new Vector2(10000, 10000));
        var enemy = director.GetComponentsInChildren<Enemy>().First(e => e.IsAlive); enemy.GetComponent<OverrunSmallZombieAnimator>().SpawnDeath();
        OverrunUpgradeFx.Ring(player.transform.position, 1);
        OverrunBullet.Fire(new Vector2(10000, 10010), new Vector2(10000, 10010), 2, Vector2.right, null);
        var weapons = player.GetComponent<OverrunWeaponSystem>(); weapons.OnWaveStarted(3); weapons.TryEquip(2);
        typeof(OverrunPlayer).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(player, 75f);
        menus.Pause(); menus.RequestAction(OverrunMenus.PendingAction.Restart); menus.CancelPanel();
        Require(player.Health == 75 && weapons.RifleUnlocked && weapons.Equipped == OverrunWeapon.AssaultRifle && director.AliveCount > 0 && menus.State == OverrunMenus.ScreenState.Paused, "Cancel restart discarded progress.");
        menus.RequestAction(OverrunMenus.PendingAction.Restart); menus.ConfirmAction();
        var modules = player.GetComponent<OverrunUpgradeSystem>();
        Require(menus.State == OverrunMenus.ScreenState.Playing && Time.timeScale == 1 && director.State == WaveDirector.Phase.Breather && director.WaveNumber == 0 && director.Countdown == director.initialGrace && director.AliveCount == 0 &&
            player.Health == player.maxHealth && !weapons.RifleUnlocked && weapons.Equipped == OverrunWeapon.Pistol && !modules.Choosing && Enumerable.Range(0, 4).All(i => modules.Rank((OverrunUpgrade)i) == 0), "Restart retained wave/player/weapon/module progress.");
        Require(OverrunBandagePickup.ActiveCount == 0 && UnityEngine.Object.FindObjectsByType<OverrunZombieCorpse>().Length == 0 && UnityEngine.Object.FindObjectsByType<OverrunBullet>().Length == 0 &&
            UnityEngine.Object.FindObjectsByType<OverrunUpgradeFx>().Length == 0, "Restart retained live transient objects.");
        Require(level.Map == map && Vector2.Distance(player.transform.position, level.PlayerStart) < .01f, "Restart duplicated/reloaded the map or used an unsafe spawn.");
        report.AppendLine("Restart confirmation/cancel preserves progress until confirmed; confirm resets health, bandages, weapon unlocks, modules, enemies, shots, corpses, effects and wave grace on the existing safe map.");
    }

    static void VerifyReturn(OverrunMenus menus, WaveDirector director, OverrunPlayer player)
    {
        menus.Pause(); menus.RequestAction(OverrunMenus.PendingAction.MainMenu); menus.CancelPanel();
        Require(menus.State == OverrunMenus.ScreenState.Paused && menus.Confirmation == OverrunMenus.PendingAction.None, "Cancel return left the pause screen.");
        menus.RequestAction(OverrunMenus.PendingAction.MainMenu); menus.ConfirmAction();
        Require(menus.State == OverrunMenus.ScreenState.MainMenu && Time.timeScale == 0 && director.State == WaveDirector.Phase.Dormant && director.AliveCount == 0 && !player.ChoosingUpgrade, "Main menu retained a live run.");
        menus.StartRun(); Require(menus.State == OverrunMenus.ScreenState.Playing && Time.timeScale == 1 && director.WaveNumber == 0 && director.Countdown == director.initialGrace, "Second start did not start a fresh run.");
        Require(UnityEngine.Object.FindObjectsByType<OverrunPlayer>().Length == 1 && UnityEngine.Object.FindObjectsByType<OverrunMenus>().Length == 1 && UnityEngine.Object.FindObjectsByType<WaveDirector>().Length == 1, "Menu cycling duplicated gameplay systems.");
        report.AppendLine("Return-to-menu confirmation/cancel and starting another run work without duplicate gameplay systems or a frozen clock.");
    }

    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    static void Fail(Exception ex)
    {
        if (heldMouse != null && heldMouse.added) { InputSystem.RemoveDevice(heldMouse); heldMouse = null; if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent(); }
        if (report == null) report = new StringBuilder(); report.AppendLine("FAIL: " + ex); Debug.LogException(ex);
        Finish(); EditorApplication.isPlaying = false;
    }
    static void Finish()
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunMenuAudit.txt", report.ToString());
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); SessionState.EraseString(SavedReport); Debug.Log("[Overrun Menu Audit] " + report);
    }
}
