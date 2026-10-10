using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 애니메이션이 재생된 뒤(LateUpdate) 캐릭터 뼈 중 가장 낮은 점을 확인해서,
    /// 바닥(플레이어 발 위치)보다 내려가면 모델 전체를 그만큼 위로 올려 줍니다.
    /// → 구르기 등 어떤 동작이든 땅을 뚫고 들어가지 않음. 캐릭터 모델(Animator가 있는 오브젝트)에 붙입니다.
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

        readonly List<Transform> _bones = new List<Transform>();
        Vector3 _baseLocalPos;
        float _lift;

        public float CurrentLift => _lift;

        void Awake()
        {
            if (!groundReference) groundReference = transform.parent;
            if (!boneRoot)
            {
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == "Armature") { boneRoot = t; break; }
            }
            if (boneRoot)
                foreach (var t in boneRoot.GetComponentsInChildren<Transform>(true))
                    if (t != boneRoot) _bones.Add(t);
            _baseLocalPos = transform.localPosition;
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
            _lift = need >= _lift ? need : Mathf.MoveTowards(_lift, need, settleSpeed * Time.deltaTime);

            transform.localPosition = _baseLocalPos + Vector3.up * _lift;
        }
    }
}
