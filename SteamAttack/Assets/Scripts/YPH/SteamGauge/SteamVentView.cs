using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 탱크의 소비 성공·실패 알림을 받아 배출구에서 파티클을 한 번씩 분출합니다.
    /// 잔량이 있을 때만 분출하며, 잔량이 0이 되면 남아 있는 입자까지 제거하고 재생을 멈춥니다.
    /// 성공은 실제 소비량에 비례하고, 양수 잔량의 부족 실패는 정해진 개수만 분출합니다. 회복만으로는 분출하지 않습니다.
    /// </summary>
    public class SteamVentView : MonoBehaviour
    {
        [Tooltip("소비 성공·실패와 잔량을 확인할 증기 탱크입니다. 잔량이 0 이하이면 분출을 멈추며 회복만으로는 분출하지 않습니다. 플레이 시작 전에 연결하세요.")]
        [SerializeField] private SteamTank _tank;
        [Tooltip("소비할 때 분출할 파티클 시스템입니다. 연결 대상을 바꾸면 증기가 나오는 위치와 모양이 바뀝니다. 평소 자동 분출을 막으려면 해당 시스템의 Rate over Time을 0으로 설정하세요.")]
        [SerializeField] private ParticleSystem _ventParticles;
        [Tooltip("실제 소비한 증기 1당 분출할 파티클 수입니다. 높일수록 많이 분출하며 소수 개수는 올림합니다. 예: 0.5에서 증기 20 소비 시 10개. 소비 성공 시 최소 1개이므로 0으로 설정해도 1개가 나옵니다.")]
        [SerializeField, Min(0f)] private float _particlesPerPressure = 0.5f;
        [Tooltip("소비 성공 한 번에 분출할 파티클 수의 상한입니다. 낮추면 큰 소비에서도 분출 개수가 제한됩니다. 소비 실패의 분출 개수에는 적용되지 않습니다.")]
        [SerializeField, Min(1)] private int _maxBurstCount = 60;
        [Tooltip("잔량은 있지만 요청량보다 부족하여 소비하지 못했을 때 분출할 파티클 수입니다. 잔량이 0이면 이 값과 관계없이 분출하지 않습니다. 0이면 모든 실패 분출을 끕니다.")]
        [SerializeField, Min(0)] private int _failBurstCount = 8;

        private void Awake()
        {
            // 알림을 보낼 탱크와 실제 분출 대상이 모두 있어야 동작하므로 누락 시 이 뷰만 중단합니다.
            if (_tank == null || _ventParticles == null)
            {
                Debug.LogError("증기 배출구의 Tank와 Vent Particles를 연결해 주세요.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            // 분출은 잔량의 현재 상태가 아니라 소비가 일어난 순간을 표현하므로 초기 분출은 하지 않습니다.
            if (_tank == null || _ventParticles == null)
            {
                return;
            }
            // 압력 알림은 빈 탱크의 재생을 멈추는 데만 사용합니다. 분출은 소비 결과 알림에서만 요청합니다.
            _tank.OnPressureChanged += OnPressureChanged;
            _tank.OnConsumed += OnConsumed;
            _tank.OnConsumeFailed += OnConsumeFailed;
            OnPressureChanged(_tank.CurrentPressure, _tank.MaxPressure);
        }

        private void OnDisable()
        {
            // 꺼진 배출구가 분출하지 않게 하고, 다시 켰을 때 알림이 중복되지 않도록 구독과 재생을 정리합니다.
            if (_tank != null)
            {
                _tank.OnPressureChanged -= OnPressureChanged;
                _tank.OnConsumed -= OnConsumed;
                _tank.OnConsumeFailed -= OnConsumeFailed;
            }
            if (_ventParticles != null)
            {
                _ventParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnPressureChanged(float current, float max)
        {
            // 소비 이벤트보다 먼저 호출됩니다. 마지막 소비로 빈 탱크가 되면 기존 입자까지 즉시 지웁니다.
            // 회복으로 양수가 되어도 여기서는 Play를 호출하지 않아 자동으로 분출하지 않습니다.
            if (current <= 0f)
            {
                _ventParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void OnConsumed(float amount)
        {
            // 소비량에 비례한 개수를 올림하되, 성공 피드백은 최소 1개를 보장하고 과도한 분출은 상한으로 막습니다.
            // Emit은 지금 이 순간 지정 개수만 생성합니다. 평소 방출량은 파티클 시스템 설정에서 0으로 둡니다.
            Emit(Mathf.Clamp(Mathf.CeilToInt(amount * _particlesPerPressure), 1, Mathf.Max(1, _maxBurstCount)));
        }

        private void OnConsumeFailed(float amount)
        {
            // 요청량이 커도 실패 연출은 같은 크기로 유지합니다. 0개 설정이면 실패 분출을 끌 수 있습니다.
            Emit(Mathf.Max(0, _failBurstCount));
        }

        private void Emit(int count)
        {
            // 성공·실패가 같은 잔량 검사를 거칩니다. 마지막 소비 알림이 정지된 배출구를 다시 켜지 않게 합니다.
            if (_tank.CurrentPressure <= 0f || count <= 0)
            {
                return;
            }
            // 자동 재생을 끈 파티클을 소비 시점에만 재생합니다. 정지 후 회복한 탱크도 다음 소비부터 다시 분출합니다.
            _ventParticles.Play();
            _ventParticles.Emit(count);
        }
    }
}
