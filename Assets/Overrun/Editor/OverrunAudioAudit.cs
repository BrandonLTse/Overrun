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
public static class OverrunAudioAudit
{
    const string Request = "Temp/OverrunAudioAudit.request", Phase = "OverrunAudioAudit.phase";
    static double started;
    static int captureFrame;
    static StringBuilder report;
    static Vector2 savedVolumes;
    static readonly FieldInfo Health = typeof(OverrunPlayer).GetField("health", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo Cooldown = typeof(OverrunPlayer).GetField("fireTimer", BindingFlags.Instance | BindingFlags.NonPublic);
    static OverrunAudioAudit() { EditorApplication.update += Tick; }
    [MenuItem("Overrun/Validate Game Audio")]
    public static void RequestAudit()
    {
        Directory.CreateDirectory("Temp"); File.WriteAllText(Request, "Verify effects-only audio, bounded mix, pause routing, event hooks and gameplay RNG isolation.");
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
        if (EditorApplication.timeSinceStartup - started > 110) { Finish("FAIL: Audio audit timed out."); return; }
        var level = UnityEngine.Object.FindAnyObjectByType<LevelGenerator>();
        if (!EditorApplication.isPlaying || !level || level.Map == null || EditorApplication.timeSinceStartup - started < 3) return;
        try
        {
            var player = UnityEngine.Object.FindAnyObjectByType<OverrunPlayer>(); var menus = OverrunMenus.Instance;
            var audio = OverrunAudio.Ensure(player.transform);
            if (phase < 3 && !OverrunMovementAudit.EnsurePlaying()) return;
            var director = UnityEngine.Object.FindAnyObjectByType<WaveDirector>(); director.enabled = false;
            if (phase == 1)
            {
                report = new StringBuilder(); savedVolumes = new Vector2(audio.Master, audio.Effects);
                audio.SetVolumes(.8f, .8f, false);
                VerifyBankAndRng(audio, player); VerifyMixer(audio, player);
                SessionState.SetInt(Phase, 2); return;
            }
            if (phase == 2)
            {
                VerifyHooks(audio, player, director);
                menus.Pause();
                audio.Advance(2, 0); OverrunAudio.UI(OverrunSound.MenuAccept);
                Require(AudioListener.pause && audio.LastSource(OverrunSound.MenuAccept).ignoreListenerPause, "Paused menu sound is not routed to UI.");
                captureFrame = Time.frameCount;
                ExportPreview(); SessionState.SetInt(Phase, 3); return;
            }
            if (Time.frameCount <= captureFrame + 3) return;
            Require(menus.State == OverrunMenus.ScreenState.Paused && Time.timeScale == 0 && AudioListener.pause, "Menu sounds unpaused the run.");
            PressMenu(Key.Escape); Require(menus.State == OverrunMenus.ScreenState.Playing && !AudioListener.pause, "Resume from pause failed.");
            audio.SetVolumes(savedVolumes.x, savedVolumes.y, false);
            report.AppendLine("Menu sounds remain pause-safe over actual paused frames; Escape resumes the run, and original effect volume preferences are restored.");
            report.AppendLine("Original sound reel exported to Logs/OverrunSoundPreview.wav.\nPASS"); Finish(report.ToString());
        }
        catch (Exception ex)
        {
            if (report == null) report = new StringBuilder(); report.AppendLine("FAIL: " + ex); Debug.LogException(ex);
            if (OverrunAudio.Instance) OverrunAudio.Instance.SetVolumes(savedVolumes.x, savedVolumes.y, false);
            Finish(report.ToString());
        }
    }
    static void Finish(string text)
    {
        Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/OverrunAudioAudit.txt", text);
        if (File.Exists(Request)) File.Move(Request, Request + ".completed-" + DateTime.UtcNow.Ticks);
        SessionState.EraseInt(Phase); Debug.Log("[Overrun Audio Audit] " + text); EditorApplication.isPlaying = false;
    }
    static void VerifyBankAndRng(OverrunAudio audio, OverrunPlayer player)
    {
        var state = UnityEngine.Random.state;
        try
        {
            UnityEngine.Random.InitState(5731); float expected = UnityEngine.Random.value; UnityEngine.Random.InitState(5731);
            int count = 0;
            for (int i = 0; i < (int)OverrunSound.Count; i++) for (int v = 0; v < OverrunAudioBank.Variants((OverrunSound)i); v++)
            {
                var clip = OverrunAudioBank.Get((OverrunSound)i, v); var data = new float[clip.samples];
                Require(clip.channels == 1 && clip.frequency == 22050 && clip.GetData(data, 0), "Clip PCM is unavailable.");
                double square = 0; float peak = 0;
                foreach (float sample in data) { Require(!float.IsNaN(sample) && !float.IsInfinity(sample), "Invalid PCM sample."); peak = Mathf.Max(peak, Mathf.Abs(sample)); square += sample * sample; }
                Require(peak > .1f && peak <= .701f && square / data.Length > .0001 && Mathf.Abs(data[0]) < .00001f && Mathf.Abs(data[data.Length - 1]) < .00001f, "Silent, clipped or click-boundary audio: " + clip.name);
                count++;
            }
            audio.ResetWorld(); audio.Advance(2, 2);
            OverrunAudio.World(OverrunSound.PistolShot, player.transform.position); OverrunAudio.UI(OverrunSound.MenuAccept);
            Require(UnityEngine.Random.value == expected, "Audio changed Unity's gameplay/drop RNG.");
            report.AppendLine($"{count} original PCM clips/variants have valid non-silent samples, capped .7 peaks and click-free boundaries; synthesis/playback do not alter gameplay RNG.");
        }
        finally { UnityEngine.Random.state = state; }
    }
    static void VerifyMixer(OverrunAudio audio, OverrunPlayer player)
    {
        int sources = audio.GetComponentsInChildren<AudioSource>(true).Length;
        Require(sources == OverrunAudio.WorldCapacity + OverrunAudio.UICapacity && audio.GetComponentsInChildren<AudioSource>(true).All(s => !s.loop) && !Enum.GetNames(typeof(OverrunSound)).Contains("Ambience"), "Effects-only audio has an extra source or a looping sound.");
        audio.ResetWorld(); audio.Advance(2, 2);
        Require(OverrunAudio.World(OverrunSound.PistolShot, player.transform.position), "World shot cue was rejected.");
        var world = audio.LastSource(OverrunSound.PistolShot); Require(world && !world.ignoreListenerPause, "World voice bypasses pause.");
        int active = audio.ActiveWorld; OverrunMenus.Instance.Pause();
        Require(!OverrunAudio.World(OverrunSound.ZombieGroan, player.transform.position) && OverrunAudio.UI(OverrunSound.MenuAccept), "Paused world/UI routing is incorrect.");
        Require(audio.LastSource(OverrunSound.MenuAccept).ignoreListenerPause, "UI audio is muted by listener pause.");
        audio.Advance(1, 0); Require(audio.ActiveWorld == active && audio.ActiveUI == 0, "Paused world sounds expired or UI failed to retire with unscaled time.");
        OverrunMenus.Instance.Resume(); audio.Advance(2, 2); Require(audio.ActiveWorld == 0, "World voices did not retire after resume.");
        audio.ResetWorld(); int before = audio.Accepted(OverrunSound.ZombieGroan);
        for (int i = 0; i < 200; i++) OverrunAudio.World(OverrunSound.ZombieGroan, player.transform.position);
        Require(audio.Accepted(OverrunSound.ZombieGroan) == before + 1, "A crowd spammed simultaneous groans.");
        for (int round = 0; round < 50; round++)
        {
            audio.Advance(.1f, .1f);
            for (int cue = 0; cue < (int)OverrunSound.MenuMove; cue++) OverrunAudio.World((OverrunSound)cue, player.transform.position);
            OverrunAudio.UI(OverrunSound.MenuMove);
            Require(audio.ActiveWorld <= 24 && audio.ActiveUI <= 4 && audio.GetComponentsInChildren<AudioSource>(true).Length == sources, "Stress allocated unbounded audio sources.");
            float worldGain = audio.GetComponentsInChildren<AudioSource>().Where(s => !s.ignoreListenerPause && !s.loop).Sum(s => s.volume);
            Require(worldGain <= audio.Master * audio.Effects + .001f, "Crowd mix exceeded its gain budget.");
        }
        audio.ResetWorld(); audio.Advance(2, 2); OverrunAudio.World(OverrunSound.Ricochet, player.transform.position + Vector3.left * 5);
        Require(audio.LastSource(OverrunSound.Ricochet).panStereo < 0, "Left world cue did not pan left.");
        audio.ResetWorld(); OverrunAudio.World(OverrunSound.Ricochet, player.transform.position + Vector3.right * 5);
        Require(audio.LastSource(OverrunSound.Ricochet).panStereo > 0 && !OverrunAudio.World(OverrunSound.ZombieGroan, player.transform.position + Vector3.right * 30), "Panning or distant culling failed.");
        audio.SetVolumes(0, 1, false); Require(audio.GetComponentsInChildren<AudioSource>().All(s => s.volume == 0), "Master mute left an audible source.");
        audio.SetVolumes(1, 0, false); Require(audio.GetComponentsInChildren<AudioSource>().All(s => s.volume == 0), "Effects mute left SFX audible.");
        audio.SetVolumes(.8f, .8f, false); audio.ResetWorld(); audio.Advance(2, 2);
        audio.enabled = false; audio.enabled = true; audio.ResetWorld();
        Require(audio.GetComponentsInChildren<AudioSource>(true).All(s => !s.loop && !s.isPlaying) && audio.GetComponentsInChildren<AudioSource>(true).Length == sources, "Reset or re-enable restarted a background loop.");
        report.AppendLine("24 world + 4 UI voices stay bounded with no ambient cue, extra source or loops, including after reset/re-enable; crowd cooldowns, gain limits, stereo panning, distance culling, mutes and pause/resume lifetimes passed.");
    }
    static void VerifyHooks(OverrunAudio audio, OverrunPlayer player, WaveDirector director)
    {
        var previousMouse = Mouse.current; var previousKeyboard = Keyboard.current;
        var mouse = InputSystem.AddDevice<Mouse>("Audio weapon mouse"); var keyboard = InputSystem.AddDevice<Keyboard>("Audio weapon keyboard");
        GameObject root = null;
        float savedHealth = player.Health; Vector3 savedPosition = player.transform.position;
        var definition = ScriptableObject.CreateInstance<EnemyDefinition>(); definition.maxHealth = 100; definition.contactDamage = 8;
        Action<bool> trigger = down =>
        {
            Vector2 cursor = Camera.main.WorldToScreenPoint(player.transform.position + Vector3.right * 8);
            InputSystem.QueueStateEvent(mouse, new MouseState { position = cursor, buttons = down ? (ushort)1 : (ushort)0 });
            InputSystem.Update(); player.SendMessage("Update");
        };
        try
        {
            mouse.MakeCurrent(); keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            audio.Advance(2, 2); audio.ResetWorld();
            var inventory = player.GetComponent<OverrunWeaponSystem>(); inventory.ResetRun();
            trigger(false); Cooldown.SetValue(player, 0f); int pistol = audio.Accepted(OverrunSound.PistolShot);
            trigger(true); Cooldown.SetValue(player, 0f); trigger(true);
            Require(audio.Accepted(OverrunSound.PistolShot) == pistol + 1, "Pistol sound repeats on hold or misses a real shot.");
            audio.Advance(1, 1); int unlock = audio.Accepted(OverrunSound.WeaponUnlock); inventory.OnWaveStarted(3);
            Require(audio.Accepted(OverrunSound.WeaponUnlock) == unlock + 1, "Weapon unlock cue is missing.");
            int switching = audio.Accepted(OverrunSound.WeaponSwitch); inventory.TryEquip(2);
            Require(audio.Accepted(OverrunSound.WeaponSwitch) == switching + 1, "Equip cue is missing.");
            trigger(false); Cooldown.SetValue(player, 0f); int rifle = audio.Accepted(OverrunSound.RifleShot); trigger(true);
            audio.Advance(.15f, .15f); Cooldown.SetValue(player, 0f); trigger(true);
            Require(audio.Accepted(OverrunSound.RifleShot) == rifle + 2, "Automatic fire audio does not follow actual shots.");
            trigger(false); OverrunBullet.ClearActive(); audio.ResetWorld(); audio.Advance(2, 2);

            root = new GameObject("Audio event fixtures");
            Enemy MakeEnemy(Vector2 offset)
            {
                var actor = UnityEngine.Object.Instantiate(director.enemies[0].prefab, root.transform);
                actor.transform.position = player.transform.position + (Vector3)offset; actor.gameObject.SetActive(true); actor.Init(definition, () => actor.gameObject.SetActive(false)); return actor;
            }
            var first = MakeEnemy(new Vector2(2, 0)); var second = MakeEnemy(new Vector2(2, 1));
            int attack = audio.Accepted(OverrunSound.ZombieAttack); first.GetComponent<OverrunSmallZombieAnimator>().BeginAttack(player, false);
            Require(audio.Accepted(OverrunSound.ZombieAttack) == attack + 1, "Melee windup sound is missing.");
            int hit = audio.Accepted(OverrunSound.ZombieHit); first.TakeDamage(1);
            Require(audio.Accepted(OverrunSound.ZombieHit) == hit + 1, "Nonlethal flesh impact sound is missing.");
            audio.Advance(.2f, .2f); int death = audio.Accepted(OverrunSound.ZombieDeath); first.TakeDamage(1000); first.TakeDamage(1000);
            Require(audio.Accepted(OverrunSound.ZombieDeath) == death + 1, "Death audio was missing or duplicated.");
            first.gameObject.SetActive(true); first.Init(definition, () => first.gameObject.SetActive(false));

            var modules = player.GetComponent<OverrunUpgradeSystem>(); modules.ResetRun();
            var ranks = (int[])typeof(OverrunUpgradeSystem).GetField("ranks", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(modules);
            ranks[1] = 1; audio.Advance(.2f, .2f); int arcs = audio.Accepted(OverrunSound.Arc); modules.OnBulletHit(first, first.transform.position, 2);
            Require(audio.Accepted(OverrunSound.Arc) > arcs, "Chain lightning audio hook is missing.");
            modules.ResetRun(); ranks[2] = 1; audio.Advance(.2f, .2f); int breach = audio.Accepted(OverrunSound.Breach);
            for (int i = 0; i < 4; i++) modules.FireWeapon(player.transform.position, player.transform.position, 2, Vector2.up, player.GetComponentsInChildren<Collider2D>());
            Require(audio.Accepted(OverrunSound.Breach) == breach + 1, "Shrapnel burst audio did not fire once per burst.");
            modules.ResetRun(); ranks[3] = 1; modules.OnWaveStarted(); audio.Advance(.2f, .2f);
            int guard = audio.Accepted(OverrunSound.Guard); float health = player.Health; player.Damage(1);
            Require(audio.Accepted(OverrunSound.Guard) == guard + 1 && player.Health == health, "Shield audio/damage handling failed.");
            Health.SetValue(player, 80f); int playerHit = audio.Accepted(OverrunSound.PlayerHit); player.Damage(1);
            Require(audio.Accepted(OverrunSound.PlayerHit) == playerHit + 1, "Player damage audio is missing.");
            var bandage = OverrunBandagePickup.Spawn(player.transform.position); int heal = audio.Accepted(OverrunSound.BandageHeal);
            Require(bandage.TryCollect(player) && !bandage.TryCollect(player) && audio.Accepted(OverrunSound.BandageHeal) == heal + 1, "Bandage cue was missing or duplicated.");
            int gameOver = audio.Accepted(OverrunSound.GameOver); player.Damage(float.MaxValue);
            Require(audio.Accepted(OverrunSound.GameOver) == gameOver + 1 && audio.LastSource(OverrunSound.GameOver).ignoreListenerPause, "Game-over cue is missing or blocked by listener pause.");
            OverrunMenus.Instance.StartRun();

            var steps = player.GetComponent<OverrunActorAudio>(); audio.Advance(1, 1); steps.SendMessage("LateUpdate");
            int walking = Enumerable.Range(0, (int)OverrunSound.Count).Sum(i => audio.Accepted((OverrunSound)i));
            player.transform.position += Vector3.right * 1.5f; steps.SendMessage("LateUpdate"); steps.SendMessage("LateUpdate");
            Require(Enumerable.Range(0, (int)OverrunSound.Count).Sum(i => audio.Accepted((OverrunSound)i)) == walking, "Movement still emitted audio.");
            player.transform.position = savedPosition; player.GetComponent<Rigidbody2D>().position = savedPosition;
            root.SetActive(false); UnityEngine.Object.Destroy(root); root = null; OverrunBullet.ClearActive();

            director.Begin(); audio.ResetWorld(); audio.Advance(2, 2); int wave = audio.Accepted(OverrunSound.WaveStart);
            director.Advance(director.initialGrace + .1f); Require(audio.Accepted(OverrunSound.WaveStart) == wave + 1, "Wave-start cue is missing.");
            int clear = audio.Accepted(OverrunSound.WaveClear);
            for (int i = 0; i < 100 && director.State != WaveDirector.Phase.Reward; i++)
            {
                audio.Advance(.15f, .15f); director.Advance(2);
                foreach (var enemy in director.GetComponentsInChildren<Enemy>().Where(e => e.IsAlive)) enemy.TakeDamage(10000);
            }
            Require(audio.Accepted(OverrunSound.WaveClear) == clear + 1 && modules.Choosing, "Wave-clear cue is missing.");
            int upgrade = audio.Accepted(OverrunSound.UpgradePick); Require(modules.Choose(0) && audio.Accepted(OverrunSound.UpgradePick) == upgrade + 1, "Upgrade confirmation cue is missing.");
            report.AppendLine("Actual gameplay hooks passed: pistol/AR trigger cadence, equip/unlock, zombie attack/hit/single death, lightning, shrapnel, guard, player hurt/game-over, single bandage collection, silent movement, wave start/clear and upgrade confirmation.");
        }
        finally
        {
            Health.SetValue(player, savedHealth); player.transform.position = savedPosition; player.GetComponent<Rigidbody2D>().position = savedPosition;
            if (root) { root.SetActive(false); UnityEngine.Object.Destroy(root); }
            UnityEngine.Object.Destroy(definition); OverrunBandagePickup.ClearActive();
            InputSystem.QueueStateEvent(mouse, new MouseState()); InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
            InputSystem.RemoveDevice(mouse); InputSystem.RemoveDevice(keyboard);
            if (previousMouse != null && previousMouse.added) previousMouse.MakeCurrent(); if (previousKeyboard != null && previousKeyboard.added) previousKeyboard.MakeCurrent();
        }
    }
    static void PressMenu(Key key)
    {
        var previous = Keyboard.current; var keyboard = InputSystem.AddDevice<Keyboard>("Audio menu keyboard");
        try
        {
            keyboard.MakeCurrent(); InputSystem.QueueStateEvent(keyboard, new KeyboardState(key)); InputSystem.Update(); OverrunMenus.Instance.SendMessage("Update");
            InputSystem.QueueStateEvent(keyboard, new KeyboardState()); InputSystem.Update();
        }
        finally { InputSystem.RemoveDevice(keyboard); if (previous != null && previous.added) previous.MakeCurrent(); }
    }
    static void ExportPreview()
    {
        var samples = new List<float>();
        for (int i = 0; i < (int)OverrunSound.Count; i++)
        {
            var clip = OverrunAudioBank.Get((OverrunSound)i); var pcm = new float[clip.samples]; clip.GetData(pcm, 0);
            foreach (float sample in pcm) samples.Add(sample * .65f);
            for (int s = 0; s < OverrunAudioBank.SampleRate / 6; s++) samples.Add(0);
        }
        using (var writer = new BinaryWriter(File.Create("Logs/OverrunSoundPreview.wav")))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples.Count * 2); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(OverrunAudioBank.SampleRate);
            writer.Write(OverrunAudioBank.SampleRate * 2); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples.Count * 2);
            foreach (float sample in samples) writer.Write((short)Mathf.RoundToInt(Mathf.Clamp(sample, -1, 1) * short.MaxValue));
        }
    }
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
