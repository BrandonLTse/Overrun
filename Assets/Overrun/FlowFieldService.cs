using UnityEngine;

/// Shared field, with per-agent swept-clearance waypoint steering near corners.
public class FlowFieldService : MonoBehaviour
{
    public static FlowFieldService Instance { get; private set; }
    public Transform player;
    public int directChaseCells = 2;
    public FlowField Field { get; private set; }
    public GridMap Map { get; private set; }
    Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);
    Vector2 lastTargetPosition;
    void Awake() { Instance = this; }
    public void Init(GridMap map)
    {
        Map = map; Field = new FlowField(map); lastCell = new Vector2Int(int.MinValue, int.MinValue);
    }
    void Update()
    {
        if (Field == null || !player || OverrunMenus.GameplayBlocked) return;
        var cell = Map.WorldToCell(player.position);
        if (cell == lastCell && (Map.IsWalkable(cell) || ((Vector2)player.position - lastTargetPosition).sqrMagnitude < .04f)) return;
        RebuildAtPlayer();
    }

    public void RebuildAtPlayer()
    {
        if (Field == null || !player) return;
        lastTargetPosition = player.position; lastCell = Map.WorldToCell(lastTargetPosition);
        var target = lastCell;
        if (!Map.IsWalkable(target))
        {
            // A wall-hugging player may occupy the free corner of a blocked cell.
            // Snap only to a centre that can physically reach their actual feet.
            float nearest = float.PositiveInfinity;
            for (int y = -3; y <= 3; y++) for (int x = -3; x <= 3; x++)
            {
                var candidate = lastCell + new Vector2Int(x, y);
                if (!Map.IsWalkable(candidate)) continue;
                Vector2 center = Map.CellCenter(candidate); float distance = (center - lastTargetPosition).sqrMagnitude;
                if (distance >= nearest || !Map.ClearSegment(center, lastTargetPosition, .35f)) continue;
                target = candidate; nearest = distance;
            }
        }
        Field.Rebuild(target);
    }

    public Vector2 GetDirection(Vector2 position, float bodyRadius = .36f)
    {
        if (Field == null || !player) return Vector2.zero;
        var cell = Map.WorldToCell(position); int distance = Field.DistanceAt(cell);
        // Slightly inset the physical cast to tolerate the physics contact skin.
        float radius = Mathf.Max(.05f, bodyRadius - .01f);
        if (distance != FlowField.Unreachable)
        {
            Vector2 target = player.position;
            if (distance <= directChaseCells && Map.ClearSegment(position, target, radius)) return (target - position).normalized;
            Vector2 waypoint = Map.CellCenter(Field.NextCellAt(cell));
            if (distance > 0 && Map.ClearSegment(position, waypoint, radius)) return (waypoint - position).normalized;
            Vector2 center = Map.CellCenter(cell);
            if ((center - position).sqrMagnitude > .0025f && Map.ClearSegment(position, center, radius)) return (center - position).normalized;
        }
        // Knockback can leave an actor in a cell whose centre is blocked. Recover
        // through a visible, reachable neighbour, never by teleporting through cover.
        Vector2 best = position; float score = float.PositiveInfinity;
        for (int y = -2; y <= 2; y++) for (int x = -2; x <= 2; x++)
        {
            var candidate = cell + new Vector2Int(x, y); int steps = Field.DistanceAt(candidate);
            if (steps == FlowField.Unreachable || !Map.IsWalkable(candidate)) continue;
            Vector2 center = Map.CellCenter(candidate); float travel = Vector2.Distance(position, center);
            if (travel < .05f || !Map.ClearSegment(position, center, radius)) continue;
            float cost = steps + travel;
            if (cost < score) { score = cost; best = center; }
        }
        return (best - position).normalized;
    }
}
