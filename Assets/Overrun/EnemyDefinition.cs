using UnityEngine;

/// Create via Assets > Create > Game > Enemy Definition. All tuning lives here.
[CreateAssetMenu(menuName = "Game/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    public Enemy prefab;
    public float maxHealth = 3f;
    public float speed = 2.5f;
    public float contactDamage = 1f;

    [Header("Wave director")]
    public int cost = 1;            // how much budget one costs
    public float weight = 1f;       // relative chance when several are affordable
    public float unlockTime = 0f;   // seconds into the run before it can appear
}
