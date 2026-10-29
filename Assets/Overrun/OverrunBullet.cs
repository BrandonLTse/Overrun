using System.Collections.Generic;
using UnityEngine;

/// Fast pooled projectile with swept collision and a two-layer light tracer.
/// The ray follows the entire movement segment, including a low-FPS long step.
public sealed class OverrunBullet : MonoBehaviour
{
    public const float Speed = 84f, Range = 24f, TracerLength = 2.3f, FadeTime = .065f;
    static readonly Queue<OverrunBullet> pool = new Queue<OverrunBullet>();
    static readonly HashSet<OverrunBullet> active = new HashSet<OverrunBullet>();
    readonly List<Enemy> struck = new List<Enemy>();
    readonly RaycastHit2D[] hits = new RaycastHit2D[64];
    LineRenderer glow, core;
    Collider2D[] owner;
    Vector2 direction;
    float damage, traveled, segmentTravel, fade, maxRange;
    int ricochets, pierces;
    OverrunUpgradeSystem upgrades;
    bool canProc;
    bool ending;
    public bool InFlight => !ending && gameObject.activeSelf;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool() { pool.Clear(); active.Clear(); }
    void OnDisable() { active.Remove(this); }
    public static void ClearActive() { foreach (var shot in new List<OverrunBullet>(active)) if (shot) shot.Recycle(); }

    public static Vector2 Fire(Vector2 origin, Vector2 muzzle, float damage, Vector2 direction, Collider2D[] owner,
        OverrunUpgradeSystem upgrades = null, int pierces = 0, float range = Range, bool canProc = true)
    {
        OverrunBullet shot = null;
        while (pool.Count > 0 && !shot) shot = pool.Dequeue();
        if (!shot) shot = new GameObject("Light Tracer").AddComponent<OverrunBullet>();
        shot.transform.position = muzzle;
        shot.Init(damage, direction, owner, upgrades, pierces, range, canProc);
        // The gun can visually touch a wall. Check the body-to-muzzle segment so
        // a shot cannot appear on the other side of point-blank cover.
        Vector2 toMuzzle = muzzle - origin;
        if (shot.FindHit(origin, toMuzzle.normalized, toMuzzle.magnitude, out var hit))
        {
            shot.transform.position = hit.point;
            shot.Hit(hit);
            return hit.point;
        }
        return muzzle;
    }

    public void Init(float damage, Vector2 direction, Collider2D[] ownerColliders = null,
        OverrunUpgradeSystem upgrades = null, int pierces = 0, float range = Range, bool canProc = true)
    {
        this.damage = damage;
        this.direction = direction.sqrMagnitude > .001f ? direction.normalized : Vector2.up;
        owner = ownerColliders;
        traveled = segmentTravel = fade = 0;
        this.upgrades = upgrades; this.pierces = pierces; this.canProc = canProc;
        maxRange = Mathf.Max(.1f, range); ricochets = upgrades ? upgrades.Ricochets : 0; struck.Clear();
        ending = false;
        if (!glow)
        {
            glow = OverrunCombatVfx.Line(transform, "Amber Afterglow", .28f, Color.white, 59);
            core = OverrunCombatVfx.Line(transform, "White Hot Core", .075f, Color.white, 60);
        }
        gameObject.SetActive(true);
        active.Add(this);
        Draw(1);
    }

    void Update() { if (!OverrunMenus.GameplayBlocked) Simulate(Time.deltaTime); }

    // Shared by Update and the explicit Play Mode collision audit.
    public void Simulate(float deltaTime)
    {
        if (!gameObject.activeSelf || deltaTime <= 0) return;
        if (ending)
        {
            fade += deltaTime;
            if (fade >= FadeTime) { Recycle(); return; }
            Draw(1 - fade / FadeTime);
            return;
        }
        float distance = Mathf.Min(Speed * deltaTime, maxRange - traveled);
        for (int step = 0; step < 5 && distance > 0 && !ending && gameObject.activeSelf; step++)
        {
            Vector2 start = transform.position;
            if (FindHit(start, direction, distance, out var hit))
            {
                traveled += hit.distance; segmentTravel += hit.distance;
                transform.position = start + direction * hit.distance;
                distance = Mathf.Max(0, distance - hit.distance - .06f);
                Hit(hit);
            }
            else
            {
                traveled += distance; segmentTravel += distance;
                transform.position = start + direction * distance;
                distance = 0;
            }
        }
        if (traveled >= maxRange) ending = true;
        Draw(1);
    }

    bool FindHit(Vector2 start, Vector2 heading, float distance, out RaycastHit2D nearest)
    {
        nearest = default;
        if (distance <= 0) return false;
        var filter = new ContactFilter2D();
        filter.SetLayerMask(Physics2D.DefaultRaycastLayers);
        filter.useTriggers = false; // XP pickups and other triggers are not bullet cover.
        int count = Physics2D.CircleCast(start, .045f, heading, filter, hits, distance);
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            if (!collider || collider.GetComponentInParent<OverrunPlayer>() || collider.GetComponentInParent<OverrunShootThrough>()) continue;
            var enemy = collider.GetComponentInParent<Enemy>();
            if (enemy && struck.Contains(enemy)) continue;
            bool owned = false;
            if (owner != null) foreach (var item in owner) if (collider == item) { owned = true; break; }
            if (owned || hits[i].distance >= best) continue;
            best = hits[i].distance;
            nearest = hits[i];
        }
        return nearest.collider;
    }

    void Hit(RaycastHit2D hit)
    {
        if (ending) return;
        var enemy = hit.collider.GetComponentInParent<Enemy>();
        OverrunCombatVfx.Impact(hit.point, hit.normal.sqrMagnitude > .001f ? hit.normal : -direction, enemy);
        if (enemy)
        {
            bool penetrate = pierces > 0;
            ending = !penetrate;
            struck.Add(enemy);
            var source = upgrades;
            enemy.TakeDamage(damage, direction);
            if (source && canProc) source.OnBulletHit(enemy, hit.point, damage);
            if (penetrate) { pierces--; transform.position += (Vector3)direction * .06f; }
        }
        else if (ricochets > 0 && hit.normal.sqrMagnitude > .001f)
        {
            OverrunAudio.World(OverrunSound.Ricochet, hit.point);
            ricochets--; segmentTravel = 0;
            transform.position = hit.centroid + hit.normal * .06f;
            direction = Vector2.Reflect(direction, hit.normal).normalized;
            if (upgrades) direction = upgrades.AimRicochet(transform.position, direction);
        }
        else { OverrunAudio.World(OverrunSound.WallImpact, hit.point); ending = true; }
    }

    void Draw(float alpha)
    {
        Vector3 head = transform.position;
        float length = Mathf.Min(TracerLength, segmentTravel);
        glow.SetPosition(0, head - (Vector3)direction * length);
        glow.SetPosition(1, head);
        core.SetPosition(0, head - (Vector3)direction * Mathf.Min(length, .95f));
        core.SetPosition(1, head);
        glow.startColor = new Color(1, .22f, .025f, 0);
        glow.endColor = new Color(1, .48f, .08f, .55f * alpha);
        core.startColor = new Color(1, .65f, .2f, .28f * alpha);
        core.endColor = new Color(2.3f, 1.9f, 1.25f, alpha);
    }

    void Recycle()
    {
        if (!gameObject.activeSelf) return;
        owner = null;
        upgrades = null; struck.Clear();
        gameObject.SetActive(false);
        if (pool.Count < 96) pool.Enqueue(this); else Destroy(gameObject);
    }
}
