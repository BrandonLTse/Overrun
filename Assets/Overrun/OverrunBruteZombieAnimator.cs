using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Larger brutes share melee/pooling behavior but retain their own full clips.
public sealed class OverrunBruteZombieAnimator : OverrunSmallZombieAnimator
{
    public new const string Folder = "OverrunArt/ZombieBig/";
    static readonly Dictionary<string, Sprite[]> clips = new Dictionary<string, Sprite[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetBruteCache() { clips.Clear(); }

    protected override Sprite[] ClipFrames(Facing direction, Pose action) => Frames(direction, action);
    protected override float FrameRate(Pose action) => action == Pose.Idle ? 6 : action == Pose.Walk ? 8 : action == Pose.FirstAttack ? 10 : 12;
    protected override int StrikeFrame(Pose action) => 3;
    protected override string CorpseName => "Brute Zombie Death";

    public new static string SheetName(Facing direction, Pose pose)
    {
        bool death = pose == Pose.FirstDeath || pose == Pose.SecondDeath;
        if (death && (direction == Facing.Up || direction == Facing.Down)) direction = Facing.Right;
        string side = direction == Facing.Right ? "Side" : direction == Facing.Left ? "Side-left" : direction.ToString();
        string action = pose == Pose.Idle ? "Idle-Sheet6" : pose == Pose.Walk ? "Walk-Sheet8" :
            pose == Pose.FirstAttack ? "First-Attack-Sheet8" : pose == Pose.SecondAttack ? "Second-Attack-Sheet15" :
            pose == Pose.FirstDeath ? "First-Death-Sheet7" : "Second-Death-Sheet8";
        return "Zombie_Big_" + side + "_" + action;
    }

    public new static Sprite[] Frames(Facing direction, Pose pose)
    {
        string name = SheetName(direction, pose);
        if (!clips.TryGetValue(name, out var frames))
        {
            frames = Resources.LoadAll<Sprite>(Folder + name).OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            int expected = pose == Pose.Idle ? 6 : pose == Pose.SecondAttack ? 15 : pose == Pose.FirstDeath ? 7 : 8;
            if (frames.Length != expected) throw new InvalidOperationException($"Brute sheet {name} has {frames.Length} sprites; expected {expected}.");
            clips.Add(name, frames);
        }
        return frames;
    }
}
