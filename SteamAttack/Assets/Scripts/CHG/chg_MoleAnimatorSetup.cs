#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// 석탄 두더지 Animator Controller(chg_MoleAnimator)를 만듭니다.
/// 처음 한 번은 Unity가 스크립트를 불러올 때 자동으로 만들고, 이후에는 메뉴 CHG/Build Mole Animator 로 다시 만들 수 있습니다.
///
/// 파라미터 (적 AI에서 사용)
///   Speed (float)          : 0.1 이상이면 걷기, 미만이면 대기
///   WalkAnimSpeed (float)  : 걷기 재생 속도 배율 (기본 1). 이동 속도에 맞춰 조절
///   Claw / Charge / Burrow / Emerge / Knockdown / GetUp (trigger)
/// 흐름
///   Idle ⇄ Walk (Speed)
///   어디서든 Claw · Charge · Burrow · Emerge · Knockdown 트리거로 진입
///   Claw · Charge · Emerge · GetUp → 끝나면 Idle
///   Burrow → 마지막(땅속) 자세 유지 → Emerge 트리거
///   Knockdown → 마지막(누운) 자세 유지 → GetUp 트리거
/// </summary>
[InitializeOnLoad]
public static class chg_MoleAnimatorSetup
{
    const string AnimDir = "Assets/Animations/CHG/";
    const string ControllerPath = AnimDir + "chg_MoleAnimator.controller";
    const string ModelPath = "Assets/Arts/Models/CHG/chg_mole.fbx";
    const string AutoKey = "chg_MoleAnimator_v1";
    static readonly string[] Clips = { "idle", "walk", "claw", "charge", "burrow", "emerge", "knockdown", "getup" };
    static readonly string[] Loops = { "idle", "walk" };

    static chg_MoleAnimatorSetup()
    {
        EditorApplication.delayCall += () =>
        {
            if (EditorPrefs.GetBool(AutoKey, false)) return;
            if (!File.Exists(ClipPath("idle"))) return;          // FBX가 아직 없으면 다음 기회에
            if (File.Exists(ControllerPath)) { EditorPrefs.SetBool(AutoKey, true); return; }   // 이미 있으면 건드리지 않음
            EditorPrefs.SetBool(AutoKey, true);
            Build();
        };
    }

    static string ClipPath(string key) => AnimDir + "chg_anim_mole_" + key + ".fbx";

    [MenuItem("CHG/Build Mole Animator")]
    public static void Build()
    {
        // 1) 가져오기 설정: Generic, 대기·걷기만 반복
        SetGeneric(ModelPath, false);
        foreach (var k in Clips) SetGeneric(ClipPath(k), Loops.Contains(k));

        // 2) 클립 불러오기
        var clip = new System.Collections.Generic.Dictionary<string, AnimationClip>();
        foreach (var k in Clips)
        {
            var c = AssetDatabase.LoadAllAssetsAtPath(ClipPath(k)).OfType<AnimationClip>()
                .FirstOrDefault(a => !a.name.StartsWith("__preview__"));
            if (!c) { Debug.LogError("[CHG] 두더지 애니메이션을 찾지 못했습니다: " + ClipPath(k)); return; }
            clip[k] = c;
        }

        // 3) 컨트롤러 만들기 (있으면 새로 만듦)
        if (File.Exists(ControllerPath)) AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        ctrl.AddParameter("Speed", AnimatorControllerParameterType.Float);
        ctrl.AddParameter("WalkAnimSpeed", AnimatorControllerParameterType.Float);
        foreach (var t in new[] { "Claw", "Charge", "Burrow", "Emerge", "Knockdown", "GetUp" })
            ctrl.AddParameter(t, AnimatorControllerParameterType.Trigger);
        var ps = ctrl.parameters;
        foreach (var p in ps) if (p.name == "WalkAnimSpeed") p.defaultFloat = 1f;
        ctrl.parameters = ps;

        var sm = ctrl.layers[0].stateMachine;
        var idle = S(sm, clip, "Idle", "idle", new Vector2(300, 0));
        var walk = S(sm, clip, "Walk", "walk", new Vector2(300, 120));
        walk.speedParameterActive = true; walk.speedParameter = "WalkAnimSpeed";
        var claw = S(sm, clip, "Claw", "claw", new Vector2(600, -120));
        var charge = S(sm, clip, "Charge", "charge", new Vector2(600, -40));
        var burrow = S(sm, clip, "Burrow", "burrow", new Vector2(600, 40));
        var emerge = S(sm, clip, "Emerge", "emerge", new Vector2(900, 40));
        var down = S(sm, clip, "Knockdown", "knockdown", new Vector2(600, 160));
        var getup = S(sm, clip, "GetUp", "getup", new Vector2(900, 160));
        sm.defaultState = idle;

        // 대기 ⇄ 걷기
        var t1 = idle.AddTransition(walk); Quick(t1, 0.15f); t1.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
        var t2 = walk.AddTransition(idle); Quick(t2, 0.15f); t2.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        // 어디서든 트리거로 진입
        AnyTo(sm, claw, "Claw", 0.1f);
        AnyTo(sm, charge, "Charge", 0.1f);
        AnyTo(sm, burrow, "Burrow", 0.1f);
        AnyTo(sm, emerge, "Emerge", 0f);        // 땅속 자세에서 바로 시작
        AnyTo(sm, down, "Knockdown", 0.05f);

        // 누운 자세에서 일어나기
        var t3 = down.AddTransition(getup); Quick(t3, 0.05f); t3.AddCondition(AnimatorConditionMode.If, 0, "GetUp");

        // 끝나면 대기로
        foreach (var s in new[] { claw, charge, emerge, getup })
        {
            var t = s.AddTransition(idle);
            t.hasExitTime = true; t.exitTime = 1f; t.hasFixedDuration = true; t.duration = 0.15f;
        }
        // Burrow / Knockdown 은 마지막 자세에서 멈춰 다음 트리거를 기다림 (반복 없음)

        EditorUtility.SetDirty(ctrl);
        AssetDatabase.SaveAssets();
        Debug.Log("[CHG][검증] chg_MoleAnimator 생성: 상태 " + sm.states.Length + "개, 파라미터 " + ctrl.parameters.Length + "개 (" + ControllerPath + ")");
    }

    static AnimatorState S(AnimatorStateMachine sm, System.Collections.Generic.Dictionary<string, AnimationClip> clip, string name, string key, Vector2 pos)
    {
        var s = sm.AddState(name, pos);
        s.motion = clip[key];
        s.writeDefaultValues = false;
        return s;
    }

    static void Quick(AnimatorStateTransition t, float dur)
    {
        t.hasExitTime = false; t.hasFixedDuration = true; t.duration = dur;
    }

    static void AnyTo(AnimatorStateMachine sm, AnimatorState s, string trigger, float dur)
    {
        var t = sm.AddAnyStateTransition(s);
        Quick(t, dur); t.canTransitionToSelf = false;
        t.AddCondition(AnimatorConditionMode.If, 0, trigger);
    }

    static void SetGeneric(string path, bool loop)
    {
        var mi = AssetImporter.GetAtPath(path) as ModelImporter;
        if (mi == null) return;
        bool changed = false;
        if (mi.animationType != ModelImporterAnimationType.Generic) { mi.animationType = ModelImporterAnimationType.Generic; changed = true; }
        if (path != ModelPath)
        {
            var clips = mi.clipAnimations;
            if (clips == null || clips.Length == 0) clips = mi.defaultClipAnimations;
            foreach (var c in clips) if (c.loopTime != loop) { c.loopTime = loop; changed = true; }
            mi.clipAnimations = clips;
        }
        if (changed) mi.SaveAndReimport();
    }
}
#endif
