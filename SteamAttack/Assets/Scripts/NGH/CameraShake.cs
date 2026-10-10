using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 공격이 적(Damageable)에게 맞으면 카메라를 흔들어 타격감을 줍니다. 카메라(NGH_PlayerCamera)에 붙입니다.
    ///  - PlayerController.OnHitLanded 를 받아서 동작 종류별로 세기를 다르게 줌 (약공격 &lt; 마무리 5타 &lt; 강공격)
    ///  - 흔들림은 ThirdPersonCamera 가 위치·회전을 정한 뒤에 얹으므로 카메라 조작·락온·충돌 처리에 영향 없음
    ///  - 세기(trauma)는 맞을 때마다 쌓이고(최대 1) 시간이 지나며 줄어듦. 실제 흔들림 크기는 trauma² 에 비례
    ///  - 시간 배율(슬로우·정지)과 무관하게 일정한 속도로 흔들리도록 unscaledDeltaTime 사용
    /// 다른 스크립트에서 직접 흔들고 싶으면 CameraShake.Add(0.5f) 처럼 호출하세요.
    /// </summary>
    [DefaultExecutionOrder(1500)]   // ThirdPersonCamera(기본 0)의 LateUpdate 다음
    public class CameraShake : MonoBehaviour
    {
        [Header("참조 (비우면 씬에서 자동으로 찾음)")]
        public PlayerController player;

        [Header("맞았을 때 세기 (0~1, 흔들림 크기는 제곱에 비례)")]
        [Range(0, 1)] public float lightHit = 0.5f;
        [Tooltip("약공격 마지막 타(5타)")]
        [Range(0, 1)] public float finisherHit = 0.75f;
        [Range(0, 1)] public float heavyHit = 1f;

        [Header("흔들림")]
        [Tooltip("세기 1일 때 최대 위치 흔들림(m). 카메라 기준 좌우·상하")]
        public float maxOffset = 0.12f;
        [Tooltip("세기 1일 때 최대 회전 흔들림(도)")]
        public float maxAngle = 3f;
        [Tooltip("흔들림 빠르기(Hz)")]
        public float frequency = 26f;
        [Tooltip("초당 세기 감소량. 클수록 빨리 멈춤 (3 = 세기 1이 약 0.33초)")]
        public float decay = 3f;
        [Tooltip("맞는 순간 카메라가 살짝 뒤로 밀렸다 돌아오는 정도(m, 세기 1 기준)")]
        public float kick = 0.06f;

        static CameraShake _instance;
        float _trauma;
        float _seed;

        /// <summary>세기를 더함 (0~1). 이미 흔들리는 중이면 쌓임</summary>
        public static void Add(float amount)
        {
            if (_instance) _instance._trauma = Mathf.Clamp01(_instance._trauma + amount);
        }

        void OnEnable()
        {
            _instance = this;
            _seed = Random.value * 100f;
            if (!player) player = FindAnyObjectByType<PlayerController>();
            if (player) player.OnHitLanded += OnHit;
        }

        void OnDisable()
        {
            if (_instance == this) _instance = null;
            if (player) player.OnHitLanded -= OnHit;
            _trauma = 0f;
        }

        void OnHit(Damageable target)
        {
            float amount;
            switch (player.CurrentAction)
            {
                case PlayerController.ActionState.HeavyAttack: amount = heavyHit; break;
                case PlayerController.ActionState.LightCombo5: amount = finisherHit; break;
                default: amount = lightHit; break;
            }
            // 이미 세게 흔들리는 중이면 더 약한 타격이 덮어써서 약해지지 않도록 쌓기만 함
            _trauma = Mathf.Clamp01(Mathf.Max(_trauma, amount) + 0.15f * amount);
        }

        void LateUpdate()
        {
            if (_trauma <= 0.0001f) { _trauma = 0f; return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);   // 프레임이 한 번 크게 튀어도 흔들림이 통째로 사라지지 않게
            float t = Time.unscaledTime * frequency;
            float s = _trauma * _trauma;

            // 위치: 카메라 기준 좌우·상하 + 맞는 순간의 뒤로 밀림(세기에 비례해 빨리 사라짐)
            Vector3 offset = new Vector3(Noise(0, t), Noise(1, t), 0f) * (maxOffset * s)
                           - Vector3.forward * (kick * s * s);
            transform.position += transform.TransformVector(offset);
            // 회전: 좌우·상하 + 살짝 기울임
            transform.rotation *= Quaternion.Euler(Noise(2, t) * maxAngle * s, Noise(3, t) * maxAngle * s, Noise(4, t) * maxAngle * 0.5f * s);

            _trauma = Mathf.Max(0f, _trauma - decay * dt);
        }

        // -1 ~ 1 부드러운 무작위
        float Noise(int channel, float t) => Mathf.PerlinNoise(_seed + channel * 17.31f, t) * 2f - 1f;
    }
}
