using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// Shared melee/animation lifecycle; subclasses supply their own imported clips.
public class OverrunSmallZombieAnimator : MonoBehaviour
{
    public enum Facing { Down, Right, Left, Up }
    public enum Pose { Idle, Walk, FirstAttack, SecondAttack, FirstDeath, SecondDeath }
    public const string Folder = "OverrunArt/ZombieSmall/";
    static readonly Dictionary<string, Sprite[]> clips = new Dictionary<string, Sprite[]>();
    SpriteRenderer visual;
    Enemy enemy;
    OverrunPlayer attackTarget;
    Vector2 motion;
    float elapsed;
    bool struck;
    int attackCount;
    Facing facing = Facing.Down, lastSide = Facing.Right;
    Pose pose = Pose.Idle;
    public bool IsAttacking => pose == Pose.FirstAttack || pose == Pose.SecondAttack;
    public Facing Direction => facing;
    public Pose CurrentPose => pose;
    public SpriteRenderer Visual => visual;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { clips.Clear(); }

    protected virtual void Awake() { visual = GetComponentInChildren<SpriteRenderer>(true); enemy = GetComponent<Enemy>(); }
    protected virtual void Update() { if (!OverrunMenus.GameplayBlocked) Advance(Time.deltaTime); }
    protected virtual void OnDisable() { attackTarget = null; }
    protected virtual Sprite[] ClipFrames(Facing direction, Pose action) => Frames(direction, action);
    protected virtual float FrameRate(Pose action) => action == Pose.Idle ? 6 : action == Pose.Walk ? 10 : action == Pose.FirstAttack ? 8 : 16;
    protected virtual int StrikeFrame(Pose action) => action == Pose.SecondAttack ? 3 : 2;
    protected virtual string CorpseName => "Small Zombie Death";

    public void ResetForSpawn()
    {
        if (!visual) visual = GetComponentInChildren<SpriteRenderer>(true);
        if (!enemy) enemy = GetComponent<Enemy>();
        facing = Facing.Down; lastSide = Facing.Right; pose = Pose.Idle;
        elapsed = 0; motion = Vector2.zero; struck = false; attackCount = 0; attackTarget = null;
        Apply();
    }

    public void SetMotion(Vector2 direction)
    {
        motion = direction;
        if (IsAttacking) return;
        Facing next = direction.sqrMagnitude > .001f ? DirectionFrom(direction) : facing;
        Pose nextPose = direction.sqrMagnitude > .001f ? Pose.Walk : Pose.Idle;
        if (facing != next || pose != nextPose) elapsed = 0;
        facing = next; pose = nextPose;
        if (facing == Facing.Left || facing == Facing.Right) lastSide = facing;
        Apply();
    }

    public bool TryAttack(OverrunPlayer target)
    {
        bool started = BeginAttack(target, (attackCount + 1) % 3 == 0);
        if (started) attackCount++;
        return started;
    }

    public bool BeginAttack(OverrunPlayer target, bool secondary)
    {
        if (!target || !enemy || !enemy.IsAlive || IsAttacking) return false;
        attackTarget = target;
        facing = DirectionFrom((Vector2)(target.transform.position - transform.position));
        if (facing == Facing.Left || facing == Facing.Right) lastSide = facing;
        pose = secondary ? Pose.SecondAttack : Pose.FirstAttack;
        OverrunAudio.World(OverrunSound.ZombieAttack, transform.position, secondary ? .9f : .7f, enemy.ContactDamage >= 20 ? .8f : 1);
        elapsed = 0; struck = false; Apply();
        return true;
    }

    public void Advance(float deltaTime)
    {
        if (!visual || deltaTime < 0) return;
        elapsed += deltaTime;
        if (IsAttacking)
        {
            float fps = FrameRate(pose);
            float strike = StrikeFrame(pose) / fps;
            if (!struck && elapsed >= strike)
            {
                struck = true;
                if (enemy) enemy.MeleeStrike(attackTarget);
            }
            if (elapsed >= ClipFrames(facing, pose).Length / fps)
            {
                attackTarget = null; pose = motion.sqrMagnitude > .001f ? Pose.Walk : Pose.Idle; elapsed = 0;
            }
        }
        Apply();
    }

    void Apply()
    {
        if (!visual) return;
        var frames = ClipFrames(facing, pose);
        float fps = FrameRate(pose);
        int index = IsAttacking ? Mathf.Min((int)(elapsed * fps), frames.Length - 1) : (int)(elapsed * fps) % frames.Length;
        visual.sprite = frames[index]; visual.flipX = false;
        // Fixed scale across all clips: death frames and wider attacks must not
        // resize the character or its physics collider frame by frame.
        visual.transform.localScale = Vector3.one * 2;
    }

    public void SpawnDeath()
    {
        if (!visual) return;
        var side = facing == Facing.Left || facing == Facing.Right ? facing : lastSide;
        var death = UnityEngine.Random.value < .5f ? Pose.FirstDeath : Pose.SecondDeath;
        OverrunZombieCorpse.Spawn(visual, ClipFrames(side, death), CorpseName);
    }

    public static Facing DirectionFrom(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y)) return direction.x < 0 ? Facing.Left : Facing.Right;
        return direction.y > 0 ? Facing.Up : Facing.Down;
    }

    public static string SheetName(Facing direction, Pose pose)
    {
        bool death = pose == Pose.FirstDeath || pose == Pose.SecondDeath;
        if (death && (direction == Facing.Up || direction == Facing.Down)) direction = Facing.Right;
        string side = direction == Facing.Right ? "Side" : direction == Facing.Left ? "Side-left" : direction.ToString();
        string action = pose == Pose.Idle ? "Idle-Sheet6" : pose == Pose.Walk ? (direction == Facing.Down ? "walk-Sheet6" : "Walk-Sheet6") :
            pose == Pose.FirstAttack ? "First-Attack-Sheet4" : pose == Pose.SecondAttack ? "Second-Attack-Sheet11" :
            pose == Pose.FirstDeath ? "First-Death-Sheet6" : "Second-Death-Sheet7";
        return "Zombie_Small_" + side + "_" + action;
    }

    public static Sprite[] Frames(Facing direction, Pose pose)
    {
        string name = SheetName(direction, pose);
        if (!clips.TryGetValue(name, out var frames))
        {
            frames = Resources.LoadAll<Sprite>(Folder + name).OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
            int expected = pose == Pose.FirstAttack ? 4 : pose == Pose.SecondAttack ? 11 : pose == Pose.SecondDeath ? 7 : 6;
            if (frames.Length != expected) throw new InvalidOperationException($"Zombie sheet {name} has {frames.Length} sprites; expected {expected}.");
            clips.Add(name, frames);
        }
        return frames;
    }
}

/// Death visuals are independent of the live-enemy pool and have no colliders.
public sealed class OverrunZombieCorpse : MonoBehaviour
{
    static readonly Queue<OverrunZombieCorpse> pool = new Queue<OverrunZombieCorpse>();
    SpriteRenderer visual;
    Sprite[] frames;
    float elapsed;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetPool() { pool.Clear(); }

    public static OverrunZombieCorpse Spawn(SpriteRenderer source, OverrunSmallZombieAnimator.Facing side)
    {
        var pose = UnityEngine.Random.value < .5f ? OverrunSmallZombieAnimator.Pose.FirstDeath : OverrunSmallZombieAnimator.Pose.SecondDeath;
        return Spawn(source, OverrunSmallZombieAnimator.Frames(side, pose), "Small Zombie Death");
    }

    public static OverrunZombieCorpse Spawn(SpriteRenderer source, Sprite[] frames, string corpseName)
    {
        OverrunZombieCorpse corpse = null;
        while (pool.Count > 0 && !corpse) corpse = pool.Dequeue();
        if (!corpse)
        {
            corpse = new GameObject(corpseName).AddComponent<OverrunZombieCorpse>();
            corpse.visual = corpse.gameObject.AddComponent<SpriteRenderer>();
        }
        corpse.name = corpseName;
        corpse.frames = frames;
        corpse.elapsed = 0;
        corpse.transform.position = source.transform.position;
        corpse.transform.localScale = source.transform.lossyScale;
        if (source.GetComponentInParent<OverrunDepthSort>()) OverrunDepthSort.Attach(corpse.gameObject);
        corpse.visual.sortingLayerID = source.sortingLayerID;
        corpse.visual.sortingOrder = source.sortingOrder - 1;
        corpse.visual.color = Color.white; corpse.visual.flipX = false; corpse.visual.sprite = corpse.frames[0];
        corpse.gameObject.SetActive(true);
        return corpse;
    }

    void Update() { if (!OverrunMenus.GameplayBlocked) Advance(Time.deltaTime); }
    public void Advance(float deltaTime)
    {
        if (!gameObject.activeSelf || frames == null) return;
        elapsed += deltaTime;
        float duration = frames.Length / 10f;
        if (elapsed > duration + .8f)
        {
            gameObject.SetActive(false);
            if (pool.Count < 64) pool.Enqueue(this); else Destroy(gameObject);
            return;
        }
        visual.sprite = frames[Mathf.Min((int)(elapsed * 10), frames.Length - 1)];
        visual.color = new Color(1, 1, 1, 1 - Mathf.Clamp01((elapsed - duration - .35f) / .45f));
    }
}
