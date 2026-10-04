# Survival-in-a-building: script structure

## How the pieces talk

    GameBootstrap
      ├─ LevelGenerator.Generate()   -> instantiates rooms, builds GridMap, collects SpawnPoints
      ├─ FlowFieldService.Init(map)  -> owns FlowField, rebuilds when the player changes cell
      └─ WaveDirector.Begin()        -> budget spawner; reads FlowField distances to choose spawn points

    Enemy.FixedUpdate -> FlowFieldService.GetDirection(pos) -> moves

| Script | Job |
|---|---|
| `RoomTemplate` | On each room prefab. Toggles door blockers for closed sides. |
| `SpawnPoint` | Empty marker inside room prefabs. |
| `LevelGenerator` | Spanning tree + extra loops, places templates, builds GridMap. |
| `GridMap` | Walkability grid, built by probing wall colliders with `OverlapCircle`. |
| `FlowField` | BFS from the player; stores distance + direction per cell. |
| `FlowFieldService` | Rebuilds the field only on player cell change; direction lookups. |
| `WaveDirector` | Budget growth, enemy purchasing, spawn point filtering, pooling. |
| `EnemyDefinition` | ScriptableObject: stats, cost, weight, unlock time. |
| `Enemy` | Pooled; follows the flow field. |

## Scene setup

1. **Layers:** create a `Walls` layer. Put every wall tilemap and door blocker on it. Assign it to `wallMask` on `LevelGenerator` and `WaveDirector`.
2. **Room prefabs (8-12):** all the same size (default 16x12 cells at cell size 1), pivot at the lower-left corner, doorway about 3 cells wide centered on each side. Add a door blocker object per side (wall segment + collider), then wire them into `RoomTemplate`. Add 1-3 `SpawnPoint` children.
3. **Wall tilemaps:** Tilemap Collider 2D + Composite Collider 2D, and set the composite's **Generation Type to Synchronous**. If you skip this, the grid may be built before colliders exist and everything looks walkable.
4. **Enemy prefab:** `Enemy`, Rigidbody2D (gravity 0, freeze rotation Z), CircleCollider2D. Make one `EnemyDefinition` per type.
5. **Game object:** add `GameBootstrap`, `LevelGenerator`, `FlowFieldService`, `WaveDirector` and assign references.

## Notes

- Cell size is assumed to match your tilemap grid. If you use 1 unit tiles, leave it at 1.
- `rb.linearVelocity` is Unity 6. On 2022/2023 LTS, use `rb.velocity`.
- The probe radius (0.4 of a cell) means a cell next to a wall can count as blocked. If narrow corridors look closed, lower `probeRadiusFactor` or make doorways wider.
- Directions are cell-centered, so enemies can brush wall corners. Circle colliders slide off fine; the direct-chase radius handles the last few cells.

## Next steps (in order)

1. Test with two hand-built rooms and gizmos drawing the flow field before generating anything.
2. Add enemy recycling: if an enemy is more than ~60 cells from the player, call `Despawn()` and let the director respawn it.
3. Add contact damage and bullet hits (`Enemy.TakeDamage`).
4. Tune `baseBudgetPerSec`, `rampPerSec`, `unlockEverySeconds` in the inspector while playing.

## Big rooms + obstacle variants

- Set `roomW`/`roomH` larger (default now 32x24). A 3x3 grid is then 96x72 cells; the BFS is still trivial.
- In each room prefab, build obstacle groups as child objects and wire them into `RoomTemplate`:
  - **Layout Variants:** alternative full obstacle layouts. Exactly one is enabled per run.
  - **Optional Props:** single extras (crates, vending machines), each with its own spawn chance.
- Everything in those groups must be on the **Walls** layer so `GridMap` treats it as non-walkable and the flow field routes around it.
- Put `SpawnPoint` objects directly under the room root, **not** inside obstacle groups.
- The generator validates after randomizing: it flood-fills from the player start and requires that at least `minReachableFraction` of walkable cells and **every** spawn point are reachable. If not, it re-rolls obstacles (up to `maxObstacleAttempts`) and logs a warning if it never succeeds. A warning usually means an obstacle blocks a doorway or a spawn point sits inside one.
- Rules of thumb: keep 2+ cell gaps between obstacles, keep ~4 cells clear around each doorway, and place an obstacle roughly every 4-6 cells so there is always cover.
