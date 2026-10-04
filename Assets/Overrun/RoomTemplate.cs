using UnityEngine;

/// Put this on the root of every room prefab.
/// Prefab rules: pivot at the LOWER-LEFT corner, size = roomW x roomH cells (same for all rooms),
/// a 2-3 cell wide doorway centered on each side, plus one "door blocker" object per side
/// (a wall segment with a collider that fills the doorway). Blockers are enabled when that side is closed.
///
/// Obstacle variation:
///  - layoutVariants: alternative obstacle layouts (e.g. "pillars", "desk rows", "half-wall split").
///    Exactly one is enabled per run.
///  - optionalProps: individual extras (crate stacks, vending machines) that each appear with a probability.
/// Everything in these groups must be on the Walls layer so the grid treats it as non-walkable.
/// Do NOT parent SpawnPoints under these groups (they'd be disabled with the group).
public class RoomTemplate : MonoBehaviour
{
    [Header("Doors")]
    public GameObject blockNorth, blockEast, blockSouth, blockWest;

    [Header("Obstacle variation")]
    public GameObject[] layoutVariants;

    [System.Serializable]
    public class OptionalProp
    {
        public GameObject root;
        [Range(0f, 1f)] public float chance = 0.5f;
    }
    public OptionalProp[] optionalProps;

    public void ApplyDoors(bool north, bool east, bool south, bool west)
    {
        if (blockNorth) blockNorth.SetActive(!north);
        if (blockEast)  blockEast.SetActive(!east);
        if (blockSouth) blockSouth.SetActive(!south);
        if (blockWest)  blockWest.SetActive(!west);
    }

    public void RandomizeObstacles(System.Random rng)
    {
        if (layoutVariants != null && layoutVariants.Length > 0)
        {
            int pick = rng.Next(layoutVariants.Length);
            for (int i = 0; i < layoutVariants.Length; i++)
                if (layoutVariants[i]) layoutVariants[i].SetActive(i == pick);
        }

        if (optionalProps != null)
            foreach (var p in optionalProps)
                if (p.root) p.root.SetActive(rng.NextDouble() < p.chance);
    }
}
