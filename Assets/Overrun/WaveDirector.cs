using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// Finite waves: prepare, spend a fixed budget, clear survivors, choose one reward.
public class WaveDirector : MonoBehaviour
{
    public enum Phase { Dormant, Breather, Spawning, Clearing, Reward }
    public LevelGenerator level;
    public Transform player;
    public Camera cam;
    public EnemyDefinition[] enemies;
    [Header("Wave pacing")]
    public int firstWaveBudget = 8, budgetPerWave = 4, firstWaveAliveCap = 4, maxAlive = 40;
    public float initialGrace = 4, betweenWaves = 5, spawnInterval = 1.25f;
    public int runnerFromWave = 3, bruteFromWave = 6;
    [Header("Safe entries")]
    public int minPathDist = 15, maxPathDist = 45, initialSpawnPoints = 3;
    public float viewMargin = 2;
    public LayerMask wallMask;
    // Retain old serialized fields for existing prototype scenes.
    [HideInInspector] public float baseBudgetPerSec = 1, rampPerSec = .02f, unlockEverySeconds = 20;
    [HideInInspector] public int maxPerTick = 1;
    public Phase State { get; private set; }
    public int WaveNumber { get; private set; }
    public int RemainingBudget { get; private set; }
    public int WaveBudget { get; private set; }
    public int AliveCount => live.Count;
    public int AliveCap => Mathf.Min(maxAlive, firstWaveAliveCap + Mathf.Max(0, WaveNumber - 1));
    public float Countdown { get; private set; }
    public bool EntryBlocked => blockedTime > 5;
    float tickTimer, blockedTime;
    OverrunUpgradeSystem upgrades;
    readonly List<SpawnPoint> points = new List<SpawnPoint>(), activePoints = new List<SpawnPoint>(), candidates = new List<SpawnPoint>();
    readonly List<EnemyDefinition> affordable = new List<EnemyDefinition>();
    readonly HashSet<Enemy> live = new HashSet<Enemy>();
    readonly Dictionary<EnemyDefinition, ObjectPool<Enemy>> pools = new Dictionary<EnemyDefinition, ObjectPool<Enemy>>();

    public void Begin()
    {
        EndRun();
        points.Clear(); points.AddRange(level.SpawnPoints);
        var actor = player.GetComponent<OverrunPlayer>();
        upgrades = actor ? actor.GetComponent<OverrunUpgradeSystem>() : null;
        if (!upgrades) throw new System.InvalidOperationException("Wave rewards need an OverrunPlayer with an upgrade system.");
        upgrades.ResetRun(); OverrunBullet.ClearActive();
        Countdown = initialGrace; State = Phase.Breather;
    }

    public void EndRun()
    {
        State = Phase.Dormant;
        foreach (var enemy in new List<Enemy>(live)) if (enemy) enemy.Despawn();
        live.Clear(); activePoints.Clear(); points.Clear();
        WaveNumber = 0; RemainingBudget = WaveBudget = 0;
        Countdown = blockedTime = tickTimer = 0;
    }

    void Update() { if (!OverrunMenus.GameplayBlocked) Advance(Time.deltaTime); }
    public void Advance(float deltaTime)
    {
        if (deltaTime <= 0 || State == Phase.Dormant || State == Phase.Reward) return;
        if (State == Phase.Breather)
        {
            Countdown = Mathf.Max(0, Countdown - deltaTime);
            if (Countdown <= 0) StartWave();
            return;
        }
        if (State == Phase.Clearing) { CheckClear(); return; }
        tickTimer -= deltaTime;
        if (tickTimer > 0) return;
        // One entry per tick; a slow frame never dumps a catch-up swarm.
        tickTimer = Mathf.Max(.4f, spawnInterval - (WaveNumber - 1) * .045f);
        if (live.Count >= AliveCap) return;
        if (!TrySpawnOne()) blockedTime += tickTimer; else blockedTime = 0;
    }

    void StartWave()
    {
        WaveNumber++;
        OverrunRunRecord.Record(WaveNumber);
        OverrunAudio.World(OverrunSound.WaveStart, player.position);
        WaveBudget = RemainingBudget = Mathf.Max(1, firstWaveBudget + (WaveNumber - 1) * budgetPerWave);
        State = Phase.Spawning; tickTimer = .35f; blockedTime = 0;
        player.GetComponent<OverrunWeaponSystem>()?.OnWaveStarted(WaveNumber);
        upgrades.OnWaveStarted();
        // Prefer nearby path-reachable entries, rather than opening random corners
        // thirty seconds away at the start of a run. Visibility is checked per spawn.
        var field = FlowFieldService.Instance;
        points.Sort((a, b) =>
        {
            if (!field || field.Field == null) return 0;
            return field.Field.DistanceAt(field.Map.WorldToCell(a.transform.position)).CompareTo(field.Field.DistanceAt(field.Map.WorldToCell(b.transform.position)));
        });
        activePoints.Clear();
        int count = Mathf.Min(points.Count, initialSpawnPoints + (WaveNumber - 1) * 2);
        for (int i = 0; i < count; i++) activePoints.Add(points[i]);
    }

    bool TrySpawnOne()
    {
        affordable.Clear(); float weight = 0;
        for (int i = 0; i < enemies.Length; i++)
        {
            var definition = enemies[i];
            int unlock = i == 0 ? 1 : i == 1 ? runnerFromWave : bruteFromWave;
            if (!definition || !definition.prefab || WaveNumber < unlock || definition.cost > RemainingBudget || definition.cost <= 0) continue;
            affordable.Add(definition); weight += Mathf.Max(.01f, definition.weight);
        }
        if (affordable.Count == 0) { RemainingBudget = 0; State = Phase.Clearing; CheckClear(); return false; }
        var point = PickSpawnPoint();
        if (!point) return false;
        float roll = Random.value * weight;
        var chosen = affordable[affordable.Count - 1];
        foreach (var definition in affordable) { roll -= Mathf.Max(.01f, definition.weight); if (roll <= 0) { chosen = definition; break; } }
        RemainingBudget -= chosen.cost;
        Spawn(chosen, point.transform.position);
        if (RemainingBudget == 0) { State = Phase.Clearing; CheckClear(); }
        return true;
    }

    SpawnPoint PickSpawnPoint()
    {
        var service = FlowFieldService.Instance;
        if (!service || service.Field == null) return null;
        for (int pass = 0; pass < 3; pass++)
        {
            candidates.Clear();
            var entries = pass == 2 ? points : activePoints;
            foreach (var point in entries)
            {
                if (!point) continue;
                Vector2 position = point.transform.position;
                int distance = service.Field.DistanceAt(service.Map.WorldToCell(position));
                if (distance == FlowField.Unreachable || distance < minPathDist || (pass == 0 && distance > maxPathDist)) continue;
                if (InCameraView(position) || !Physics2D.Linecast(player.position, position, wallMask)) continue;
                candidates.Add(point);
            }
            if (candidates.Count > 0)
            {
                var pick = candidates[Random.Range(0, Mathf.Min(4, candidates.Count))];
                if (!activePoints.Contains(pick)) activePoints.Add(pick);
                return pick;
            }
        }
        return null; // Wait for a fair entry; never spawn visibly on top of the player.
    }

    bool InCameraView(Vector2 position)
    {
        float height = cam.orthographicSize + viewMargin, width = height * cam.aspect;
        Vector2 center = cam.transform.position;
        return Mathf.Abs(position.x - center.x) < width && Mathf.Abs(position.y - center.y) < height;
    }

    void Spawn(EnemyDefinition definition, Vector2 position)
    {
        if (!pools.TryGetValue(definition, out var pool))
        {
            pool = new ObjectPool<Enemy>(() => Instantiate(definition.prefab, transform), e => e.gameObject.SetActive(true),
                e => e.gameObject.SetActive(false), e => Destroy(e.gameObject), defaultCapacity: 16, maxSize: 150);
            pools.Add(definition, pool);
        }
        var enemy = pool.Get(); enemy.transform.position = position; live.Add(enemy);
        enemy.Init(definition, () =>
        {
            if (!live.Remove(enemy)) return;
            pool.Release(enemy); CheckClear();
        });
    }

    void CheckClear()
    {
        if ((State != Phase.Spawning && State != Phase.Clearing) || RemainingBudget > 0 || live.Count > 0) return;
        State = Phase.Reward;
        OverrunAudio.World(OverrunSound.WaveClear, player.position);
        OverrunBullet.ClearActive();
        upgrades.BeginChoice(this);
    }

    public bool AcceptUpgrade()
    {
        if (State != Phase.Reward) return false;
        State = Phase.Breather; Countdown = betweenWaves;
        return true;
    }
}
