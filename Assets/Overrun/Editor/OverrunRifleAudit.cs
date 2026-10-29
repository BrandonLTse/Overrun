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
public static class OverrunRifleAudit
{
    const string Request = "Temp/OverrunRifleAudit.request", Phase = "OverrunRifleAudit.phase";
    static double started;
    static StringBuilder report;
    static readonly FieldInfo Cooldown = typeof(OverrunPlayer).GetField("fireTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    static OverrunRifleAudit() { EditorApplication.update += Tick; }
    [MenuItem("Overrun/Validate Animated AR and Reload")]
    public static void RequestAudit() { Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify AR imports, animation, 30-round automatic fire, reload and independent magazines."); }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            OverrunPistolSpriteImport.ImportNewRifleSheets();
            started = EditorApplication.timeSinceStartup; SessionState.SetInt(Phase, 1); EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - started > 100) { Finish("FAIL: AR audit timed out."); return; }
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3 || !OverrunMovementAudit.EnsurePlaying()) return;
        try
        {
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>(); director.enabled = false;
            var player = director.player.GetComponent<OverrunPlayer>();
            if (phase == 1)
            {
                report = new StringBuilder(); OverrunMovementAudit.Verify(report); VerifyImports(); VerifyAnimations(player); BeginMagazineTest(player);
                SessionState.SetInt(Phase, 2); return;
            }
            CompleteMagazineTest(player, level); Capture(player); report.AppendLine("PASS"); Finish(report.ToString());
        }
        catch (Exception ex) { if (report == null) report = new StringBuilder(); report.AppendLine("FAIL: " + ex); Debug.LogException(ex); Finish(report.ToString()); }
    }
    static void Finish(string result)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunRifleAudit.txt", result);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); Debug.Log("[Overrun AR Audit] " + result); EditorApplication.isPlaying = false;
    }
    static void VerifyImports()
    {
        var paths = AssetDatabase.FindAssets("t:Texture2D", new[] { OverrunPistolSpriteImport.RifleFolder.TrimEnd('/') }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        Require(paths.Length == 14, "Expected fourteen AR sheets."); int total = 0;
        foreach (var path in paths)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path); importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path); var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().OrderBy(s => s.name).ToArray();
            int count = int.Parse(Regex.Match(Path.GetFileNameWithoutExtension(path), @"Sheet(\d+)$").Groups[1].Value);
            Require(texture.width == width && texture.height == height && sprites.Length == count && importer.filterMode == FilterMode.Point && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.npotScale == TextureImporterNPOTScale.None && importer.textureCompression == TextureImporterCompression.Uncompressed, "AR source resized, compressed, blurred or mis-sliced.");
            for (int i = 0; i < count; i++) Require(sprites[i].pixelsPerUnit == 16 && sprites[i].rect == new Rect(i * (width / count), 0, width / count, height) && sprites[i].name.EndsWith("_" + i.ToString("D2"), StringComparison.Ordinal), "AR frame cropped or reordered.");
            total += count;
        }
        Require(total == 80, "Expected 80 AR frames.");
        foreach (OverrunPistolFacing facing in Enum.GetValues(typeof(OverrunPistolFacing))) foreach (OverrunPistolPose pose in Enum.GetValues(typeof(OverrunPistolPose)))
        foreach (var sprite in OverrunRifleClips.Frames(facing, pose))
        {
            var direction = pose == OverrunPistolPose.Death && facing != OverrunPistolFacing.Left ? OverrunPistolFacing.Right : facing;
            Require(Vector2.Distance(sprite.pivot, OverrunRifleClips.Pivot(direction, pose, sprite.rect.width, sprite.rect.height)) < .001f, "Wrong rifle wrist anchor.");
        }
        report.AppendLine("14 untouched sheets / 80 full-cell frames retain original dimensions, point/no-mips/clamp, uncompressed 16 PPU, stable wrist pivots and source frame ordering.");
    }
    static void EquipRifle(OverrunPlayer player)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); inventory.ResetRun(); inventory.OnWaveStarted(3); Require(inventory.TryEquip(2), "Unlocked AR did not equip.");
    }
    static void VerifyAnimations(OverrunPlayer player)
    {
        EquipRifle(player); var feedback = player.GetComponentInChildren<OverrunWeaponFeedback>(); var animation = feedback.RifleAnimation;
        var body = player.GetComponent<OverrunCharacterAnimator>(); var dirs = new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up };
        Require(animation.Visual.enabled && !feedback.PistolAnimation.Visual.enabled, "Both weapon overlays are visible.");
        for (int i = 0; i < 4; i++)
        {
            body.SetMotion(dirs[i], dirs[i]); feedback.SetAim(dirs[i]); animation.Advance(0);
            Require((int)animation.Facing == i && animation.Visual.sprite == OverrunRifleClips.Frames((OverrunPistolFacing)i, OverrunPistolPose.IdleRun)[body.FrameIndex], "Wrong AR direction/run frame.");
            typeof(OverrunCharacterAnimator).GetField("frame", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(body, 4); body.SendMessage("ApplyFrame"); animation.Advance(0);
            Require(animation.Visual.sprite.name.EndsWith("_04", StringComparison.Ordinal), "AR hands are not synchronized with the run cycle.");
            var attachment = animation.Visual.transform.localPosition;
            animation.Shoot(); animation.Advance(.05f); Require(animation.Visual.sprite.name.Contains("shoot") && animation.Visual.sprite.name.EndsWith("_01", StringComparison.Ordinal), "AR firing did not advance.");
            animation.Advance(.04f); Require(animation.Visual.sprite.name.EndsWith("_02", StringComparison.Ordinal), "AR third firing frame missing.");
            animation.Advance(.05f); Require(animation.Pose == OverrunPistolPose.IdleRun, "AR firing did not return to idle.");
            animation.BeginReload(); animation.Advance(.5f);
            Require(animation.Pose == OverrunPistolPose.Reload && animation.Visual.sprite.name.IndexOf("reload", StringComparison.OrdinalIgnoreCase) >= 0 && animation.Visual.sprite.name.EndsWith("_04", StringComparison.Ordinal) && animation.Visual.transform.localPosition == attachment, "Wrong AR reload clip, timing or attachment.");
            Require(animation.Visual.transform.localRotation == Quaternion.identity && !animation.Visual.flipX && animation.Visual.transform.localScale == Vector3.one * player.transform.Find("Character Visual").localScale.y && player.GetComponent<CircleCollider2D>().radius == .45f && player.transform.localScale == Vector3.one, "AR animation changed physics/scale or rotated directional art.");
            Require(animation.Visual.sortingOrder == (i == 3 ? 49 : 51), "Wrong weapon/body depth ordering."); animation.EndReload(); body.SetMotion(Vector2.zero, dirs[i]);
        }
        player.GetComponent<OverrunWeaponSystem>().TryEquip(1); Require(!animation.Visual.enabled && feedback.PistolAnimation.Visual.enabled, "Switching did not hide rifle hands.");
        report.AppendLine("Four directions animate idle/run, all three shooting frames and eight reload frames; body frame synchronization, scale, wrist attachments, depth order and exclusive weapon visibility pass without collider changes.");
    }
    static void WithInput(OverrunPlayer player, Action<Action<bool>, Action<Key>> test)
    {
        var oldMouse = Mouse.current; var oldKeyboard = Keyboard.current; var mouse = InputSystem.AddDevice<Mouse>("AR audit mouse"); var keyboard = InputSystem.AddDevice<Keyboard>("AR audit keyboard");
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
    static void BeginMagazineTest(OverrunPlayer player)
    {
        EquipRifle(player); var inventory = player.GetComponent<OverrunWeaponSystem>(); var feedback = player.GetComponentInChildren<OverrunWeaponFeedback>();
        var audio = OverrunAudio.Ensure(); audio.Advance(2, 2);
        Require(inventory.RifleRounds == 30 && inventory.PistolRounds == 12 && !inventory.TryReload(), "Fresh/full AR magazine is wrong.");
        WithInput(player, (trigger, key) =>
        {
            trigger(false);
            for (int i = 0; i < 30; i++)
            {
                OverrunBullet.ClearActive(); audio.Advance(.3f, .3f); Cooldown.SetValue(player, 0f); trigger(true);
                Require(inventory.RifleRounds == 29 - i && inventory.PistolRounds == 12 && UnityEngine.Object.FindObjectsByType<OverrunBullet>().Count(b => b.InFlight) == 1 && feedback.RifleAnimation.Pose == OverrunPistolPose.Shoot, "Held AR spent wrong ammo, failed to fire/animate or touched pistol ammo.");
                Cooldown.SetValue(player, 1f); trigger(true); Require(inventory.RifleRounds == 29 - i, "AR bypassed shot cooldown.");
            }
            OverrunBullet.ClearActive(); audio.Advance(1, 1); int dry = audio.Accepted(OverrunSound.RifleEmpty), bang = audio.Accepted(OverrunSound.RifleShot), reload = audio.Accepted(OverrunSound.RifleReload);
            Cooldown.SetValue(player, 0f); trigger(true);
            Require(inventory.RifleRounds == 0 && !inventory.IsReloading && !UnityEngine.Object.FindObjectsByType<OverrunBullet>().Any(b => b.InFlight) && audio.Accepted(OverrunSound.RifleEmpty) == dry + 1 && audio.Accepted(OverrunSound.RifleShot) == bang, "Empty AR fired, auto-reloaded or played a gunshot.");
            key(Key.R); Require(inventory.IsReloading && feedback.RifleAnimation.Pose == OverrunPistolPose.Reload && audio.Accepted(OverrunSound.RifleReload) == reload + 1 && !inventory.TryReload(), "R did not start exactly one audible rifle reload.");
            Cooldown.SetValue(player, 0f); trigger(true); Require(!UnityEngine.Object.FindObjectsByType<OverrunBullet>().Any(b => b.InFlight), "AR fired during reload.");
            key(Key.W); player.SendMessage("FixedUpdate"); Require(player.GetComponent<Rigidbody2D>().linearVelocity.y > 0, "AR reload blocked movement."); trigger(false); player.SendMessage("FixedUpdate");
            var frame = feedback.Visual.sprite; float remaining = inventory.ReloadSecondsRemaining; OverrunMenus.Instance.Pause(); inventory.Advance(10); feedback.RifleAnimation.SendMessage("LateUpdate");
            Require(inventory.ReloadSecondsRemaining == remaining && inventory.RifleRounds == 0 && feedback.Visual.sprite == frame, "Pause advanced AR reload."); OverrunMenus.Instance.Resume();
        });
        report.AppendLine("A held trigger fires exactly 30 rounds, spends only AR ammo, respects cooldown and animates every shot. Empty fire produces only a dry click. R starts one reload; firing is blocked, movement works and pause freezes timer, ammo and frames.");
    }
    static void CompleteMagazineTest(OverrunPlayer player, LevelGenerator level)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); var feedback = player.GetComponentInChildren<OverrunWeaponFeedback>(); var audio = OverrunAudio.Ensure();
        Require(inventory.IsReloading, "AR reload lost state across frames."); int ready = audio.Accepted(OverrunSound.RifleReady);
        inventory.Advance(Mathf.Max(0, inventory.ReloadSecondsRemaining - .01f)); Require(inventory.RifleRounds == 0 && inventory.IsReloading, "AR refilled early.");
        inventory.Advance(.02f); Require(inventory.RifleRounds == 30 && !inventory.IsReloading && feedback.RifleAnimation.Pose == OverrunPistolPose.IdleRun && audio.Accepted(OverrunSound.RifleReady) == ready + 1, "AR completion did not refill, reset visuals or play ready sound.");
        WithInput(player, (trigger, key) =>
        {
            trigger(false); Cooldown.SetValue(player, 0f); trigger(true); Require(inventory.RifleRounds == 29, "Reloaded AR did not fire.");
            key(Key.R); Cooldown.SetValue(player, 0f); trigger(true); Require(inventory.RifleRounds == 29, "Partial reload allowed firing.");
            inventory.Advance(OverrunWeaponSystem.RifleReloadDuration); Cooldown.SetValue(player, 0f); trigger(true); Require(inventory.RifleRounds == 29, "Automatic fire did not resume after reload on a held trigger.");
            key(Key.R); key(Key.Digit1); Require(!inventory.IsReloading && inventory.RifleRounds == 29 && inventory.PistolRounds == 12, "Switch to pistol did not cancel AR reload.");
            inventory.Advance(3); trigger(false); Cooldown.SetValue(player, 0f); trigger(true); Require(inventory.PistolRounds == 11 && inventory.RifleRounds == 29, "Magazines are not independent.");
            key(Key.R); key(Key.Digit2); Require(!inventory.IsReloading && inventory.PistolRounds == 11 && inventory.RifleRounds == 29, "Switch to AR did not cancel pistol reload.");
            inventory.Advance(3); Require(inventory.PistolRounds == 11 && inventory.RifleRounds == 29, "Cancelled reload granted background ammo.");
            key(Key.R); Require(inventory.IsReloading && !feedback.PistolAnimation.Visual.enabled && feedback.RifleAnimation.Visual.enabled, "AR could not reload after switching.");
        });
        var before = UnityEngine.Object.FindObjectsByType<OverrunRifleDeathVisual>(); player.Damage(float.MaxValue);
        Require(player.Health == 0 && OverrunMenus.Instance.State == OverrunMenus.ScreenState.GameOver && !inventory.IsReloading && inventory.RifleRounds == 29 && inventory.PistolRounds == 11, "AR death failed to end the run, changed ammo or retained reload.");
        var effects = UnityEngine.Object.FindObjectsByType<OverrunRifleDeathVisual>(); Require(effects.Length == before.Length + 1, "AR death visual missing/duplicated."); var effect = effects.Single(e => !before.Contains(e));
        Require(effect.GetComponentsInChildren<Collider2D>().Length == 0, "AR death visual has collision."); var renderer = effect.GetComponentInChildren<SpriteRenderer>(); var initial = renderer.sprite;
        effect.Advance(.21f); Require(renderer.sprite != initial && renderer.sprite.name.StartsWith("Gun_", StringComparison.Ordinal) && renderer.sprite.name.Contains("Death"), "AR death frames failed.");
        effect.Advance(1); Require(!effect.gameObject.activeSelf && player.Health == 0, "AR death effect revived the player.");
        OverrunMenus.Instance.StartRun(); Require(inventory.PistolRounds == 12 && inventory.RifleRounds == 30 && !inventory.IsReloading && !inventory.RifleUnlocked && inventory.Equipped == OverrunWeapon.Pistol && !feedback.RifleAnimation.Visual.enabled && UnityEngine.Object.FindObjectsByType<OverrunWeaponDeathVisual>().Length == 0, "New run retained ammo, unlock, reload or death visuals.");
        report.AppendLine("Completion refills only the equipped magazine and plays its ready cue; held AR fire resumes after reload. Switching either direction cancels without granting ammo. Partial magazines persist independently. Death creates the supplied cosmetic visual and ends the run; restart resets both magazines, ownership and all death effects.");
    }
    static void Capture(OverrunPlayer player)
    {
        var root = new GameObject("AR animation contact sheet"); root.transform.position = new Vector3(200, 0, 0);
        var prior = RenderTexture.active; RenderTexture target = null; Texture2D capture = null;
        try
        {
            var dirs = new[] { Vector2.down, Vector2.right, Vector2.left, Vector2.up };
            for (int row = 0; row < 3; row++) for (int col = 0; col < 4; col++)
            {
                var actor = UnityEngine.Object.Instantiate(player.gameObject, root.transform); var controller = actor.GetComponent<OverrunPlayer>(); controller.enabled = false; actor.GetComponent<Rigidbody2D>().simulated = false;
                actor.transform.localPosition = new Vector3(3 + col * 5, 12 - row * 4, 0); actor.GetComponent<OverrunDepthSort>()?.Refresh(); EquipRifle(controller);
                var feedback = actor.GetComponentInChildren<OverrunWeaponFeedback>(); actor.GetComponent<OverrunCharacterAnimator>().SetMotion(Vector2.zero, dirs[col]); feedback.SetAim(dirs[col]);
                if (row == 1) { feedback.RifleAnimation.Shoot(); feedback.RifleAnimation.Advance(.05f); }
                if (row == 2) { feedback.RifleAnimation.BeginReload(); feedback.RifleAnimation.Advance(.5f); }
            }
            var camera = new GameObject("AR preview camera").AddComponent<Camera>(); camera.transform.SetParent(root.transform, false); camera.transform.localPosition = new Vector3(10.5f, 7, -10);
            camera.orthographic = true; camera.orthographicSize = 7.5f; camera.backgroundColor = new Color(.06f, .08f, .1f); camera.clearFlags = CameraClearFlags.SolidColor;
            target = new RenderTexture(1200, 750, 24); target.Create(); camera.targetTexture = target; SortingGroup.UpdateAllSortingGroups(); camera.Render(); RenderTexture.active = target;
            capture = new Texture2D(1200, 750, TextureFormat.RGBA32, false); capture.ReadPixels(new Rect(0, 0, 1200, 750), 0, 0); capture.Apply(); File.WriteAllBytes("Logs/OverrunRifleAnimations.png", capture.EncodeToPNG());
            report.AppendLine("Live AR/body composites rendered: rows idle, firing, reload; columns down, right, left, up.");
        }
        finally { root.SetActive(false); UnityEngine.Object.Destroy(root); RenderTexture.active = prior; if (target) { target.Release(); UnityEngine.Object.Destroy(target); } if (capture) UnityEngine.Object.Destroy(capture); }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
