using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class LevelGenerator : MonoBehaviour
{
    public RoomTemplate[] templates;
    public int gridW = 3, gridH = 3;
    public int roomW = 32, roomH = 24;
    public float cellSize = 1f;
    public int extraLoops = 2;
    public int seed = 0;
    public int maxObstacleAttempts = 10;
    [Range(0.5f, 1f)] public float minReachableFraction = 0.98f;
    public LayerMask wallMask;
    public GridMap Map { get; private set; }
    public Vector2 PlayerStart { get; private set; }
    public readonly List<SpawnPoint> SpawnPoints = new List<SpawnPoint>();
    public bool ConnectivityValid { get; private set; }

    bool[,] openEast, openNorth;
    readonly List<RoomTemplate> rooms = new List<RoomTemplate>();
    System.Random rng;

    public void Generate()
    {
        if (templates == null || templates.Length == 0) { Debug.LogError("LevelGenerator needs at least one room template."); return; }
        ConnectivityValid = false;
        rng = new System.Random(seed != 0 ? seed : System.Environment.TickCount);
        openEast = new bool[gridW, gridH]; openNorth = new bool[gridW, gridH];
        CarveSpanningTree(); EnsureMinimumConnections(); AddLoops(); BuildRooms();
        var desiredStart = new Vector2Int(gridW * roomW / 2, gridH * roomH / 2);
        Vector2Int start = desiredStart; bool valid = false;
        for (int attempt = 0; attempt < maxObstacleAttempts && !valid; attempt++)
        {
            foreach (var room in rooms) room.RandomizeObstacles(rng);
            ScanGeometry();
            valid = Map.NearestWalkable(desiredStart, 12, out start) && Validate(start);
        }
        if (!valid)
        {
            // Fail safely if a future prop/footprint change makes sampling too dense.
            // Never knowingly begin a procedural run with sealed rooms or spawns.
            foreach (var room in rooms)
            {
                var procedural = room.GetComponent<OverrunProceduralRoom>();
                if (procedural) procedural.ClearObstacles();
            }
            ScanGeometry();
            valid = Map.NearestWalkable(desiredStart, 12, out start) && Validate(start);
            if (!valid) throw new System.InvalidOperationException("LevelGenerator: room layout failed connectivity validation.");
            Debug.LogWarning("LevelGenerator: cleared procedural obstacles to preserve safe routes.");
        }
        ConnectivityValid = true;
        PlayerStart = Map.CellCenter(start);
    }

    void ScanGeometry()
    {
        foreach (var room in rooms)
        {
            foreach (var collider in room.GetComponentsInChildren<TilemapCollider2D>()) collider.ProcessTilemapChanges();
            foreach (var composite in room.GetComponentsInChildren<CompositeCollider2D>()) composite.GenerateGeometry();
        }
        Physics2D.SyncTransforms();
        Map = new GridMap(Vector2.zero, gridW * roomW, gridH * roomH, cellSize, wallMask);
        CollectSpawnPoints();
    }

    void CollectSpawnPoints() { SpawnPoints.Clear(); foreach (var room in rooms) SpawnPoints.AddRange(room.GetComponentsInChildren<SpawnPoint>()); }
    bool Validate(Vector2Int start)
    {
        var reach = Map.ReachableFrom(start, out int reached);
        if (reached < Map.WalkableCount * minReachableFraction) return false;
        foreach (var sp in SpawnPoints) { var c = Map.WorldToCell(sp.transform.position); if (!Map.InBounds(c) || !reach[Map.Index(c)]) return false; }
        return true;
    }

    static readonly Vector2Int[] Cardinals = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    void CarveSpanningTree()
    {
        var visited = new bool[gridW, gridH]; var stack = new Stack<Vector2Int>(); var start = new Vector2Int(rng.Next(gridW), rng.Next(gridH)); visited[start.x, start.y] = true; stack.Push(start); var options = new List<Vector2Int>(4);
        while (stack.Count > 0) { var c = stack.Peek(); options.Clear(); foreach (var d in Cardinals) { var n = c + d; if (InGrid(n) && !visited[n.x, n.y]) options.Add(n); } if (options.Count == 0) { stack.Pop(); continue; } var next = options[rng.Next(options.Count)]; Open(c, next); visited[next.x, next.y] = true; stack.Push(next); }
    }
    int RoomDegree(Vector2Int c) { int degree = 0; foreach (var d in Cardinals) if (InGrid(c + d) && IsOpen(c, c + d)) degree++; return degree; }

    // The spanning tree guarantees connectivity; this pass removes leaf rooms by
    // opening nearby unused sides, giving each room at least two doorways.
    void EnsureMinimumConnections()
    {
        int limit = gridW * gridH * 4;
        for (int pass = 0; pass < limit; pass++)
        {
            Vector2Int chosen = new Vector2Int(-1, -1), neighbor = chosen;
            int bestNeighborDegree = int.MaxValue;
            for (int y = 0; y < gridH; y++) for (int x = 0; x < gridW; x++)
            {
                var c = new Vector2Int(x, y); if (RoomDegree(c) >= 2) continue;
                foreach (var d in Cardinals)
                {
                    var n = c + d; if (!InGrid(n) || IsOpen(c, n)) continue;
                    int nd = RoomDegree(n); if (nd >= 3 || nd >= bestNeighborDegree) continue;
                    chosen = c; neighbor = n; bestNeighborDegree = nd;
                }
            }
            if (chosen.x < 0) break;
            Open(chosen, neighbor);
        }
    }

    void AddLoops()
    {
        int added = 0, attempts = 0;
        while (added < extraLoops && attempts++ < 100)
        {
            var c = new Vector2Int(rng.Next(gridW), rng.Next(gridH)); var n = c + Cardinals[rng.Next(4)];
            if (!InGrid(n) || IsOpen(c, n) || RoomDegree(c) >= 3 || RoomDegree(n) >= 3) continue;
            Open(c, n); added++;
        }
    }
    bool InGrid(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < gridW && c.y < gridH;
    void Open(Vector2Int a, Vector2Int b) { if (b.x > a.x) openEast[a.x, a.y] = true; else if (b.x < a.x) openEast[b.x, b.y] = true; else if (b.y > a.y) openNorth[a.x, a.y] = true; else openNorth[b.x, b.y] = true; }
    bool IsOpen(Vector2Int a, Vector2Int b) { if (!InGrid(b)) return false; if (b.x > a.x) return openEast[a.x, a.y]; if (b.x < a.x) return openEast[b.x, b.y]; if (b.y > a.y) return openNorth[a.x, a.y]; return openNorth[b.x, b.y]; }
    void BuildRooms()
    {
        // Unity's script refresh can discard the managed cache while retaining
        // runtime scene children. They are still owned rooms, not new templates.
        foreach (Transform child in transform)
            if (child.TryGetComponent<RoomTemplate>(out var existing) && !rooms.Contains(existing)) rooms.Add(existing);
        foreach (var old in rooms)
        {
            if (!old) continue;
            old.gameObject.SetActive(false);
            if (Application.isPlaying) Destroy(old.gameObject); else DestroyImmediate(old.gameObject);
        }
        rooms.Clear();
        for (int y = 0; y < gridH; y++) for (int x = 0; x < gridW; x++)
        {
            var cell = new Vector2Int(x, y); var pos = new Vector3(x * roomW * cellSize, y * roomH * cellSize, 0); var room = Instantiate(templates[rng.Next(templates.Length)], pos, Quaternion.identity, transform);
            room.gameObject.SetActive(true);
            room.ApplyDoors(IsOpen(cell, cell + Vector2Int.up), IsOpen(cell, cell + Vector2Int.right), IsOpen(cell, cell + Vector2Int.down), IsOpen(cell, cell + Vector2Int.left)); rooms.Add(room);
        }
    }
}
