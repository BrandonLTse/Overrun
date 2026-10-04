using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : MonoBehaviour
{
    Rigidbody2D rb;
    EnemyDefinition def;
    float hp;
    Action onDespawn;
    public float ContactDamage => def != null ? def.contactDamage : 0f;

    void Awake() => rb = GetComponent<Rigidbody2D>();

    public void Init(EnemyDefinition definition, Action despawnCallback)
    {
        def = definition;
        hp = def.maxHealth;
        onDespawn = despawnCallback;
    }

    void FixedUpdate()
    {
        var svc = FlowFieldService.Instance;
        if (svc == null || def == null) return;
        Vector2 dir = svc.GetDirection(rb.position);
        rb.linearVelocity = dir * def.speed;
    }

    public void TakeDamage(float amount)
    {
        hp -= amount;
        if (hp <= 0f)
        {
            OverrunXpPickup.Spawn(transform.position, def != null && def.cost >= 8 ? 5 : 1);
            Despawn();
        }
    }

    public void Despawn()
    {
        var cb = onDespawn;
        onDespawn = null;
        cb?.Invoke();
    }
}
