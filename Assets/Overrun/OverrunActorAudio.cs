using UnityEngine;

/// Ambient zombie voices only. Player and zombie movement is deliberately silent.
public sealed class OverrunActorAudio : MonoBehaviour
{
    public bool isPlayer;
    float groanTimer;
    Enemy enemy;
    void OnEnable()
    {
        enemy = GetComponent<Enemy>(); groanTimer = 2.5f + Mathf.Abs(GetEntityId().GetHashCode() % 37) * .1f;
    }
    void LateUpdate()
    {
        if (isPlayer || OverrunMenus.GameplayBlocked || !enemy || !enemy.IsAlive) return;
        Vector2 position = transform.position;
        groanTimer -= Time.deltaTime;
        if (groanTimer <= 0)
        {
            groanTimer = 4 + Mathf.Abs(GetEntityId().GetHashCode() % 29) * .13f;
            var animation = GetComponent<OverrunSmallZombieAnimator>();
            if (!animation || !animation.IsAttacking) OverrunAudio.World(OverrunSound.ZombieGroan, position, 1, enemy.ContactDamage >= 20 ? .8f : enemy.ContactDamage >= 12 ? 1.15f : 1);
        }
    }
}
