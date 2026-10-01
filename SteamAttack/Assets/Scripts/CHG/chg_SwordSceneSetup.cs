#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 메뉴: CHG > Setup Sword Scene
/// 1) 달리기/대기 클립 Loop 설정
/// 2) chg_CharAnimator.controller 생성 (모든 chg_char 클립을 상태로 등록, 트리거로 전환)
/// 3) 캐릭터 + 칼(오른손에 부착)을 배치한 chg_SwordScene.unity 생성
/// </summary>
public static class chg_SwordSceneSetup
{
    const string AnimDir = "Assets/Animations/CHG";
    const string CharPath = AnimDir + "/chg_char,t-pose.fbx";   // T-포즈 모델에 칼을 맞춘 뒤 애니메이터로 동작 재생
    const string SwordPath = AnimDir + "/chg_sword.fbx";
    const string ControllerPath = AnimDir + "/chg_CharAnimator.controller";
    const string ScenePath = "Assets/Scenes/CHG/chg_SwordScene.unity";
    const string DefaultState = "standby";
    static readonly string[] LoopClips = { "standby", "run", "fastrun" };

    [MenuItem("CHG/Setup Sword Scene")]
    public static void Setup()
    {
        var clipFiles = Directory.GetFiles(AnimDir, "chg_char,*.fbx")
            .Select(p => p.Replace('\\', '/')).ToList();

        SetLoops(clipFiles);
        var controller = BuildController(clipFiles);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // --- 캐릭터 ---
        var charAsset = AssetDatabase.LoadAssetAtPath<GameObject>(CharPath);
        var ch = (GameObject)PrefabUtility.InstantiatePrefab(charAsset);
        ch.name = "chg_Character";
        ch.transform.position = Vector3.zero;
        var anim = ch.GetComponent<Animator>();
        if (anim == null) anim = ch.AddComponent<Animator>();
        anim.runtimeAnimatorController = controller;
        anim.applyRootMotion = false;

        // --- 칼 ---
        var hand = FindBone(ch.transform, "RightHand");
        var swordAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SwordPath);
        var sw = (GameObject)PrefabUtility.InstantiatePrefab(swordAsset);
        sw.name = "chg_Sword";
        var swAnim = sw.GetComponent<Animator>();
        if (swAnim != null) swAnim.enabled = false;

        if (hand != null) FitSwordToHand(sw.transform, ch.transform, hand);
        else Debug.LogWarning("[CHG] RightHand 뼈를 찾지 못했습니다. 칼을 원점에 둡니다.");

        // --- 바닥 / 카메라 ---
        var b = GetBounds(ch.transform);
        float h = Mathf.Max(0.1f, b.size.y);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "chg_Ground";
        ground.transform.position = new Vector3(0, b.min.y, 0);
        ground.transform.localScale = Vector3.one * Mathf.Max(1f, h);

        var cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = b.center + new Vector3(h * 0.9f, h * 0.2f, h * 2.0f);
            cam.transform.LookAt(b.center);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = sw;
        Debug.Log("[CHG] 완료: " + ScenePath + " (칼: chg_Sword, 오른손: " + (hand ? hand.name : "없음") + ")");
    }

    // ---------------------------------------------------------------- 애니메이션

    static string StateName(string path) =>
        Path.GetFileNameWithoutExtension(path).Replace("chg_char,", "");

    static void SetLoops(List<string> files)
    {
        foreach (var path in files)
        {
            bool loop = LoopClips.Contains(StateName(path));
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) continue;
            var clips = imp.clipAnimations.Length > 0 ? imp.clipAnimations : imp.defaultClipAnimations;
            bool changed = false;
            foreach (var c in clips)
                if (c.loopTime != loop) { c.loopTime = loop; changed = true; }
            if (changed || imp.clipAnimations.Length == 0)
            {
                imp.clipAnimations = clips;
                imp.SaveAndReimport();
            }
        }
    }

    static AnimatorController BuildController(List<string> files)
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);
        var ctrl = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var sm = ctrl.layers[0].stateMachine;

        AnimatorState idle = null;
        var states = new List<(AnimatorState st, string name)>();
        foreach (var path in files)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) continue;
            string n = StateName(path);
            var st = sm.AddState(n);
            st.motion = clip;
            states.Add((st, n));
            if (n == DefaultState) idle = st;
        }
        if (idle != null) sm.defaultState = idle;

        foreach (var (st, n) in states)
        {
            ctrl.AddParameter(n, AnimatorControllerParameterType.Trigger);
            var t = sm.AddAnyStateTransition(st);
            t.AddCondition(AnimatorConditionMode.If, 0, n);
            t.duration = 0.15f;
            t.canTransitionToSelf = false;

            // 한 번만 재생되는 동작은 끝나면 대기로 복귀
            if (idle != null && st != idle && !LoopClips.Contains(n))
            {
                var back = st.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.2f;
            }
        }
        AssetDatabase.SaveAssets();
        return ctrl;
    }

    // ---------------------------------------------------------------- 칼 맞추기

    static Transform FindBone(Transform root, string suffix)
    {
        return root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name.EndsWith(suffix));
    }

    // Blender에서 T-포즈 기준으로 맞춰 본 값 (Unity 좌표로 변환)
    static readonly Vector3 GripFromHand = new Vector3(0.07f, -0.007f, 0f);   // 손목 뼈 → 주먹 중심
    static readonly Vector3 SwordGripLocal = new Vector3(0f, 0.07f, -0.29f);  // 칼 원본 기준 손잡이 쥐는 점
    const float SwordNativeLength = 0.989f;                                   // 칼 원본 길이
    const float SwordLength = 0.593f;                                         // 배치 길이 (캐릭터 키 약 1m의 60%)
    static readonly Vector3 SwordEuler = new Vector3(0f, 0f, 90f);            // 칼끝은 앞(+Z), 칼날은 바깥쪽

    static void FitSwordToHand(Transform sw, Transform ch, Transform hand)
    {
        // 캐릭터가 T-포즈인 상태에서 월드 기준으로 맞춘 뒤 손 뼈에 붙인다
        sw.SetParent(null, false);
        sw.localScale = Vector3.one;
        sw.rotation = ch.rotation * Quaternion.Euler(SwordEuler);

        float nativeLen = GetBounds(sw).size.z;
        if (nativeLen < 1e-5f) nativeLen = SwordNativeLength;
        float unitFactor = nativeLen / SwordNativeLength;   // Unity로 들어올 때 단위가 달라졌을 경우 보정
        sw.localScale = Vector3.one * (SwordLength / nativeLen);

        Vector3 gripTarget = hand.position + ch.rotation * GripFromHand;
        sw.position += gripTarget - sw.TransformPoint(SwordGripLocal * unitFactor);

        sw.SetParent(hand, true);
    }

    static Bounds GetBounds(Transform root)
    {
        var rs = root.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(root.position, Vector3.one);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
#endif
