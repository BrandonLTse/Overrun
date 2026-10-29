using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum OverrunPistolFacing { Down, Right, Left, Up }
public enum OverrunPistolPose { IdleRun, Shoot, Reload, Death }

/// Imported hands-and-gun overlays, not replacements for the player's body.
public static class OverrunPistolClips
{
    public const string Folder = "OverrunArt/PistolAnimations/";
    static readonly Dictionary<string, Sprite[]> clips = new Dictionary<string, Sprite[]>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { clips.Clear(); }
    public static string SheetName(OverrunPistolFacing facing, OverrunPistolPose pose)
    {
        if (pose == OverrunPistolPose.Death && (facing == OverrunPistolFacing.Up || facing == OverrunPistolFacing.Down)) facing = OverrunPistolFacing.Right;
        string side = facing == OverrunPistolFacing.Down ? "down" : facing == OverrunPistolFacing.Up ? "up" : facing == OverrunPistolFacing.Right ? "side" : "side-left";
        string action = pose == OverrunPistolPose.IdleRun ? "idle-and-run-Sheet6" : pose == OverrunPistolPose.Shoot ? "shoot-Sheet3" : pose == OverrunPistolPose.Reload ? "Reload-Sheet11" : "Death-Sheet6";
        return "Pistol_" + side + "_" + action;
    }
    public static Sprite[] Frames(OverrunPistolFacing facing, OverrunPistolPose pose)
    {
        string name = SheetName(facing, pose);
        if (!clips.TryGetValue(name, out var frames))
        {
            frames = Resources.LoadAll<Sprite>(Folder + name).OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            int count = pose == OverrunPistolPose.Shoot ? 3 : pose == OverrunPistolPose.Reload ? 11 : 6;
            if (frames.Length != count) throw new InvalidOperationException($"Pistol sheet {name} has {frames.Length} frames, expected {count}.");
            clips.Add(name, frames);
        }
        return frames;
    }
    // Pixel anchors align the first pose's wrists to the idle overlay, preserving
    // the original full cells and subsequent authored recoil/reload motion.
    public static Vector2 Pivot(OverrunPistolFacing facing, OverrunPistolPose pose, float width, float height)
    {
        if (pose == OverrunPistolPose.IdleRun) return new Vector2(width * .5f, height * .5f);
        if (pose == OverrunPistolPose.Shoot)
            return facing == OverrunPistolFacing.Down ? new Vector2(2.5f, 6.5f) : facing == OverrunPistolFacing.Up ? new Vector2(2.5f, 4.5f) : facing == OverrunPistolFacing.Right ? new Vector2(4, 3.5f) : new Vector2(6, 3.5f);
        if (pose == OverrunPistolPose.Reload)
            return facing == OverrunPistolFacing.Down ? new Vector2(6.5f, 3.5f) : facing == OverrunPistolFacing.Up ? new Vector2(14.5f, 12.5f) : facing == OverrunPistolFacing.Right ? new Vector2(7, 7.5f) : new Vector2(5, 7.5f);
        return facing == OverrunPistolFacing.Left ? new Vector2(5, 5.5f) : new Vector2(8, 5.5f);
    }
    public static Vector2 AttachmentPixels(OverrunPistolFacing facing) => facing == OverrunPistolFacing.Down ? new Vector2(0, -6) :
        facing == OverrunPistolFacing.Up ? new Vector2(0, 4) : new Vector2(facing == OverrunPistolFacing.Left ? -3 : 3, -3);
}

public sealed class OverrunPistolAnimator : OverrunGunAnimator
{
    public const float ShootFPS = 16, ReloadFPS = 12;
    protected override string VisualName => "Pistol Hands";
    protected override float ShootingFPS => ShootFPS;
    protected override float ReloadingFPS => ReloadFPS;
    protected override Sprite[] Frames(OverrunPistolFacing direction, OverrunPistolPose action) => OverrunPistolClips.Frames(direction, action);
    protected override Vector2 Attachment(OverrunPistolFacing direction) => OverrunPistolClips.AttachmentPixels(direction);
    protected override Vector2 MuzzlePixels(OverrunPistolFacing direction)
    {
        Vector2 pivot = direction == OverrunPistolFacing.Left || direction == OverrunPistolFacing.Right ? new Vector2(4, 4.5f) : new Vector2(2.5f, 5.5f);
        Vector2 tip = direction == OverrunPistolFacing.Down ? new Vector2(2.5f, 2.5f) : direction == OverrunPistolFacing.Up ? new Vector2(2.5f, 9.5f) : direction == OverrunPistolFacing.Right ? new Vector2(7.5f, 6.5f) : new Vector2(.5f, 6.5f);
        return tip - pivot;
    }
    protected override void Death(Vector3 position, OverrunPistolFacing side, float scale, bool depthSorted) => OverrunPistolDeathVisual.Spawn(position, side, scale, depthSorted);
}

/// Both authored weapon overlays share frame timing and body synchronization.
public abstract class OverrunGunAnimator : MonoBehaviour
{
    protected abstract string VisualName { get; }
    protected abstract float ShootingFPS { get; }
    protected abstract float ReloadingFPS { get; }
    protected abstract Sprite[] Frames(OverrunPistolFacing direction, OverrunPistolPose action);
    protected abstract Vector2 Attachment(OverrunPistolFacing direction);
    protected abstract Vector2 MuzzlePixels(OverrunPistolFacing direction);
    protected abstract void Death(Vector3 position, OverrunPistolFacing side, float scale, bool depthSorted);
    SpriteRenderer visual;
    Transform bodyVisual;
    OverrunCharacterAnimator body;
    bool equipped;
    float elapsed;
    OverrunPistolFacing facing, lastSide = OverrunPistolFacing.Right;
    OverrunPistolPose pose;
    public SpriteRenderer Visual { get { EnsureVisual(); return visual; } }
    public OverrunPistolFacing Facing => facing;
    public OverrunPistolPose Pose => pose;
    public float VisualScale => bodyVisual ? bodyVisual.localScale.y : 16f / 9;
    protected void Awake() { EnsureVisual(); }
    void EnsureVisual()
    {
        if (!body) body = GetComponent<OverrunCharacterAnimator>();
        if (!bodyVisual) bodyVisual = transform.Find("Character Visual");
        if (visual) return;
        var child = transform.Find(VisualName);
        if (!child) { child = new GameObject(VisualName).transform; child.SetParent(transform, false); }
        visual = child.GetComponent<SpriteRenderer>(); if (!visual) visual = child.gameObject.AddComponent<SpriteRenderer>();
        visual.enabled = equipped;
    }
    public void SetEquipped(bool active)
    {
        EnsureVisual(); equipped = active; visual.enabled = active; pose = OverrunPistolPose.IdleRun; elapsed = 0; Apply();
    }
    public void ResetForRun()
    {
        facing = OverrunPistolFacing.Down; lastSide = OverrunPistolFacing.Right; pose = OverrunPistolPose.IdleRun; elapsed = 0; Apply();
    }
    public void SetAim(Vector2 aim)
    {
        facing = Mathf.Abs(aim.x) > Mathf.Abs(aim.y) ? (aim.x < 0 ? OverrunPistolFacing.Left : OverrunPistolFacing.Right) : (aim.y > 0 ? OverrunPistolFacing.Up : OverrunPistolFacing.Down);
        if (facing == OverrunPistolFacing.Left || facing == OverrunPistolFacing.Right) lastSide = facing;
        Apply();
    }
    public void Shoot() { if (!equipped) return; pose = OverrunPistolPose.Shoot; elapsed = 0; Apply(); }
    public void BeginReload() { if (!equipped) return; pose = OverrunPistolPose.Reload; elapsed = 0; Apply(); }
    public void EndReload() { pose = OverrunPistolPose.IdleRun; elapsed = 0; Apply(); }
    protected void LateUpdate() { if (!OverrunMenus.GameplayBlocked) Advance(Time.deltaTime); }
    public void Advance(float delta)
    {
        if (!equipped || delta < 0) return;
        elapsed += delta;
        if (pose == OverrunPistolPose.Shoot && elapsed >= 3 / ShootingFPS) { pose = OverrunPistolPose.IdleRun; elapsed = 0; }
        Apply();
    }
    void Apply()
    {
        EnsureVisual(); if (!equipped) { visual.enabled = false; return; }
        var frames = Frames(facing, pose);
        int index = pose == OverrunPistolPose.IdleRun ? (body ? body.FrameIndex % 6 : 0) : Mathf.Min((int)(elapsed * (pose == OverrunPistolPose.Reload ? ReloadingFPS : ShootingFPS)), frames.Length - 1);
        visual.enabled = true; visual.sprite = frames[index]; visual.flipX = false;
        visual.transform.localScale = Vector3.one * VisualScale;
        visual.transform.localPosition = Attachment(facing) * (VisualScale / 16);
        visual.sortingOrder = facing == OverrunPistolFacing.Up ? 49 : 51;
    }
    public Vector2 MuzzlePosition
    {
        get
        {
            return transform.TransformPoint((Attachment(facing) + MuzzlePixels(facing)) * (VisualScale / 16));
        }
    }
    public void SpawnDeath()
    {
        if (!equipped) return;
        var side = facing == OverrunPistolFacing.Left || facing == OverrunPistolFacing.Right ? facing : lastSide;
        Death(transform.position, side, VisualScale, GetComponent<OverrunDepthSort>());
    }
}

/// Cosmetic hands/gun at the death location, cleared when the next run starts.
public sealed class OverrunPistolDeathVisual : OverrunWeaponDeathVisual
{
    public static void Spawn(Vector3 position, OverrunPistolFacing side, float scale, bool depthSorted) =>
        Create<OverrunPistolDeathVisual>("Pistol Death Visual", position, OverrunPistolClips.Frames(side, OverrunPistolPose.Death), OverrunPistolClips.AttachmentPixels(side), scale, depthSorted);
}

public abstract class OverrunWeaponDeathVisual : MonoBehaviour
{
    SpriteRenderer visual;
    Sprite[] frames;
    float elapsed;
    protected static void Create<T>(string name, Vector3 position, Sprite[] frames, Vector2 attachment, float scale, bool depthSorted) where T : OverrunWeaponDeathVisual
    {
        OverrunWeaponDeathVisual effect = new GameObject(name).AddComponent<T>(); effect.transform.position = position;
        var child = new GameObject("Falling Weapon Hands"); child.transform.SetParent(effect.transform, false);
        child.transform.localPosition = attachment * (scale / 16); child.transform.localScale = Vector3.one * scale;
        effect.visual = child.AddComponent<SpriteRenderer>(); effect.visual.sortingOrder = 49;
        effect.frames = frames; effect.visual.sprite = effect.frames[0];
        if (depthSorted) OverrunDepthSort.Attach(effect.gameObject);
    }
    protected void Update() { if (!OverrunMenus.GameplayBlocked) Advance(Time.deltaTime); }
    public void Advance(float delta)
    {
        elapsed += Mathf.Max(0, delta); if (elapsed >= .9f) { gameObject.SetActive(false); Destroy(gameObject); return; }
        visual.sprite = frames[Mathf.Min((int)(elapsed * 10), 5)]; visual.color = new Color(1, 1, 1, 1 - Mathf.Clamp01((elapsed - .6f) / .3f));
    }
}
