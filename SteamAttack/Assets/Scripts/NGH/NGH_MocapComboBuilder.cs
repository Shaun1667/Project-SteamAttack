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
        // 다음 타가 예약돼 있으면 넘겨주는 원본 프레임 (프리팹 chainAt). 공격 후 다시 일어서기 전 지점
        public float chainFrame;
        // 파지 보정(도): 팔뚝 축으로 손을 돌려 칼이 나오는 방향을 바꿈 (시작·끝에서 서서히). 0 = 원본 그대로
        public float gripTwist;
        public ArmReach reach;     // 베고 난 뒤 오른팔을 뻗는 자세 덧씌우기 (없으면 null)
        public float Length => retime[retime.Length - 1].y + hold;
    }

    /// <summary>플레이어 기준 방향(방위: 0 = 앞, 90 = 오른쪽 / 높이: + 위)으로 오른팔을 쭉 뻗고 칼을 눕힘.
    /// 원본 프레임 inFrom→inTo 동안 들어가고, outFrom→outTo 동안 원래 동작으로 돌아감</summary>
    public class ArmReach
    {
        public float armYaw, armPitch, bladeYaw, bladePitch;
        public float inFrom, inTo, outFrom, outTo;

        public float Weight(float srcFrame) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inFrom, inTo, srcFrame)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(outFrom, outTo, srcFrame)));
    }

    // 휴머노이드 원본(EEJANAI Free Sword Animations)
    const string SlashDir = "Assets/AssetStore/NGH/EEJANAI_Team/FreeSwordAnimations/Animations/";
    const string HumanSrcModel = "Assets/AssetStore/NGH/EEJANAI_Team/Commons/Model/EEJANAIbot.fbx";
    const string TemplateClip = "Assets/Animations/CHG/chg_anim_swordslash.fbx";   // 플레이어 뼈 커브 목록
    const string ChgAvatarPath = OutDir + "NGH_ChgHumanAvatar.asset";

    public const float Speed = 0.85f;      // 전체 재생 속도 (원본 = 1)
    public const float LeadTime = 0.25f;   // 앞 타 자세에서 섞어 들어가는 시간(초)

    static Segment Slash(string outName, string clip, int frames, float chainFrame, float hold) =>
        Paced(outName, clip, chainFrame, hold, (frames, 1f));

    /// <summary>구간별 시간(초): parts = (이 원본 프레임까지, 걸리는 시간) 순서대로. 예) (8, 0.25) = 0~8프레임을 0.25초에</summary>
    static Segment Timed(string outName, string clip, float chainFrame, float hold, params (int toFrame, float seconds)[] parts)
    {
        var keys = new List<Vector2> { Vector2.zero };
        float t = 0f;
        foreach (var (toFrame, seconds) in parts) { t += seconds; keys.Add(new Vector2(toFrame, t)); }
        return new Segment { outName = outName, source = SlashDir + clip + ".anim", hold = hold, chainFrame = chainFrame, retime = keys.ToArray() };
    }

    /// <summary>구간별 속도: parts = (이 원본 프레임까지, 기준 속도 대비 배율) 순서대로. 예) (12, 1.6) = 0~12프레임을 1.6배</summary>
    static Segment Paced(string outName, string clip, float chainFrame, float hold, params (int toFrame, float speed)[] parts)
    {
        var keys = new List<Vector2> { Vector2.zero };
        int from = 0; float t = 0f;
        foreach (var (toFrame, speed) in parts)
        {
            t += (toFrame - from) / SrcFps / (Speed * speed);
            keys.Add(new Vector2(toFrame, t));
            from = toFrame;
        }
        return new Segment { outName = outName, source = SlashDir + clip + ".anim", hold = hold, chainFrame = chainFrame, retime = keys.ToArray() };
    }

    // 원본끼리 자세 차이가 커서, 2타부터는 앞 타가 넘겨주는 지점(chainFrame) 자세에서 섞어 들어가는 연결 구간을 넣음
    public static readonly Segment[] Segments = WithLeads(new[]
    {
        Slash("NGH_Anim_LightAttack", "slash1", 18, 18, 0.05f),   // 대각선 내려베기 (끝 자세가 낮아서 끝까지 재생 후 넘김)
        // 몸을 돌리며 가로베기: 준비(0~12) 짧게, 베기(12~17) 1.1배, 회수(17~22) 2.5배로 후딜 짧게
        Paced("NGH_Anim_Combo2", "slash4", 22, 0.02f, (12, 1.6f), (17, 1.1f), (22, 2.5f)),
        // 앞으로 깊게 찌르듯 베기: 준비(0~11) 짧게, 찌르기(11~18) 빠르게(약 0.11초),
        //  회수는 연타로 넘길 때(18~27)와 끝까지 갈 때(27~38) 후딜이 모두 이전의 0.8배가 되게 나눔
        Paced("NGH_Anim_Combo3", "slash7", 27, 0.065f, (11, 1.8f * 1.2f), (18, 2.6f * 1.3f * 1.2f), (27, 2.0f * 1.2f / 1.3f), (38, 1.4f * 1.2f / 1.3f)),
        // 돌려차기에 이어 베기: 준비(0~10)를 1.2배 길게, 베기(10~14) 그대로, 회수(14~35)를 1.2배 길게
        Paced("NGH_Anim_Combo4", "slash6", 27, 0.05f, (10, 1f / 1.2f * 1.2f), (14, 1f * 1.2f), (35, 1f / 1.2f * 1.2f)),
        // 마무리: 크게 휩쓸어 베기. 구간별 시간(초): 준비(0~8), 베기(8~13), 팔 뻗기(13~16), 팔 뻗은 채 유지(16~23), 회수(23~28)
        //  원본은 28프레임에서 움직임이 멈추므로 거기서 끝냄 (멈춘 뒤에 조작이 막혀 있지 않게)
        //  원본 그대로면 칼이 팔뚝 둘레로 다른 타보다 약 70~100° 틀어져 역수처럼 보여서 파지 보정
        //  베고 난 뒤(13~23)에는 오른팔을 몸 오른쪽 뒤로 크게 벌려 쭉 펴고 칼을 팔 연장선으로 눕힘
        Reach(Grip(Timed("NGH_Anim_Combo5", "slash3", 28, 0f, (8, 0.248f), (13, 0.097f), (16, 0.170f), (23, 0.502f), (28, 0.452f)), -100f),
              new ArmReach { armYaw = 120f, armPitch = 0f, bladeYaw = 115f, bladePitch = -3f, inFrom = 13f, inTo = 16f, outFrom = 23f, outTo = 27f }),
    });

    static Segment Reach(Segment s, ArmReach r) { s.reach = r; return s; }

    static Segment Grip(Segment s, float twist) { s.gripTwist = twist; return s; }
    const float GripOutTime = 0.35f;   // 끝에서 원래 파지로 돌아가는 시간(초)

    static Segment[] WithLeads(Segment[] segs)
    {
        for (int i = 1; i < segs.Length; i++)
        {
            segs[i].leadFrom = segs[i - 1].source;
            segs[i].leadFrame = segs[i - 1].chainFrame;
            // 준비 구간을 짧게 한 타는 연결 구간도 준비 구간 안에 들어가게 줄임
            segs[i].leadTime = Mathf.Min(LeadTime, segs[i].retime[1].y * 0.9f);
        }
        return segs;
    }

    /// <summary>프리팹 chainAt 값 (클립 기준 0~1)</summary>
    public static float ChainAt(Segment s)
    {
        float end = s.retime[s.retime.Length - 1].x;
        float clipLength = Mathf.Round(s.Length * OutFps) / OutFps;   // 구운 클립 길이 (60fps 프레임 단위)
        return s.chainFrame >= end ? 1f : OutputTimeOf(s, s.chainFrame) / clipLength;
    }

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
                if (Mathf.Abs(s.gripTwist) > 0.01f)
                {
                    float lead = s.leadTime > 0f ? s.leadTime : 0.15f;
                    float w = Mathf.SmoothStep(0f, 1f, t / lead) * Mathf.SmoothStep(0f, 1f, (s.Length - t) / GripOutTime);
                    TwistGrip(bones, s.gripTwist * w);
                }
                if (s.reach != null) ApplyReach(bones, s.reach, s.reach.Weight(srcT * SrcFps));

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

    static Vector3 PlayerDir(float yaw, float pitch) =>
        Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;   // 미리보기 플레이어는 원점에서 +Z를 봄

    /// <summary>오른팔을 r 방향으로 쭉 펴고(IK) 칼을 r 방향으로 눕힌 자세를 weight만큼 섞음</summary>
    static void ApplyReach(Dictionary<string, Transform> b, ArmReach r, float weight)
    {
        if (weight < 1e-3f) return;
        Transform arm = b["RightArm"], fore = b["RightForeArm"], hand = b["RightHand"];
        Quaternion a0 = arm.localRotation, f0 = fore.localRotation, h0 = hand.localRotation;

        float length = (fore.position - arm.position).magnitude + (hand.position - fore.position).magnitude;
        Vector3 target = arm.position + PlayerDir(r.armYaw, r.armPitch) * length * 0.97f;
        Vector3 pole = Vector3.down * 0.8f + Vector3.back * 0.4f;   // 팔꿈치는 아래·뒤로
        NGH_AttackClipBuilder.TwoBoneIK(arm, fore, hand, target, pole);
        Vector3 bladeNow = hand.rotation * BladeLocal;
        hand.rotation = Quaternion.FromToRotation(bladeNow, PlayerDir(r.bladeYaw, r.bladePitch)) * hand.rotation;

        arm.localRotation = Quaternion.Slerp(a0, arm.localRotation, weight);
        fore.localRotation = Quaternion.Slerp(f0, fore.localRotation, weight);
        hand.localRotation = Quaternion.Slerp(h0, hand.localRotation, weight);
    }

    /// <summary>팔뚝 축(팔꿈치→손목)으로 손을 angle만큼 돌림. 팔뚝·손에 절반씩 나눠 손목이 비틀려 보이지 않게</summary>
    static void TwistGrip(Dictionary<string, Transform> b, float angle)
    {
        Transform fore = b["RightForeArm"], hand = b["RightHand"];
        Vector3 axis = (hand.position - fore.position).normalized;
        fore.rotation = Quaternion.AngleAxis(angle * 0.5f, axis) * fore.rotation;
        hand.rotation = Quaternion.AngleAxis(angle * 0.5f, axis) * hand.rotation;
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
