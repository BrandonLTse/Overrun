using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class OverrunRifleClips
{
    public const string Folder = "OverrunArt/RifleAnimations/";
    static readonly Dictionary<string, Sprite[]> clips = new Dictionary<string, Sprite[]>();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { clips.Clear(); }
    public static string SheetName(OverrunPistolFacing facing, OverrunPistolPose pose)
    {
        if (pose == OverrunPistolPose.Death && facing != OverrunPistolFacing.Left) facing = OverrunPistolFacing.Right;
        string side = facing == OverrunPistolFacing.Down ? "down" : facing == OverrunPistolFacing.Up ? "up" : facing == OverrunPistolFacing.Right ? "side" : "side-left";
        // The source pack capitalizes only the left-facing Reload sheet.
        string action = pose == OverrunPistolPose.IdleRun ? "idle-and-run-Sheet6" : pose == OverrunPistolPose.Shoot ? "shoot-Sheet3" : pose == OverrunPistolPose.Death ? "Death-Sheet6" : facing == OverrunPistolFacing.Left ? "Reload-Sheet8" : "reload-Sheet8";
        return "Gun_" + side + "_" + action;
    }
    public static Sprite[] Frames(OverrunPistolFacing facing, OverrunPistolPose pose)
    {
        string name = SheetName(facing, pose);
        if (!clips.TryGetValue(name, out var frames))
        {
            frames = Resources.LoadAll<Sprite>(Folder + name).OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            int count = pose == OverrunPistolPose.Shoot ? 3 : pose == OverrunPistolPose.Reload ? 8 : 6;
            if (frames.Length != count) throw new InvalidOperationException($"Rifle sheet {name} has {frames.Length} frames, expected {count}.");
            clips.Add(name, frames);
        }
        return frames;
    }
    // Full-cell pivots align the first authored pose to the idle wrists.
    public static Vector2 Pivot(OverrunPistolFacing facing, OverrunPistolPose pose, float width, float height)
    {
        if (pose == OverrunPistolPose.IdleRun) return new Vector2(width * .5f, height * .5f);
        if (pose == OverrunPistolPose.Shoot)
            return facing == OverrunPistolFacing.Down ? new Vector2(2.5f, 9) : facing == OverrunPistolFacing.Up ? new Vector2(2.5f, 6) : facing == OverrunPistolFacing.Right ? new Vector2(8, 4) : new Vector2(10, 4);
        if (pose == OverrunPistolPose.Reload)
            return facing == OverrunPistolFacing.Down ? new Vector2(3.5f, 6) : facing == OverrunPistolFacing.Up ? new Vector2(18.5f, 11) : facing == OverrunPistolFacing.Right ? new Vector2(11, 4) : new Vector2(9, 4);
        return new Vector2(9, 5);
    }
    public static Vector2 AttachmentPixels(OverrunPistolFacing facing) => facing == OverrunPistolFacing.Down ? new Vector2(0, -5.5f) :
        facing == OverrunPistolFacing.Up ? new Vector2(0, 6.5f) : new Vector2(facing == OverrunPistolFacing.Left ? -4 : 4, -3.5f);
}

public sealed class OverrunRifleAnimator : OverrunGunAnimator
{
    public const float ShootFPS = 24, ReloadFPS = 8;
    protected override string VisualName => "Rifle Hands";
    protected override float ShootingFPS => ShootFPS;
    protected override float ReloadingFPS => ReloadFPS;
    protected override Sprite[] Frames(OverrunPistolFacing direction, OverrunPistolPose action) => OverrunRifleClips.Frames(direction, action);
    protected override Vector2 Attachment(OverrunPistolFacing direction) => OverrunRifleClips.AttachmentPixels(direction);
    protected override Vector2 MuzzlePixels(OverrunPistolFacing direction)
    {
        bool side = direction == OverrunPistolFacing.Left || direction == OverrunPistolFacing.Right;
        Vector2 pivot = side ? new Vector2(8, 5) : new Vector2(2.5f, 8);
        Vector2 tip = direction == OverrunPistolFacing.Down ? new Vector2(2.5f, 1.5f) : direction == OverrunPistolFacing.Up ? new Vector2(2.5f, 15.5f) : direction == OverrunPistolFacing.Right ? new Vector2(15.5f, 7) : new Vector2(.5f, 7);
        return tip - pivot;
    }
    protected override void Death(Vector3 position, OverrunPistolFacing side, float scale, bool depthSorted) => OverrunRifleDeathVisual.Spawn(position, side, scale, depthSorted);
}

public sealed class OverrunRifleDeathVisual : OverrunWeaponDeathVisual
{
    public static void Spawn(Vector3 position, OverrunPistolFacing side, float scale, bool depthSorted) =>
        Create<OverrunRifleDeathVisual>("Rifle Death Visual", position, OverrunRifleClips.Frames(side, OverrunPistolPose.Death), OverrunRifleClips.AttachmentPixels(side), scale, depthSorted);
}
