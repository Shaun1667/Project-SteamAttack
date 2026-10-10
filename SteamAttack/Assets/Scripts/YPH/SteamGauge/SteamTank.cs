using System;
using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 스팀백에 남은 증기압을 관리합니다. 소비·회복 요청을 검사한 뒤 값을 바꾸고 이벤트로 결과를 알립니다.
    /// 화면 표현은 각 뷰가 담당하므로, 이 클래스는 게이지·바늘·파티클을 직접 조작하지 않습니다.
    /// </summary>
    // 탱크를 뷰보다 먼저 초기화하여, 뷰가 OnEnable에서 읽는 값이 시작 압력이 되도록 합니다.
    [DefaultExecutionOrder(-100)]
    public class SteamTank : MonoBehaviour
    {
        // 소수 계산의 반올림 오차 때문에 잔량과 거의 같은 비용이 부족으로 판정되지 않게 합니다.
        private const float ConsumeEpsilon = 0.0001f;

        [Tooltip("저장할 수 있는 최대 증기압입니다. 시작 압력이 같으면 이 값을 높일수록 게이지의 채움 비율은 낮아집니다. 플레이 시작 전에 설정하세요. 실행 중 용량 변경은 지원하지 않습니다.")]
        [SerializeField, Min(1f)] private float _maxPressure = 100f;
        [Tooltip("플레이 시작 시 보유할 증기압입니다. 높일수록 게이지가 더 차고 바늘이 가득 찬 쪽으로 이동합니다. 시작할 때 0~최대 압력으로 제한되며, 실행 중 이 값을 바꿔도 현재 압력은 바뀌지 않습니다.")]
        [SerializeField, Min(0f)] private float _startPressure = 100f;

        // 인스펙터의 시작값과 실제 잔량을 분리합니다. 초기화 후에는 아래 소비·회복 경로로만 바꿉니다.
        private float _currentPressure;

        /// <summary>현재 남은 증기압입니다.</summary>
        public float CurrentPressure => _currentPressure;
        /// <summary>최대 증기압입니다.</summary>
        public float MaxPressure => _maxPressure;
        /// <summary>뷰에서 사용할 잔량 비율입니다. 0은 빈 상태, 1은 가득 찬 상태입니다.</summary>
        public float NormalizedPressure => Mathf.Clamp01(_currentPressure / _maxPressure);

        /// <summary>실제 값 변경 후 현재값·최대값을 전달합니다. 채움 뷰와 바늘 뷰가 이 알림을 받습니다.</summary>
        public event Action<float, float> OnPressureChanged;
        /// <summary>소비로 줄어든 실제 증기량을 전달합니다. 압력 변경 알림 다음에 발생하며 배출구가 받습니다.</summary>
        public event Action<float> OnConsumed;
        /// <summary>유효한 요청을 잔량 부족으로 처리하지 못했을 때 요청량을 전달합니다. 잘못된 입력과 0 요청은 제외합니다.</summary>
        public event Action<float> OnConsumeFailed;

        private void Awake()
        {
            // 최대값이 0이면 비율 계산이 불가능하므로 최소 1을 보장합니다.
            // NaN·무한대도 저장하지 않으며, 시작 잔량을 용량 안으로 제한합니다.
            // 초기화 이벤트는 보내지 않습니다. 뷰가 활성화될 때 현재값을 직접 읽습니다.
            _maxPressure = IsFinite(_maxPressure) ? Mathf.Max(1f, _maxPressure) : 100f;
            _currentPressure = IsFinite(_startPressure) ? Mathf.Clamp(_startPressure, 0f, _maxPressure) : 0f;
        }

        /// <summary>
        /// 잔량을 바꾸거나 이벤트를 보내지 않고 소비 가능 여부만 확인합니다.
        /// 0은 항상 가능하며, 음수·NaN·무한대는 오류 로그 후 false를 반환합니다.
        /// </summary>
        public bool CanConsume(float amount)
        {
            if (!IsValidAmount(amount))
            {
                return false;
            }
            // 빈 탱크에서는 오차 허용값보다 작은 양수 비용도 성공시키지 않습니다.
            return amount == 0f || (_currentPressure > 0f && amount <= _currentPressure + ConsumeEpsilon);
        }

        /// <summary>
        /// 수류탄 한 개처럼 비용 전체를 감당해야 하는 요청을 처리하고 성공 여부를 반환합니다.
        /// 잔량이 부족하면 일부만 쓰지 않고 그대로 유지합니다. 0 요청은 이벤트 없이 성공합니다.
        /// </summary>
        public bool TryConsume(float amount)
        {
            // 입력 오류는 소비 실패 연출과 구분합니다. 잘못된 요청에는 오류 로그만 남깁니다.
            if (!IsValidAmount(amount))
            {
                return false;
            }
            // 비용이 없으면 압력 변경이나 배출구 반응도 필요하지 않습니다.
            if (amount == 0f)
            {
                return true;
            }
            // 유효하지만 감당할 수 없는 요청에만 실패 이벤트를 보내 작은 분출을 재생합니다.
            if (_currentPressure <= 0f || amount > _currentPressure + ConsumeEpsilon)
            {
                OnConsumeFailed?.Invoke(amount);
                return false;
            }
            // 성공 경로는 낱개 소비와 같은 내부 메서드를 사용해 값 변경·이벤트 순서를 일치시킵니다.
            Consume(amount);
            return true;
        }

        /// <summary>
        /// 총알 재장전처럼 같은 비용의 낱개를 가능한 개수만큼 소비하고 처리한 개수를 반환합니다.
        /// 예: 잔량 14, 발당 비용 2, 요청 10발이면 7발분을 소비하고 7을 반환합니다.
        /// 호출하는 쪽은 요청 개수가 아니라 반환된 개수만큼 장전해야 합니다.
        /// </summary>
        public int TryConsumeUnits(float unitCost, int requestedUnits)
        {
            // 0 이하 비용은 나눗셈·개수 계산의 기준이 될 수 없고, 음수 개수도 의미가 없습니다.
            if (!IsFinite(unitCost) || unitCost <= 0f || requestedUnits < 0)
            {
                Debug.LogError("증기 낱개 비용은 유한한 양수, 요청 개수는 0 이상이어야 합니다.", this);
                return 0;
            }
            // 장전할 발이 없는 요청은 부족 실패가 아니므로 이벤트 없이 끝냅니다.
            if (requestedUnits == 0)
            {
                return 0;
            }
            // 잔량을 발당 비용으로 나눈 뒤 소수 부분을 버려 온전한 발 수만 구합니다.
            // 빈 탱크는 0발로 고정하고, double 계산 후 요청 개수로 제한하여 int 범위를 넘지 않게 합니다.
            int count = _currentPressure <= 0f ? 0 : (int)Math.Min(requestedUnits, Math.Floor(((double)_currentPressure + ConsumeEpsilon) / unitCost));
            if (count == 0)
            {
                // 한 발도 처리할 수 없을 때만 실패를 알립니다. 곱셈은 double로 하고 알림값은 float 범위로 제한합니다.
                OnConsumeFailed?.Invoke((float)Math.Min(float.MaxValue, (double)unitCost * requestedUnits));
                return 0;
            }
            // 일부만 처리했어도 성공 소비입니다. 처리하지 못한 나머지에 대해 실패 이벤트를 추가로 보내지 않습니다.
            Consume((float)((double)count * unitCost));
            return count;
        }

        /// <summary>
        /// 적 타격 등으로 얻은 증기를 최대 용량까지 회복합니다. 실제 연결은 호출하는 쪽에서 담당합니다.
        /// 회복은 압력 변경만 알리므로 배출구가 분출하지 않습니다. 이미 가득 찼으면 알림도 없습니다.
        /// </summary>
        public void Refill(float amount)
        {
            if (!IsValidAmount(amount))
            {
                return;
            }
            // 회복 전용 이벤트를 만들지 않고 공통 값 변경 경로로 보내 뷰만 갱신합니다.
            SetPressure(Mathf.Min(_maxPressure, _currentPressure + amount));
        }

        /// <summary>
        /// 시간 역행(CHG_TimeRewind) 등으로 저장해 둔 압력으로 되돌립니다. — NGH(남귀훈) 추가
        /// 소비가 아니므로 배출구 분출(OnConsumed)·실패 이벤트 없이 압력 변경 알림만 보냅니다. 0~최대 압력으로 제한합니다.
        /// </summary>
        public void RestorePressure(float pressure)
        {
            if (!IsFinite(pressure))
            {
                Debug.LogError("복원할 증기압은 유한한 값이어야 합니다.", this);
                return;
            }
            SetPressure(Mathf.Clamp(pressure, 0f, _maxPressure));
        }

        private void Consume(float amount)
        {
            // 오차 허용으로 요청량이 잔량보다 아주 조금 커질 수 있어 0 아래로 내려가지 않게 합니다.
            // 배출구에는 요청량 대신 변경 전후 차이를 전달해야 실제 소비량에 맞게 분출합니다.
            float previous = _currentPressure;
            float next = Mathf.Max(0f, previous - amount);
            float consumed = previous - next;
            // 먼저 잔량을 저장하고 뷰에 알린 뒤, 실제 감소가 있을 때만 소비 연출을 알립니다.
            SetPressure(next);
            if (consumed > 0f)
            {
                OnConsumed?.Invoke(consumed);
            }
        }

        private void SetPressure(float pressure)
        {
            // 소비·회복의 공통 변경 지점입니다. 호출부에서 이미 유효한 범위로 계산한 값을 받습니다.
            // 같은 값을 반복 통지하면 불필요한 뷰 갱신이 생기므로 값이 달라졌을 때만 이벤트를 보냅니다.
            if (_currentPressure == pressure)
            {
                return;
            }
            _currentPressure = pressure;
            OnPressureChanged?.Invoke(_currentPressure, _maxPressure);
        }

        private bool IsValidAmount(float amount)
        {
            // 음수 요청으로 소비가 회복처럼 동작하거나, NaN·무한대가 잔량 계산에 퍼지는 것을 막습니다.
            if (IsFinite(amount) && amount >= 0f)
            {
                return true;
            }
            Debug.LogError("증기량은 유한한 0 이상의 값이어야 합니다.", this);
            return false;
        }

        private static bool IsFinite(float value)
        {
            // NaN은 계산 불가능한 값, Infinity는 무한대입니다. 둘 다 유효한 증기량으로 사용하지 않습니다.
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
