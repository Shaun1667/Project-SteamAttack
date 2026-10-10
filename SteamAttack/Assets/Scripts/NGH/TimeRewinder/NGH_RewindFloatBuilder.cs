#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 시간 역행 중 재생할 "똑바로 선 채 공중에 뜬" 동작을 떨어지는 모션(Mixamo Falling)에서 구워 만드는 에디터 도구.
///  메뉴: Tools/NGH/Build Rewind Float Clip
///  - 원본(model@Falling.fbx)은 휴머노이드로 읽어 플레이어(Generic) 뼈대로 옮김 (원본 동작 데이터는 수정하지 않음)
///  - 원본(엎드린 채 떨어지는 자세)의 팔다리 자세는 그대로, 몸통은 일부만 쓰고, 몸 전체 방향만 대기 자세처럼 똑바로 세움
///    → FloatSet 으로 팔은 옆으로 활짝 벌리고 무릎은 굽혀 접은 채 떠 있는 자세 (레퍼런스 이미지 기준)
///  - 원본을 원래 속도로 재생해 허우적이는 느낌을 살리고, 끝을 처음 자세로 섞어 끊김 없이 반복(루프) + 위아래로 떠다님
///  - 실제로 땅에서 떠오르는 높이는 NGH_TimeRewind(Travel Lift Height)가 담당
///  - 결과: Assets/Animations/NGH/NGH_Anim_RewindFloat.anim, 컨트롤러에 "RewindFloat" 상태가 없으면 추가
/// </summary>
public static class NGH_RewindFloatBuilder
{
    const string SrcPath = "Assets/Animations/NGH/model@Falling.fbx";
    const string ModelPath = "Assets/Arts/Models/CHG/chg_player.fbx";
    const string IdlePath = "Assets/Animations/CHG/chg_anim_standby.fbx";
    const string TemplateClip = "Assets/Animations/CHG/chg_anim_swordslash.fbx";   // 플레이어 뼈 커브 목록
    const string ChgAvatarPath = "Assets/Animations/NGH/NGH_ChgHumanAvatar.asset";
    const string ControllerPath = "Assets/Animations/NGH/NGH_PlayerAnimator.controller";
    const string OutPath = "Assets/Animations/NGH/NGH_Anim_RewindFloat.anim";
    public const string StateName = "RewindFloat";
    const float ModelYaw = 17.53f;
    const float OutFps = 30f;

    // ---- 동작 조절값
    public static float Length = 2f;        // 결과 클립 길이(초, 한 번 반복). 원본을 원래 속도로 이만큼 재생
    public static float SrcFrom = 0.5f;     // 원본에서 쓸 구간 시작(초)
    public static float LoopBlend = 0.4f;   // 끝 부분 이 시간(초) 동안 처음 자세로 섞어 끊김 없이 반복 (SrcFrom 이상이어야 함)
    public static float ArmFlail = 1f;      // 앞으로 뻗은 팔 위에 원본 팔의 허우적임을 얹는 정도 (1 = 원본만큼)
    // 원본(엎드린 채 떨어지는 자세)의 팔다리·몸통 자세를 얼마나 그대로 쓸지 (0 = 대기 자세, 1 = 원본 그대로).
    // 몸 전체 방향만 똑바로 세우므로, 원본의 "앞으로 뻗은 팔·뒤로 뻗은 다리"가 "선 채 앞으로 뻗은 팔·아래로 늘어진 다리"가 됨
    public static float ArmWeight = 0f;     // 팔은 FloatSet 으로 직접 정함 (원본의 어깨·비틀림이 섞이면 팔뚝이 위로 꺾임)
    public static float LegWeight = 0.7f;
    public static float SpineWeight = 0.6f; // 몸통·목·머리 (원본은 등을 젖히고 고개를 든 자세 → 선 자세에서는 조금만)
    public static float Bob = 0.05f;        // 위아래로 떠다니는 폭(m)

    /// <summary>
    /// 원본 자세 위에 더하는 보정 (근육 값 -1 ~ 1에 더함: 보정 + 흔들림 폭 × sin(반복 위상 + 어긋남)).
    /// </summary>
    public static readonly (string muscle, float add, float sway, float phase)[] FloatPose =
    {
    };

    /// <summary>
    /// 기준 값을 직접 정하는 근육 (기준 + 원본의 허우적임 × flail). 레퍼런스: 양팔을 옆으로 활짝 벌려 살짝 올리고,
    /// 양 무릎을 굽혀 정강이를 뒤로 접은 채 떠 있는 자세 (값은 뼈 방향을 맞춰 찾은 것 — 팔 위쪽 약 20°·살짝 뒤,
    /// 허벅지는 앞으로 약 40~55°, 무릎 약 95~100° 굽힘, 오른다리가 조금 더 높음).
    /// 허우적임 = 원본 근육 값 - 원본 전체 평균 → 원본이 팔다리를 휘젓는 만큼 따라 움직임
    /// </summary>
    public static readonly (string muscle, float value, float flail)[] FloatSet =
    {
        // 팔: 위팔은 옆(살짝 뒤·위), 팔꿈치를 약 80° 굽혀 아래팔이 정면 사선(바깥 약 25°)을 향하는 L자
        ("Left Arm Front-Back", 0.65f, 0.5f),        ("Right Arm Front-Back", 0.70f, 0.5f),
        ("Left Arm Down-Up", 0.75f, 0.4f),           ("Right Arm Down-Up", 0.85f, 0.4f),
        ("Left Arm Twist In-Out", 0.10f, 0.2f),      ("Right Arm Twist In-Out", 0.10f, 0.2f),
        ("Left Forearm Stretch", 0.10f, 0.3f),       ("Right Forearm Stretch", 0.10f, 0.3f),
        ("Left Upper Leg Front-Back", -0.10f, 0.5f), ("Right Upper Leg Front-Back", -0.30f, 0.5f),
        ("Left Upper Leg In-Out", 0.00f, 0.5f),      ("Right Upper Leg In-Out", 0.00f, 0.5f),
        ("Left Lower Leg Stretch", -0.25f, 0.5f),    ("Right Lower Leg Stretch", -0.30f, 0.5f),
    };

    [MenuItem("Tools/NGH/Build Rewind Float Clip")]
    public static void BuildMenu()
    {
        Build();
        AssetDatabase.SaveAssets();
    }

    public static AnimationClip Build()
    {
        if (!EnsureHumanoidSource()) return null;
        var srcClip = FirstClip(SrcPath);
        var idleClip = FirstClip(IdlePath);
        var chgAvatar = AssetDatabase.LoadAssetAtPath<Avatar>(ChgAvatarPath);
        if (!srcClip || !idleClip || !chgAvatar) { Debug.LogError("[NGH] 시간 역행 뜨기 동작: 원본/대기 클립 또는 플레이어 아바타가 없습니다."); return null; }

        var scene = EditorSceneManager.NewPreviewScene();
        GameObject src = null;
        try
        {
            var player = new GameObject("NGH_PreviewPlayer");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(player, scene);
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), player.transform);
            go.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);

            // 원본(휴머노이드)은 미리보기 장면 안에서는 샘플링해도 자세가 갱신되지 않아서, 저장되지 않는 임시 객체로 둠
            src = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(SrcPath));
            src.hideFlags = HideFlags.HideAndDontSave;
            var srcAnimator = src.GetComponent<Animator>();
            var reader = new HumanPoseHandler(srcAnimator.avatar, src.transform);
            var writer = new HumanPoseHandler(chgAvatar, go.transform);

            // 대기 자세 (몸통을 곧게 세우는 기준)
            idleClip.SampleAnimation(go, 0f);
            var idle = new HumanPose();
            writer.GetHumanPose(ref idle);

            // 근육별로 팔/다리/몸통 구분
            var weight = new float[HumanTrait.MuscleCount];
            for (int m = 0; m < weight.Length; m++)
            {
                string bone = ((HumanBodyBones)HumanTrait.BoneFromMuscle(m)).ToString();
                bool arm = bone.Contains("Shoulder") || bone.Contains("Arm") || bone.Contains("Hand") || bone.Contains("Thumb")
                        || bone.Contains("Index") || bone.Contains("Middle") || bone.Contains("Ring") || bone.Contains("Little");
                bool leg = bone.Contains("Leg") || bone.Contains("Foot") || bone.Contains("Toes");
                bool face = bone.Contains("Eye") || bone.Contains("Jaw");
                weight[m] = arm ? ArmWeight : leg ? LegWeight : face ? 0f : SpineWeight;
            }
            var poseIndex = new int[FloatPose.Length];
            for (int p = 0; p < FloatPose.Length; p++)
            {
                poseIndex[p] = System.Array.IndexOf(HumanTrait.MuscleName, FloatPose[p].muscle);
                if (poseIndex[p] < 0) Debug.LogWarning("[NGH] 근육 이름 없음: " + FloatPose[p].muscle);
            }

            var bindings = AnimationUtility.GetCurveBindings(AssetDatabase.LoadAssetAtPath<AnimationClip>(TemplateClip));
            var targets = new Transform[bindings.Length];
            var curves = new AnimationCurve[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                targets[i] = string.IsNullOrEmpty(bindings[i].path) ? go.transform : go.transform.Find(bindings[i].path);
                curves[i] = new AnimationCurve();
            }
            var prevRot = new Dictionary<Transform, Quaternion>();
            var hp = new HumanPose();

            // 원본 근육 값 (시간 t초)
            System.Func<float, float[]> sourceMuscles = st =>
            {
                srcClip.SampleAnimation(src, Mathf.Clamp(st, 0f, srcClip.length));
                src.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var sp = new HumanPose();
                reader.GetHumanPose(ref sp);
                return sp.muscles;
            };
            // 실제로 쓰는 구간의 원본 평균 (허우적임 = 평균에서 벗어난 만큼 → 기준 자세를 중심으로 흔들림)
            var mean = new float[HumanTrait.MuscleCount];
            int meanCount = 0;
            for (float st = SrcFrom; st <= SrcFrom + Length; st += 1f / OutFps, meanCount++)
            {
                var ms = sourceMuscles(st);
                for (int m = 0; m < mean.Length; m++) mean[m] += ms[m];
            }
            for (int m = 0; m < mean.Length; m++) mean[m] /= Mathf.Max(1, meanCount);
            var setIndex = new int[FloatSet.Length];
            for (int s = 0; s < FloatSet.Length; s++) setIndex[s] = System.Array.IndexOf(HumanTrait.MuscleName, FloatSet[s].muscle);

            int frames = Mathf.RoundToInt(Length * OutFps);
            for (int f = 0; f <= frames; f++)
            {
                float t = f / OutFps, phase = t / Length;
                // 원본을 원래 속도로 재생. 끝 LoopBlend 동안은 "처음 자세로 이어지는 원본"과 섞어 반복이 끊기지 않게
                var raw = sourceMuscles(SrcFrom + t);
                float bw = LoopBlend > 0f ? Mathf.SmoothStep(0f, 1f, (t - (Length - LoopBlend)) / LoopBlend) : 0f;
                if (bw > 0f)
                {
                    var into = sourceMuscles(SrcFrom - (Length - t));
                    for (int m = 0; m < raw.Length; m++) raw[m] = Mathf.Lerp(raw[m], into[m], bw);
                }
                hp.muscles = (float[])raw.Clone();
                // 원본 자세를 부위별 비율로 (나머지는 대기 자세) → 보정을 더함
                for (int m = 0; m < hp.muscles.Length; m++) hp.muscles[m] = Mathf.Lerp(idle.muscles[m], hp.muscles[m], weight[m]);
                for (int p = 0; p < FloatPose.Length; p++)
                {
                    int m = poseIndex[p];
                    if (m < 0) continue;
                    var fp = FloatPose[p];
                    hp.muscles[m] = Mathf.Clamp(hp.muscles[m] + fp.add + fp.sway * Mathf.Sin(2f * Mathf.PI * (phase + fp.phase)), -1f, 1f);
                }
                // 팔다리: 레퍼런스 자세 기준 + 원본이 팔다리를 휘젓는 만큼
                for (int s = 0; s < FloatSet.Length; s++)
                {
                    int m = setIndex[s];
                    if (m >= 0) hp.muscles[m] = Mathf.Clamp(FloatSet[s].value + (raw[m] - mean[m]) * FloatSet[s].flail * ArmFlail, -1f, 1f);
                }
                hp.bodyRotation = idle.bodyRotation;   // 몸 전체는 똑바로 선 방향
                hp.bodyPosition = idle.bodyPosition + Vector3.up * (Bob * Mathf.Sin(2f * Mathf.PI * phase));
                writer.SetHumanPose(ref hp);

                var rotCache = new Dictionary<Transform, Quaternion>();
                for (int i = 0; i < bindings.Length; i++)
                {
                    var tr = targets[i];
                    if (!tr) continue;
                    string prop = bindings[i].propertyName;
                    float v;
                    if (prop.StartsWith("m_LocalRotation"))
                    {
                        if (!rotCache.TryGetValue(tr, out var q))
                        {
                            q = tr.localRotation;
                            if (prevRot.TryGetValue(tr, out var pq) && Quaternion.Dot(pq, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                            rotCache[tr] = q;
                        }
                        v = prop.EndsWith("x") ? q.x : prop.EndsWith("y") ? q.y : prop.EndsWith("z") ? q.z : q.w;
                    }
                    else if (prop.StartsWith("m_LocalPosition")) v = Comp(tr.localPosition, prop);
                    else if (prop.StartsWith("m_LocalScale")) v = Comp(tr.localScale, prop);
                    else continue;
                    curves[i].AddKey(new Keyframe(t, v));
                }
                foreach (var kv in rotCache) prevRot[kv.Key] = kv.Value;
            }

            var clip = new AnimationClip { name = "NGH_Anim_RewindFloat", frameRate = OutFps };
            for (int i = 0; i < bindings.Length; i++)
            {
                if (curves[i].length == 0) continue;
                for (int k = 0; k < curves[i].length; k++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curves[i], k, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curves[i], k, AnimationUtility.TangentMode.ClampedAuto);
                }
                AnimationUtility.SetEditorCurve(clip, bindings[i], curves[i]);
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);

            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(OutPath);
            if (existing) { EditorUtility.CopySerialized(clip, existing); clip = existing; }
            else AssetDatabase.CreateAsset(clip, OutPath);
            EditorUtility.SetDirty(clip);
            EnsureState(clip);
            Debug.Log($"[NGH] 시간 역행 뜨기 동작 생성: {OutPath} ({Length:0.##}s, 반복)");
            return clip;
        }
        finally
        {
            if (src) Object.DestroyImmediate(src);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    // 원본을 휴머노이드로 가져오기 (Mixamo 뼈 자동 연결 + T자세 맞춤). 동작 데이터는 그대로
    static bool EnsureHumanoidSource()
    {
        var imp = AssetImporter.GetAtPath(SrcPath) as ModelImporter;
        if (!imp) { Debug.LogError("[NGH] 원본 없음: " + SrcPath); return false; }
        if (imp.animationType != ModelImporterAnimationType.Human || imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.SaveAndReimport();
        }
        var avatar = AssetDatabase.LoadAssetAtPath<Avatar>(SrcPath);
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(SrcPath)) if (o is Avatar a) avatar = a;
        if (!avatar || !avatar.isValid || !avatar.isHuman) { Debug.LogError("[NGH] 원본을 휴머노이드로 읽지 못했습니다: " + SrcPath); return false; }
        return true;
    }

    // 컨트롤러에 RewindFloat 상태 추가/갱신 (전환 없음 — NGH_TimeRewind 가 코드로 재생)
    static void EnsureState(AnimationClip clip)
    {
        var ctrl = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!ctrl) return;
        var sm = ctrl.layers[0].stateMachine;
        foreach (var cs in sm.states)
            if (cs.state.name == StateName) { cs.state.motion = clip; EditorUtility.SetDirty(ctrl); return; }
        var st = sm.AddState(StateName, new Vector3(600f, 400f, 0f));
        st.motion = clip;
        if (sm.states.Length > 1) st.writeDefaultValues = sm.states[0].state.writeDefaultValues;   // 다른 상태와 같게
        EditorUtility.SetDirty(ctrl);
    }

    static AnimationClip FirstClip(string path)
    {
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o is AnimationClip c && !c.name.StartsWith("__preview")) return c;
        return null;
    }

    static float Comp(Vector3 v, string prop) => prop.EndsWith("x") ? v.x : prop.EndsWith("y") ? v.y : v.z;
}
#endif
