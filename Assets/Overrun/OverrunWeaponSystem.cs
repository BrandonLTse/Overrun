using UnityEngine;

public enum OverrunWeapon { Pistol, AssaultRifle }

/// Run-local inventory. Number keys equip weapons only outside reward drafts.
public sealed class OverrunWeaponSystem : MonoBehaviour
{
    public const int RifleUnlockWave = 3;
    public const int PistolMagazineSize = 12;
    public const int RifleMagazineSize = 30;
    public const float PistolReloadDuration = 11f / OverrunPistolAnimator.ReloadFPS;
    public const float RifleReloadDuration = 8f / OverrunRifleAnimator.ReloadFPS;
    public OverrunWeapon Equipped { get; private set; } = OverrunWeapon.Pistol;
    public bool RifleUnlocked { get; private set; }
    public bool Automatic => Equipped == OverrunWeapon.AssaultRifle;
    public float DamageMultiplier => Automatic ? 1 : 1.5f;
    public float IntervalMultiplier => Automatic ? .75f : 2;
    public string WeaponName => Automatic ? "AR / AUTO" : "PISTOL / SINGLE";
    public bool ShowingUnlockNotice => notice > 0;
    public int PistolRounds { get; private set; } = PistolMagazineSize;
    public int RifleRounds { get; private set; } = RifleMagazineSize;
    public int CurrentRounds => Automatic ? RifleRounds : PistolRounds;
    public int CurrentMagazineSize => Automatic ? RifleMagazineSize : PistolMagazineSize;
    public bool IsReloading => reloadRemaining > 0;
    public float ReloadSecondsRemaining => reloadRemaining;
    public string AmmoLabel => IsReloading ? "RELOADING…" : $"{CurrentRounds} / {CurrentMagazineSize}";
    float notice, reloadRemaining;

    void Update() { if (!OverrunMenus.GameplayBlocked) Advance(Time.deltaTime); }
    public void Advance(float delta)
    {
        if (OverrunMenus.GameplayBlocked || delta <= 0) return;
        notice = Mathf.Max(0, notice - delta);
        if (!IsReloading) return;
        reloadRemaining = Mathf.Max(0, reloadRemaining - delta);
        if (reloadRemaining > 0) return;
        if (Automatic) RifleRounds = RifleMagazineSize; else PistolRounds = PistolMagazineSize;
        GetComponentInChildren<OverrunWeaponFeedback>(true)?.EndReload();
        OverrunAudio.World(Automatic ? OverrunSound.RifleReady : OverrunSound.PistolReady, transform.position);
    }
    public void ResetRun()
    {
        RifleUnlocked = false; Equipped = OverrunWeapon.Pistol; notice = reloadRemaining = 0; PistolRounds = PistolMagazineSize; RifleRounds = RifleMagazineSize; ApplyVisual();
    }
    public void OnWaveStarted(int wave)
    {
        if (RifleUnlocked || wave < RifleUnlockWave) return;
        RifleUnlocked = true; notice = 5;
        OverrunAudio.UI(OverrunSound.WeaponUnlock);
    }
    public bool TryEquip(int slot)
    {
        var player = GetComponent<OverrunPlayer>();
        if (OverrunMenus.GameplayBlocked || !player || player.ChoosingUpgrade || (slot != 1 && slot != 2) || (slot == 2 && !RifleUnlocked)) return false;
        var next = slot == 1 ? OverrunWeapon.Pistol : OverrunWeapon.AssaultRifle;
        if (next == Equipped) return true;
        CancelReload(); Equipped = next; ApplyVisual(); player.WeaponChanged(); OverrunAudio.World(OverrunSound.WeaponSwitch, transform.position); return true;
    }
    public bool TryConsumeShot()
    {
        if (OverrunMenus.GameplayBlocked || IsReloading || GetComponent<OverrunPlayer>().ChoosingUpgrade) return false;
        if (CurrentRounds <= 0) { OverrunAudio.World(Automatic ? OverrunSound.RifleEmpty : OverrunSound.PistolEmpty, transform.position, .6f); return false; }
        if (Automatic) RifleRounds--; else PistolRounds--;
        return true;
    }
    public bool TryReload()
    {
        if (OverrunMenus.GameplayBlocked || IsReloading || CurrentRounds == CurrentMagazineSize || GetComponent<OverrunPlayer>().ChoosingUpgrade) return false;
        reloadRemaining = Automatic ? RifleReloadDuration : PistolReloadDuration;
        GetComponentInChildren<OverrunWeaponFeedback>(true)?.BeginReload();
        OverrunAudio.World(Automatic ? OverrunSound.RifleReload : OverrunSound.PistolReload, transform.position); return true;
    }
    public void CancelReload()
    {
        if (!IsReloading) return;
        reloadRemaining = 0; GetComponentInChildren<OverrunWeaponFeedback>(true)?.EndReload();
    }
    void ApplyVisual()
    {
        var visual = GetComponentInChildren<OverrunWeaponFeedback>(true);
        if (visual) visual.Equip(Equipped);
    }
}

public static class OverrunPickupArt
{
    public const string Folder = "OverrunArt/Pickables/";
    static Sprite bandage, pistol, rifle;
    public static Sprite Bandage => bandage ? bandage : bandage = Load("Bandage");
    public static Sprite Pistol => pistol ? pistol : pistol = Load("Pistol");
    public static Sprite Rifle => rifle ? rifle : rifle = Load("Gun");
    static Sprite Load(string name)
    {
        var sprite = Resources.Load<Sprite>(Folder + name);
        if (!sprite) throw new System.InvalidOperationException("Missing supplied pickup sprite: " + Folder + name);
        return sprite;
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { bandage = pistol = rifle = null; }
}
