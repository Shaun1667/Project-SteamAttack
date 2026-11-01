using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 낮은 HP 화면 연출: HP가 기준보다 낮아지면 화면 색이 바래고, 가장자리에 먹빛이 심장 박동처럼 번졌다 빠짐.
    ///  - HP가 낮을수록 더 바래고, 박동이 빨라지고, 먹빛이 안쪽까지 들어옴
    ///  - 화면 효과는 NGH_ScreenFX 셰이더가 전역 값 _NGH_LowHpWash(바램)·_NGH_LowHpPulse(먹빛)를 읽어 그림
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class LowHpScreen : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public PlayerController player;

        [Header("언제")]
        [Tooltip("최대 HP에 대한 비율이 이보다 낮으면 연출 시작")]
        [Range(0.05f, 1f)] public float startRatio = 0.35f;
        [Tooltip("이 비율 이하이면 연출이 가장 셈")]
        [Range(0f, 1f)] public float fullRatio = 0.1f;

        [Header("세기")]
        [Tooltip("가장 셀 때 화면이 바래는 정도 (0~1)")]
        [Range(0f, 1f)] public float maxWash = 0.6f;
        [Tooltip("박동 사이에도 남아 있는 먹빛 (0~1)")]
        [Range(0f, 1f)] public float basePulse = 0.4f;
        [Tooltip("박동이 가장 셀 때 먹빛 (0~1)")]
        [Range(0f, 1f)] public float peakPulse = 0.85f;

        [Header("박동 (1분에 몇 번)")]
        [Tooltip("연출이 막 시작될 때 박동 빠르기")]
        public float slowBpm = 60f;
        [Tooltip("가장 셀 때 박동 빠르기")]
        public float fastBpm = 110f;

        [Tooltip("연출이 켜지고 꺼지는 빠르기 (초당 변화량). 시간 역행으로 HP가 돌아올 때 부드럽게 빠짐")]
        public float blendSpeed = 2f;

        static readonly int WashId = Shader.PropertyToID("_NGH_LowHpWash");
        static readonly int PulseId = Shader.PropertyToID("_NGH_LowHpPulse");

        float _amount;   // 0~1, 지금 연출 세기
        float _phase;    // 박동 진행 (0~1 반복)

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
        }

        void OnDisable()
        {
            _amount = 0f;
            Shader.SetGlobalFloat(WashId, 0f);
            Shader.SetGlobalFloat(PulseId, 0f);
        }

        void Update()
        {
            float target = 0f;
            if (player && player.maxHp > 0)
            {
                float ratio = (float)player.Hp / player.maxHp;
                target = player.IsDead ? 1f : Mathf.InverseLerp(startRatio, fullRatio, ratio);
            }
            _amount = Mathf.MoveTowards(_amount, target, blendSpeed * Time.unscaledDeltaTime);
            if (_amount <= 0.001f)
            {
                _phase = 0f;
                Shader.SetGlobalFloat(WashId, 0f);
                Shader.SetGlobalFloat(PulseId, 0f);
                return;
            }

            // 박동: '쿵-쿵' 두 번 뛰고 쉼. 사망하면 멈춘 채 남음
            float beat = 0f;
            if (!(player && player.IsDead))
            {
                _phase = (_phase + Time.unscaledDeltaTime * Mathf.Lerp(slowBpm, fastBpm, _amount) / 60f) % 1f;
                beat = Mathf.Max(Beat(_phase, 0f), Beat(_phase, 0.18f) * 0.6f);
            }
            float pulse = Mathf.Lerp(basePulse, peakPulse, beat) * _amount;
            Shader.SetGlobalFloat(WashId, maxWash * _amount);
            Shader.SetGlobalFloat(PulseId, pulse);
        }

        // start 시점에 빠르게 올랐다가 천천히 빠지는 한 번의 박동 (0~1)
        static float Beat(float phase, float start)
        {
            float t = phase - start;
            if (t < 0f) return 0f;
            const float rise = 0.05f, fall = 0.25f;
            if (t < rise) return t / rise;
            t -= rise;
            return t < fall ? 1f - t / fall : 0f;
        }
    }
}
