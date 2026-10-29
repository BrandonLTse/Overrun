using System;
using System.Collections.Generic;
using UnityEngine;

public enum OverrunSound
{
    PistolShot, RifleShot, PistolEmpty, PistolReload, PistolReady, WeaponSwitch, WeaponUnlock, PlayerHit, GameOver,
    ZombieGroan, ZombieAttack, ZombieHit, ZombieDeath, WallImpact, Ricochet,
    BandageDrop, BandageHeal, WaveStart, WaveClear, UpgradePick, Arc, Breach, Guard,
    MenuMove, MenuAccept, MenuBack, Pause, Resume, RunStart, RifleEmpty, RifleReload, RifleReady, Count
}

/// Original PCM effects: self-contained, deterministic, and free of external audio assets.
/// This RNG is deliberately separate from Unity's gameplay/drop/spawn RNG.
public static class OverrunAudioBank
{
    public const int SampleRate = 22050;
    static readonly Dictionary<int, AudioClip> clips = new Dictionary<int, AudioClip>();
    public static bool Ready => clips.TryGetValue(0, out var first) && first;
    public static int Variants(OverrunSound cue) => cue == OverrunSound.ZombieGroan ||
        cue == OverrunSound.ZombieHit || cue == OverrunSound.ZombieDeath || cue == OverrunSound.PistolShot || cue == OverrunSound.RifleShot ? 3 : 1;
    public static AudioClip Get(OverrunSound cue, int variant = 0)
    {
        variant = Mathf.Abs(variant) % Variants(cue); int key = (int)cue * 4 + variant;
        if (clips.TryGetValue(key, out var clip) && clip) return clip;
        var data = Synthesize(cue, variant);
        clip = AudioClip.Create("Overrun original / " + cue + " / " + variant, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0); clips[key] = clip; return clip;
    }
    public static void Prewarm()
    {
        for (int i = 0; i < (int)OverrunSound.Count; i++) for (int v = 0; v < Variants((OverrunSound)i); v++) Get((OverrunSound)i, v);
    }
    public static void Dispose()
    {
        foreach (var clip in clips.Values) if (clip) UnityEngine.Object.Destroy(clip);
        clips.Clear();
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { Dispose(); }

    static float Duration(OverrunSound cue)
    {
        switch (cue)
        {
            case OverrunSound.ZombieGroan: return .85f;
            case OverrunSound.ZombieDeath: return .6f;
            case OverrunSound.ZombieAttack: return .28f;
            case OverrunSound.WaveStart: case OverrunSound.WaveClear: case OverrunSound.RunStart: return .65f;
            case OverrunSound.WeaponUnlock: case OverrunSound.GameOver: case OverrunSound.BandageHeal: case OverrunSound.UpgradePick: return .48f;
            case OverrunSound.PistolShot: case OverrunSound.Guard: return .24f;
            case OverrunSound.MenuMove: return .055f;
            default: return .15f;
        }
    }
    static float Sin(float phase) => Mathf.Sin(phase * Mathf.PI * 2);
    static float Decay(float t, float start, float speed) => t < start ? 0 : Mathf.Exp(-(t - start) * speed);
    public static float[] Synthesize(OverrunSound cue, int variant)
    {
        float duration = Duration(cue); var data = new float[Mathf.CeilToInt(duration * SampleRate)];
        var noise = new System.Random(18457 + (int)cue * 977 + variant * 131);
        float low = 0, phase = 0, sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate, u = t / duration;
            float n = (float)(noise.NextDouble() * 2 - 1); low += (n - low) * .14f;
            float value;
            switch (cue)
            {
                case OverrunSound.PistolShot: case OverrunSound.RifleShot: case OverrunSound.Breach:
                    float bass = cue == OverrunSound.PistolShot ? 145 : cue == OverrunSound.Breach ? 90 : 190;
                    phase += Mathf.Lerp(bass, 48, Mathf.Min(1, t * 10)) / SampleRate;
                    value = n * .8f * Mathf.Exp(-t * 90) + low * 1.1f * Mathf.Exp(-t * 25) + Sin(phase) * .55f * Mathf.Exp(-t * 32);
                    break;
                case OverrunSound.ZombieGroan:
                    phase += (65 + variant * 13 + Sin(t * 5) * 6 - u * 13) / SampleRate;
                    value = (Sin(phase) * .6f + Sin(phase * 2.03f) * .23f + low * .9f) * Mathf.Sin(Mathf.PI * u) * (.75f + .25f * Sin(t * 18)); break;
                case OverrunSound.ZombieAttack: case OverrunSound.ZombieDeath:
                    phase += Mathf.Lerp(cue == OverrunSound.ZombieDeath ? 105 : 140, 42 + variant * 6, u) / SampleRate;
                    value = (Sin(phase) * .5f + low * 1.2f + Sin(phase * 2) * .15f) * Mathf.Sin(Mathf.PI * u) * Mathf.Exp(-u * 2); break;
                case OverrunSound.ZombieHit: case OverrunSound.PlayerHit:
                    value = (low * 1.3f + Sin(t * (cue == OverrunSound.PlayerHit ? 125 : 78 + variant * 7)) * .45f) * Mathf.Exp(-t * 28); break;
                case OverrunSound.WallImpact:
                    value = (n * .7f + Sin(t * 510) * .2f + low * .4f) * Mathf.Exp(-t * 50); break;
                case OverrunSound.Ricochet:
                    phase += Mathf.Lerp(2300, 650, u) / SampleRate;
                    value = (Sin(phase) * .6f + n * .15f) * Mathf.Exp(-t * 28); break;
                case OverrunSound.PistolEmpty: case OverrunSound.RifleEmpty:
                    value = (n * .3f + Sin(t * 1100) * .15f) * Mathf.Exp(-t * 95); break;
                case OverrunSound.PistolReload: case OverrunSound.PistolReady: case OverrunSound.RifleReload: case OverrunSound.RifleReady: case OverrunSound.WeaponSwitch:
                    value = (n * .5f + Sin(t * 870) * .2f) * (Decay(t, 0, 85) + Decay(t, .065f, 80)); break;
                case OverrunSound.BandageDrop:
                    value = low * Mathf.Exp(-t * 32) + n * .12f * Mathf.Sin(Mathf.PI * u); break;
                case OverrunSound.Arc:
                    value = (Sin(t * 1250 + Sin(t * 60) * .1f) * .4f + n * .65f) * Mathf.Exp(-t * 22); break;
                case OverrunSound.Guard:
                    phase += Mathf.Lerp(460, 70, u) / SampleRate;
                    value = (Sin(phase) * .7f + low * .6f) * Mathf.Sin(Mathf.PI * u) * Mathf.Exp(-u * 2); break;
                case OverrunSound.WaveStart:
                    value = Sin(t * 110) * Decay(t, 0, 15) + Sin(t * 165) * Decay(t, .2f, 15) + Sin(t * 220) * Decay(t, .4f, 18); break;
                case OverrunSound.WaveClear: case OverrunSound.UpgradePick: case OverrunSound.WeaponUnlock:
                case OverrunSound.RunStart: case OverrunSound.GameOver: case OverrunSound.BandageHeal:
                    int note = Mathf.Min(3, (int)(t / .12f));
                    float hz = note == 0 ? 440 : note == 1 ? 554.37f : note == 2 ? 659.25f : 880;
                    if (cue == OverrunSound.GameOver) hz = note == 0 ? 330 : note == 1 ? 261.63f : note == 2 ? 196 : 130.81f;
                    value = (Sin(t * hz) + .18f * Sin(t * hz * 2)) * Decay(t, note * .12f, 17);
                    if (cue == OverrunSound.BandageHeal) value = value * .45f + low * Mathf.Exp(-t * 30);
                    break;
                case OverrunSound.MenuMove:
                    value = Sin(t * 760) * Mathf.Sin(Mathf.PI * u); break;
                case OverrunSound.MenuAccept:
                    value = (Sin(t * 660) + .35f * Sin(t * 990)) * Mathf.Exp(-t * 24); break;
                case OverrunSound.MenuBack: case OverrunSound.Pause: case OverrunSound.Resume:
                    phase += Mathf.Lerp(cue == OverrunSound.Resume ? 260 : 620, cue == OverrunSound.Resume ? 620 : 260, u) / SampleRate;
                    value = Sin(phase) * Mathf.Sin(Mathf.PI * u) * .6f; break;
                default: throw new ArgumentOutOfRangeException(nameof(cue), cue, "Unknown sound effect.");
            }
            data[i] = value; sum += value;
        }
        // Remove DC, apply click-free boundaries, and cap every clip at .7 peak.
        float mean = sum / data.Length, peak = 0;
        for (int i = 0; i < data.Length; i++)
        {
            float fade = Mathf.Min(1, Mathf.Min(i, data.Length - 1 - i) / (SampleRate * .004f));
            data[i] = (data[i] - mean) * fade; peak = Mathf.Max(peak, Mathf.Abs(data[i]));
        }
        if (peak > 0) for (int i = 0; i < data.Length; i++) data[i] *= .7f / peak;
        return data;
    }
}
