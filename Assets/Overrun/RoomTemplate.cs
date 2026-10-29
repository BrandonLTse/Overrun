using UnityEngine;

/// Room shells use a lower-left origin and a door blocker for each side.
/// Existing Tiled templates and their layer/door contracts remain supported.
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
        [Range(0f, 1f)] public float chance = .5f;
    }
    public OptionalProp[] optionalProps;

    public void ApplyDoors(bool north, bool east, bool south, bool west)
    {
        if (blockNorth) blockNorth.SetActive(!north);
        if (blockEast) blockEast.SetActive(!east);
        if (blockSouth) blockSouth.SetActive(!south);
        if (blockWest) blockWest.SetActive(!west);
    }

    public void RandomizeObstacles(System.Random rng)
    {
        var procedural = GetComponent<OverrunProceduralRoom>();
        if (procedural) procedural.Randomize(rng);
        if (layoutVariants != null && layoutVariants.Length > 0)
        {
            int pick = rng.Next(layoutVariants.Length);
            for (int i = 0; i < layoutVariants.Length; i++)
                if (layoutVariants[i]) layoutVariants[i].SetActive(i == pick);
        }
        if (optionalProps != null)
            foreach (var prop in optionalProps)
                if (prop.root) prop.root.SetActive(rng.NextDouble() < prop.chance);
    }
}
