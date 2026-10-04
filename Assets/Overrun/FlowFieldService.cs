using UnityEngine;

/// Owns the flow field, rebuilds it only when the player changes cell, and answers "which way?" queries.
public class FlowFieldService : MonoBehaviour
{
    public static FlowFieldService Instance { get; private set; }

    public Transform player;
    [Tooltip("Within this many cells of the player, enemies skip the field and chase directly (smoother).")]
    public int directChaseCells = 2;

    public FlowField Field { get; private set; }
    public GridMap Map { get; private set; }

    Vector2Int lastCell = new Vector2Int(int.MinValue, int.MinValue);

    void Awake() => Instance = this;

    public void Init(GridMap map)
    {
        Map = map;
        Field = new FlowField(map);
        lastCell = new Vector2Int(int.MinValue, int.MinValue);
    }

    void Update()
    {
        if (Field == null) return;
        var cell = Map.WorldToCell(player.position);
        if (cell == lastCell) return;
        lastCell = cell;
        Field.Rebuild(cell);
    }

    public Vector2 GetDirection(Vector2 worldPos)
    {
        if (Field == null) return Vector2.zero;
        var cell = Map.WorldToCell(worldPos);
        int d = Field.DistanceAt(cell);
        if (d == FlowField.Unreachable) return Vector2.zero;
        if (d <= directChaseCells) return ((Vector2)player.position - worldPos).normalized;
        return Field.DirectionAt(cell);
    }
}
