using System.Collections.Generic;
using UnityEngine;

/// Occasional healing loot, independent of wave rewards and weapon progression.
public sealed class OverrunBandagePickup : MonoBehaviour
{
    public const float DropChance = .2f, HealAmount = 15, Lifetime = 35;
    static readonly Queue<OverrunBandagePickup> pool = new Queue<OverrunBandagePickup>();
    static readonly HashSet<OverrunBandagePickup> active = new HashSet<OverrunBandagePickup>();
    public static int ActiveCount => active.Count;
    SpriteRenderer visual;
    float age;
    bool collected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool() { pool.Clear(); active.Clear(); }
    public static OverrunBandagePickup TryDrop(Vector2 position)
    {
        if (Random.value >= DropChance) return null;
        var pickup = Spawn(position); OverrunAudio.World(OverrunSound.BandageDrop, position, .6f); return pickup;
    }
    public static OverrunBandagePickup Spawn(Vector2 position)
    {
        OverrunBandagePickup pickup = null;
        while (pool.Count > 0 && !pickup) pickup = pool.Dequeue();
        if (!pickup)
        {
            pickup = new GameObject("Healing Bandage").AddComponent<OverrunBandagePickup>();
            pickup.visual = pickup.gameObject.AddComponent<SpriteRenderer>();
            pickup.visual.sprite = OverrunPickupArt.Bandage;
            var collider = pickup.gameObject.AddComponent<CircleCollider2D>(); collider.isTrigger = true; collider.radius = .18f;
        }
        pickup.transform.position = position; pickup.transform.localScale = Vector3.one * 1.8f;
        pickup.visual.sortingOrder = OverrunDepthSort.OrderAt(position.y) - 3;
        pickup.visual.color = Color.white; pickup.age = 0; pickup.collected = false;
        pickup.gameObject.SetActive(true); active.Add(pickup); return pickup;
    }
    public bool TryCollect(OverrunPlayer player)
    {
        if (!gameObject.activeSelf || collected || !player || OverrunMenus.GameplayBlocked || !player.TryHeal(HealAmount)) return false;
        collected = true; OverrunAudio.World(OverrunSound.BandageHeal, transform.position); Recycle(); return true;
    }
    void OnTriggerEnter2D(Collider2D collider) { TryCollect(collider.GetComponentInParent<OverrunPlayer>()); }
    void OnTriggerStay2D(Collider2D collider) { TryCollect(collider.GetComponentInParent<OverrunPlayer>()); }
    void Update() { if (!OverrunMenus.GameplayBlocked) Advance(Time.deltaTime); }
    public void Advance(float deltaTime)
    {
        if (!gameObject.activeSelf || deltaTime <= 0) return;
        age += deltaTime;
        if (age >= Lifetime) { Recycle(); return; }
        float alpha = age > Lifetime - 4 ? .45f + .55f * Mathf.Abs(Mathf.Sin(age * 6)) : 1;
        visual.color = new Color(1, 1, 1, alpha);
    }
    void OnEnable() { active.Add(this); }
    void OnDisable() { active.Remove(this); }
    void Recycle()
    {
        if (!gameObject.activeSelf) return;
        gameObject.SetActive(false);
        if (pool.Count < 48) pool.Enqueue(this); else Destroy(gameObject);
    }
    public static void ClearActive() { foreach (var pickup in new List<OverrunBandagePickup>(active)) if (pickup) pickup.Recycle(); }
}
