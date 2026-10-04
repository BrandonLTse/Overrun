using UnityEngine;

/// Wires everything in the right order. Put on an empty "Game" object.
public class GameBootstrap : MonoBehaviour
{
    public LevelGenerator level;
    public FlowFieldService flow;
    public WaveDirector director;
    public Transform player;

    void Start()
    {
        level.Generate();                       // 1. rooms + walkability grid
        player.position = level.PlayerStart;
        flow.Init(level.Map);                   // 2. flow field over the merged grid
        director.Begin();                       // 3. start spawning
    }
}
