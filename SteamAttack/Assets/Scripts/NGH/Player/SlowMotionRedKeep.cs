using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 시간이 느려지는 순간(보스 처치 슬로모션 등) 화면이 흑백이 되고 붉은색만 남는 연출.
    ///  - Time.timeScale 이 일정 시간 이상 낮게 유지될 때만 켜짐 (짧은 히트스톱·일시정지(0)는 무시)
    ///  - 화면 효과는 NGH_ScreenFX 셰이더(PC_Renderer 의 NGH_ScreenFX)가 전역 값 _NGH_RedKeep 를 읽어 그림
    /// 플레이어에 붙입니다.
    /// </summary>
    public class SlowMotionRedKeep : MonoBehaviour
    {
        [Tooltip("이 값보다 timeScale 이 낮으면 느려진 것으로 봄")]
        [Range(0.05f, 1f)] public float slowBelow = 0.6f;
        [Tooltip("느려진 상태가 이 시간(실제 초) 넘게 이어져야 켜짐 — 히트스톱 같은 짧은 멈춤은 무시")]
        [Min(0f)] public float minSlowTime = 0.15f;
        [Tooltip("켜질 때 / 꺼질 때 걸리는 시간 (실제 초)")]
        [Min(0.01f)] public float fadeIn = 0.25f;
        [Min(0.01f)] public float fadeOut = 0.6f;

        static readonly int RedKeepId = Shader.PropertyToID("_NGH_RedKeep");

        float _value, _slowFor;

        void OnDisable()
        {
            _value = _slowFor = 0f;
            Shader.SetGlobalFloat(RedKeepId, 0f);
        }

        void Update()
        {
            float ts = Time.timeScale;
            bool slow = ts > 0.001f && ts < slowBelow;   // 0 = 일시정지라 제외
            _slowFor = slow ? _slowFor + Time.unscaledDeltaTime : 0f;
            bool on = _slowFor >= minSlowTime;
            float speed = Time.unscaledDeltaTime / (on ? fadeIn : fadeOut);
            float next = Mathf.MoveTowards(_value, on ? 1f : 0f, speed);
            if (Mathf.Approximately(next, _value) && next == 0f) return;
            _value = next;
            Shader.SetGlobalFloat(RedKeepId, Mathf.SmoothStep(0f, 1f, _value));
        }
    }
}
