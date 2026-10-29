using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    static readonly HashSet<Enemy> active = new HashSet<Enemy>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() { active.Clear(); }
    public static void CopyActiveTo(List<Enemy> result) { result.Clear(); result.AddRange(active); }
    Rigidbody2D rb;
    EnemyDefinition def;
    float hp, flashTime, stunTime;
    int hitFrame = -1;
    Vector2 impactVelocity;
    SpriteRenderer[] renderers;
    Color[] baseColors;
    Action onDespawn;
    OverrunSmallZombieAnimator zombie;
    CircleCollider2D bodyCollider;
    public bool IsAlive => hp > 0;
    public float ContactDamage => def != null ? def.contactDamage : 0f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<CircleCollider2D>();
        zombie = GetComponent<OverrunSmallZombieAnimator>();
        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;
    }

    public void Init(EnemyDefinition definition, Action despawnCallback)
    {
        def = definition;
        hp = def.maxHealth;
        onDespawn = despawnCallback;
        impactVelocity = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
        flashTime = 0;
        stunTime = 0;
        active.Add(this);
        RestoreColors();
        if (zombie) zombie.ResetForSpawn();
    }

    void FixedUpdate()
    {
        if (OverrunMenus.GameplayBlocked) return;
        var svc = FlowFieldService.Instance;
        if (svc == null || def == null) return;
        Vector2 dir = svc.GetDirection(rb.position, bodyCollider ? bodyCollider.radius * Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y)) : .36f);
        if (stunTime > 0) dir = Vector2.zero;
        if (zombie) zombie.SetMotion(dir * def.speed);
        if (zombie && zombie.IsAttacking) dir = Vector2.zero;
        rb.linearVelocity = dir * def.speed + impactVelocity;
        impactVelocity = Vector2.MoveTowards(impactVelocity, Vector2.zero, 28f * Time.fixedDeltaTime);
    }

    void Update()
    {
        if (OverrunMenus.GameplayBlocked) return;
        stunTime = Mathf.Max(0, stunTime - Time.deltaTime);
        if (flashTime <= 0 || hitFrame == Time.frameCount) return;
        flashTime -= Time.deltaTime;
        if (flashTime <= 0) RestoreColors();
    }

    void OnDisable() { active.Remove(this); impactVelocity = Vector2.zero; flashTime = stunTime = 0; RestoreColors(); }

    public void Push(Vector2 direction, float strength)
    {
        if (IsAlive) impactVelocity = Vector2.ClampMagnitude(impactVelocity + direction.normalized * strength, 9);
    }
    public void Stagger(float duration) { if (IsAlive) stunTime = Mathf.Max(stunTime, duration); }

    void RestoreColors()
    {
        if (renderers == null) return;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].color = baseColors[i];
    }

    public void TakeDamage(float amount, Vector2 hitDirection)
    {
        if (!IsAlive) return;
        float resistance = def != null && def.cost >= 8 ? 3f : 1f;
        impactVelocity = Vector2.ClampMagnitude(impactVelocity + hitDirection.normalized * (4f / resistance), 5f);
        flashTime = .065f;
        hitFrame = Time.frameCount;
        for (int i = 0; i < renderers.Length; i++) if (renderers[i]) renderers[i].color = new Color(2.5f, 2.1f, 1.6f, baseColors[i].a);
        TakeDamage(amount);
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive) return;
        hp -= amount;
        if (amount > 0) OverrunAudio.World(hp <= 0 ? OverrunSound.ZombieDeath : OverrunSound.ZombieHit, transform.position, 1, ContactDamage >= 20 ? .8f : 1);
        if (hp <= 0f)
        {
            if (zombie) zombie.SpawnDeath();
            OverrunBandagePickup.TryDrop(transform.position);
            Despawn();
        }
    }

    public bool TryMeleeAttack(OverrunPlayer player)
    {
        if (OverrunMenus.GameplayBlocked || !IsAlive || !player || stunTime > 0) return false;
        if (zombie) return zombie.TryAttack(player);
        player.Damage(ContactDamage);
        return true;
    }

    public void MeleeStrike(OverrunPlayer player)
    {
        if (OverrunMenus.GameplayBlocked || !IsAlive || !player || stunTime > 0) return;
        var own = GetComponent<Collider2D>();
        var target = player.GetComponent<Collider2D>();
        if (own && target && own.enabled && target.enabled && own.Distance(target).distance <= .14f)
            player.Damage(ContactDamage);
    }

    public void Despawn()
    {
        hp = 0;
        active.Remove(this);
        var cb = onDespawn;
        onDespawn = null;
        cb?.Invoke();
    }
}
