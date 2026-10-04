using UnityEngine;

/// Empty child object inside a room prefab (vent, window, door...). Just a marker.
/// The generator collects all of these into one list for the wave director.
public class SpawnPoint : MonoBehaviour
{
    void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, 0.4f);
    }
}
