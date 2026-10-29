using UnityEngine;
using UnityEngine.Rendering;

/// Sort complete characters/props at their ground anchor, not their sprite centre.
public sealed class OverrunDepthSort : MonoBehaviour
{
    public float groundOffset;
    SortingGroup group;
    public static int OrderAt(float groundY) => Mathf.Clamp(16000 - Mathf.RoundToInt(groundY * 64), -30000, 30000);
    public static OverrunDepthSort Attach(GameObject root, float groundOffset = 0)
    {
        var sort = root.GetComponent<OverrunDepthSort>();
        if (!sort) sort = root.AddComponent<OverrunDepthSort>();
        sort.groundOffset = groundOffset; sort.Refresh(); return sort;
    }
    void OnEnable() { Refresh(); }
    void LateUpdate() { Refresh(); }
    public void Refresh()
    {
        if (!group) group = GetComponent<SortingGroup>();
        if (!group) group = gameObject.AddComponent<SortingGroup>();
        group.sortingOrder = OrderAt(transform.TransformPoint(new Vector3(0, groundOffset, 0)).y);
    }
}

public static class OverrunCollisionMaterial
{
    static PhysicsMaterial2D sliding;
    public static PhysicsMaterial2D Sliding
    {
        get
        {
            if (!sliding) sliding = new PhysicsMaterial2D("Overrun zero-friction contacts") { friction = 0, bounciness = 0 };
            return sliding;
        }
    }
}
