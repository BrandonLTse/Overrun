using System.Collections.Generic;
using UnityEngine;

/// Shared BFS with physically clear edges and explicit next-cell waypoints.
public class FlowField
{
    public const int Unreachable = -1;
    readonly GridMap map;
    readonly int[] dist;
    readonly Vector2Int[] next;
    readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();
    static readonly Vector2Int[] Dirs =
    {
        Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down,
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };

    public FlowField(GridMap map)
    {
        this.map = map; dist = new int[map.Width * map.Height]; next = new Vector2Int[dist.Length];
        System.Array.Fill(dist, Unreachable);
    }

    public void Rebuild(Vector2Int target)
    {
        // Clear first: an invalid target must not leave a stale previous field.
        System.Array.Fill(dist, Unreachable); queue.Clear();
        if (!map.NearestWalkable(target, 3, out target)) return;
        dist[map.Index(target)] = 0; next[map.Index(target)] = target; queue.Enqueue(target);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            foreach (var step in Dirs)
            {
                var neighbor = cell + step;
                if (!map.CanTraverse(cell, neighbor)) continue;
                int index = map.Index(neighbor);
                if (dist[index] != Unreachable) continue;
                dist[index] = dist[map.Index(cell)] + 1;
                // Reverse BFS directly records the next cell towards the target.
                next[index] = cell; queue.Enqueue(neighbor);
            }
        }
    }

    public int DistanceAt(Vector2Int cell) => map.InBounds(cell) ? dist[map.Index(cell)] : Unreachable;
    public Vector2Int NextCellAt(Vector2Int cell) => DistanceAt(cell) > 0 ? next[map.Index(cell)] : cell;
    public Vector2 DirectionAt(Vector2Int cell) => DistanceAt(cell) > 0 ? ((Vector2)(NextCellAt(cell) - cell)).normalized : Vector2.zero;
}
