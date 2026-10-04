using System.Collections.Generic;
using UnityEngine;

/// One BFS from the target (player) outward. Afterwards every cell knows its step-distance to the
/// player and the direction of its best neighbor. Enemies just look up their cell: O(1) each.
public class FlowField
{
    public const int Unreachable = -1;

    readonly GridMap map;
    readonly int[] dist;
    readonly Vector2[] dir;
    readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();

    static readonly Vector2Int[] Dirs =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1)
    };

    public FlowField(GridMap map)
    {
        this.map = map;
        dist = new int[map.Width * map.Height];
        dir = new Vector2[dist.Length];
    }

    // Diagonals are only allowed if both orthogonal neighbors are open (no corner cutting).
    bool CanStep(Vector2Int from, Vector2Int d)
    {
        if (!map.IsWalkable(from + d)) return false;
        if (d.x != 0 && d.y != 0)
            return map.IsWalkable(new Vector2Int(from.x + d.x, from.y)) &&
                   map.IsWalkable(new Vector2Int(from.x, from.y + d.y));
        return true;
    }

    public void Rebuild(Vector2Int target)
    {
        // Player can stand in a cell that probes as "blocked" (hugging a wall). Snap to a walkable neighbor.
        if (!map.IsWalkable(target))
        {
            bool found = false;
            foreach (var d in Dirs)
                if (map.IsWalkable(target + d)) { target += d; found = true; break; }
            if (!found) return;
        }

        System.Array.Fill(dist, Unreachable);
        queue.Clear();
        dist[map.Index(target)] = 0;
        queue.Enqueue(target);

        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            int cd = dist[map.Index(c)];
            foreach (var d in Dirs)
            {
                if (!CanStep(c, d)) continue;
                var n = c + d;
                int ni = map.Index(n);
                if (dist[ni] != Unreachable) continue;
                dist[ni] = cd + 1;
                queue.Enqueue(n);
            }
        }

        // Direction pass: point at the neighbor with the lowest distance.
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Vector2Int(x, y);
                int i = map.Index(c);
                if (dist[i] <= 0) { dir[i] = Vector2.zero; continue; }

                int best = dist[i];
                Vector2Int bestD = Vector2Int.zero;
                foreach (var d in Dirs)
                {
                    if (!CanStep(c, d)) continue;
                    int nd = dist[map.Index(c + d)];
                    if (nd != Unreachable && nd < best) { best = nd; bestD = d; }
                }
                dir[i] = ((Vector2)bestD).normalized;
            }
    }

    public int DistanceAt(Vector2Int c) => map.InBounds(c) ? dist[map.Index(c)] : Unreachable;
    public Vector2 DirectionAt(Vector2Int c) => map.InBounds(c) ? dir[map.Index(c)] : Vector2.zero;
}
