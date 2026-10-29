using System.Collections.Generic;
using UnityEngine;

/// Shared procedural light streaks; no projectile sprite atlas or bloom setup required.
public static class OverrunCombatVfx
{
    static Material material;
    static Sprite pixel;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { material = null; pixel = null; }

    public static LineRenderer Line(Transform parent, string name, float width, Color color, int order = 60)
    {
        if (!material)
        {
            var shader = Resources.Load<Shader>("OverrunArt/TracerGlow");
            if (!shader) shader = Shader.Find("Sprites/Default");
            material = new Material(shader) { name = "Shared Tracer Light" };
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.widthMultiplier = width;
        line.startColor = line.endColor = color;
        line.numCapVertices = 2;
        // World actors now use ground-depth groups. Preserve the original
        // above-world tracer/flash/pulse ordering without affecting gun-part order.
        line.sortingOrder = order >= 20000 ? order : 20000 + order;
        line.textureMode = LineTextureMode.Stretch;
        return line;
    }

    public static SpriteRenderer GunPart(Transform parent, string name, Vector2 size, Vector2 position, Color color)
    {
        if (!pixel)
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "Weapon Pixel", filterMode = FilterMode.Point };
            texture.SetPixel(0, 0, Color.white); texture.Apply();
            pixel = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1);
        }
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = new Vector3(size.x, size.y, 1);
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = pixel;
        renderer.color = color;
        renderer.sortingOrder = 51;
        return renderer;
    }

    public static void Muzzle(Vector2 position, Vector2 direction) { OverrunShotBurst.Emit(position, direction, false, false); }
    public static void Impact(Vector2 position, Vector2 normal, bool flesh) { OverrunShotBurst.Emit(position, normal, true, flesh); }
}

/// Pooled, very short-lived muzzle flashes and sparks. At most 64 bursts stay cached.
public sealed class OverrunShotBurst : MonoBehaviour
{
    static readonly Queue<OverrunShotBurst> pool = new Queue<OverrunShotBurst>();
    const int Rays = 6;
    readonly LineRenderer[] lines = new LineRenderer[Rays];
    readonly Vector2[] velocities = new Vector2[Rays];
    readonly Color[] colors = new Color[Rays];
    Vector2 origin;
    float age, lifetime;
    int bornFrame;
    bool impact;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool() { pool.Clear(); }

    public static void Emit(Vector2 position, Vector2 direction, bool impact, bool flesh)
    {
        OverrunShotBurst burst = null;
        while (pool.Count > 0 && !burst) burst = pool.Dequeue();
        if (!burst)
        {
            burst = new GameObject("Shot Light Burst").AddComponent<OverrunShotBurst>();
            for (int i = 0; i < Rays; i++) burst.lines[i] = OverrunCombatVfx.Line(burst.transform, "Light Ray " + i, .08f, Color.white, 62);
        }
        burst.origin = position;
        burst.transform.position = position;
        burst.impact = impact;
        burst.age = 0;
        burst.bornFrame = Time.frameCount;
        burst.lifetime = impact ? .16f : .055f;
        burst.gameObject.SetActive(true);
        Vector2 tangent = new Vector2(-direction.y, direction.x);
        for (int i = 0; i < Rays; i++)
        {
            float spread = (i - (Rays - 1) * .5f) / (Rays * .5f);
            burst.velocities[i] = (direction * (impact ? 1.8f : 4.8f) + tangent * spread * (impact ? 5.5f : 3f)) * Random.Range(.75f, 1.25f);
            burst.colors[i] = flesh ? new Color(1f, .16f, .045f, .85f) : new Color(1f, .6f, .16f, .9f);
            if (i == Rays / 2) burst.colors[i] = new Color(1.5f, 1.3f, .85f, 1f);
            burst.lines[i].widthMultiplier = impact ? .055f : .13f;
        }
        burst.Draw();
    }

    void Update()
    {
        if (OverrunMenus.GameplayBlocked) return;
        if (Time.frameCount == bornFrame) return;
        age += Time.deltaTime;
        if (age >= lifetime)
        {
            gameObject.SetActive(false);
            if (pool.Count < 64) pool.Enqueue(this); else Destroy(gameObject);
            return;
        }
        Draw();
    }

    void Draw()
    {
        float fade = 1 - age / lifetime;
        for (int i = 0; i < Rays; i++)
        {
            Vector2 head = origin + velocities[i] * (impact ? age : .045f + age * .3f);
            Vector2 tail = impact ? head - velocities[i] * .018f : origin;
            lines[i].SetPosition(0, tail); lines[i].SetPosition(1, head);
            Color color = colors[i]; color.a *= fade * fade;
            lines[i].startColor = lines[i].endColor = color;
        }
    }
}

/// Authored weapon hands follow the body; projectile aim remains continuous.
public sealed class OverrunWeaponFeedback : MonoBehaviour
{
    OverrunPistolAnimator pistol;
    OverrunRifleAnimator rifle;
    bool usingPistol = true;
    OverrunGunAnimator ActiveAnimation => usingPistol ? (OverrunGunAnimator)pistol : rifle;
    public SpriteRenderer Visual => ActiveAnimation ? ActiveAnimation.Visual : null;
    public OverrunPistolAnimator PistolAnimation => pistol;
    public OverrunRifleAnimator RifleAnimation => rifle;
    public Vector2 MuzzlePosition => ActiveAnimation ? ActiveAnimation.MuzzlePosition : (Vector2)transform.position;
    public void ResetForRun() { transform.localRotation = Quaternion.identity; if (pistol) pistol.ResetForRun(); if (rifle) rifle.ResetForRun(); }

    void Awake()
    {
        EnsureVisual(); Equip(OverrunWeapon.Pistol);
    }

    void EnsureVisual()
    {
        var owner = transform.parent ? transform.parent.gameObject : gameObject;
        if (!pistol) { pistol = owner.GetComponent<OverrunPistolAnimator>(); if (!pistol) pistol = owner.AddComponent<OverrunPistolAnimator>(); }
        if (!rifle) { rifle = owner.GetComponent<OverrunRifleAnimator>(); if (!rifle) rifle = owner.AddComponent<OverrunRifleAnimator>(); }
        var old = transform.Find("Gun Visual");
        if (old) { old.gameObject.SetActive(false); Destroy(old.gameObject); }
    }

    public void Equip(OverrunWeapon equipped)
    {
        EnsureVisual();
        usingPistol = equipped == OverrunWeapon.Pistol;
        pistol.SetEquipped(usingPistol); rifle.SetEquipped(!usingPistol);
    }

    public void Shot(Vector2 position, Vector2 direction)
    {
        if (ActiveAnimation) ActiveAnimation.Shoot();
        OverrunCombatVfx.Muzzle(position, direction);
    }
    public void SetAim(Vector2 aim) { if (pistol) pistol.SetAim(aim); if (rifle) rifle.SetAim(aim); }
    public void BeginReload() { if (ActiveAnimation) ActiveAnimation.BeginReload(); }
    public void EndReload() { if (ActiveAnimation) ActiveAnimation.EndReload(); }
    public void SpawnDeath() { if (ActiveAnimation) ActiveAnimation.SpawnDeath(); }
}
