#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 좌클릭 약공격 1~5타 클립을 원본 모션 클립에서 구워 만드는 에디터 도구.
///  메뉴: Tools/NGH/Build Light Combo Clips (Mocap)
///  - 원본은 읽기만 함. 휴머노이드 원본(EEJANAI slash)은 NGH_ChgHumanAvatar로 플레이어(Generic) 뼈대에 옮겨 구움
///  - 시간 배치(Retime): 원본 프레임 → 결과 시간 표로 준비 동작·베기는 빠르게, 끝은 감속 후 잠깐 멈춤(절도)
///  - 팔 덧씌우기(Arm Layer): 원본 몸 동작은 그대로 두고 칼 든 손의 높이·칼끝 각도만 바꿔 베는 방향을 수정
///  - 결과: Assets/Animations/NGH/NGH_Anim_LightAttack, NGH_Anim_Combo2~5 (.anim, 60fps 키)
///  - 다시 실행하면 같은 파일을 덮어씀 (컨트롤러 연결 유지, Animation 창에서 고친 키는 사라짐)
/// </summary>
public static class NGH_MocapComboBuilder
{
    const string ModelPath = "Assets/Arts/Models/CHG/chg_player.fbx";
    const string SrcDir = "Assets/Animations/CHG/";
    const string OutDir = "Assets/Animations/NGH/";
    const float SrcFps = 30f;
    const float OutFps = 60f;
    const float ModelYaw = 17.53f;
    static readonly Vector3 BladeLocal = new Vector3(0.492f, 0.014f, 0.039f).normalized;   // 오른손 기준 칼끝 방향

    /// <summary>덧씌우기 키: 원본 프레임 시점에 손을 위(+)/아래(-)로 옮기고 칼끝을 들거나(+) 내림(-),
    /// 몸 전체를 오른쪽(+)/왼쪽(-)으로 돌림(베고 나서 상대에게 등을 보이지 않게)</summary>
    public struct ArmKey
    {
        public float srcFrame, handUp, tipPitch, bodyYaw;
        public ArmKey(float srcFrame, float handUp, float tipPitch, float bodyYaw = 0f)
        { this.srcFrame = srcFrame; this.handUp = handUp; this.tipPitch = tipPitch; this.bodyYaw = bodyYaw; }
    }

    public class Segment
    {
        public string outName, source;
        public Vector2[] retime;   // (원본 프레임, 결과 시간 초). 마지막 구간은 감속
        public float hold;         // 끝 자세 유지 시간(초)
        public ArmKey[] arm;       // 없으면 원본 그대로
        // 연결 구간(휴머노이드 원본만): 앞 타 원본의 leadFrame 자세에서 시작해 leadTime(초) 동안 이 타의 동작으로 섞어 들어감
        public string leadFrom; public float leadFrame, leadTime;
        public float Length => retime[retime.Length - 1].y + hold;
    }

    // 휴머노이드 원본(EEJANAI Free Sword Animations)
    const string SlashDir = "Assets/AssetStore/NGH/EEJANAI_Team/FreeSwordAnimations/Animations/";
    const string HumanSrcModel = "Assets/AssetStore/NGH/EEJANAI_Team/Commons/Model/EEJANAIbot.fbx";
    const string TemplateClip = "Assets/Animations/CHG/chg_anim_swordslash.fbx";   // 플레이어 뼈 커브 목록
    const string ChgAvatarPath = OutDir + "NGH_ChgHumanAvatar.asset";

    static Segment Slash(string outName, string clip, int frames, float speed, float hold) => new Segment
    {
        outName = outName, source = SlashDir + clip + ".anim", hold = hold,
        retime = new[] { new Vector2(0, 0f), new Vector2(frames, frames / SrcFps / speed) },
    };

    static Segment Lead(Segment s, string fromClip, float fromFrame, float time)
    { s.leadFrom = SlashDir + fromClip + ".anim"; s.leadFrame = fromFrame; s.leadTime = time; return s; }

    // 3→4, 4→5는 원본끼리 자세 차이가 커서, 앞 타가 넘겨주는 지점(프리팹 chainAt) 자세에서 섞어 들어가는 연결 구간을 넣음
    //  넘겨주는 지점 = ChainFrameCombo3/4 (원본 프레임). 결과 클립 시간은 OutputTimeOf로 환산
    public static readonly Segment[] Segments =
    {
        Slash("NGH_Anim_LightAttack", "slash1", 18, 1.0f, 0.05f),
        Slash("NGH_Anim_Combo2",      "slash4", 22, 1.0f, 0.05f),
        Slash("NGH_Anim_Combo3",      "slash6", 35, 1.0f, 0.05f),
        Lead(Slash("NGH_Anim_Combo4", "slash7", 38, 1.0f, 0.05f), "slash6", ChainFrameCombo3, 0.22f),
        Lead(Slash("NGH_Anim_Combo5", "slash3", 32, 1.0f, 0.10f), "slash7", ChainFrameCombo4, 0.25f),
    };
    public const float ChainFrameCombo3 = 27f;   // slash6에서 공격 후 일어서기 전
    public const float ChainFrameCombo4 = 33f;   // slash7에서 공격 후 일어서기 전

    [MenuItem("Tools/NGH/Build Light Combo Clips (Mocap)")]
    public static void BuildAll()
    {
        foreach (var s in Segments) Build(s);
        AssetDatabase.SaveAssets();
    }

    public static AnimationClip Build(Segment s)
    {
        string srcPath = s.source.Contains("/") ? s.source : SrcDir + s.source;
        var src = AssetDatabase.LoadAssetAtPath<AnimationClip>(srcPath);
        if (!src) { Debug.LogError($"[NGH] 원본 클립 없음: {srcPath}"); return null; }

        var map = RetimeCurve(s);

        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var player = new GameObject("NGH_PreviewPlayer");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(player, scene);
            var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), player.transform);
            go.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
            var bones = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) bones[t.name] = t;

            // 휴머노이드 원본: 원본 캐릭터에 재생 → 근육 자세를 읽어 플레이어 뼈대에 적용
            System.Action<float, float> pose;   // (원본 시간, 결과 시간)
            if (src.humanMotion)
            {
                var avatar = GetOrBuildChgAvatar(go);
                var humanSrc = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(HumanSrcModel));
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(humanSrc, scene);
                var srcAnimator = humanSrc.GetComponent<Animator>();
                var reader = new HumanPoseHandler(srcAnimator.avatar, humanSrc.transform);
                var writer = new HumanPoseHandler(avatar, go.transform);
                var hp = new HumanPose();
                src.SampleAnimation(humanSrc, 0f);
                humanSrc.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                reader.GetHumanPose(ref hp);
                Vector3 body0 = hp.bodyPosition;

                HumanPose lead = default;
                bool hasLead = !string.IsNullOrEmpty(s.leadFrom) && s.leadTime > 0f;
                if (hasLead)
                {
                    var leadClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(s.leadFrom);
                    leadClip.SampleAnimation(humanSrc, s.leadFrame / SrcFps);
                    humanSrc.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    lead = new HumanPose();
                    reader.GetHumanPose(ref lead);
                }

                pose = (t, outT) =>
                {
                    src.SampleAnimation(humanSrc, t);
                    humanSrc.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    reader.GetHumanPose(ref hp);
                    float y = hp.bodyPosition.y;
                    if (hasLead && outT < s.leadTime)
                    {
                        float w = Mathf.SmoothStep(0f, 1f, outT / s.leadTime);
                        for (int m = 0; m < hp.muscles.Length; m++) hp.muscles[m] = Mathf.Lerp(lead.muscles[m], hp.muscles[m], w);
                        hp.bodyRotation = Quaternion.Slerp(lead.bodyRotation, hp.bodyRotation, w);
                        y = Mathf.Lerp(lead.bodyPosition.y, y, w);
                    }
                    // 제자리 동작으로: 앞으로 나아가는 이동은 컨트롤러의 lungeDistance가 담당
                    hp.bodyPosition = new Vector3(body0.x, y, body0.z);
                    writer.SetHumanPose(ref hp);
                };
            }
            else pose = (t, outT) => src.SampleAnimation(go, t);

            var bindings = AnimationUtility.GetCurveBindings(src.humanMotion ? AssetDatabase.LoadAssetAtPath<AnimationClip>(TemplateClip) : src);
            var targets = new Transform[bindings.Length];
            var curves = new AnimationCurve[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                targets[i] = string.IsNullOrEmpty(bindings[i].path) ? go.transform : go.transform.Find(bindings[i].path);
                curves[i] = new AnimationCurve();
            }
            var prevRot = new Dictionary<Transform, Quaternion>();

            int frames = Mathf.RoundToInt(s.Length * OutFps);
            for (int f = 0; f <= frames; f++)
            {
                float t = f / OutFps;
                float srcT = map.Evaluate(Mathf.Min(t, s.retime[s.retime.Length - 1].y));
                pose(srcT, t);
                if (s.arm != null) ApplyArmLayer(bones, s.arm, srcT * SrcFps);

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

            var clip = new AnimationClip { name = s.outName, frameRate = OutFps };
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

            string path = OutDir + s.outName + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);   // GUID 유지
                clip = existing;
            }
            else AssetDatabase.CreateAsset(clip, path);
            EditorUtility.SetDirty(clip);
            Debug.Log($"[NGH] {s.outName} ← {s.source} ({s.Length:0.00}s)");
            return clip;
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    /// <summary>결과 시간 → 원본 시간. 중간은 일정 속도, 마지막 구간만 감속</summary>
    static AnimationCurve RetimeCurve(Segment s)
    {
        var map = new AnimationCurve();
        foreach (var p in s.retime) map.AddKey(new Keyframe(p.y, p.x / SrcFps));
        for (int i = 0; i < map.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(map, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(map, i, AnimationUtility.TangentMode.Linear);
        }
        var last = map[map.length - 1]; last.inTangent = 0f;
        AnimationUtility.SetKeyLeftTangentMode(map, map.length - 1, AnimationUtility.TangentMode.Free);
        map.MoveKey(map.length - 1, last);
        return map;
    }

    /// <summary>원본 프레임이 결과 클립에서 몇 초 지점인지 (프리팹 chainAt 계산용)</summary>
    public static float OutputTimeOf(Segment s, float srcFrame)
    {
        var map = RetimeCurve(s);
        float end = s.retime[s.retime.Length - 1].y, target = srcFrame / SrcFps, lo = s.retime[0].y, hi = end;
        for (int i = 0; i < 40; i++) { float mid = (lo + hi) * 0.5f; if (map.Evaluate(mid) < target) lo = mid; else hi = mid; }
        return (lo + hi) * 0.5f;
    }

    /// <summary>원본 자세 위에 손 높이·칼끝 각도만 바꿔 덧씌움 (양손으로 잡고 있으면 왼손도 같이 이동)</summary>
    static void ApplyArmLayer(Dictionary<string, Transform> b, ArmKey[] keys, float srcFrame)
    {
        float up, pitch, yaw;
        var first = keys[0]; var lastKey = keys[keys.Length - 1];
        if (srcFrame <= first.srcFrame) { up = first.handUp; pitch = first.tipPitch; yaw = first.bodyYaw; }
        else if (srcFrame >= lastKey.srcFrame) { up = lastKey.handUp; pitch = lastKey.tipPitch; yaw = lastKey.bodyYaw; }
        else
        {
            int i = 1;
            while (keys[i].srcFrame < srcFrame) i++;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(keys[i - 1].srcFrame, keys[i].srcFrame, srcFrame));
            up = Mathf.Lerp(keys[i - 1].handUp, keys[i].handUp, u);
            pitch = Mathf.Lerp(keys[i - 1].tipPitch, keys[i].tipPitch, u);
            yaw = Mathf.Lerp(keys[i - 1].bodyYaw, keys[i].bodyYaw, u);
        }

        // 몸 전체 방향 (Armature를 제자리에서 돌림)
        if (Mathf.Abs(yaw) > 1e-3f)
        {
            var armature = b["Armature"];
            armature.rotation = Quaternion.AngleAxis(yaw, Vector3.up) * armature.rotation;
        }
        if (Mathf.Abs(up) < 1e-4f && Mathf.Abs(pitch) < 1e-3f) return;

        Transform rArm = b["RightArm"], rFore = b["RightForeArm"], rHand = b["RightHand"];
        Transform lArm = b["LeftArm"], lFore = b["LeftForeArm"], lHand = b["LeftHand"];
        Vector3 handPos = rHand.position, lPos = lHand.position;
        Quaternion handRot = rHand.rotation, lRot = lHand.rotation;
        Vector3 tip = handRot * BladeLocal;
        Vector3 axis = Vector3.Cross(tip, Vector3.up);
        Quaternion delta = axis.sqrMagnitude > 1e-6f ? Quaternion.AngleAxis(pitch, axis.normalized) : Quaternion.identity;
        bool twoHanded = (lPos - handPos).magnitude < 0.15f;
        Vector3 rPole = rFore.position - (rArm.position + handPos) * 0.5f;
        Vector3 lPole = lFore.position - (lArm.position + lPos) * 0.5f;

        Vector3 newHand = handPos + Vector3.up * up;
        NGH_AttackClipBuilder.TwoBoneIK(rArm, rFore, rHand, newHand, rPole);
        rHand.rotation = delta * handRot;
        if (twoHanded)
        {
            NGH_AttackClipBuilder.TwoBoneIK(lArm, lFore, lHand, newHand + delta * (lPos - handPos), lPole);
            lHand.rotation = delta * lRot;
        }
    }

    /// <summary>플레이어(chg_player, Generic) 뼈대용 휴머노이드 아바타. 원본 FBX 설정은 건드리지 않고 NGH 에셋으로만 저장</summary>
    static Avatar GetOrBuildChgAvatar(GameObject chgInstance)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Avatar>(ChgAvatarPath);
        if (existing && existing.isValid && existing.isHuman) return existing;

        var map = new Dictionary<string, string>
        {
            { "Hips", "Hips" }, { "Spine", "Spine" }, { "Chest", "Spine1" }, { "UpperChest", "Spine2" }, { "Neck", "Neck" }, { "Head", "Head" },
            { "LeftUpperLeg", "LeftUpLeg" }, { "LeftLowerLeg", "LeftLeg" }, { "LeftFoot", "LeftFoot" }, { "LeftToes", "LeftToeBase" },
            { "RightUpperLeg", "RightUpLeg" }, { "RightLowerLeg", "RightLeg" }, { "RightFoot", "RightFoot" }, { "RightToes", "RightToeBase" },
            { "LeftShoulder", "LeftShoulder" }, { "LeftUpperArm", "LeftArm" }, { "LeftLowerArm", "LeftForeArm" }, { "LeftHand", "LeftHand" },
            { "RightShoulder", "RightShoulder" }, { "RightUpperArm", "RightArm" }, { "RightLowerArm", "RightForeArm" }, { "RightHand", "RightHand" },
        };
        var human = new List<HumanBone>();
        foreach (var kv in map)
            human.Add(new HumanBone { humanName = kv.Key, boneName = kv.Value, limit = new HumanLimit { useDefaultValues = true } });

        // 기준 자세 = FBX 기본(바인드) 자세
        var reference = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var skeleton = new List<SkeletonBone>();
        foreach (var t in reference.GetComponentsInChildren<Transform>(true))
            skeleton.Add(new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale });

        var desc = new HumanDescription
        {
            human = human.ToArray(), skeleton = skeleton.ToArray(),
            upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
            armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = false,
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(reference, desc);
        avatar.name = "NGH_ChgHumanAvatar";
        if (!avatar.isValid) { Debug.LogError("[NGH] 플레이어 휴머노이드 아바타 생성 실패"); return avatar; }
        if (existing) AssetDatabase.DeleteAsset(ChgAvatarPath);
        AssetDatabase.CreateAsset(avatar, ChgAvatarPath);
        return avatar;
    }

    static float Comp(Vector3 v, string prop) => prop.EndsWith("x") ? v.x : prop.EndsWith("y") ? v.y : v.z;
}
#endif
