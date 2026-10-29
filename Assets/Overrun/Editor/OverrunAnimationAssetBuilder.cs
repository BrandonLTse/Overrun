#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

[InitializeOnLoad]
public static class OverrunAnimationAssetBuilder
{
    const string Root = "Assets/Overrun/Art/Resources/OverrunArt/";
    const string AtlasPath = Root + "OverrunCharacterAnimationAtlas.png";
    const string ControllerPath = Root + "Idle.controller";
    const int Directions = 4;
    const int FramesPerClip = 6;
    // The base rows are the no-hands character. The weapon is rendered and
    // rotated separately by OverrunPlayer, which keeps aiming independent of
    // the character animation.
    const int IdleFullRow = 0;
    const int RunFullRow = 12;
    const float FrameRate = 10f;

    static bool building;

    static OverrunAnimationAssetBuilder() { EditorApplication.update += TryBuild; }

    [MenuItem("Overrun/Build Directional Character Animations")]
    public static void BuildFromMenu() { Build(true); }

    static void TryBuild()
    {
        if (building) return;
        var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);
        if (!atlas) return;
        Build(false);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var clipNames = new[] { "IdleFront", "IdleRight", "IdleLeft", "IdleBack", "RunFront", "RunRight", "RunLeft", "RunBack" };
        bool complete = HasControllerStates(controller, clipNames.Length) &&
            clipNames.Select(n => AssetDatabase.LoadAssetAtPath<AnimationClip>(Root + n + ".anim")).All(HasSpriteKeys);
        if (complete) EditorApplication.update -= TryBuild;
    }

    static void Build(bool force)
    {
        if (building) return;
        if (!AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath)) return;

        var atlasSprites = LoadAtlasSprites();
        if (atlasSprites.Length < 144)
        {
            Debug.LogWarning("Overrun animation builder found only " + atlasSprites.Length + " atlas sprites; expected 144.");
            return;
        }

        var clipNames = new[] { "IdleFront", "IdleRight", "IdleLeft", "IdleBack", "RunFront", "RunRight", "RunLeft", "RunBack" };
        var clipPaths = clipNames.Select(n => Root + n + ".anim").ToArray();
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        bool controllerNeedsRecreate = controller == null || controller.layers == null || controller.layers.Length == 0;
        bool controllerNeedsStates = !controllerNeedsRecreate && !HasControllerStates(controller, clipNames.Length);
        bool clipsMissingKeys = clipPaths.Select(AssetDatabase.LoadAssetAtPath<AnimationClip>).Any(c => !HasSpriteKeys(c));
        if (!force && !controllerNeedsRecreate && !controllerNeedsStates && !clipsMissingKeys) return;

        building = true;
        try
        {
            var clips = new AnimationClip[clipNames.Length];
            for (int i = 0; i < clipNames.Length; i++)
            {
                int row = i < Directions ? IdleFullRow + i : RunFullRow + (i - Directions);
                clips[i] = BuildClip(clipPaths[i], atlasSprites, row);
            }

            if (controllerNeedsRecreate)
            {
                if (controller != null)
                {
                    AssetDatabase.DeleteAsset(ControllerPath);
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }
                else
                {
                    AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                }
                return;
            }
            BuildController(controller, clips);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Overrun] Built six-frame idle/run clips and a valid directional Idle.controller.");
        }
        finally { building = false; }
    }

    static Sprite[] LoadAtlasSprites()
    {
        return AssetDatabase.LoadAllAssetRepresentationsAtPath(AtlasPath)
            .OfType<Sprite>()
            .OrderBy(s => ParseSuffix(s.name))
            .ToArray();
    }

    static int ParseSuffix(string name)
    {
        int split = name.LastIndexOf('_');
        return split >= 0 && int.TryParse(name.Substring(split + 1), out int value) ? value : int.MaxValue;
    }

    static bool HasSpriteKeys(AnimationClip clip)
    {
        if (clip == null) return false;
        var bindings = AnimationUtility.GetObjectReferenceCurveBindings(clip)
            .Where(b => b.type == typeof(SpriteRenderer) && b.propertyName == "m_Sprite")
            .ToArray();
        // Keep both paths so the same clip works with the current Character Visual
        // child setup and with a hand-made prefab whose SpriteRenderer is on its root.
        return bindings.Any(b => b.path == "Character Visual") && bindings.Any(b => b.path == string.Empty);
    }

    static bool HasControllerStates(AnimatorController controller, int expectedCount)
    {
        return controller != null && controller.layers != null && controller.layers.Length > 0 &&
            controller.layers[0].stateMachine != null && controller.layers[0].stateMachine.states.Length >= expectedCount;
    }

    static AnimationClip BuildClip(string path, Sprite[] atlasSprites, int row)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (clip == null)
        {
            clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(path) };
            AssetDatabase.CreateAsset(clip, path);
        }

        var binding = EditorCurveBinding.PPtrCurve("Character Visual", typeof(SpriteRenderer), "m_Sprite");
        var rootBinding = EditorCurveBinding.PPtrCurve(string.Empty, typeof(SpriteRenderer), "m_Sprite");
        var keys = new ObjectReferenceKeyframe[FramesPerClip];
        for (int frame = 0; frame < FramesPerClip; frame++)
        {
            keys[frame] = new ObjectReferenceKeyframe
            {
                time = frame / FrameRate,
                value = atlasSprites[row * FramesPerClip + frame]
            };
        }

        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
        AnimationUtility.SetObjectReferenceCurve(clip, rootBinding, keys);
        clip.frameRate = FrameRate;
        clip.wrapMode = WrapMode.Loop;
        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = true;
        settings.stopTime = FramesPerClip / FrameRate;
        AnimationUtility.SetAnimationClipSettings(clip, settings);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static void BuildController(AnimatorController controller, AnimationClip[] clips)
    {
        if (controller.layers == null || controller.layers.Length == 0)
        {
            Debug.LogError("[Overrun] Refusing to add states to a controller without a valid layer.");
            return;
        }

        if (!controller.parameters.Any(p => p.name == "Moving"))
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
        if (!controller.parameters.Any(p => p.name == "Direction"))
            controller.AddParameter("Direction", AnimatorControllerParameterType.Int);

        var stateMachine = controller.layers[0].stateMachine;
        var existingStates = stateMachine.states;

        // Existing generated controllers are already wired. Update their motions
        // in place; destroying states and immediately recreating them can leave
        // Unity's native AnimatorStateMachine in an invalid state and crash the editor.
        if (existingStates.Length >= clips.Length)
        {
            for (int i = 0; i < clips.Length; i++)
            {
                existingStates[i].state.motion = clips[i];
                existingStates[i].state.writeDefaultValues = false;
            }
            stateMachine.defaultState = existingStates[0].state;
            EditorUtility.SetDirty(controller);
            return;
        }

        // A partially populated controller is safer to repair by recreating the
        // asset on a later editor update than by mixing old and new native states.
        if (existingStates.Length > 0)
        {
            Debug.LogWarning("[Overrun] Directional controller has partial state data; leaving it unchanged for a safe rebuild.");
            return;
        }

        var states = new AnimatorState[clips.Length];
        for (int i = 0; i < clips.Length; i++)
        {
            states[i] = stateMachine.AddState(clips[i].name);
            states[i].motion = clips[i];
            states[i].writeDefaultValues = false;
        }
        stateMachine.defaultState = states[0];

        for (int i = 0; i < states.Length; i++)
        {
            var transition = stateMachine.AddAnyStateTransition(states[i]);
            transition.hasExitTime = false;
            transition.duration = 0f;
            transition.canTransitionToSelf = false;
            bool running = i >= Directions;
            int direction = i % Directions;
            transition.AddCondition(running ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, "Moving");
            transition.AddCondition(AnimatorConditionMode.Equals, direction, "Direction");
        }
        EditorUtility.SetDirty(controller);
    }
}
#endif
