using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class OverrunWeaponAudit
{
    const string Request = "Temp/OverrunWeaponAudit.request", Phase = "OverrunWeaponAudit.phase";
    static double started;
    static StringBuilder report;
    static readonly FieldInfo Health = typeof(OverrunPlayer).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo Cooldown = typeof(OverrunPlayer).GetField("fireTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    static OverrunWeaponAudit() { EditorApplication.update += Tick; }
    [MenuItem("Overrun/Validate Bandages and Weapon Progression")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify supplied sprites, occasional healing loot, semi-auto pistol, AR unlock, contextual keys and run resets.");
    }
    static void Tick()
    {
        if (!File.Exists(Request) || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        int phase = SessionState.GetInt(Phase, 0);
        if (phase == 0)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { EditorApplication.isPlaying = false; return; }
            SessionState.SetInt(Phase, 1); started = EditorApplication.timeSinceStartup;
            EditorApplication.isPaused = false; EditorApplication.isPlaying = true; return;
        }
        if (started <= 0) started = EditorApplication.timeSinceStartup;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup - started > 100)
        {
            var current = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
            var menu = OverrunMenus.Instance;
            Finish($"FAIL: Weapon audit timed out: playing={EditorApplication.isPlaying}, level={current != null}, map={current && current.Map != null}, menus={menu != null}, ready={menu && menu.IsReady}, state={(menu ? menu.State.ToString() : "none")}, frames={Time.frameCount}, inputBlocked={OverrunMenus.InputBlocked}."); return;
        }
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3 || !OverrunMovementAudit.EnsurePlaying()) return;
        try
        {
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>(); director.enabled = false;
            var player = director.player.GetComponent<OverrunPlayer>();
            if (phase == 1)
            {
                report = new StringBuilder(); OverrunMovementAudit.Verify(report); VerifyImports(); VerifyBandages(director, player);
                SessionState.SetInt(Phase, 2); return; // Resume creates an intentional input boundary.
            }
            VerifyWeapons(director, player); Capture(player);
            report.AppendLine("PASS"); Finish(report.ToString());
        }
        catch (Exception ex)
        {
            if (report == null) report = new StringBuilder(); report.AppendLine("FAIL: " + ex); Debug.LogException(ex); Finish(report.ToString());
        }
    }
    static void Finish(string result)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunWeaponAudit.txt", result);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); Debug.Log("[Overrun Weapon Audit] " + result); EditorApplication.isPlaying = false;
    }
    static void VerifyImports()
    {
        var sprites = new[] { OverrunPickupArt.Bandage, OverrunPickupArt.Pistol, OverrunPickupArt.Rifle };
        foreach (var sprite in sprites)
        {
            string path = AssetDatabase.GetAssetPath(sprite); var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            Require(sprite.texture.width == width && sprite.texture.height == height && sprite.rect.size == new Vector2(width, height) && sprite.pixelsPerUnit == 16 &&
                importer.filterMode == FilterMode.Point && !importer.mipmapEnabled && importer.wrapMode == TextureWrapMode.Clamp && importer.npotScale == TextureImporterNPOTScale.None,
                "Pickup/weapon art was resized, cropped or blurred: " + path);
        }
        Require(OverrunPickupArt.Bandage.rect.size == new Vector2(5, 7) && OverrunPickupArt.Pistol.rect.size == new Vector2(8, 7) && OverrunPickupArt.Rifle.rect.size == new Vector2(16, 8), "Wrong supplied sprite dimensions.");
        report.AppendLine("All three supplied PNGs retain original dimensions, full sprites, 16 PPU, point/no-mips/clamp imports.");
    }
    static void VerifyBandages(WaveDirector director, OverrunPlayer player)
    {
        float savedHealth = player.Health; var mode = Physics2D.simulationMode;
        var random = UnityEngine.Random.state; GameObject enemyRoot = null;
        try
        {
            OverrunBandagePickup.ClearActive(); Physics2D.simulationMode = SimulationMode2D.Script;
            Health.SetValue(player, 70f);
            var pickup = OverrunBandagePickup.Spawn(player.transform.position); Physics2D.SyncTransforms(); Physics2D.Simulate(.02f);
            Require(player.Health == 85 && !pickup.gameObject.activeSelf && !pickup.TryCollect(player), "Actual pickup trigger did not heal once for 15 HP.");
            Health.SetValue(player, player.maxHealth);
            var reused = OverrunBandagePickup.Spawn(player.transform.position); Require(reused == pickup, "Bandage pool did not reuse its object.");
            Physics2D.SyncTransforms(); Physics2D.Simulate(.02f);
            Require(reused.gameObject.activeSelf, "Full-health player wasted a bandage.");
            Health.SetValue(player, 95f); Physics2D.Simulate(.02f);
            Require(player.Health == 100 && !reused.gameObject.activeSelf, "Bandage did not clamp healing or retry on trigger stay.");
            Health.SetValue(player, 70f); pickup = OverrunBandagePickup.Spawn(player.transform.position);
            OverrunMenus.Instance.Pause(); Require(!pickup.TryCollect(player) && player.Health == 70, "Paused game consumed a bandage.");
            OverrunMenus.Instance.Resume(); pickup.Advance(OverrunBandagePickup.Lifetime + .1f);
            Require(!pickup.gameObject.activeSelf, "Expired bandage did not retire.");
            report.AppendLine("Real trigger collection heals once, caps at max HP, preserves full-health loot, retries on trigger stay, respects pause, expires and pools cleanly.");

            enemyRoot = new GameObject("Loot death fixture");
            var enemy = UnityEngine.Object.Instantiate(director.enemies[0].prefab, enemyRoot.transform);
            enemy.transform.position = new Vector3(10000, 10000, 0); UnityEngine.Random.InitState(84017);
            for (int i = 0; i < 200; i++)
            {
                enemy.gameObject.SetActive(true); enemy.Init(director.enemies[0], () => enemy.gameObject.SetActive(false));
                int before = OverrunBandagePickup.ActiveCount;
                enemy.TakeDamage(100); int after = OverrunBandagePickup.ActiveCount; enemy.TakeDamage(100);
                Require(after - before <= 1 && OverrunBandagePickup.ActiveCount == after, "A death dropped duplicate loot.");
            }
            int drops = OverrunBandagePickup.ActiveCount;
            Require(drops >= 15 && drops <= 70, "Bandages are not occasional drops: " + drops + "/200 deaths.");
            Require(!UnityEngine.Object.FindObjectsByType<SpriteRenderer>().Any(s => s.gameObject.name == "XP Pickup"), "Legacy XP pellets still spawned.");
            report.AppendLine($"Seeded 200 actual enemy deaths produced {drops} bandages, never duplicate drops; no XP pellets remain.");
        }
        finally
        {
            Health.SetValue(player, savedHealth); OverrunBandagePickup.ClearActive(); UnityEngine.Random.state = random; Physics2D.simulationMode = mode;
            if (enemyRoot) { enemyRoot.SetActive(false); UnityEngine.Object.Destroy(enemyRoot); }
            foreach (var corpse in UnityEngine.Object.FindObjectsByType<OverrunZombieCorpse>().Where(c => c.transform.position.x > 9990)) { corpse.gameObject.SetActive(false); UnityEngine.Object.Destroy(corpse.gameObject); }
        }
    }
    static int Shots() => UnityEngine.Object.FindObjectsByType<OverrunBullet>().Count(s => s.InFlight);
    static void VerifyWeapons(WaveDirector director, OverrunPlayer player)
    {
        var inventory = player.GetComponent<OverrunWeaponSystem>(); var feedback = player.GetComponentInChildren<OverrunWeaponFeedback>();
        var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        var mouse = InputSystem.AddDevice<Mouse>("Weapon audit mouse"); var keyboard = InputSystem.AddDevice<Keyboard>("Weapon audit keyboard");
        Vector2 cursor = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.right * 8);
        Action<bool> trigger = down => { InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor, buttons = down ? (ushort)1 : (ushort)0 }); InputSystem.Update(); player.SendMessage("Update"); };
        Action<Key> key = pressed =>
        {
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(pressed)); InputSystem.Update(); player.SendMessage("Update");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        };
        try
        {
            mouse.MakeCurrent(); keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            Require(inventory.Equipped == OverrunWeapon.Pistol && !inventory.RifleUnlocked, "A fresh run did not start with only a pistol.");
            OverrunBullet.ClearActive(); trigger(false); Cooldown.SetValue(player, 0f); trigger(true); Require(Shots() == 1, "Pistol first click did not fire once.");
            var shot = UnityEngine.Object.FindObjectsByType<OverrunBullet>().First(s => s.InFlight);
            Require((float)typeof(OverrunBullet).GetField("damage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(shot) == 3 && Mathf.Approximately(player.fireRate * inventory.IntervalMultiplier, .32f), "Pistol damage/rate profile is wrong.");
            Cooldown.SetValue(player, 0f); trigger(true); Require(Shots() == 1, "Holding the pistol fired again after cooldown.");
            trigger(false); Cooldown.SetValue(player, 0f); trigger(true); Require(Shots() == 2, "A second pistol click did not fire.");
            trigger(false); Cooldown.SetValue(player, 1f); trigger(true); Require(Shots() == 2, "Pistol ignored its cooldown.");
            Cooldown.SetValue(player, 0f); trigger(true); Require(Shots() == 2, "An early held click became an automatic pistol shot.");
            trigger(false); key(Key.Digit2); Require(inventory.Equipped == OverrunWeapon.Pistol && !inventory.RifleUnlocked, "Locked AR was equippable.");
            report.AppendLine("Pistol fires once per distinct click (3 damage / 0.32s), never repeats on hold or buffers an early held click; locked slot 2 is rejected.");

            director.Begin(); var modules = player.GetComponent<OverrunUpgradeSystem>();
            for (int wave = 1; wave <= 3; wave++)
            {
                director.Advance(wave == 1 ? director.initialGrace + .1f : director.betweenWaves + .1f);
                Require(inventory.RifleUnlocked == (wave >= 3), "AR unlocked at the wrong wave.");
                for (int tick = 0; tick < 100 && director.State != WaveDirector.Phase.Reward; tick++)
                {
                    director.Advance(2); foreach (var enemy in director.GetComponentsInChildren<Enemy>().Where(e => e.IsAlive)) enemy.TakeDamage(10000);
                }
                Require(modules.Choosing, "Weapon progression fixture did not clear a wave.");
                if (wave < 3) Require(modules.Choose(0), "Earlier wave reward could not be chosen.");
            }
            Require(inventory.Equipped == OverrunWeapon.Pistol, "Unlock forcibly equipped the AR.");
            var offer = modules.Choices[1]; key(Key.Digit2);
            Require(!modules.Choosing && inventory.Equipped == OverrunWeapon.Pistol && (offer.rank == 0 || modules.Rank(offer.kind) == offer.rank), "Reward key 2 also switched weapons or picked the wrong module.");
            key(Key.Digit2); Require(inventory.Equipped == OverrunWeapon.AssaultRifle && inventory.RifleUnlocked, "Combat key 2 did not equip the unlocked AR.");
            OverrunBullet.ClearActive(); trigger(false); Cooldown.SetValue(player, 0f); trigger(true); Require(Shots() == 1, "AR trigger did not fire.");
            Cooldown.SetValue(player, 1f); trigger(true); Require(Shots() == 1, "AR ignored cooldown.");
            Cooldown.SetValue(player, 0f); trigger(true); Require(Shots() == 2, "AR did not repeat while held.");
            Require(Mathf.Approximately(player.fireRate * inventory.IntervalMultiplier, .12f) && inventory.DamageMultiplier == 1, "AR profile is wrong.");
            Cooldown.SetValue(player, 1f); int beforeSwitch = Shots(); key(Key.Digit1);
            Require(inventory.Equipped == OverrunWeapon.Pistol && Shots() == beforeSwitch && (float)Cooldown.GetValue(player) > .5f, "Switching bypassed cooldown or fired through held input.");
            trigger(false); key(Key.Digit2);
            report.AppendLine("Real wave progression locks the AR through wave 2 and unlocks in wave 3; reward key 2 picks a card only, combat keys 1/2 equip weapons. AR repeats on hold (2 damage / 0.12s); switching preserves cooldown and blocks held-click shots.");

            foreach (var mode in new[] { OverrunWeapon.Pistol, OverrunWeapon.AssaultRifle })
            {
                inventory.TryEquip(mode == OverrunWeapon.Pistol ? 1 : 2);
                Require(feedback.Visual.sprite.name.StartsWith(mode == OverrunWeapon.Pistol ? "Pistol_" : "Gun_", StringComparison.Ordinal), "Weapon visual did not use the supplied sprite.");
                foreach (var direction in new[] { Vector2.right, Vector2.up, Vector2.left, Vector2.down, new Vector2(1, 1).normalized })
                {
                    cursor = Camera.main.WorldToScreenPoint(player.transform.position + (Vector3)direction * 8); trigger(false);
                    Vector2 muzzle = feedback.MuzzlePosition - (Vector2)feedback.transform.position;
                    float forward = mode == OverrunWeapon.Pistol ? .35f : .6f, side = mode == OverrunWeapon.Pistol ? .9f : 1.2f;
                    Require(Vector2.Dot(feedback.transform.up, direction) > .99f && Vector2.Dot(muzzle, direction) > forward && Mathf.Abs(Vector2.Dot(muzzle, Vector2.Perpendicular(direction))) < side, "Weapon barrel/muzzle does not follow aim.");
                }
            }
            OverrunMenus.Instance.Pause(); Require(!inventory.TryEquip(1), "Pause allowed weapon switching."); OverrunMenus.Instance.Resume();
            OverrunBandagePickup.Spawn(new Vector2(10000, 10000)); OverrunMenus.Instance.StartRun();
            Require(inventory.Equipped == OverrunWeapon.Pistol && !inventory.RifleUnlocked && OverrunBandagePickup.ActiveCount == 0 && player.Health == player.maxHealth, "Restart retained a weapon unlock or healing loot.");
            report.AppendLine("Both supplied gun sprites and muzzles align in five aim directions, pause blocks switching, and a new run resets to pistol-only with no old bandages.");
        }
        finally
        {
            InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent(); if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        }
    }
    static void Capture(OverrunPlayer player)
    {
        var camera = Camera.main; var inventory = player.GetComponent<OverrunWeaponSystem>(); var feedback = player.GetComponentInChildren<OverrunWeaponFeedback>();
        var previousTarget = camera.targetTexture; var previousActive = RenderTexture.active;
        RenderTexture target = null; Texture2D capture = null;
        try
        {
            OverrunBandagePickup.Spawn(player.transform.position + Vector3.left * 2);
            OverrunBandagePickup.Spawn(player.transform.position + Vector3.down * 2);
            feedback.transform.rotation = Quaternion.Euler(0, 0, -90); inventory.ResetRun();
            target = new RenderTexture(960, 540, 24); target.Create(); camera.targetTexture = target;
            capture = new Texture2D(960, 540, TextureFormat.RGBA32, false);
            for (int i = 0; i < 2; i++)
            {
                if (i == 1) { inventory.OnWaveStarted(3); inventory.TryEquip(2); }
                SortingGroup.UpdateAllSortingGroups(); camera.Render(); RenderTexture.active = target;
                capture.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); capture.Apply();
                File.WriteAllBytes("Logs/Overrun" + (i == 0 ? "Pistol" : "Rifle") + "Preview.png", capture.EncodeToPNG());
            }
            report.AppendLine("Pistol/AR and supplied healing bandages rendered in the live procedural building.");
        }
        finally
        {
            camera.targetTexture = previousTarget; RenderTexture.active = previousActive;
            if (target) { target.Release(); UnityEngine.Object.Destroy(target); } if (capture) UnityEngine.Object.Destroy(capture);
        }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
