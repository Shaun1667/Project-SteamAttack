using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 현무 냉기 피격 화면 연출: 현무가 있는 씬에서 맞으면 화면 가장자리에 성에가 끼었다가 서서히 녹음.
    ///  - 강공격(피격 대)이면 더 안쪽까지 번짐
    ///  - 화면 효과는 NGH_ScreenFX 셰이더(PC_Renderer 의 NGH_ScreenFX)가 전역 값 _NGH_Frost 를 읽어 그림
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class HitScreenFrost : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public PlayerController player;

        [Header("세기")]
        [Tooltip("한 번 맞았을 때 성에 양 (0~1)")]
        [Range(0f, 1f)] public float amount = 0.55f;
        [Tooltip("강공격(피격 대) 때 성에 양")]
        [Range(0f, 1f)] public float heavyAmount = 0.9f;
        [Tooltip("현무(거북·뱀 보스)가 씬에 있을 때만 성에가 낌")]
        public bool onlyWithHyeonmu = true;

        [Header("시간 (초, 실제 시간 기준)")]
        [Min(0f)] public float growTime = 0.12f;
        [Tooltip("다 낀 뒤 녹기 시작할 때까지")]
        [Min(0f)] public float hold = 1.6f;
        [Min(0.01f)] public float meltTime = 4.1f;

        static readonly int FrostId = Shader.PropertyToID("_NGH_Frost");

        float _value, _target, _holdUntil;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
        }

        void OnEnable()
        {
            if (player) player.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            if (player) player.Damaged -= OnDamaged;
            _value = _target = 0f;
            Shader.SetGlobalFloat(FrostId, 0f);
        }

        void OnDamaged(int damage, bool heavy)
        {
            if (onlyWithHyeonmu && !HyeonmuPresent()) return;
            _target = Mathf.Max(_target, heavy ? heavyAmount : amount);
            _holdUntil = Time.unscaledTime + growTime + hold;
        }

        static bool HyeonmuPresent()
        {
            return FindAnyObjectByType<BossFSM>() != null || FindAnyObjectByType<SnakeFSM>() != null;
        }

        void Update()
        {
            if (_target <= 0f && _value <= 0f) return;
            if (_value < _target)
                _value = Mathf.MoveTowards(_value, _target, Time.unscaledDeltaTime / Mathf.Max(growTime, 0.01f));
            else if (Time.unscaledTime >= _holdUntil)
            {
                _target = 0f;
                _value = Mathf.MoveTowards(_value, 0f, Time.unscaledDeltaTime / meltTime);
            }
            Shader.SetGlobalFloat(FrostId, Mathf.SmoothStep(0f, 1f, _value));
        }
    }
}
