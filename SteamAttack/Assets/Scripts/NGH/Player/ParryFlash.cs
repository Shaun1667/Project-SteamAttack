using UnityEngine;
using YPH;

namespace NGH
{
    /// <summary>
    /// 받아치기 성공 화면 연출: 성공하는 순간 화면이 잠깐 종이색↔먹색으로 뒤집히고, 받아친 자리에서 충격파가 퍼짐.
    ///  - 반전은 NGH_ScreenFX 셰이더가 전역 값 _NGH_ParryFlash(0~1)를 읽어 그림
    ///  - 충격파는 같은 플레이어의 ScreenShockwave.Play()를 다시 씀
    /// 플레이어(ParryController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class ParryFlash : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public ParryController parry;
        public ScreenShockwave shockwave;

        [Header("반전 (초, 실제 시간 기준)")]
        [Tooltip("완전히 뒤집힌 채로 머무는 시간")]
        [Min(0f)] public float holdTime = 0.05f;
        [Tooltip("원래 화면으로 돌아오는 시간")]
        [Min(0.01f)] public float fadeTime = 0.1f;
        [Tooltip("가장 세게 뒤집혔을 때의 세기 (1 = 완전히 반전)")]
        [Range(0f, 1f)] public float strength = 1f;

        [Header("충격파")]
        [Tooltip("받아친 자리에서 충격파를 퍼뜨림")]
        public bool playShockwave = true;
        [Tooltip("받아친 대상을 모를 때 플레이어 발 기준 충격파 높이(m)")]
        public float fallbackHeight = 1.2f;

        static readonly int FlashId = Shader.PropertyToID("_NGH_ParryFlash");

        float _t = -1f;
        IParryTarget _target;   // TargetParried → ParryAttempted(true) 사이에만 유효

        void Awake()
        {
            if (!parry) parry = GetComponent<ParryController>();
            if (!shockwave) shockwave = GetComponent<ScreenShockwave>();
        }

        void OnEnable()
        {
            if (!parry) return;
            parry.TargetParried += OnTargetParried;
            parry.ParryAttempted += OnParryAttempted;
        }

        void OnDisable()
        {
            if (parry)
            {
                parry.TargetParried -= OnTargetParried;
                parry.ParryAttempted -= OnParryAttempted;
            }
            _target = null;
            _t = -1f;
            Shader.SetGlobalFloat(FlashId, 0f);
        }

        // 성공 때 TargetParried가 ParryAttempted(true)보다 먼저 옴
        void OnTargetParried(IParryTarget target) => _target = target;

        void OnParryAttempted(bool success)
        {
            IParryTarget target = _target;
            _target = null;
            if (!success) return;

            _t = 0f;
            Shader.SetGlobalFloat(FlashId, strength);
            if (playShockwave && shockwave)
                shockwave.Play(target != null ? target.ParryPoint : transform.position + Vector3.up * fallbackHeight);
        }

        void Update()
        {
            if (_t < 0f) return;
            _t += Time.unscaledDeltaTime;
            if (_t >= holdTime + fadeTime) { _t = -1f; Shader.SetGlobalFloat(FlashId, 0f); return; }
            float k = _t <= holdTime ? 1f : 1f - (_t - holdTime) / fadeTime;
            Shader.SetGlobalFloat(FlashId, strength * k * k);   // 머문 뒤 빠르게 빠짐
        }
    }
}
