using System.Collections.Generic;
using UnityEngine;

/// Walkability grid for the whole merged building. Built once after generation by probing physics.
public class GridMap
{
    public readonly int Width, Height;
    public readonly float CellSize;
    public readonly Vector2 Origin;      // world position of the lower-left corner of cell (0,0)
    public readonly int WalkableCount;
    readonly bool[] walkable;

    public GridMap(Vector2 origin, int width, int height, float cellSize, LayerMask wallMask, float probeRadiusFactor = 0.4f)
    {
        Origin = origin; Width = width; Height = height; CellSize = cellSize;
        walkable = new bool[width * height];

        float r = cellSize * probeRadiusFactor;
        int count = 0;
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = new Vector2Int(x, y);
                bool free = Physics2D.OverlapCircle(CellCenter(c), r, wallMask) == null;
                walkable[Index(c)] = free;
                if (free) count++;
            }
        WalkableCount = count;
    }

    public int Index(Vector2Int c) => c.y * Width + c.x;
    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Width && c.y < Height;
    public bool IsWalkable(Vector2Int c) => InBounds(c) && walkable[Index(c)];

    public Vector2Int WorldToCell(Vector2 p) => new Vector2Int(
        Mathf.FloorToInt((p.x - Origin.x) / CellSize),
        Mathf.FloorToInt((p.y - Origin.y) / CellSize));

    public Vector2 CellCenter(Vector2Int c) => Origin + new Vector2(c.x + 0.5f, c.y + 0.5f) * CellSize;

    /// Searches outward in square rings for the closest walkable cell. Returns false if none within maxRadius.
    public bool NearestWalkable(Vector2Int from, int maxRadius, out Vector2Int result)
    {
        if (IsWalkable(from)) { result = from; return true; }
        for (int r = 1; r <= maxRadius; r++)
            for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dy) != r) continue;   // ring only
                    var c = new Vector2Int(from.x + dx, from.y + dy);
                    if (IsWalkable(c)) { result = c; return true; }
                }
        result = from;
        return false;
    }

    /// 4-neighbor flood fill. Returns a reachability array and how many cells it reached.
    public bool[] ReachableFrom(Vector2Int start, out int reachedCount)
    {
        var seen = new bool[Width * Height];
        reachedCount = 0;
        if (!IsWalkable(start)) return seen;

        var q = new Queue<Vector2Int>();
        seen[Index(start)] = true;
        q.Enqueue(start);
        reachedCount = 1;

        var steps = new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            foreach (var s in steps)
            {
                var n = c + s;
                if (!IsWalkable(n)) continue;
                int i = Index(n);
                if (seen[i]) continue;
                seen[i] = true;
                reachedCount++;
                q.Enqueue(n);
            }
        }
        return seen;
    }
}
