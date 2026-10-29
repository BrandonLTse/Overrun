using UnityEngine;

/// Bounded 2D audio mixing: positional world effects and pause-safe UI.
public sealed class OverrunAudio : MonoBehaviour
{
    public const int WorldCapacity = 24, UICapacity = 4;
    const string MasterKey = "Overrun.Audio.Master", EffectsKey = "Overrun.Audio.Effects";
    public static OverrunAudio Instance { get; private set; }
    public float Master { get; private set; }
    public float Effects { get; private set; }
    public int ActiveWorld => Count(false);
    public int ActiveUI => Count(true);
    public bool Ready { get; private set; }
    sealed class Voice
    {
        public AudioSource source;
        public OverrunSound cue;
        public Vector2 position;
        public float remaining, gain, pitch, mixedGain;
        public bool ui;
    }
    Voice[] voices;
    Transform listener;
    double uiTime, worldTime;
    readonly double[] nextUI = new double[(int)OverrunSound.Count], nextWorld = new double[(int)OverrunSound.Count];
    readonly int[] accepted = new int[(int)OverrunSound.Count];
    readonly System.Random variation = new System.Random(41957);
    bool savePreferences;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatic() { Instance = null; }
    public static OverrunAudio Ensure(Transform target = null)
    {
        if (!Instance) Instance = FindAnyObjectByType<OverrunAudio>();
        if (!Instance) Instance = new GameObject("Overrun Audio").AddComponent<OverrunAudio>();
        if (!Instance.Ready || !OverrunAudioBank.Ready || Instance.voices == null || Instance.voices.Length != WorldCapacity + UICapacity || Instance.voices[0] == null || !Instance.voices[0].source) Instance.Initialize();
        if (target) Instance.listener = target;
        return Instance;
    }
    void Initialize()
    {
        Instance = this;
        foreach (Transform child in transform) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
        Master = Clean(PlayerPrefs.GetFloat(MasterKey, .8f)); Effects = Clean(PlayerPrefs.GetFloat(EffectsKey, .85f));
        OverrunAudioBank.Prewarm(); voices = new Voice[WorldCapacity + UICapacity];
        for (int i = 0; i < voices.Length; i++)
        {
            bool ui = i >= WorldCapacity; var source = NewSource((ui ? "UI voice " : "World voice ") + i, ui);
            voices[i] = new Voice { source = source, ui = ui };
        }
        Ready = true; ApplyMix();
    }
    AudioSource NewSource(string name, bool ui)
    {
        var root = new GameObject(name); root.transform.SetParent(transform, false);
        var source = root.AddComponent<AudioSource>(); source.playOnAwake = false; source.spatialBlend = 0;
        source.ignoreListenerPause = ui; source.dopplerLevel = 0; source.priority = ui ? 32 : 96; return source;
    }
    public static bool World(OverrunSound cue, Vector2 position, float gain = 1, float pitch = 1)
    {
        if (OverrunMenus.GameplayBlocked) return false;
        return Ensure().TryPlay(cue, position, false, gain, pitch);
    }
    public static bool UI(OverrunSound cue) => Ensure().TryPlay(cue, Vector2.zero, true, 1, 1);
    public int Accepted(OverrunSound cue) => accepted[(int)cue];
    public AudioSource LastSource(OverrunSound cue)
    {
        if (voices == null) return null;
        for (int i = voices.Length - 1; i >= 0; i--) if (voices[i].remaining > 0 && voices[i].cue == cue) return voices[i].source;
        return null;
    }
    static float Clean(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);
    public void SetVolumes(float master, float effects, bool persist = true)
    {
        Master = Clean(master); Effects = Clean(effects); ApplyMix();
        if (persist)
        {
            PlayerPrefs.SetFloat(MasterKey, Master); PlayerPrefs.SetFloat(EffectsKey, Effects); savePreferences = true;
        }
    }
    static float Cooldown(OverrunSound cue)
    {
        switch (cue)
        {
            case OverrunSound.MenuMove: return .065f;
            case OverrunSound.ZombieGroan: return .3f;
            case OverrunSound.ZombieHit: case OverrunSound.ZombieDeath: case OverrunSound.ZombieAttack: return .065f;
            case OverrunSound.Arc: case OverrunSound.WallImpact: case OverrunSound.Ricochet: return .055f;
            default: return .035f;
        }
    }
    static int Limit(OverrunSound cue) => cue == OverrunSound.ZombieGroan ? 2 : 4;
    static int Priority(OverrunSound cue) => cue == OverrunSound.GameOver ? 20 : cue == OverrunSound.Pause || cue == OverrunSound.Resume || cue == OverrunSound.MenuBack || cue == OverrunSound.MenuAccept ? 30 :
        cue == OverrunSound.RunStart || cue == OverrunSound.UpgradePick || cue == OverrunSound.WeaponUnlock ? 40 : cue == OverrunSound.MenuMove ? 80 :
        cue == OverrunSound.ZombieGroan ? 180 :
        cue == OverrunSound.WaveStart || cue == OverrunSound.WaveClear ? 60 : cue == OverrunSound.PistolShot || cue == OverrunSound.RifleShot ? 65 : 95;
    public bool TryPlay(OverrunSound cue, Vector2 position, bool ui, float gain = 1, float pitch = 1)
    {
        if (!Ready || cue < 0 || cue >= OverrunSound.Count || (!ui && OverrunMenus.GameplayBlocked)) return false;
        var cooldowns = ui ? nextUI : nextWorld; double clock = ui ? uiTime : worldTime;
        if (clock < cooldowns[(int)cue]) return false;
        Vector2 ear = listener ? (Vector2)listener.position : Camera.main ? (Vector2)Camera.main.transform.position : Vector2.zero;
        if (!ui && (position - ear).sqrMagnitude > 22 * 22) return false;
        Voice free = null, lowest = null; int same = 0;
        foreach (var voice in voices)
        {
            if (voice.ui != ui) continue;
            if (voice.remaining <= 0) { if (free == null) free = voice; continue; }
            if (voice.cue == cue) same++;
            if (lowest == null || Priority(voice.cue) > Priority(lowest.cue)) lowest = voice;
        }
        if (same >= Limit(cue)) return false;
        if (free == null && lowest != null && Priority(lowest.cue) > Priority(cue)) free = lowest;
        if (free == null) return false;
        free.source.Stop(); free.cue = cue; free.position = position;
        free.gain = Mathf.Clamp(gain, 0, 1) * (cue == OverrunSound.ZombieGroan ? .3f : .5f);
        free.pitch = Mathf.Clamp(pitch * (float)(.96 + variation.NextDouble() * .08), .6f, 1.5f);
        free.source.clip = OverrunAudioBank.Get(cue, variation.Next(OverrunAudioBank.Variants(cue)));
        free.source.pitch = free.pitch; free.source.priority = ui ? 32 : Priority(cue);
        free.remaining = free.source.clip.length / free.pitch; cooldowns[(int)cue] = clock + Cooldown(cue); accepted[(int)cue]++;
        ApplyMix(); free.source.Play(); return true;
    }
    void Update() { Advance(Time.unscaledDeltaTime, AudioListener.pause || OverrunMenus.GameplayBlocked ? 0 : Time.deltaTime); }
    public void Advance(float unscaledDelta, float worldDelta)
    {
        if (!Ready) return;
        uiTime += Mathf.Max(0, unscaledDelta); worldTime += Mathf.Max(0, worldDelta);
        foreach (var voice in voices)
        {
            if (voice.remaining <= 0) continue;
            voice.remaining -= Mathf.Max(0, voice.ui ? unscaledDelta : worldDelta);
            if (voice.remaining <= 0) { voice.source.Stop(); voice.remaining = 0; }
        }
        ApplyMix();
    }
    void ApplyMix()
    {
        if (voices == null) return;
        float worldSum = 0, uiSum = 0;
        Vector2 ear = listener ? (Vector2)listener.position : Camera.main ? (Vector2)Camera.main.transform.position : Vector2.zero;
        foreach (var voice in voices)
        {
            voice.mixedGain = 0; if (voice.remaining <= 0) continue;
            float distance = Vector2.Distance(ear, voice.position);
            voice.mixedGain = voice.gain * (voice.ui ? 1 : Mathf.Clamp01(1 - distance / 22) / (1 + distance * distance / 90));
            if (voice.ui) uiSum += voice.mixedGain; else worldSum += voice.mixedGain;
            voice.source.panStereo = voice.ui ? 0 : Mathf.Clamp((voice.position.x - ear.x) / 14, -.7f, .7f);
        }
        foreach (var voice in voices)
            voice.source.volume = voice.mixedGain * Master * Effects / Mathf.Max(1, (voice.ui ? uiSum / .3f : worldSum));
    }
    int Count(bool ui)
    {
        int count = 0; if (voices != null) foreach (var voice in voices) if (voice.ui == ui && voice.remaining > 0) count++; return count;
    }
    public void ResetWorld()
    {
        if (!Ready) return;
        foreach (var voice in voices) if (!voice.ui) { voice.source.Stop(); voice.remaining = 0; }
        System.Array.Clear(nextWorld, 0, nextWorld.Length); worldTime = 0;
        ApplyMix();
    }
    void OnDisable()
    {
        if (voices != null) foreach (var voice in voices) { if (voice.source) voice.source.Stop(); voice.remaining = 0; }
    }
    void OnDestroy()
    {
        if (Instance != this) return;
        if (savePreferences) PlayerPrefs.Save();
        Instance = null; OverrunAudioBank.Dispose();
    }
}
