using System.Collections.Generic;
using UnityEngine;

public sealed class OverrunUpgradeFx : MonoBehaviour
{
    static readonly Queue<OverrunUpgradeFx> pool = new Queue<OverrunUpgradeFx>();
    LineRenderer line;
    float age, radius;
    Vector2 center;
    bool ring;
    Color tint;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool() { pool.Clear(); }

    static OverrunUpgradeFx Get()
    {
        OverrunUpgradeFx fx = null;
        while (pool.Count > 0 && !fx) fx = pool.Dequeue();
        if (!fx)
        {
            fx = new GameObject("Upgrade Light").AddComponent<OverrunUpgradeFx>();
            fx.line = OverrunCombatVfx.Line(fx.transform, "Electric Pulse", .095f, Color.white, 63);
        }
        fx.age = 0; fx.gameObject.SetActive(true); return fx;
    }

    public static void Arc(Vector2 from, Vector2 to)
    {
        var fx = Get(); fx.ring = false; fx.tint = new Color(.4f, 1.5f, 2, 1);
        fx.line.positionCount = 5;
        Vector2 perpendicular = Vector2.Perpendicular((to - from).normalized);
        for (int i = 0; i < 5; i++) fx.line.SetPosition(i, Vector2.Lerp(from, to, i / 4f) + perpendicular * (i == 0 || i == 4 ? 0 : Random.Range(-.15f, .15f)));
        fx.line.startColor = fx.line.endColor = fx.tint;
    }

    public static void Ring(Vector2 center, float radius)
    {
        var fx = Get(); fx.ring = true; fx.center = center; fx.radius = radius;
        fx.tint = new Color(.3f, 1.4f, 2, .9f); fx.line.positionCount = 33; fx.DrawRing(0);
    }

    void Update()
    {
        if (OverrunMenus.GameplayBlocked) return;
        age += Time.deltaTime;
        float duration = ring ? .3f : .14f;
        if (age >= duration)
        {
            gameObject.SetActive(false);
            if (pool.Count < 64) pool.Enqueue(this); else Destroy(gameObject);
            return;
        }
        if (ring) DrawRing(age / duration);
        Color color = tint; color.a *= 1 - age / duration;
        line.startColor = line.endColor = color;
    }

    void DrawRing(float progress)
    {
        float distance = Mathf.Lerp(.25f, radius, progress);
        for (int i = 0; i < 33; i++)
        {
            float angle = i * Mathf.PI * 2 / 32;
            line.SetPosition(i, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance);
        }
        line.startColor = line.endColor = tint;
    }
}
