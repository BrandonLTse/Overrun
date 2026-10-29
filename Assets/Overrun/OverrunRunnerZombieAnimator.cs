using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// The faster runner uses the supplied axe sheets, not the shambler's frames.
public sealed class OverrunRunnerZombieAnimator : OverrunSmallZombieAnimator
{
    public new const string Folder = "OverrunArt/ZombieAxe/";
    static readonly Dictionary<string, Sprite[]> clips = new Dictionary<string, Sprite[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetRunnerCache() { clips.Clear(); }

    protected override Sprite[] ClipFrames(Facing direction, Pose action) => Frames(direction, action);
    protected override float FrameRate(Pose action) => action == Pose.Idle ? 6 : action == Pose.FirstAttack ? 14 : 16;
    protected override int StrikeFrame(Pose action) => action == Pose.SecondAttack ? 4 : 3;
    protected override string CorpseName => "Runner Zombie Death";

    public new static string SheetName(Facing direction, Pose pose)
    {
        bool death = pose == Pose.FirstDeath || pose == Pose.SecondDeath;
        if (death && (direction == Facing.Up || direction == Facing.Down)) direction = Facing.Right;
        // The supplied set has no right-facing second death. Use its first death
        // rather than requesting a missing resource or borrowing shambler art.
        if (direction == Facing.Right && pose == Pose.SecondDeath) pose = Pose.FirstDeath;
        string side = direction == Facing.Right ? "Side" : direction == Facing.Left ? "Side-left" : direction.ToString();
        string action = pose == Pose.Idle ? "Idle-Sheet6" : pose == Pose.Walk ? "Walk-Sheet8" :
            pose == Pose.FirstAttack ? "First-Attack-Sheet7" : pose == Pose.SecondAttack ? "Second-Attack-Sheet9" :
            pose == Pose.FirstDeath ? "First-Death-Sheet6" : "Second-Death-Sheet7";
        return "Zombie_Axe_" + side + "_" + action;
    }

    public new static Sprite[] Frames(Facing direction, Pose pose)
    {
        string name = SheetName(direction, pose);
        if (!clips.TryGetValue(name, out var frames))
        {
            frames = Resources.LoadAll<Sprite>(Folder + name).OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            int expected = pose == Pose.Walk ? 8 : pose == Pose.FirstAttack ? 7 : pose == Pose.SecondAttack ? 9 :
                name.EndsWith("Second-Death-Sheet7", StringComparison.Ordinal) ? 7 : 6;
            if (frames.Length != expected) throw new InvalidOperationException($"Runner sheet {name} has {frames.Length} sprites; expected {expected}.");
            clips.Add(name, frames);
        }
        return frames;
    }
}
