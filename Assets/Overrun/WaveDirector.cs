using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// Budget-based spawner. Budget grows over time; each tick it buys enemies and drops them at
/// spawn points that are far away (by PATH distance), off-screen, and not in line of sight.
public class WaveDirector : MonoBehaviour
{
    [Header("References")]
    public LevelGenerator level;
    public Transform player;
    public Camera cam;
    public EnemyDefinition[] enemies;

    [Header("Escalation")]
    public float baseBudgetPerSec = 1f;
    public float rampPerSec = 0.02f;          // extra budget/sec gained per second of run time
    public float spawnInterval = 0.75f;
    public int maxPerTick = 4;
    public int maxAlive = 150;

    [Header("Spawn point rules")]
    public int minPathDist = 15;              // in cells
    public int maxPathDist = 40;
    public float viewMargin = 2f;
    public int initialSpawnPoints = 3;
    public float unlockEverySeconds = 20f;    // opens one more spawn point this often
    public LayerMask wallMask;                // for line-of-sight checks

    bool running;
    float time, budget, tickTimer;
    int alive;
    List<SpawnPoint> points;
    readonly List<SpawnPoint> candidates = new List<SpawnPoint>();
    readonly List<EnemyDefinition> affordable = new List<EnemyDefinition>();
    readonly Dictionary<EnemyDefinition, ObjectPool<Enemy>> pools = new Dictionary<EnemyDefinition, ObjectPool<Enemy>>();

    public void Begin()
    {
        points = new List<SpawnPoint>(level.SpawnPoints);
        Shuffle(points);
        time = budget = tickTimer = 0f;
        running = true;
    }

    void Update()
    {
        if (!running) return;

        time += Time.deltaTime;
        budget += (baseBudgetPerSec + rampPerSec * time) * Time.deltaTime;

        tickTimer -= Time.deltaTime;
        if (tickTimer > 0f) return;
        tickTimer = spawnInterval;

        for (int i = 0; i < maxPerTick; i++)
            if (!TrySpawnOne()) break;
    }

    bool TrySpawnOne()
    {
        if (alive >= maxAlive) return false;

        // What can we afford and what has unlocked?
        affordable.Clear();
        float totalWeight = 0f;
        foreach (var e in enemies)
            if (time >= e.unlockTime && e.cost <= budget) { affordable.Add(e); totalWeight += e.weight; }
        if (affordable.Count == 0) return false;

        var point = PickSpawnPoint();
        if (point == null) return false;

        // Weighted pick
        float roll = Random.value * totalWeight;
        EnemyDefinition chosen = affordable[affordable.Count - 1];
        foreach (var e in affordable)
        {
            roll -= e.weight;
            if (roll <= 0f) { chosen = e; break; }
        }

        Spawn(chosen, point.transform.position);
        budget -= chosen.cost;
        return true;
    }

    SpawnPoint PickSpawnPoint()
    {
        var svc = FlowFieldService.Instance;
        if (svc == null || svc.Field == null) return null;

        int active = Mathf.Min(points.Count, initialSpawnPoints + (int)(time / unlockEverySeconds));

        // Pass 0: strict rules. Pass 1: relaxed (closer allowed, no max) so we never stall.
        for (int pass = 0; pass < 2; pass++)
        {
            candidates.Clear();
            int min = pass == 0 ? minPathDist : minPathDist / 2;
            int max = pass == 0 ? maxPathDist : int.MaxValue;

            for (int i = 0; i < active; i++)
            {
                var p = points[i];
                Vector2 pos = p.transform.position;

                int d = svc.Field.DistanceAt(svc.Map.WorldToCell(pos));
                if (d == FlowField.Unreachable || d < min || d > max) continue;
                if (InCameraView(pos)) continue;
                if (HasLineOfSight(pos)) continue;

                candidates.Add(p);
            }
            if (candidates.Count > 0) return candidates[Random.Range(0, candidates.Count)];
        }
        return null;
    }

    bool InCameraView(Vector2 pos)
    {
        float h = cam.orthographicSize + viewMargin;
        float w = h * cam.aspect;
        Vector2 c = cam.transform.position;
        return Mathf.Abs(pos.x - c.x) < w && Mathf.Abs(pos.y - c.y) < h;
    }

    bool HasLineOfSight(Vector2 pos) =>
        !Physics2D.Linecast(player.position, pos, wallMask);

    // ---- pooling ------------------------------------------------------------------------------

    void Spawn(EnemyDefinition def, Vector2 pos)
    {
        if (!pools.TryGetValue(def, out var pool))
        {
            pool = new ObjectPool<Enemy>(
                createFunc: () => Instantiate(def.prefab, transform),
                actionOnGet: e => e.gameObject.SetActive(true),
                actionOnRelease: e => e.gameObject.SetActive(false),
                actionOnDestroy: e => Destroy(e.gameObject),
                defaultCapacity: 32, maxSize: 300);
            pools[def] = pool;
        }

        var enemy = pool.Get();
        enemy.transform.position = pos;
        alive++;
        enemy.Init(def, () => { pool.Release(enemy); alive--; });
    }

    static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
