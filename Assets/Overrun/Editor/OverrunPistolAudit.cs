using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class OverrunPistolAudit
{
    const string Request = "Temp/OverrunPistolAudit.request", Phase = "OverrunPistolAudit.phase";
    static double started;
    static StringBuilder report;
    static readonly FieldInfo Cooldown = typeof(OverrunPlayer).GetField("fireTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    static OverrunPistolAudit() { EditorApplication.update += Tick; }
    [MenuItem("Overrun/Validate Animated Pistol and Reload")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify pistol sheet anchors, body synchronization, magazine/reload input, silent movement and game-over visuals.");
    }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            started = EditorApplication.timeSinceStartup; SessionState.SetInt(Phase, 1); EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - started > 100) { Finish("FAIL: Pistol audit timed out."); return; }
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3 || !OverrunMovementAudit.EnsurePlaying()) return;
        try
        {
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>(); director.enabled = false;
            var player = director.player.GetComponent<OverrunPlayer>();
            if (phase == 1)
            {
                report = new StringBuilder(); OverrunMovementAudit.Verify(report); VerifyImports(); VerifyDirections(player); VerifySilentMovement(player, director);
                VerifyMagazineAndBeginReload(player); SessionState.SetInt(Phase, 2); return;
            }
            VerifyReloadCompletionAndDeath(player, level); Capture(player);
            report.AppendLine("PASS"); Finish(report.ToString());
        }
        catch (Exception ex)
        {
            if (report == null) report = new StringBuilder(); report.AppendLine("FAIL: " + ex); Debug.LogException(ex); Finish(report.ToString());
        }
    }
    static void Finish(string result)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunPistolAudit.txt", result);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); Debug.Log("[Overrun Pistol Audit] " + result); EditorApplication.isPlaying = false;
    }
    static void VerifyImports()
    {
        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { OverrunPistolSpriteImport.Folder.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        Require(paths.Length == 14, "Not all fourteen pistol sheets were imported."); int total = 0;
        foreach (var path in paths)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            int count = int.Parse(Regex.Match(Path.GetFileNameWithoutExtension(path), @"Sheet(\d+)$").Groups[1].Value);
            Require(texture.width == width && texture.height == height && sprites.Length == count && importer.filterMode == FilterMode.Point && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.npotScale == TextureImporterNPOTScale.None, "Sheet was resized, blurred or mis-sliced: " + path);
            for (int i = 0; i < count; i++) Require(sprites[i].pixelsPerUnit == 16 && sprites[i].rect == new Rect(i * (width / count), 0, width / count, height) && sprites[i].name.EndsWith("_" + i.ToString("D2"), StringComparison.Ordinal), "Pistol frame was cropped or reordered.");
            total += count;
        }
        Require(total == 92, "Expected 92 pistol frames.");
        foreach (OverrunPistolFacing facing in Enum.GetValues(typeof(OverrunPistolFacing)))
        foreach (OverrunPistolPose pose in Enum.GetValues(typeof(OverrunPistolPose)))
        {
            var sprites = OverrunPistolClips.Frames(facing, pose);
            foreach (var sprite in sprites)
            {
                var actualFacing = pose == OverrunPistolPose.Death && facing != OverrunPistolFacing.Left ? OverrunPistolFacing.Right : facing;
                Require(Vector2.Distance(sprite.pivot, OverrunPistolClips.Pivot(actualFacing, pose, sprite.rect.width, sprite.rect.height)) < .001f, "Wrong wrist anchor: " + sprite.name);
            }
        }
        report.AppendLine("All 14 original-size sheets / 92 full-cell sprites use point/no-mips/clamp, 16 PPU, ordered frames and pose-specific wrist anchors.");
    }
    static void VerifyDirections(OverrunPlayer player)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); inventory.ResetRun();
        var feedback = player.GetComponentInChildren<OverrunWeaponFeedback>(); var animation = feedback.PistolAnimation; var body = player.GetComponent<OverrunCharacterAnimator>();
        var directions = new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up };
        for (int i = 0; i < 4; i++)
        {
            body.SetMotion(directions[i], directions[i]); feedback.SetAim(directions[i]); animation.Advance(0);
            Require((int)animation.Facing == i && animation.Visual.sprite == OverrunPistolClips.Frames((OverrunPistolFacing)i, OverrunPistolPose.IdleRun)[body.FrameIndex], "Incorrect idle/run direction.");
            typeof(OverrunCharacterAnimator).GetField("frame", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(body, 3); body.SendMessage("ApplyFrame"); animation.Advance(0);
            Require(animation.Visual.sprite.name.EndsWith("_03", StringComparison.Ordinal), "Hands did not synchronize with body run frame.");
            Vector3 attachment = animation.Visual.transform.localPosition;
            animation.Shoot(); animation.Advance(.08f); Require(animation.Pose == OverrunPistolPose.Shoot && animation.Visual.sprite.name.Contains("shoot") && animation.Visual.sprite.name.EndsWith("_01", StringComparison.Ordinal), "Firing frames did not advance.");
            animation.Advance(.2f); Require(animation.Pose == OverrunPistolPose.IdleRun, "Firing animation never returned to locomotion.");
            animation.BeginReload(); animation.Advance(.5f);
            Require(animation.Pose == OverrunPistolPose.Reload && animation.Visual.sprite.name.Contains("Reload") && animation.Visual.transform.localPosition == attachment, "Reload moved the hands' attachment or chose the wrong clip.");
            Require(animation.Visual.transform.localRotation == Quaternion.identity && animation.Visual.transform.localScale == Vector3.one * player.transform.Find("Character Visual").localScale.y && player.transform.localScale == Vector3.one && player.GetComponent<CircleCollider2D>().radius == .45f, "Animation rotated the directional source or changed collider scale.");
            animation.EndReload(); body.SetMotion(Vector2.zero, directions[i]);
        }
        inventory.OnWaveStarted(3); inventory.TryEquip(2);
        Require(!animation.Visual.enabled && feedback.RifleAnimation.Visual.enabled && feedback.Visual.sprite.name.StartsWith("Gun_", StringComparison.Ordinal), "Rifle did not hide pistol hands.");
        inventory.TryEquip(1); Require(animation.Visual.enabled, "Switching back did not restore pistol hands."); inventory.ResetRun();
        report.AppendLine("Four directional idle/run/shoot/reload sets animate at a fixed body-matched scale; run frames synchronize, reload anchors stay fixed, and AR switching hides/restores the overlay without changing physics.");
    }
    static void VerifySilentMovement(OverrunPlayer player, WaveDirector director)
    {
        Require(!Enum.GetNames(typeof(OverrunSound)).Any(s => s == "Footstep"), "Footstep cue is still registered.");
        var audio = OverrunAudio.Ensure(); int count = Enumerable.Range(0, (int)OverrunSound.Count).Sum(i => audio.Accepted((OverrunSound)i));
        var saved = player.transform.position; player.transform.position += Vector3.right * 1.5f; player.GetComponent<OverrunActorAudio>().SendMessage("LateUpdate"); player.transform.position = saved;
        var zombie = UnityEngine.Object.Instantiate(director.enemies[0].prefab); zombie.gameObject.SetActive(true); zombie.Init(director.enemies[0], null);
        zombie.transform.position = player.transform.position + Vector3.right * 1.5f; zombie.GetComponent<OverrunActorAudio>().SendMessage("LateUpdate");
        Require(Enumerable.Range(0, (int)OverrunSound.Count).Sum(i => audio.Accepted((OverrunSound)i)) == count, "Player/zombie movement emitted a sound.");
        zombie.gameObject.SetActive(false); UnityEngine.Object.Destroy(zombie.gameObject);
        report.AppendLine("Player and zombie movement emit no sounds; the footstep cue is removed, while other audio remains available.");
    }
    static void WithInput(OverrunPlayer player, Action<Action<bool>, Action<Key>> test)
    {
        var oldMouse = Mouse.current; var oldKeyboard = Keyboard.current; var mouse = InputSystem.AddDevice<Mouse>("Pistol animation mouse"); var keyboard = InputSystem.AddDevice<Keyboard>("Pistol reload keyboard");
        try
        {
            mouse.MakeCurrent(); keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Action<bool> trigger = down => { var point = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.right * 8); InputSystem.QueueStateEvent(mouse, new MouseState { position = point, buttons = down ? (ushort)1 : (ushort)0 }); InputSystem.Update(); player.SendMessage("Update"); };
            Action<Key> key = pressed => { InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed)); InputSystem.Update(); player.SendMessage("Update"); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); };
            test(trigger, key);
        }
        finally
        {
            InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update(); InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
            if (oldMouse != null && oldMouse.added) oldMouse.MakeCurrent(); if (oldKeyboard != null && oldKeyboard.added) oldKeyboard.MakeCurrent();
        }
    }
    static void VerifyMagazineAndBeginReload(OverrunPlayer player)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); var animation = player.GetComponentInChildren<OverrunWeaponFeedback>().PistolAnimation;
        inventory.ResetRun(); Require(inventory.PistolRounds == 12 && !inventory.TryReload(), "Fresh/full magazine state is wrong.");
        WithInput(player, (trigger, key) =>
        {
            for (int i = 0; i < 12; i++)
            {
                OverrunBullet.ClearActive(); OverrunAudio.Ensure().Advance(.4f, .4f); trigger(false); Cooldown.SetValue(player, 0f); trigger(true);
                Require(inventory.PistolRounds == 11 - i && UnityEngine.Object.FindObjectsByType<OverrunBullet>().Count(b => b.InFlight) == 1 && animation.Pose == OverrunPistolPose.Shoot, "Shot failed, spent multiple rounds or did not animate.");
                Cooldown.SetValue(player, 0f); trigger(true); Require(inventory.PistolRounds == 11 - i, "Held pistol consumed ammo.");
            }
            OverrunBullet.ClearActive(); var audio = OverrunAudio.Ensure(); audio.Advance(.4f, .4f); int empty = audio.Accepted(OverrunSound.PistolEmpty), bangs = audio.Accepted(OverrunSound.PistolShot);
            trigger(false); Cooldown.SetValue(player, 0f); trigger(true);
            Require(inventory.PistolRounds == 0 && !inventory.IsReloading && !UnityEngine.Object.FindObjectsByType<OverrunBullet>().Any(b => b.InFlight) && audio.Accepted(OverrunSound.PistolEmpty) == empty + 1 && audio.Accepted(OverrunSound.PistolShot) == bangs, "Empty click spawned a bullet, auto-reloaded or played a gunshot.");
            key(Key.R); Require(inventory.IsReloading && animation.Pose == OverrunPistolPose.Reload && !inventory.TryReload(), "R did not start a single reload.");
            Cooldown.SetValue(player, 0f); trigger(false); trigger(true); Require(inventory.PistolRounds == 0 && !UnityEngine.Object.FindObjectsByType<OverrunBullet>().Any(b => b.InFlight), "Reload allowed firing.");
            key(Key.W); player.SendMessage("FixedUpdate"); Require(player.GetComponent<Rigidbody2D>().linearVelocity.y > 0, "Reload disabled movement.");
            trigger(false); player.SendMessage("FixedUpdate");
            Sprite frame = animation.Visual.sprite; OverrunMenus.Instance.Pause(); inventory.Advance(10); animation.SendMessage("LateUpdate");
            Require(inventory.IsReloading && inventory.PistolRounds == 0 && animation.Visual.sprite == frame, "Pause advanced reload/ammo/visuals."); OverrunMenus.Instance.Resume();
        });
        report.AppendLine("Twelve distinct clicks consume exactly one round each; holds stay semi-auto, empty clicks only dry-fire, R starts reload, reload blocks firing but allows movement, and pause freezes reload.");
    }
    static void VerifyReloadCompletionAndDeath(OverrunPlayer player, LevelGenerator level)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); var animation = player.GetComponentInChildren<OverrunWeaponFeedback>().PistolAnimation;
        Require(inventory.IsReloading, "Reload state was lost across a frame boundary.");
        inventory.Advance(Mathf.Max(0, inventory.ReloadSecondsRemaining - .01f)); Require(inventory.IsReloading && inventory.PistolRounds == 0, "Magazine refilled before reload finished.");
        inventory.Advance(.02f); Require(!inventory.IsReloading && inventory.PistolRounds == 12 && animation.Pose == OverrunPistolPose.IdleRun, "Reload did not finish/refill/return to idle.");
        WithInput(player, (trigger, key) =>
        {
            trigger(false); Cooldown.SetValue(player, 0f); trigger(true); Require(inventory.PistolRounds == 11, "Post-reload pistol did not fire.");
            key(Key.R); inventory.Advance(OverrunWeaponSystem.PistolReloadDuration); Cooldown.SetValue(player, 0f); trigger(true);
            Require(inventory.PistolRounds == 12, "Holding fire through reload caused an automatic shot.");
            trigger(false); Cooldown.SetValue(player, 0f); trigger(true); Require(inventory.PistolRounds == 11, "Fresh click after reload failed.");
            key(Key.R); inventory.OnWaveStarted(3); key(Key.Digit2);
            Require(!inventory.IsReloading && inventory.Equipped == OverrunWeapon.AssaultRifle && inventory.PistolRounds == 11 && !animation.Visual.enabled, "AR switch did not cancel reload cleanly.");
            inventory.Advance(2); key(Key.Digit1); Require(inventory.PistolRounds == 11 && !inventory.IsReloading, "Cancelled reload refilled in the background.");
            key(Key.R); Require(inventory.IsReloading, "Pistol could not reload after switching back.");
        });
        var before = UnityEngine.Object.FindObjectsByType<OverrunPistolDeathVisual>();
        player.Damage(float.MaxValue);
        Require(player.Health == 0 && OverrunMenus.Instance.State == OverrunMenus.ScreenState.GameOver && !inventory.IsReloading && inventory.PistolRounds == 11, "Death failed to end the run, refilled ammo or retained reload.");
        var effects = UnityEngine.Object.FindObjectsByType<OverrunPistolDeathVisual>(); Require(effects.Length == before.Length + 1, "Pistol death visual missing/duplicated.");
        var effect = effects.Single(e => !before.Contains(e)); Require(effect.GetComponentsInChildren<Collider2D>().Length == 0, "Pistol death visual blocks movement.");
        var renderer = effect.GetComponentInChildren<SpriteRenderer>(); Sprite first = renderer.sprite; effect.Advance(.21f); Require(renderer.sprite != first && renderer.sprite.name.Contains("Death"), "Death frames are not advancing.");
        effect.Advance(1); Require(!effect.gameObject.activeSelf && player.Health == 0, "Falling pistol cleanup revived the player.");
        OverrunMenus.Instance.StartRun(); Require(inventory.PistolRounds == 12 && !inventory.IsReloading && !inventory.RifleUnlocked && UnityEngine.Object.FindObjectsByType<OverrunPistolDeathVisual>().Length == 0, "Run reset retained ammo/reload/death state.");
        report.AppendLine("Reload refills only on completion, held fire does not shoot on completion, switching cancels without granting ammo, death creates the noncolliding pistol visual and ends the run, and a new run resets all state.");
    }
    static void Capture(OverrunPlayer player)
    {
        var root = new GameObject("Pistol animation contact sheet"); root.transform.position = new Vector3(200, 0, 0);
        var prior = RenderTexture.active; RenderTexture target = null; Texture2D capture = null;
        try
        {
            var directions = new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up };
            for (int row = 0; row < 3; row++) for (int col = 0; col < 4; col++)
            {
                var actor = UnityEngine.Object.Instantiate(player.gameObject, root.transform); actor.GetComponent<OverrunPlayer>().enabled = false; actor.GetComponent<Rigidbody2D>().simulated = false;
                actor.transform.localPosition = new Vector3(3 + col * 5, 12 - row * 4, 0); actor.GetComponent<OverrunDepthSort>()?.Refresh();
                var feedback = actor.GetComponentInChildren<OverrunWeaponFeedback>(); actor.GetComponent<OverrunWeaponSystem>().ResetRun();
                actor.GetComponent<OverrunCharacterAnimator>().SetMotion(Vector2.zero, directions[col]); feedback.SetAim(directions[col]);
                if (row == 1) { feedback.PistolAnimation.Shoot(); feedback.PistolAnimation.Advance(.08f); }
                if (row == 2) { feedback.PistolAnimation.BeginReload(); feedback.PistolAnimation.Advance(.42f); }
            }
            var camera = new GameObject("Pistol animation preview camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false);
            camera.transform.localPosition = new Vector3(10.5f, 7, -10); camera.orthographic = true; camera.orthographicSize = 7.5f; camera.backgroundColor = new Color(.06f, .08f, .1f); camera.clearFlags = CameraClearFlags.SolidColor;
            target = new RenderTexture(1200, 750, 24); target.Create(); camera.targetTexture = target; SortingGroup.UpdateAllSortingGroups(); camera.Render(); RenderTexture.active = target;
            capture = new Texture2D(1200, 750, TextureFormat.RGBA32, false); capture.ReadPixels(new Rect(0, 0, 1200, 750), 0, 0); capture.Apply(); File.WriteAllBytes("Logs/OverrunPistolAnimations.png", capture.EncodeToPNG());
            report.AppendLine("Live character/pistol composites rendered: rows idle, firing, reload; columns down, right, left, up.");
        }
        finally
        {
            root.SetActive(false); UnityEngine.Object.Destroy(root); RenderTexture.active = prior;
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); } if (capture) UnityEngine.Object.Destroy(capture);
        }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
