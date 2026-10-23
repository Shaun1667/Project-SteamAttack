using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace NGH
{
    /// <summary>
    /// 공격이 끝나 대기 자세로 돌아가는 동안 왼팔이 몸(치마·저고리)을 뚫고 지나가지 않게 바깥으로 비켜 줍니다.
    ///  - 약공격 끝 자세(두 손으로 칼을 오른쪽 허리에 쥠)와 대기 자세(왼팔을 왼쪽 옆에 내림)를 근육 값으로 섞으면
    ///    왼손이 허리 앞을 그대로 가로질러 치마 안으로 10cm 넘게 들어감
    ///  - 몸통 겉면을 골반 기준 '높이 × 방향 → 반지름' 표로 미리 구워 두고(기준 = 대기 자세),
    ///    손목·손바닥·아래팔이 그 안에 들어가면 골반 축에서 바깥쪽(대개 몸 앞)으로 밀어낸 위치로 팔을 2관절 IK 로 다시 맞춤
    ///  - 손 방향은 애니메이션 그대로. 전환 시작 직후에는 서서히 켜서 칼을 쥐고 있던 왼손이 툭 튀지 않게 함
    /// 애니메이션·GroundClamp(1000) 뒤, 소매 물리(SleevePhysics 1060) 앞에 실행합니다 (소매가 고친 팔을 따라가게).
    /// </summary>
    [DefaultExecutionOrder(1040)]
    [DisallowMultipleComponent]
    public class ArmBodyGuard : MonoBehaviour
    {
        public PlayerController player;
        public Animator animator;
        [Tooltip("공격 끝 전환이 시작된 뒤 이 비율(0~1)까지 서서히 켬. 칼을 쥐고 있던 왼손이 첫 프레임에 튀지 않게")]
        [Range(0f, 0.5f)] public float fadeIn = 0.15f;
        [Tooltip("공격 끝 전환이 끝나거나 다음 동작으로 끊긴 뒤 보정을 끄는 시간(초). 바로 공격을 이어도 손이 툭 튀지 않게")]
        [Min(0f)] public float fadeOut = 0.1f;
        [Tooltip("몸통 겉면을 이만큼(m) 안쪽으로 잡음 (팔이 겉면에 살짝 묻혀도 되면 키움)")]
        [Range(0f, 0.05f)] public float surfaceInset;
        [Tooltip("겉면 바깥으로 띄우는 부드러운 띠 두께(m). 이 안에서 서서히 밀기 시작해서 손이 겉면에 닿을 때 툭 꺾이지 않음")]
        [Range(0.005f, 0.06f)] public float clearance = 0.02f;
        [Tooltip("확인용: 공격 끝 전환이 아닐 때도 항상 켬")]
        public bool alwaysOn;

        [Header("몸통 겉면 (굽기로 채움)")]
        [Tooltip("골반 기준 위쪽 방향")] public Vector3 upLocal = Vector3.up;
        [Tooltip("골반 기준 앞쪽 방향")] public Vector3 forwardLocal = Vector3.forward;
        [Tooltip("표의 가장 낮은 높이 (골반에서, m)")] public float heightMin = -0.36f;
        [Tooltip("표의 높이 간격 (m)")] public float heightStep = 0.03f;
        public int heightBins, angleBins;
        [Tooltip("[높이 * angleBins + 방향] 의 겉면 반지름 (골반 축에서, m). 0 = 몸통 없음")]
        public float[] radius = new float[0];

        Transform _hips, _upper, _lower, _hand, _middle;
        float _weight;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            if (!animator && player) animator = player.animator;
            Cache();
        }

        bool Cache()
        {
            if (!animator) return false;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            _lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            _hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
            _middle = animator.GetBoneTransform(HumanBodyBones.LeftMiddleProximal);
            return _hips && _upper && _lower && _hand;
        }

        void LateUpdate()
        {
            if (radius == null || radius.Length == 0) return;
            if (!_hand && !Cache()) return;
            float target = alwaysOn ? 1f : Weight();
            // 켤 때는 바로(서서히 켜는 건 Weight 가 맡음), 끌 때는 fadeOut 동안 서서히
            _weight = target >= _weight || fadeOut <= 0f ? target : Mathf.MoveTowards(_weight, target, Time.deltaTime / fadeOut);
            if (_weight > 0f) Apply(_weight);
        }

        float Weight()
        {
            float x = player ? player.AttackEndBlend01 : -1f;
            if (x < 0f) return 0f;
            return fadeIn > 0f ? Mathf.SmoothStep(0f, 1f, x / fadeIn) : 1f;
        }

        /// <summary>지금 자세에서 왼팔을 몸 밖으로 비켜 냄 (w = 0~1 세기)</summary>
        public void Apply(float w)
        {
            if (!_hand && !Cache()) return;
            for (int it = 0; it < 3; it++)
            {
                Vector3 wrist = _hand.position;
                Vector3 palm = _middle ? Vector3.Lerp(wrist, _middle.position, 0.5f) : wrist;   // 손바닥 가운데
                Vector3 fore = Vector3.Lerp(_lower.position, wrist, 0.5f);                       // 아래팔 가운데
                // 가장 깊은 점 쪽으로 기우는 가중 평균 (|밀기|⁴ 가중). 고르는 점이 바뀌어도 미는 방향이 툭 바뀌지 않음
                Vector3 sum = Vector3.zero; float wsum = 0f;
                Add(Push(wrist), ref sum, ref wsum);
                Add(Push(palm), ref sum, ref wsum);
                Add(Push(fore) * 2f, ref sum, ref wsum);   // 손목을 밀면 아래팔 가운데는 절반만 움직임
                if (wsum <= 0f) break;
                Vector3 offset = sum / wsum;
                if (offset.sqrMagnitude < 0.001f * 0.001f) break;
                Solve(wrist + offset * w);
            }
            // 팔꿈치가 몸 안이면 팔꿈치만 바깥으로 돌림 (손목 위치는 그대로)
            var elbowPush = Push(_lower.position);
            if (elbowPush.sqrMagnitude > 0.001f * 0.001f) Solve(_hand.position, _lower.position + elbowPush * w);
        }

        static void Add(Vector3 push, ref Vector3 sum, ref float wsum)
        {
            float m = push.sqrMagnitude;
            float wt = m * m;
            sum += push * wt; wsum += wt;
        }

        // 점이 몸통 겉면(+띄울 띠) 안이면 밖으로 밀어내는 벡터 (월드, m). 밖이면 0.
        // 골반 축에서 잰 거리 ρ 를 겉면 반지름 r 에 대해 부드럽게 바꿈: ρ ≥ r + c 이면 그대로,
        // 그 안쪽은 r + c·e^((ρ - r - c)/c) (깊이 들어간 점일수록 겉면 r 에 가깝게, 경계에서 기울기까지 이어짐)
        Vector3 Push(Vector3 p)
        {
            var inv = Quaternion.Inverse(_hips.rotation);
            Vector3 local = inv * (p - _hips.position);
            float h = Vector3.Dot(local, upLocal);
            float hf = (h - heightMin) / heightStep;
            if (hf < 0f || hf > heightBins - 1) return Vector3.zero;
            Vector3 flat = local - upLocal * h;
            float rho = flat.magnitude;
            if (rho < 1e-4f) return Vector3.zero;
            Vector3 right = Vector3.Cross(upLocal, forwardLocal);
            float ang = Mathf.Atan2(Vector3.Dot(flat, right), Vector3.Dot(flat, forwardLocal));   // 0 = 앞, + = 오른쪽
            float af = Mathf.Repeat(ang / (2f * Mathf.PI), 1f) * angleBins;
            float r = Sample(hf, af);
            if (r <= 0f) return Vector3.zero;
            r -= surfaceInset;
            float c = clearance;
            if (rho >= r + c) return Vector3.zero;
            float rho2 = r + c * Mathf.Exp((rho - r - c) / c);
            Vector3 target = upLocal * h + flat * (rho2 / rho);
            return _hips.rotation * (target - local);
        }

        float Sample(float hf, float af)
        {
            int h0 = Mathf.FloorToInt(hf), h1 = Mathf.Min(h0 + 1, heightBins - 1);
            int a0 = Mathf.FloorToInt(af) % angleBins, a1 = (a0 + 1) % angleBins;
            float th = hf - h0, ta = af - Mathf.Floor(af);
            float r00 = radius[h0 * angleBins + a0], r01 = radius[h0 * angleBins + a1];
            float r10 = radius[h1 * angleBins + a0], r11 = radius[h1 * angleBins + a1];
            return Mathf.Lerp(Mathf.Lerp(r00, r01, ta), Mathf.Lerp(r10, r11, ta), th);
        }

        // 2관절 IK: 손목을 target 으로. 팔꿈치는 지금 굽은 쪽(또는 elbowHint 쪽)으로. 손 방향은 그대로
        void Solve(Vector3 target, Vector3? elbowHint = null)
        {
            Quaternion handRot = _hand.rotation;
            Vector3 s = _upper.position, e = _lower.position, wr = _hand.position;
            float l1 = Vector3.Distance(s, e), l2 = Vector3.Distance(e, wr);
            Vector3 d = target - s;
            float len = Mathf.Clamp(d.magnitude, Mathf.Abs(l1 - l2) + 1e-3f, l1 + l2 - 1e-3f);
            if (d.sqrMagnitude < 1e-8f) return;
            Vector3 dn = d.normalized;
            Vector3 hint = (elbowHint ?? e) - s;
            Vector3 pole = hint - Vector3.Dot(hint, dn) * dn;
            if (pole.sqrMagnitude < 1e-8f) return;
            float a = (l1 * l1 - l2 * l2 + len * len) / (2f * len);
            float hh = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 newElbow = s + dn * a + pole.normalized * hh;
            _upper.rotation = Quaternion.FromToRotation(e - s, newElbow - s) * _upper.rotation;
            Vector3 e2 = _lower.position;
            _lower.rotation = Quaternion.FromToRotation(_hand.position - e2, s + dn * len - e2) * _lower.rotation;
            _hand.rotation = handRot;
        }

        // 굽기 설정 (빌드에서도 직렬화 모양이 같도록 #if 밖에 둠)
        [Header("굽기")]
        [Tooltip("몸통 겉면을 구울 때의 기준 자세 (대기)")]
        public AnimationClip referenceClip;
        [Tooltip("몸통 겉면을 만들 메시. 양팔(위팔부터 아래) 부분은 자동으로 뺌")]
        public SkinnedMeshRenderer[] bodyMeshes = new SkinnedMeshRenderer[0];
        [Tooltip("표의 가장 높은 높이 (골반에서, m)")]
        public float heightMax = 0.27f;
        [Tooltip("방향 칸 수 (360° 를 나눔)")]
        public int angleCount = 36;

#if UNITY_EDITOR
        [ContextMenu("몸통 겉면 굽기 (기준 자세)")]
        public void BakeProfile()
        {
            if (!player) player = GetComponent<PlayerController>();
            if (!animator && player) animator = player.animator;
            if (!Cache()) { Debug.LogError("[NGH] ArmBodyGuard: Animator·뼈를 찾지 못했습니다.", this); return; }
            Undo.RecordObject(this, "NGH ArmBodyGuard Bake");
            AnimationMode.StartAnimationMode();
            try
            {
                if (referenceClip) AnimationMode.SampleAnimationClip(animator.gameObject, referenceClip, 0f);
                var excl = new HashSet<Transform>();
                foreach (var b in new[] { HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm })
                {
                    var t = animator.GetBoneTransform(b);
                    if (t) foreach (var x in t.GetComponentsInChildren<Transform>(true)) excl.Add(x);
                }
                var inv = Quaternion.Inverse(_hips.rotation);
                Vector3 origin = _hips.position;
                upLocal = (inv * Vector3.up).normalized;
                forwardLocal = Vector3.ProjectOnPlane(inv * animator.transform.forward, upLocal).normalized;
                Vector3 right = Vector3.Cross(upLocal, forwardLocal);
                angleBins = Mathf.Max(8, angleCount);
                heightBins = Mathf.CeilToInt((heightMax - heightMin) / heightStep) + 1;
                var r = new float[heightBins * angleBins];
                var mesh = new Mesh();
                foreach (var smr in bodyMeshes)
                {
                    if (!smr) continue;
                    smr.BakeMesh(mesh, false);
                    var verts = mesh.vertices;
                    var weights = smr.sharedMesh.boneWeights;
                    var bones = smr.bones;
                    var tr = smr.transform;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        if (excl.Contains(bones[weights[i].boneIndex0])) continue;
                        Vector3 local = inv * (tr.position + tr.rotation * verts[i] - origin);
                        float h = Vector3.Dot(local, upLocal);
                        int hb = Mathf.RoundToInt((h - heightMin) / heightStep);
                        if (hb < 0 || hb >= heightBins) continue;
                        Vector3 flat = local - upLocal * h;
                        float ang = Mathf.Atan2(Vector3.Dot(flat, right), Vector3.Dot(flat, forwardLocal));
                        int ab = Mathf.RoundToInt(Mathf.Repeat(ang / (2f * Mathf.PI), 1f) * angleBins) % angleBins;
                        int k = hb * angleBins + ab;
                        r[k] = Mathf.Max(r[k], flat.magnitude);
                    }
                }
                DestroyImmediate(mesh);
                // 빈 칸은 같은 높이의 이웃 방향 → 위아래 높이 순으로 채움
                for (int pass = 0; pass < angleBins; pass++)
                {
                    bool any = false;
                    var next = (float[])r.Clone();
                    for (int hb = 0; hb < heightBins; hb++)
                        for (int ab = 0; ab < angleBins; ab++)
                        {
                            int k = hb * angleBins + ab;
                            if (r[k] > 0f) continue;
                            float a = r[hb * angleBins + (ab + 1) % angleBins], b = r[hb * angleBins + (ab + angleBins - 1) % angleBins];
                            float v = a > 0f && b > 0f ? (a + b) * 0.5f : Mathf.Max(a, b);
                            if (v <= 0f && hb > 0) v = r[(hb - 1) * angleBins + ab];
                            if (v <= 0f && hb < heightBins - 1) v = r[(hb + 1) * angleBins + ab];
                            if (v > 0f) { next[k] = v; any = true; }
                        }
                    r = next;
                    if (!any) break;
                }
                // 기준 자세(대기)의 왼팔은 밀지 않도록 그 자리의 반지름을 줄임 → 전환이 끝나 보정이 꺼질 때 손이 툭 돌아오지 않음
                if (referenceClip)
                {
                    for (int s = 0; s <= 4; s++)
                    {
                        AnimationMode.SampleAnimationClip(animator.gameObject, referenceClip, referenceClip.length * s / 4f);
                        var inv2 = Quaternion.Inverse(_hips.rotation);
                        Vector3 o2 = _hips.position, wrist = _hand.position;
                        var pts = new[] { wrist, _middle ? Vector3.Lerp(wrist, _middle.position, 0.5f) : wrist, Vector3.Lerp(_lower.position, wrist, 0.5f), _lower.position };
                        foreach (var p in pts)
                        {
                            Vector3 local = inv2 * (p - o2);
                            float h = Vector3.Dot(local, upLocal);
                            float hf = (h - heightMin) / heightStep;
                            if (hf < 0f || hf > heightBins - 1) continue;
                            Vector3 flat = local - upLocal * h;
                            float ang = Mathf.Atan2(Vector3.Dot(flat, right), Vector3.Dot(flat, forwardLocal));
                            float af = Mathf.Repeat(ang / (2f * Mathf.PI), 1f) * angleBins;
                            float limit = Mathf.Max(0.001f, flat.magnitude + surfaceInset - clearance - 0.003f);
                            int h0 = Mathf.FloorToInt(hf), a0 = Mathf.FloorToInt(af);
                            for (int dh = 0; dh <= 1; dh++)
                                for (int da = 0; da <= 1; da++)
                                {
                                    int k = Mathf.Min(h0 + dh, heightBins - 1) * angleBins + (a0 + da) % angleBins;
                                    r[k] = Mathf.Min(r[k], limit);
                                }
                        }
                    }
                }
                radius = r;
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            EditorUtility.SetDirty(this);
        }
#endif
    }
}
