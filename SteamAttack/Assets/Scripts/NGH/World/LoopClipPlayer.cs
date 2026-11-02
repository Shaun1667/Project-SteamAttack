using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace NGH
{
    /// <summary>
    /// Animator Controller 없이 클립 하나를 계속 반복 재생 (어좌의 고종처럼 한 동작만 하는 인물용).
    /// 받아 온 동작이 이 인물 몸에 딱 맞지 않을 때 손을 보정 (예: 두꺼운 옷 위에 손을 얹을 때 손이 옷 속으로 파고듦).
    ///  - 손 위치를 옮기고(팔 IK), 손바닥을 아래로 눕혀 손가락이 허벅지를 따라 앞으로 뻗게 함
    /// Animator 가 있는 오브젝트에 붙입니다.
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public class LoopClipPlayer : MonoBehaviour
    {
        [Tooltip("반복 재생할 클립")]
        public AnimationClip clip;
        [Tooltip("재생 빠르기")]
        public float speed = 1f;
        [Tooltip("여러 명이 같은 동작일 때 어긋나게 할 시작 지점 (0~1)")]
        [Range(0f, 1f)] public float startOffset;

        [Header("손 보정 (몸 기준)")]
        [Tooltip("왼손을 옮길 양 (m, 몸 기준: x=바깥쪽, y=위, z=앞)")]
        public Vector3 leftHandOffset;
        [Tooltip("오른손을 옮길 양 (m, 몸 기준: x=바깥쪽, y=위, z=앞)")]
        public Vector3 rightHandOffset;
        [Tooltip("손바닥을 아래로 눕히는 정도 (0=동작 그대로, 1=손바닥이 완전히 아래를 보고 손가락이 앞으로)")]
        [Range(0f, 1f)] public float handFlatten;
        [Tooltip("눕힌 손의 손가락이 앞에서 아래로 기우는 각도 (허벅지 기울기에 맞춤)")]
        public float handPitchDown = 15f;
        [Tooltip("눕힌 손의 손가락이 바깥쪽으로 벌어지는 각도")]
        public float handYawOut = 10f;
        [Tooltip("손가락을 편안히 모은 모양으로 다시 잡음 (눕힌 손과 함께 사용)")]
        public bool relaxFingers;
        [Tooltip("손가락 마디마다 아래로 굽히는 각도 (허벅지를 살짝 감싸게)")]
        public float fingerCurl = 12f;
        [Tooltip("엄지가 검지 쪽에서 벌어지는 각도")]
        public float thumbSpread = 35f;

        static readonly HumanBodyBones[] LeftThumb = { HumanBodyBones.LeftThumbProximal, HumanBodyBones.LeftThumbIntermediate, HumanBodyBones.LeftThumbDistal };
        static readonly HumanBodyBones[] RightThumb = { HumanBodyBones.RightThumbProximal, HumanBodyBones.RightThumbIntermediate, HumanBodyBones.RightThumbDistal };
        static readonly HumanBodyBones[] LeftFingers =
        {
            HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftIndexIntermediate, HumanBodyBones.LeftIndexDistal,
            HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftMiddleIntermediate, HumanBodyBones.LeftMiddleDistal,
            HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftRingIntermediate, HumanBodyBones.LeftRingDistal,
            HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftLittleIntermediate, HumanBodyBones.LeftLittleDistal,
        };
        static readonly HumanBodyBones[] RightFingers =
        {
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        PlayableGraph _graph;
        Animator _anim;

        void OnEnable()
        {
            _anim = GetComponent<Animator>();
            if (!clip) return;
            var playable = AnimationPlayableUtilities.PlayClip(_anim, clip, out _graph);
            playable.SetSpeed(speed);
            playable.SetTime(startOffset * clip.length);
        }

        void OnDisable()
        {
            if (_graph.IsValid()) _graph.Destroy();
        }

        void LateUpdate() => ApplyHandFix();

        /// <summary>현재 자세 위에 손 보정을 얹음 (에디터에서 클립을 샘플한 뒤 미리보기용으로도 호출)</summary>
        public void ApplyHandFix()
        {
            if (!_anim) _anim = GetComponent<Animator>();
            if (!_anim || !_anim.isHuman) return;
            if (leftHandOffset == Vector3.zero && rightHandOffset == Vector3.zero && handFlatten == 0f
                && !relaxFingers) return;
            FixArm(true);
            FixArm(false);
        }

        Transform Bone(bool left, HumanBodyBones l, HumanBodyBones r) => _anim.GetBoneTransform(left ? l : r);

        void FixArm(bool left)
        {
            var up = Bone(left, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm);
            var lo = Bone(left, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm);
            var hd = Bone(left, HumanBodyBones.LeftHand, HumanBodyBones.RightHand);
            if (!up || !lo || !hd) return;
            Vector3 side = left ? -transform.right : transform.right;   // 몸 바깥쪽
            Vector3 off = left ? leftHandOffset : rightHandOffset;
            Vector3 goal = hd.position + side * off.x + transform.up * off.y + transform.forward * off.z;
            Quaternion handRot = hd.rotation;

            // 2관절 IK: 팔꿈치는 원래 굽은 쪽을 유지
            Vector3 a = up.position, b = lo.position, c = hd.position;
            float la = Vector3.Distance(a, b), lb = Vector3.Distance(b, c);
            Vector3 toGoal = goal - a;
            float d = Mathf.Clamp(toGoal.magnitude, Mathf.Abs(la - lb) + 0.001f, (la + lb) * 0.995f);
            Vector3 dir = toGoal.normalized;
            Vector3 bend = Vector3.ProjectOnPlane(b - a, dir).normalized;
            float ang = Mathf.Acos(Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f));
            Vector3 elbow = a + (dir * Mathf.Cos(ang) + bend * Mathf.Sin(ang)) * la;
            up.rotation = Quaternion.FromToRotation(b - a, elbow - a) * up.rotation;
            lo.rotation = Quaternion.FromToRotation(hd.position - lo.position, a + dir * d - lo.position) * lo.rotation;
            hd.rotation = handRot;

            // 손 눕히기: 손바닥은 아래, 손가락은 앞(조금 아래·바깥)으로
            var mid = Bone(left, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.RightMiddleProximal);
            var idx = Bone(left, HumanBodyBones.LeftIndexProximal, HumanBodyBones.RightIndexProximal);
            var lit = Bone(left, HumanBodyBones.LeftLittleProximal, HumanBodyBones.RightLittleProximal);
            if (handFlatten > 0f && mid && idx && lit)
            {
                Vector3 f0 = (mid.position - hd.position).normalized;
                Vector3 n0 = -Vector3.Cross(f0, idx.position - lit.position).normalized * (left ? 1f : -1f);   // 손바닥이 향하는 쪽
                Vector3 pitchAxis = Vector3.Cross(transform.forward, Vector3.up).normalized;
                Vector3 f1 = Quaternion.AngleAxis(-handPitchDown, pitchAxis) * transform.forward;
                f1 = Quaternion.AngleAxis(left ? -handYawOut : handYawOut, Vector3.up) * f1;
                Vector3 n1 = Vector3.ProjectOnPlane(Vector3.down, f1).normalized;
                Quaternion flat = Quaternion.LookRotation(f1, n1) * Quaternion.Inverse(Quaternion.LookRotation(f0, n0)) * hd.rotation;
                hd.rotation = Quaternion.Slerp(hd.rotation, flat, handFlatten);
            }

            // 손가락·엄지: 손등 방향을 따라 앞으로 뻗고 마디마다 조금씩 아래로 굽힘
            if (!relaxFingers || !mid || !idx || !lit) return;
            Vector3 fwd = (mid.position - hd.position).normalized;
            Vector3 across = (idx.position - lit.position).normalized;              // 새끼 → 검지 쪽
            Vector3 curlAxis = Vector3.ProjectOnPlane(across, fwd).normalized;
            // curlAxis 로 돌렸을 때 손가락 끝이 아래로 가는 쪽을 + 로
            float curlSign = Vector3.Dot(Quaternion.AngleAxis(10f, curlAxis) * fwd, Vector3.up) < Vector3.Dot(fwd, Vector3.up) ? 1f : -1f;
            var fingers = left ? LeftFingers : RightFingers;
            for (int i = 0; i < fingers.Length; i++)
            {
                var f = _anim.GetBoneTransform(fingers[i]);
                if (!f || f.childCount == 0) continue;
                int joint = i % 3;   // 0=첫마디 1=가운데 2=끝마디
                Vector3 want = Quaternion.AngleAxis(curlSign * fingerCurl * (joint + 1), curlAxis) * fwd;
                f.rotation = Quaternion.FromToRotation(f.GetChild(0).position - f.position, want) * f.rotation;
            }
            // 엄지: 손 앞쪽에서 검지 쪽으로 벌리고 살짝 아래로
            var thumb = left ? LeftThumb : RightThumb;
            Vector3 thumbDir = (Vector3.Slerp(fwd, across, thumbSpread / 90f) + Vector3.down * 0.08f).normalized;
            for (int i = 0; i < thumb.Length; i++)
            {
                var f = _anim.GetBoneTransform(thumb[i]);
                if (!f || f.childCount == 0) continue;
                Vector3 want = i == 0 ? thumbDir : Vector3.Slerp(thumbDir, fwd, 0.25f * i).normalized;
                f.rotation = Quaternion.FromToRotation(f.GetChild(0).position - f.position, want) * f.rotation;
            }
        }
    }
}
