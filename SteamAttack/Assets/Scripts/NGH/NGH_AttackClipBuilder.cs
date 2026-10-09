#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// For Honor 스타일 강공격 클립을 키 포즈 + IK로 생성하는 에디터 도구. (약공격 1~5타는 NGH_MocapComboBuilder)
///  메뉴: Tools/NGH/Build Heavy Attack Clip
///  - 기준 자세: chg_anim_swordslash 0프레임 (원본은 읽기만 함)
///  - 오른손 위치 / 칼끝 방향 / 날 방향을 키로 지정하면 팔은 2본 IK, 다리는 발 고정 IK로 풀어서 30fps로 굽습니다.
///  - 결과: Assets/Animations/NGH/NGH_Anim_*.anim
/// </summary>
public static class NGH_AttackClipBuilder
{
    const string ModelPath = "Assets/Arts/Models/CHG/chg_player.fbx";
    const string BaseClipPath = "Assets/Animations/CHG/chg_anim_swordslash.fbx";
    const string OutDir = "Assets/Animations/NGH";
    const float ModelYaw = 17.53f;      // NGH_Player 프리팹의 chg_Model 회전과 같게
    const float Fps = 30f;
    const float GripSpacing = 0.075f;   // 오른손과 왼손 사이 손잡이 간격(m)
    const int DenseSub = 8;             // 촘촘한 구간: 30fps × 8 = 240fps

    public enum Ease { Linear, In, Out, InOut }

    /// <summary>키 포즈. 좌표·방향은 플레이어 기준(+Z 앞, +X 오른쪽, +Y 위)</summary>
    public class Key
    {
        public float t;
        public Ease ease = Ease.InOut;          // 이전 키 → 이 키 구간의 가속 방식
        public Vector3 hand;                    // 오른손 위치
        public Vector3 tip;                     // 칼끝 방향
        public Vector3 edge;                    // 날이 향하는 방향 (칼끝 방향과 직교화됨)
        public Vector3 elbow = new Vector3(0.7f, -0.6f, -0.3f);   // 오른 팔꿈치가 향할 방향
        public Vector3 lElbow = new Vector3(-0.7f, -0.6f, -0.2f); // 왼 팔꿈치가 향할 방향
        public float twoHand = 1f;              // 왼손을 손잡이에 붙이는 정도 (0~1)
        public Vector3 hips;                    // 골반 이동
        public float hipsYaw;                   // 골반 좌우 회전(도, +는 오른쪽으로 돎)
        public float spineYaw, spinePitch, spineRoll;   // 상체 회전(도). Pitch +는 앞으로 숙임
        public Vector3 lFoot, rFoot;            // 발 이동

        public Key Clone() => (Key)MemberwiseClone();
    }

    // ================================================================ 동작 정의

    static Key Guard() => new Key
    {
        hand = new Vector3(0.02f, 0.42f, 0.20f),
        tip = new Vector3(-0.15f, 0.65f, 0.75f),
        edge = new Vector3(0f, -0.4f, 1f),
    };

    static Key K(float t, Key from, Ease ease, System.Action<Key> edit)
    {
        var k = from.Clone(); k.t = t; k.ease = ease; edit?.Invoke(k); return k;
    }

    /// <summary>강공격: 머리 위로 크게 들어 올려 잠깐 멈추는(읽히는) 예비동작 → 한 걸음 내디디며 수직 내려찍기</summary>
    public static List<Key> HeavyKeys()
    {
        var g = Guard();
        var raised = K(0.35f, g, Ease.Out, k => { k.hand = new Vector3(0.05f, 0.80f, -0.02f); k.tip = new Vector3(0.1f, 0.25f, -1f); k.edge = new Vector3(0f, 1f, 0.2f);
                                                  k.elbow = new Vector3(0.9f, 0.1f, 0.2f); k.lElbow = new Vector3(-0.9f, 0.1f, 0.2f);
                                                  k.spineYaw = 12f; k.hipsYaw = 5f; k.spinePitch = -14f; k.hips = new Vector3(0f, 0.01f, -0.04f); });
        // 예비동작 유지 (상대가 보고 반응할 수 있는 구간)
        var hold = K(0.55f, raised, Ease.InOut, k => { k.hand = new Vector3(0.05f, 0.82f, -0.05f); k.tip = new Vector3(0.1f, 0.1f, -1f); k.spinePitch = -17f; k.hips = new Vector3(0f, 0.015f, -0.05f); });
        var keys = new List<Key>
        {
            K(0.00f, g, Ease.Linear, null),
            K(0.12f, g, Ease.Out, k => { k.hand = new Vector3(0.06f, 0.50f, 0.12f); k.tip = new Vector3(0.1f, 0.9f, 0.3f); k.edge = new Vector3(0f, 0f, 1f); k.spineYaw = 6f; k.hips = new Vector3(0f, -0.01f, -0.01f); }),
            raised,
            hold,
        };
        // 내려찍기 + 앞발 내딛기: 손은 어깨 앞 중심으로 원을 그리고 칼날은 끊김 없이 돌며 계속 빨라짐.
        // (키 두 개를 따로 보간하면 0.63초에서 속도가 1/3로 떨어지고 칼끝이 정면에서 꺾여 수직으로 떨어졌음)
        var impact = K(0.70f, g, Ease.Linear, k => { k.hand = new Vector3(0f, 0.38f, 0.34f); k.tip = new Vector3(0f, -0.35f, 1f); k.edge = new Vector3(0f, -1f, -0.3f);
                                                      k.elbow = new Vector3(0.9f, -0.2f, 0.1f); k.lElbow = new Vector3(-0.9f, -0.2f, 0.1f);
                                                      k.spinePitch = 30f; k.hips = new Vector3(0f, -0.06f, 0.09f); k.rFoot = new Vector3(0f, 0f, 0.1f); });
        keys.AddRange(ArcSwing(hold, impact, HeavyPivot, 12));
        keys.AddRange(new List<Key>
        {
            // 칼끝이 몸 쪽으로 말려 들어오지 않게 같은 원을 따라 조금 더 내려간 뒤 멈춤
            K(0.80f, g, Ease.Out, k => { k.hand = new Vector3(-0.04f, 0.30f, 0.28f); k.tip = new Vector3(-0.06f, -0.47f, 0.88f); k.edge = new Vector3(0f, -1f, -0.4f);
                                          k.spinePitch = 36f; k.hips = new Vector3(0f, -0.075f, 0.09f); k.rFoot = new Vector3(0f, 0f, 0.1f); }),
            K(1.05f, g, Ease.Out, k => { k.hand = new Vector3(-0.04f, 0.31f, 0.25f); k.tip = new Vector3(-0.1f, -0.38f, 0.9f); k.edge = new Vector3(0f, -1f, -0.4f);
                                          k.spinePitch = 32f; k.hips = new Vector3(0f, -0.07f, 0.08f); k.rFoot = new Vector3(0f, 0f, 0.1f); }),
            K(1.60f, g, Ease.InOut, null),
        });
        return keys;
    }

    // 내려찍기 원의 중심 (플레이어 기준, 오른 어깨 앞 아래)
    static readonly Vector3 HeavyPivot = new Vector3(0.02f, 0.52f, 0.02f);

    /// <summary>
    /// from → to 사이를 원호 운동으로 잇는 키들 (from 다음부터 to 까지, steps 개).
    ///  - 손: pivot 을 중심으로 옆에서 본 평면(YZ)에서 각도·반지름을 함께 보간 → 원을 그림
    ///  - 칼끝: 옆에서 본 각도를 보간 → 칼날이 끊김 없이 돎. 날 방향은 움직이는 쪽
    ///  - 진행: s = x³ (원래 Ease.In 처럼 계속 빨라져 내려찍는 순간 가장 빠름). 키 사이는 Linear 로 이어 속도가 끊기지 않게
    /// </summary>
    static List<Key> ArcSwing(Key from, Key to, Vector3 pivot, int steps)
    {
        var list = new List<Key>();
        Vector3 h0 = from.hand - pivot, h1 = to.hand - pivot;
        float a0 = Mathf.Atan2(h0.y, h0.z), a1 = Mathf.Atan2(h1.y, h1.z);
        float r0 = new Vector2(h0.y, h0.z).magnitude, r1 = new Vector2(h1.y, h1.z).magnitude;
        float p0 = Mathf.Atan2(from.tip.y, from.tip.z), p1 = Mathf.Atan2(to.tip.y, to.tip.z);   // 칼끝 각도 (옆에서 봄)
        if (a1 > a0) a1 -= 2f * Mathf.PI;   // 위 → 앞 → 아래 (각도가 줄어드는 쪽)
        if (p1 > p0) p1 -= 2f * Mathf.PI;
        for (int i = 1; i <= steps; i++)
        {
            float x = i / (float)steps, s = x * x * x;
            var k = to.Clone();
            k.t = Mathf.Lerp(from.t, to.t, x);
            k.ease = Ease.Linear;
            float a = Mathf.Lerp(a0, a1, s), r = Mathf.Lerp(r0, r1, s), p = Mathf.Lerp(p0, p1, s);
            k.hand = pivot + new Vector3(Mathf.Lerp(h0.x, h1.x, s), Mathf.Sin(a) * r, Mathf.Cos(a) * r);
            float side = Mathf.Lerp(from.tip.normalized.x, to.tip.normalized.x, s);
            k.tip = new Vector3(side, Mathf.Sin(p), Mathf.Cos(p));
            k.edge = new Vector3(0f, -Mathf.Cos(p), Mathf.Sin(p));   // 날은 칼이 움직이는 쪽(각도가 줄어드는 쪽)
            k.elbow = Vector3.Slerp(from.elbow.normalized, to.elbow.normalized, s);
            k.lElbow = Vector3.Slerp(from.lElbow.normalized, to.lElbow.normalized, s);
            k.twoHand = Mathf.Lerp(from.twoHand, to.twoHand, s);
            k.hips = Vector3.Lerp(from.hips, to.hips, s);
            k.hipsYaw = Mathf.Lerp(from.hipsYaw, to.hipsYaw, s);
            k.spineYaw = Mathf.Lerp(from.spineYaw, to.spineYaw, s);
            k.spinePitch = Mathf.Lerp(from.spinePitch, to.spinePitch, s);
            k.spineRoll = Mathf.Lerp(from.spineRoll, to.spineRoll, s);
            k.lFoot = Vector3.Lerp(from.lFoot, to.lFoot, s);
            k.rFoot = Vector3.Lerp(from.rFoot, to.rFoot, s);
            list.Add(k);
        }
        return list;
    }

    // ================================================================ 메뉴

    // 약공격 1~5타는 모션캡처 구간 편집으로 만듦 → NGH_MocapComboBuilder
    [MenuItem("Tools/NGH/Build Heavy Attack Clip")]
    public static void BuildAll()
    {
        Build("NGH_Anim_HeavyAttack", HeavyKeys(), 0.53f, 0.85f);   // 내려찍는 구간은 촘촘히
        AssetDatabase.SaveAssets();
    }

    // ================================================================ 생성

    class Rig
    {
        public UnityEngine.SceneManagement.Scene scene;
        public Transform player, model;
        public Transform hips, spine, spine1, spine2, head;
        public Transform rArm, rFore, rHand, lArm, lFore, lHand;
        public Transform lUp, lLeg, lFoot, rUp, rLeg, rFoot;
        public AnimationClip baseClip;
        public Vector3 lFootPos, rFootPos; public Quaternion lFootRot, rFootRot;
        public Vector3 lKneeDir, rKneeDir, torsoRight;
        public Vector3 bladeLocal = Vector3.right, edgeLocal = Vector3.forward;
    }

    static Rig CreateRig()
    {
        var r = new Rig();
        r.scene = EditorSceneManager.NewPreviewScene();
        var p = new GameObject("NGH_PreviewPlayer");
        UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(p, r.scene);
        var go = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), p.transform);
        go.transform.localRotation = Quaternion.Euler(0f, ModelYaw, 0f);
        r.player = p.transform; r.model = go.transform;
        var map = new Dictionary<string, Transform>();
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) map[t.name] = t;
        r.hips = map["Hips"]; r.spine = map["Spine"]; r.spine1 = map["Spine1"]; r.spine2 = map["Spine2"]; r.head = map["Head"];
        r.rArm = map["RightArm"]; r.rFore = map["RightForeArm"]; r.rHand = map["RightHand"];
        r.lArm = map["LeftArm"]; r.lFore = map["LeftForeArm"]; r.lHand = map["LeftHand"];
        r.lUp = map["LeftUpLeg"]; r.lLeg = map["LeftLeg"]; r.lFoot = map["LeftFoot"];
        r.rUp = map["RightUpLeg"]; r.rLeg = map["RightLeg"]; r.rFoot = map["RightFoot"];
        r.baseClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(BaseClipPath);
        ResetPose(r);
        r.lFootPos = r.lFoot.position; r.rFootPos = r.rFoot.position;
        r.lFootRot = r.lFoot.rotation; r.rFootRot = r.rFoot.rotation;
        r.lKneeDir = KneeDir(r.lUp, r.lLeg, r.lFoot);
        r.rKneeDir = KneeDir(r.rUp, r.rLeg, r.rFoot);
        r.torsoRight = Vector3.ProjectOnPlane(r.rArm.position - r.lArm.position, Vector3.up).normalized;
        return r;
    }

    static Vector3 KneeDir(Transform a, Transform b, Transform c)
    {
        var axis = (c.position - a.position).normalized;
        return Vector3.ProjectOnPlane(b.position - a.position, axis).normalized;
    }

    static void ResetPose(Rig r) => r.baseClip.SampleAnimation(r.model.gameObject, 0f);

    static void ApplyPose(Rig r, Key k)
    {
        ResetPose(r);
        Vector3 up = Vector3.up;

        // 골반
        r.hips.position += k.hips;
        r.hips.rotation = Quaternion.AngleAxis(k.hipsYaw, up) * r.hips.rotation;

        // 상체 (세 마디에 나눠서)
        Vector3 right = Quaternion.AngleAxis(k.hipsYaw, up) * r.torsoRight;
        foreach (var s in new[] { r.spine, r.spine1, r.spine2 })
        {
            var q = Quaternion.AngleAxis(k.spineYaw / 3f, up) * Quaternion.AngleAxis(k.spinePitch / 3f, right)
                  * Quaternion.AngleAxis(k.spineRoll / 3f, Vector3.Cross(right, up));
            s.rotation = q * s.rotation;
        }
        // 머리는 앞(상대)을 계속 봄
        r.head.rotation = Quaternion.AngleAxis(-(k.hipsYaw + k.spineYaw) * 0.7f, up) * r.head.rotation;

        // 다리: 발은 제자리 (+이동값)
        TwoBoneIK(r.lUp, r.lLeg, r.lFoot, r.lFootPos + k.lFoot, Quaternion.AngleAxis(k.hipsYaw * 0.5f, up) * r.lKneeDir);
        TwoBoneIK(r.rUp, r.rLeg, r.rFoot, r.rFootPos + k.rFoot, Quaternion.AngleAxis(k.hipsYaw * 0.5f, up) * r.rKneeDir);
        r.lFoot.rotation = r.lFootRot; r.rFoot.rotation = r.rFootRot;

        // 오른팔 + 칼 방향
        Vector3 tip = k.tip.normalized;
        Vector3 edge = Vector3.ProjectOnPlane(k.edge, tip);
        if (edge.sqrMagnitude < 1e-4f) edge = Vector3.ProjectOnPlane(Vector3.up, tip);
        TwoBoneIK(r.rArm, r.rFore, r.rHand, k.hand, k.elbow);
        Quaternion handRot = Quaternion.LookRotation(tip, edge.normalized)
                           * Quaternion.Inverse(Quaternion.LookRotation(r.bladeLocal, r.edgeLocal));
        r.rHand.rotation = handRot;

        // 왼팔: 손잡이 아래쪽을 잡음
        if (k.twoHand > 0.001f)
        {
            Vector3 freeHand = r.lHand.position;
            Vector3 grip = r.rHand.position - tip * GripSpacing;
            Vector3 target = Vector3.Lerp(freeHand, grip, k.twoHand);
            TwoBoneIK(r.lArm, r.lFore, r.lHand, target, k.lElbow);
            r.lHand.rotation = Quaternion.Slerp(r.lHand.rotation, handRot, k.twoHand);
        }
    }

    internal static void TwoBoneIK(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
    {
        float la = (b.position - a.position).magnitude, lb = (c.position - b.position).magnitude;
        Vector3 at = target - a.position;
        float d = Mathf.Clamp(at.magnitude, Mathf.Abs(la - lb) + 1e-4f, la + lb - 1e-4f);
        Vector3 dir = at.sqrMagnitude > 1e-8f ? at.normalized : (c.position - a.position).normalized;
        Vector3 poleDir = Vector3.ProjectOnPlane(pole, dir);
        if (poleDir.sqrMagnitude < 1e-6f) poleDir = Vector3.ProjectOnPlane(b.position - a.position, dir);
        poleDir.Normalize();
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        float angA = Mathf.Acos(cosA);
        Vector3 bTarget = a.position + (dir * Mathf.Cos(angA) + poleDir * Mathf.Sin(angA)) * la;
        a.rotation = Quaternion.FromToRotation(b.position - a.position, bTarget - a.position) * a.rotation;
        Vector3 cTarget = a.position + dir * d;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, cTarget - b.position) * b.rotation;
    }

    static Key Sample(List<Key> keys, float t)
    {
        if (t <= keys[0].t) return keys[0];
        if (t >= keys[keys.Count - 1].t) return keys[keys.Count - 1];
        int i = 1;
        while (keys[i].t < t) i++;
        Key a = keys[i - 1], b = keys[i];
        float u = Mathf.InverseLerp(a.t, b.t, t);
        switch (b.ease)
        {
            case Ease.In: u = u * u * u; break;
            case Ease.Out: u = 1f - Mathf.Pow(1f - u, 3f); break;
            case Ease.InOut: u = u * u * (3f - 2f * u); break;
        }
        Key p0 = keys[Mathf.Max(0, i - 2)], p3 = keys[Mathf.Min(keys.Count - 1, i + 1)];
        return new Key
        {
            t = t,
            hand = CatmullRom(p0.hand, a.hand, b.hand, p3.hand, u),
            tip = Vector3.Slerp(a.tip.normalized, b.tip.normalized, u),
            edge = Vector3.Slerp(a.edge.normalized, b.edge.normalized, u),
            elbow = Vector3.Slerp(a.elbow.normalized, b.elbow.normalized, u),
            lElbow = Vector3.Slerp(a.lElbow.normalized, b.lElbow.normalized, u),
            twoHand = Mathf.Lerp(a.twoHand, b.twoHand, u),
            hips = Vector3.Lerp(a.hips, b.hips, u),
            hipsYaw = Mathf.Lerp(a.hipsYaw, b.hipsYaw, u),
            spineYaw = Mathf.Lerp(a.spineYaw, b.spineYaw, u),
            spinePitch = Mathf.Lerp(a.spinePitch, b.spinePitch, u),
            spineRoll = Mathf.Lerp(a.spineRoll, b.spineRoll, u),
            lFoot = Vector3.Lerp(a.lFoot, b.lFoot, u),
            rFoot = Vector3.Lerp(a.rFoot, b.rFoot, u),
        };
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        // 끝점에서는 중심 차분 대신 한쪽 차분 (튀는 것 방지용으로 접선을 절반만 사용)
        Vector3 m1 = (p2 - p0) * 0.5f, m2 = (p3 - p1) * 0.5f;
        float u2 = u * u, u3 = u2 * u;
        return (2f * u3 - 3f * u2 + 1f) * p1 + (u3 - 2f * u2 + u) * m1 + (-2f * u3 + 3f * u2) * p2 + (u3 - u2) * m2;
    }

    /// <summary>denseFrom~denseTo(초) 구간은 Fps × DenseSub 로 키를 찍음 (빠른 동작에서 키 사이 관절 보간으로 칼 경로가 휘지 않게)</summary>
    public static AnimationClip Build(string name, List<Key> keys, float denseFrom = -1f, float denseTo = -1f)
    {
        var r = CreateRig();
        try
        {
            var bindings = AnimationUtility.GetCurveBindings(r.baseClip);
            float length = keys[keys.Count - 1].t;
            int frames = Mathf.RoundToInt(length * Fps);
            var curves = new AnimationCurve[bindings.Length];
            var targets = new Transform[bindings.Length];
            for (int i = 0; i < bindings.Length; i++)
            {
                curves[i] = new AnimationCurve();
                targets[i] = string.IsNullOrEmpty(bindings[i].path) ? r.model : r.model.Find(bindings[i].path);
            }
            var prevRot = new Dictionary<Transform, Quaternion>();

            var times = new List<float>();
            for (int f = 0; f <= frames; f++)
            {
                float t0 = f / Fps;
                times.Add(t0);
                if (f == frames || t0 + 1f / Fps <= denseFrom || t0 >= denseTo) continue;
                for (int s = 1; s < DenseSub; s++) times.Add(t0 + s / (Fps * DenseSub));
            }

            foreach (float t in times)
            {
                ApplyPose(r, Sample(keys, t));
                // 쿼터니언 부호를 이전 프레임과 맞춰 보간 시 뒤집힘 방지
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

            var clip = new AnimationClip { name = name, frameRate = Fps };
            for (int i = 0; i < bindings.Length; i++)
            {
                if (curves[i].length == 0) continue;
                for (int k = 0; k < curves[i].length; k++) AnimationUtility.SetKeyLeftTangentMode(curves[i], k, AnimationUtility.TangentMode.ClampedAuto);
                for (int k = 0; k < curves[i].length; k++) AnimationUtility.SetKeyRightTangentMode(curves[i], k, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetEditorCurve(clip, bindings[i], curves[i]);
            }
            clip.EnsureQuaternionContinuity();

            if (!AssetDatabase.IsValidFolder(OutDir)) AssetDatabase.CreateFolder("Assets/Animations", "NGH");
            string path = $"{OutDir}/{name}.anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing)
            {
                EditorUtility.CopySerialized(clip, existing);   // GUID 유지 (컨트롤러 참조가 끊기지 않게)
                clip = existing;
            }
            else AssetDatabase.CreateAsset(clip, path);
            EditorUtility.SetDirty(clip);
            Debug.Log($"[NGH] 공격 클립 생성: {path} ({length:0.00}s)");
            return clip;
        }
        finally { EditorSceneManager.ClosePreviewScene(r.scene); }
    }

    static float Comp(Vector3 v, string prop) => prop.EndsWith("x") ? v.x : prop.EndsWith("y") ? v.y : v.z;

    // ================================================================ 미리보기 (확인용 연속 이미지)

    /// <summary>클립을 프레임별로 찍어 PNG 한 장으로 저장 (윗줄: 정면, 아랫줄: 오른쪽 옆)</summary>
    public static string RenderSheet(AnimationClip clip, string fileName, int cols = 10)
    {
        var r = CreateRig();
        RenderTexture rt = null;
        try
        {
            var lightGo = new GameObject("NGH_PreviewLight");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(lightGo, r.scene);
            var light = lightGo.AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.3f;
            lightGo.transform.rotation = Quaternion.Euler(35f, 160f, 0f);
            var camGo = new GameObject("NGH_PreviewCam");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, r.scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = r.scene; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.78f, 0.78f, 0.82f);
            cam.orthographic = true; cam.orthographicSize = 0.6f; cam.nearClipPlane = 0.01f;
            const int w = 180, h = 180;
            var views = new[]
            {
                (new Vector3(0f, 0.45f, 3f), Quaternion.Euler(0f, 180f, 0f)),   // 정면
                (new Vector3(3f, 0.45f, 0f), Quaternion.Euler(0f, -90f, 0f)),   // 오른쪽
                (new Vector3(0f, 3.4f, 0.1f), Quaternion.Euler(90f, 180f, 0f)), // 위 (아래쪽이 앞)
                (Quaternion.Euler(20f, 215f, 0f) * new Vector3(0f, 0f, -3f) + new Vector3(0f, 0.45f, 0f), Quaternion.Euler(20f, 215f, 0f)), // 오른쪽 앞 대각선
            };
            var sheet = new Texture2D(cols * w, views.Length * h, TextureFormat.RGB24, false);
            rt = new RenderTexture(w, h, 24); cam.targetTexture = rt;
            // 한 에디터 프레임 안에서는 스킨드 메시가 다시 계산되지 않으므로, 프레임마다 메시를 직접 구워서 그림
            var baked = new List<(SkinnedMeshRenderer smr, UnityEngine.Mesh mesh)>();
            foreach (var smr in r.model.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var holder = new GameObject("NGH_Baked_" + smr.name);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, r.scene);
                var mesh = new UnityEngine.Mesh();
                holder.AddComponent<MeshFilter>().sharedMesh = mesh;
                holder.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                holder.transform.SetParent(smr.transform, false);
                smr.enabled = false;
                baked.Add((smr, mesh));
            }
            for (int row = 0; row < views.Length; row++)
                for (int c = 0; c < cols; c++)
                {
                    float t = clip.length * c / (cols - 1);
                    clip.SampleAnimation(r.model.gameObject, t);
                    foreach (var b in baked) b.smr.BakeMesh(b.mesh);
                    camGo.transform.SetPositionAndRotation(views[row].Item1, views[row].Item2);
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, w, h), c * w, (views.Length - 1 - row) * h);
                }
            RenderTexture.active = null;
            cam.targetTexture = null;
            sheet.Apply();
            string dir = Path.Combine(Path.GetTempPath(), "NGH_AnimPreview");
            Directory.CreateDirectory(dir);
            string outPath = Path.Combine(dir, fileName);
            File.WriteAllBytes(outPath, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            foreach (var b in baked) Object.DestroyImmediate(b.mesh);
            return outPath;
        }
        finally
        {
            if (rt) Object.DestroyImmediate(rt);
            EditorSceneManager.ClosePreviewScene(r.scene);
        }
    }
}
#endif
