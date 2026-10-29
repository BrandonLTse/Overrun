using System.Collections.Generic;
using UnityEngine;

/// Physics-derived walkability and cached, swept-clearance edges for static rooms.
public class GridMap
{
    public readonly int Width, Height;
    public readonly float CellSize, ProbeRadius;
    public readonly Vector2 Origin;
    public readonly int WalkableCount;
    public readonly LayerMask WallMask;
    readonly bool[] walkable;
    readonly byte[] knownEdges, openEdges;
    static readonly Vector2Int[] Steps = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
        new Vector2Int(1, 1), new Vector2Int(-1, -1), new Vector2Int(1, -1), new Vector2Int(-1, 1) };

    public GridMap(Vector2 origin, int width, int height, float cellSize, LayerMask wallMask, float probeRadiusFactor = .4f)
    {
        Origin = origin; Width = width; Height = height; CellSize = cellSize; WallMask = wallMask;
        ProbeRadius = cellSize * probeRadiusFactor;
        walkable = new bool[width * height]; knownEdges = new byte[walkable.Length]; openEdges = new byte[walkable.Length];
        int count = 0;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            var cell = new Vector2Int(x, y);
            bool free = !Physics2D.OverlapCircle(CellCenter(cell), ProbeRadius, wallMask);
            walkable[Index(cell)] = free; if (free) count++;
        }
        WalkableCount = count;
    }

    public int Index(Vector2Int cell) => cell.y * Width + cell.x;
    public bool InBounds(Vector2Int cell) => cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;
    public bool IsWalkable(Vector2Int cell) => InBounds(cell) && walkable[Index(cell)];
    public Vector2Int WorldToCell(Vector2 position) => new Vector2Int(Mathf.FloorToInt((position.x - Origin.x) / CellSize), Mathf.FloorToInt((position.y - Origin.y) / CellSize));
    public Vector2 CellCenter(Vector2Int cell) => Origin + new Vector2(cell.x + .5f, cell.y + .5f) * CellSize;

    public bool ClearSegment(Vector2 from, Vector2 to, float radius)
    {
        Vector2 delta = to - from;
        if (delta.sqrMagnitude < .000001f) return !Physics2D.OverlapCircle(from, radius, WallMask);
        return !Physics2D.CircleCast(from, radius, delta.normalized, delta.magnitude, WallMask);
    }

    public bool CanTraverse(Vector2Int from, Vector2Int to)
    {
        if (!IsWalkable(from) || !IsWalkable(to)) return false;
        var delta = to - from; int edge = -1;
        for (int i = 0; i < Steps.Length; i++) if (Steps[i] == delta) { edge = i; break; }
        if (edge < 0) return false;
        int index = Index(from), bit = 1 << edge;
        if ((knownEdges[index] & bit) != 0) return (openEdges[index] & bit) != 0;
        bool diagonalClear = delta.x == 0 || delta.y == 0 ||
            (IsWalkable(from + new Vector2Int(delta.x, 0)) && IsWalkable(from + new Vector2Int(0, delta.y)));
        bool clear = diagonalClear && ClearSegment(CellCenter(from), CellCenter(to), ProbeRadius);
        knownEdges[index] |= (byte)bit;
        int opposite = edge ^ 1, reverse = Index(to);
        knownEdges[reverse] |= (byte)(1 << opposite);
        if (clear) { openEdges[index] |= (byte)bit; openEdges[reverse] |= (byte)(1 << opposite); }
        return clear;
    }

    public bool NearestWalkable(Vector2Int from, int maxRadius, out Vector2Int result)
    {
        if (IsWalkable(from)) { result = from; return true; }
        for (int radius = 1; radius <= maxRadius; radius++)
            for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
            {
                if (Mathf.Abs(dx) != radius && Mathf.Abs(dy) != radius) continue;
                var cell = from + new Vector2Int(dx, dy);
                if (IsWalkable(cell)) { result = cell; return true; }
            }
        result = from; return false;
    }

    public bool[] ReachableFrom(Vector2Int start, out int reachedCount)
    {
        var seen = new bool[walkable.Length]; reachedCount = 0;
        if (!IsWalkable(start)) return seen;
        var queue = new Queue<Vector2Int>(); seen[Index(start)] = true; queue.Enqueue(start); reachedCount = 1;
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            for (int i = 0; i < 4; i++)
            {
                var neighbor = cell + Steps[i];
                if (!CanTraverse(cell, neighbor) || seen[Index(neighbor)]) continue;
                seen[Index(neighbor)] = true; reachedCount++; queue.Enqueue(neighbor);
            }
        }
        return seen;
    }
}
