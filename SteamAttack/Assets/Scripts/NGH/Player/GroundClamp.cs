using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 애니메이션이 재생된 뒤(LateUpdate) 캐릭터 뼈 중 가장 낮은 점을 확인해서,
    /// 바닥(플레이어 발 위치)보다 내려가면 모델 전체를 그만큼 위로 올려 줍니다.
    /// → 구르기 등 어떤 동작이든 땅을 뚫고 들어가지 않음. 캐릭터 모델(Animator가 있는 오브젝트)에 붙입니다.
    /// 칼집·무기·천 충돌체 같은 장신구 뼈는 빼고 몸(휴머노이드) 뼈만 봅니다.
    /// 걷기·뛰기 중에는 높이를 고정해서 발끝을 따라 몸이 위아래로 흔들리지 않게 합니다.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class GroundClamp : MonoBehaviour
    {
        [Tooltip("바닥 높이 기준 (비우면 부모 = 플레이어 발 위치)")]
        public Transform groundReference;
        [Tooltip("뼈가 바닥에서 최소 이만큼 떠 있도록 (m)")]
        public float minClearance = 0f;
        [Tooltip("다시 내려올 때 속도 (m/s). 올라갈 때는 즉시")]
        public float settleSpeed = 1.5f;
        [Tooltip("확인할 뼈의 루트 (비우면 'Armature'를 자동으로 찾음)")]
        public Transform boneRoot;

        [Header("걷기·뛰기 높이 고정")]
        [Tooltip("이 상태들에서는 아래 Locomotion Lift 높이로 고정 (Animator 상태 이름)")]
        public string[] locomotionStates = { "Locomotion", "StrafeLeft", "StrafeRight" };
        [Tooltip("걷기·뛰기 중 모델을 들어 올리는 높이 (m). 대기 자세에서 발바닥이 바닥에 닿는 높이 (Hero_Bound 기준 약 0.08)")]
        public float locomotionLift = 0.08f;
        [Tooltip("걷기·뛰기 중 발끝이 이만큼까지는 바닥을 파고들어도 들어 올리지 않음 (m)")]
        public float locomotionSinkTolerance = 0.04f;

        readonly List<Transform> _bones = new List<Transform>();
        Animator _animator;
        int[] _locomotionHashes;
        Vector3 _baseLocalPos;
        float _lift;

        public float CurrentLift => _lift;

        void Awake()
        {
            if (!groundReference) groundReference = transform.parent;
            _animator = GetComponent<Animator>();
            if (_animator && _animator.isHuman)
            {
                // 몸 뼈만 (+ 발가락 끝 뼈). 칼집·무기 등 휴머노이드가 아닌 뼈는 제외
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    var t = _animator.GetBoneTransform((HumanBodyBones)i);
                    if (t) _bones.Add(t);
                }
                foreach (var toes in new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
                {
                    var t = _animator.GetBoneTransform(toes);
                    if (t) foreach (Transform c in t) _bones.Add(c);
                }
            }
            else
            {
                if (!boneRoot)
                {
                    foreach (var t in GetComponentsInChildren<Transform>(true))
                        if (t.name == "Armature") { boneRoot = t; break; }
                }
                if (boneRoot)
                    foreach (var t in boneRoot.GetComponentsInChildren<Transform>(true))
                        if (t != boneRoot) _bones.Add(t);
            }
            _locomotionHashes = new int[locomotionStates.Length];
            for (int i = 0; i < locomotionStates.Length; i++)
                _locomotionHashes[i] = Animator.StringToHash(locomotionStates[i]);
            _baseLocalPos = transform.localPosition;
        }

        bool InLocomotion()
        {
            if (!_animator) return false;
            int hash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            for (int i = 0; i < _locomotionHashes.Length; i++)
                if (_locomotionHashes[i] == hash) return true;
            return false;
        }

        void LateUpdate()
        {
            if (_bones.Count == 0) return;
            float groundY = groundReference ? groundReference.position.y : 0f;

            // 현재 들어올린 만큼을 빼고 원래 자세 기준으로 가장 낮은 뼈 높이 계산
            float minY = float.MaxValue;
            for (int i = 0; i < _bones.Count; i++)
            {
                float y = _bones[i].position.y;
                if (y < minY) minY = y;
            }
            minY -= _lift;

            float need = Mathf.Max(0f, groundY + minClearance - minY);
            // 걷기·뛰기: 고정 높이 유지, 발끝이 허용치보다 깊이 파고들 때만 추가로 올림
            if (InLocomotion()) need = Mathf.Max(locomotionLift, need - locomotionSinkTolerance);
            _lift = need >= _lift ? need : Mathf.MoveTowards(_lift, need, settleSpeed * Time.deltaTime);

            transform.localPosition = _baseLocalPos + Vector3.up * _lift;
        }
    }
}
