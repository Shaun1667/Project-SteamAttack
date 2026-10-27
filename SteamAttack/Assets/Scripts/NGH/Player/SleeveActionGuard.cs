using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 빠른 동작(공격·구르기·피격 등) 중 소매 MagicaCloth가 팔에 뚫려 뒤집히는 것을 막습니다.
    ///  - 동작이 시작되면 소매의 Blend Weight 를 0(애니메이션 그대로)으로 빠르게 내려 팔을 그대로 따라가게 하고,
    ///  - 동작이 끝나면 시뮬레이션을 리셋(팔을 통과한 천을 제자리로)한 뒤 Blend Weight 를 1로 서서히 올립니다.
    /// 3.3배속 공격에서 칼끝이 초속 20m 가까이 움직여 천이 팔을 따라잡지 못하고 뚫리는 문제의 대책입니다.
    /// </summary>
    [DefaultExecutionOrder(1070)]
    public class SleeveActionGuard : MonoBehaviour
    {
        [Tooltip("동작 상태를 읽을 플레이어 (비우면 같은 오브젝트에서 찾음)")]
        public PlayerController player;
        [Tooltip("보정할 소매 클로스 (비우면 자식 중 이름에 Sleeve 가 들어간 MagicaCloth)")]
        public List<MagicaCloth> sleeves = new List<MagicaCloth>();

        [Tooltip("동작 시작 때 애니메이션 자세로 붙이는 시간(초)")]
        [Min(0f)] public float fadeToAnimation = 0.04f;
        [Tooltip("동작 끝난 뒤 시뮬레이션으로 되돌리는 시간(초)")]
        [Min(0f)] public float fadeToSimulation = 0.35f;
        [Tooltip("동작이 끝날 때 시뮬레이션을 리셋해 팔을 통과한 천을 제자리로 돌림")]
        public bool resetOnActionEnd = true;
        [Tooltip("느린 동작이라 보정하지 않을 상태")]
        public List<PlayerController.ActionState> ignoredActions = new List<PlayerController.ActionState>
        {
            PlayerController.ActionState.None, PlayerController.ActionState.Draw, PlayerController.ActionState.Sheath,
            PlayerController.ActionState.Death, PlayerController.ActionState.StandUp,
        };

        float _weight = 1f;
        bool _wasFast;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            if (sleeves.Count == 0)
                foreach (var c in GetComponentsInChildren<MagicaCloth>(true))
                    if (c.name.Contains("Sleeve")) sleeves.Add(c);
        }

        void Update()
        {
            if (!player) return;
            bool fast = !ignoredActions.Contains(player.CurrentAction);
            if (_wasFast && !fast && resetOnActionEnd)
                foreach (var c in sleeves) if (c && c.IsValid()) c.ResetCloth();   // 표시는 아직 애니메이션 자세라 튀지 않음
            float target = fast ? 0f : 1f;
            float time = fast ? fadeToAnimation : fadeToSimulation;
            _weight = time > 0f ? Mathf.MoveTowards(_weight, target, Time.deltaTime / time) : target;
            foreach (var c in sleeves)
            {
                if (!c || Mathf.Approximately(c.SerializeData.blendWeight, _weight)) continue;
                c.SerializeData.blendWeight = _weight;
                c.SetParameterChange();
            }
            _wasFast = fast;
        }

        void OnDisable()
        {
            foreach (var c in sleeves)
                if (c && !Mathf.Approximately(c.SerializeData.blendWeight, 1f)) { c.SerializeData.blendWeight = 1f; c.SetParameterChange(); }
            _weight = 1f; _wasFast = false;
        }
    }
}
