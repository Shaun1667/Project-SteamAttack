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
    /// 시간 역행(TimeRewind) 중에도 기록된 자세를 빠르게 거꾸로 재생하므로 같은 방식으로 보정합니다.
    /// 가만히 서 있을 때도 천이 처지며 소맷부리가 뒤집혀 안감(붉은색)이 보이므로 애니메이션 자세로 붙여 둡니다.
    /// </summary>
    [DefaultExecutionOrder(1070)]
    public class SleeveActionGuard : MonoBehaviour
    {
        [Tooltip("동작 상태를 읽을 플레이어 (비우면 같은 오브젝트에서 찾음)")]
        public PlayerController player;
        [Tooltip("시간 역행 (비우면 같은 오브젝트에서 찾음). 역행 중에도 애니메이션 자세를 따름")]
        public TimeRewind rewind;
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

        [Header("가만히 서 있을 때")]
        [Tooltip("가만히 서 있으면 소매를 애니메이션 자세로 붙임 (서 있을 때 천이 처지며 소맷부리가 뒤집혀 안감이 보이는 것을 막음)")]
        public bool settleWhenIdle = true;
        [Tooltip("서 있을 때의 Blend Weight (0 = 애니메이션 그대로, 1 = 천 시뮬레이션 그대로)")]
        [Range(0f, 1f)] public float idleBlendWeight = 0f;
        [Tooltip("이 속도(m/s)보다 느리면 서 있는 것으로 봄")]
        [Min(0f)] public float idleSpeed = 0.15f;
        [Tooltip("이만큼(초) 서 있어야 붙이기 시작 (잠깐 멈출 때 출렁이지 않게)")]
        [Min(0f)] public float idleDelay = 0.3f;
        [Tooltip("서 있을 때 애니메이션 자세로 붙는 시간(초)")]
        [Min(0.01f)] public float fadeToIdle = 0.5f;

        [Header("서 있을 때 계산 줄이기 (최적화)")]
        [Tooltip("소매가 애니메이션 자세로 다 붙으면 소매 천 계산을 멈춤 (보이는 모습은 같고 계산만 줄어듦)")]
        public bool pauseSleevesWhenSettled = true;
        [Tooltip("서 있을 때 치마 천 계산 빠르기 (1 = 평소, 낮출수록 계산이 줄고 흔들림이 느려짐)")]
        [Range(0f, 1f)] public float idleSkirtTimeScale = 0.4f;
        [Tooltip("치마 클로스 (비우면 자식 중 이름에 Skirt 가 들어간 MagicaCloth)")]
        public List<MagicaCloth> skirts = new List<MagicaCloth>();

        /// <summary>소매가 애니메이션 자세로 붙어 소매 천 계산을 멈춘 상태 — 소매 보정(YPH 소매 접촉·분리)도 이때는 건너뜀</summary>
        public static bool SleevesResting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => SleevesResting = false;

        float _weight = 1f;
        bool _wasFast, _wasIdle, _paused;
        float _idleTime, _skirtScale = 1f;
        CharacterController _cc;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            _cc = GetComponent<CharacterController>();
            if (!rewind) rewind = GetComponent<TimeRewind>();
            if (sleeves.Count == 0)
                foreach (var c in GetComponentsInChildren<MagicaCloth>(true))
                    if (c.name.Contains("Sleeve")) sleeves.Add(c);
            if (skirts.Count == 0)
                foreach (var c in GetComponentsInChildren<MagicaCloth>(true))
                    if (c.name.Contains("Skirt")) skirts.Add(c);
        }

        void SetSleevesPaused(bool paused)
        {
            if (_paused == paused) return;
            _paused = paused;
            SleevesResting = paused;
            foreach (var c in sleeves) if (c && c.IsValid()) c.SetTimeScale(paused ? 0f : 1f);
        }

        void SetSkirtScale(float scale)
        {
            if (Mathf.Approximately(_skirtScale, scale)) return;
            _skirtScale = scale;
            foreach (var c in skirts) if (c && c.IsValid()) c.SetTimeScale(scale);
        }

        void Update()
        {
            if (!player) return;
            bool fast = !ignoredActions.Contains(player.CurrentAction) || (rewind && rewind.IsRewinding);
            if (_wasFast && !fast && resetOnActionEnd)
                foreach (var c in sleeves) if (c && c.IsValid()) c.ResetCloth();   // 표시는 아직 애니메이션 자세라 튀지 않음

            // 가만히 서 있음: 다른 동작이 없고 거의 안 움직이는 상태가 idleDelay 이상 이어짐
            bool still = !fast && player.CurrentAction == PlayerController.ActionState.None;
            if (still && _cc) { Vector3 v = _cc.velocity; v.y = 0f; still = v.magnitude < idleSpeed; }
            _idleTime = still ? _idleTime + Time.deltaTime : 0f;
            bool idle = settleWhenIdle && _idleTime >= idleDelay;
            // 다시 움직이기 시작하면 서 있던 동안 처진 천을 지우고 애니메이션 자세에서 새로 시작 (표시는 아직 애니메이션 자세라 튀지 않음)
            if (_wasIdle && !idle)
            {
                SetSleevesPaused(false);
                foreach (var c in sleeves) if (c && c.IsValid()) c.ResetCloth();
            }
            _wasIdle = idle;
            SetSkirtScale(idle ? idleSkirtTimeScale : 1f);

            float target = fast ? 0f : idle ? idleBlendWeight : 1f;
            float time = fast ? fadeToAnimation : idle ? fadeToIdle : fadeToSimulation;
            _weight = time > 0f ? Mathf.MoveTowards(_weight, target, Time.deltaTime / time) : target;
            foreach (var c in sleeves)
            {
                if (!c || Mathf.Approximately(c.SerializeData.blendWeight, _weight)) continue;
                c.SerializeData.blendWeight = _weight;
                c.SetParameterChange();
            }
            // 서 있는 채 소매가 애니메이션 자세로 다 붙었으면 소매 천 계산을 멈춤
            if (idle && pauseSleevesWhenSettled && _weight <= 0.001f && idleBlendWeight <= 0.001f) SetSleevesPaused(true);
            _wasFast = fast;
        }

        void OnDisable()
        {
            SetSleevesPaused(false);
            SetSkirtScale(1f);
            foreach (var c in sleeves)
                if (c && !Mathf.Approximately(c.SerializeData.blendWeight, 1f)) { c.SerializeData.blendWeight = 1f; c.SetParameterChange(); }
            _weight = 1f; _wasFast = false; _wasIdle = false; _idleTime = 0f;
        }
    }
}
