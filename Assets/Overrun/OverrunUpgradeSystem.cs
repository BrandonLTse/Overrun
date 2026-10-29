using System.Collections.Generic;
using UnityEngine;

public enum OverrunUpgrade { BankShot, ArcRounds, BreachShot, ShockGuard, Overcharge, BreachPack, Fortify }

public readonly struct OverrunUpgradeChoice
{
    public readonly OverrunUpgrade kind;
    public readonly int rank;
    public OverrunUpgradeChoice(OverrunUpgrade kind, int rank) { this.kind = kind; this.rank = rank; }
    public string Title => OverrunUpgradeSystem.Title(kind) + (rank > 0 ? "  " + new[] { "", "I", "II", "III" }[rank] : "");
    public string Description => OverrunUpgradeSystem.Description(kind, rank);
}

/// Four build-changing modules, each with three tiers; late runs also offer
/// one-wave tactical supplies so every clear always provides three valid cards.
public sealed class OverrunUpgradeSystem : MonoBehaviour
{
    readonly int[] ranks = new int[4];
    readonly List<OverrunUpgradeChoice> offers = new List<OverrunUpgradeChoice>(3);
    readonly List<Enemy> targets = new List<Enemy>();
    readonly List<Enemy> arcVisited = new List<Enemy>(6);
    WaveDirector director;
    int triggerCount;
    OverrunUpgrade? nextSupply, activeSupply;
    public bool Choosing { get; private set; }
    public int GuardCharges { get; private set; }
    public OverrunUpgrade? ActiveSupply => activeSupply;
    public OverrunUpgrade? PendingSupply => nextSupply;
    public IReadOnlyList<OverrunUpgradeChoice> Choices => offers;
    public int Rank(OverrunUpgrade kind) => (int)kind < 4 ? ranks[(int)kind] : 0;
    public int Ricochets => Rank(OverrunUpgrade.BankShot) == 0 ? 0 : Rank(OverrunUpgrade.BankShot) == 1 ? 1 : 2;

    public void ResetRun()
    {
        System.Array.Clear(ranks, 0, ranks.Length); offers.Clear();
        Choosing = false; GuardCharges = triggerCount = 0; nextSupply = activeSupply = null; director = null;
    }

    public void BeginChoice(WaveDirector source)
    {
        if (Choosing || !source || source.State != WaveDirector.Phase.Reward) return;
        director = source; Choosing = true; offers.Clear();
        var eligible = new List<OverrunUpgrade>();
        for (int i = 0; i < 4; i++) if (ranks[i] < 3) eligible.Add((OverrunUpgrade)i);
        if (eligible.Count < 3)
        {
            if (Rank(OverrunUpgrade.ArcRounds) > 0) eligible.Add(OverrunUpgrade.Overcharge);
            if (Rank(OverrunUpgrade.BreachShot) > 0) eligible.Add(OverrunUpgrade.BreachPack);
            eligible.Add(OverrunUpgrade.Fortify);
        }
        for (int i = eligible.Count - 1; i > 0; i--) { int j = Random.Range(0, i + 1); (eligible[i], eligible[j]) = (eligible[j], eligible[i]); }
        for (int i = 0; i < 3; i++) offers.Add(new OverrunUpgradeChoice(eligible[i], (int)eligible[i] < 4 ? Rank(eligible[i]) + 1 : 0));
    }

    public bool Choose(int index)
    {
        if (OverrunMenus.GameplayBlocked || !Choosing || !director || director.State != WaveDirector.Phase.Reward || index < 0 || index >= offers.Count) return false;
        var choice = offers[index];
        if ((int)choice.kind < 4) ranks[(int)choice.kind] = choice.rank; else nextSupply = choice.kind;
        Choosing = false;
        GetComponent<OverrunPlayer>().RewardChosen();
        OverrunAudio.UI(OverrunSound.UpgradePick);
        director.AcceptUpgrade();
        return true;
    }

    public void OnWaveStarted()
    {
        activeSupply = nextSupply; nextSupply = null;
        GuardCharges = Rank(OverrunUpgrade.ShockGuard) == 0 ? 0 : Rank(OverrunUpgrade.ShockGuard) == 1 ? 1 : 2;
        if (activeSupply == OverrunUpgrade.Fortify) GuardCharges += 2;
    }

    public Vector2 FireWeapon(Vector2 origin, Vector2 muzzle, float damage, Vector2 direction, Collider2D[] owner)
    {
        triggerCount++;
        Vector2 flash = OverrunBullet.Fire(origin, muzzle, damage, direction, owner, this);
        int rank = Rank(OverrunUpgrade.BreachShot);
        int interval = rank >= 2 ? 3 : 4;
        if (rank > 0 && (activeSupply == OverrunUpgrade.BreachPack || triggerCount % interval == 0))
        {
            OverrunAudio.World(OverrunSound.Breach, muzzle, .8f);
            int pellets = rank == 1 ? 2 : 4;
            for (int i = 0; i < pellets; i++)
            {
                float angle = (i - (pellets - 1) * .5f) * 14;
                Vector2 spread = Quaternion.Euler(0, 0, angle) * direction;
                OverrunBullet.Fire(origin, muzzle, damage * .7f, spread, owner, this, rank == 3 ? 1 : 0, 8, false);
            }
        }
        return flash;
    }

    public void OnBulletHit(Enemy first, Vector2 position, float damage)
    {
        int rank = Rank(OverrunUpgrade.ArcRounds);
        if (rank == 0 || Choosing) return;
        int hops = rank + (activeSupply == OverrunUpgrade.Overcharge ? 2 : 0);
        arcVisited.Clear(); arcVisited.Add(first);
        for (int i = 0; i < hops; i++)
        {
            Enemy next = Nearest(position, 4.5f, arcVisited);
            if (!next) break;
            Vector2 end = next.transform.position;
            OverrunUpgradeFx.Arc(position, end);
            OverrunAudio.World(OverrunSound.Arc, end, .8f);
            arcVisited.Add(next);
            next.TakeDamage(damage * .6f, (end - position).normalized);
            if (rank == 3 && next.IsAlive) next.Stagger(.25f);
            position = end;
        }
    }

    public Vector2 AimRicochet(Vector2 position, Vector2 reflected)
    {
        if (Rank(OverrunUpgrade.BankShot) < 3) return reflected;
        var target = Nearest(position, 8, null);
        return target ? ((Vector2)target.transform.position - position).normalized : reflected;
    }

    Enemy Nearest(Vector2 position, float radius, List<Enemy> excluded)
    {
        Enemy.CopyActiveTo(targets);
        Enemy best = null; float distance = radius * radius;
        foreach (var enemy in targets)
        {
            if (!enemy || !enemy.IsAlive || (excluded != null && excluded.Contains(enemy))) continue;
            float squared = ((Vector2)enemy.transform.position - position).sqrMagnitude;
            if (squared >= distance || Physics2D.Linecast(position, enemy.transform.position, LayerMask.GetMask("Walls"))) continue;
            best = enemy; distance = squared;
        }
        return best;
    }

    public bool AbsorbHit()
    {
        if (GuardCharges <= 0 || Choosing) return false;
        GuardCharges--;
        int rank = Rank(OverrunUpgrade.ShockGuard);
        float radius = rank >= 2 ? 4 : 3;
        Vector2 origin = transform.position;
        OverrunUpgradeFx.Ring(origin, radius);
        OverrunAudio.World(OverrunSound.Guard, origin);
        Enemy.CopyActiveTo(targets);
        foreach (var enemy in targets)
        {
            if (!enemy || !enemy.IsAlive || ((Vector2)enemy.transform.position - origin).sqrMagnitude > radius * radius ||
                Physics2D.Linecast(origin, enemy.transform.position, LayerMask.GetMask("Walls"))) continue;
            Vector2 direction = ((Vector2)enemy.transform.position - origin).normalized;
            enemy.TakeDamage(3, direction);
            if (enemy.IsAlive) { enemy.Push(direction, 8); if (rank == 3) enemy.Stagger(.7f); }
        }
        return true;
    }

    public static string Title(OverrunUpgrade kind)
    {
        switch (kind)
        {
            case OverrunUpgrade.BankShot: return "BANK SHOT";
            case OverrunUpgrade.ArcRounds: return "ARC ROUNDS";
            case OverrunUpgrade.BreachShot: return "BREACH SHOT";
            case OverrunUpgrade.ShockGuard: return "SHOCK GUARD";
            case OverrunUpgrade.Overcharge: return "OVERCHARGE";
            case OverrunUpgrade.BreachPack: return "BREACH PACK";
            default: return "FORTIFY";
        }
    }

    public static string Description(OverrunUpgrade kind, int rank)
    {
        switch (kind)
        {
            case OverrunUpgrade.BankShot: return rank == 1 ? "Shots bounce once off solid walls and cover. Turn a corridor into a firing angle." : rank == 2 ? "Shots bounce twice, reaching enemies around corners." : "After a bounce, shots steer toward a nearby enemy they can see.";
            case OverrunUpgrade.ArcRounds: return rank == 1 ? "Direct hits arc to one nearby zombie. Lightning cannot pass through cover." : rank == 2 ? "Lightning jumps to two additional zombies." : "Lightning jumps to three zombies and briefly interrupts them.";
            case OverrunUpgrade.BreachShot: return rank == 1 ? "Every fourth shot adds two short-range shrapnel pellets. Best when a pack gets close." : rank == 2 ? "Every third shot adds four shrapnel pellets." : "Shrapnel pierces through its first zombie to hit another behind it.";
            case OverrunUpgrade.ShockGuard: return rank == 1 ? "Block one hit each wave and blast nearby zombies backward." : rank == 2 ? "Block two hits each wave. The defensive blast reaches farther." : "Your defensive blast also stuns nearby zombies.";
            case OverrunUpgrade.Overcharge: return "NEXT WAVE ONLY: lightning gets two extra jumps. All permanent modules are kept.";
            case OverrunUpgrade.BreachPack: return "NEXT WAVE ONLY: every shot triggers your shrapnel burst. All permanent modules are kept.";
            default: return "NEXT WAVE ONLY: gain two extra shock-guard charges. All permanent modules are kept.";
        }
    }
}
