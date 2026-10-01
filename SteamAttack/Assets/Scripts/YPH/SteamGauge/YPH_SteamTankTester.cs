using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 실제 공격 시스템 대신 키 1·2·3으로 탱크의 부분 소비·전체 소비·회복을 호출하는 개발용 입력입니다.
/// 탱크가 보내는 이벤트를 통해 연결된 뷰까지 함께 시험하며, 총알이나 수류탄 자체는 만들지 않습니다.
/// 2단계(총·수류탄 공격)에서 실제 코드가 탱크를 쓰기 시작하면 삭제합니다.
/// </summary>
public class YPH_SteamTankTester : MonoBehaviour
{
    [Tooltip("키 1·2·3으로 소비·회복을 시험할 증기 탱크입니다. 플레이 시작 전에 연결하세요.")]
    [SerializeField] private YPH_SteamTank _tank;
    [Tooltip("1번 키 테스트에서 총알 한 발당 소비할 증기량입니다. 높일수록 같은 잔량으로 소비할 수 있는 발 수가 줄어듭니다. 반드시 0보다 커야 합니다.")]
    [SerializeField, Min(0.001f)] private float _costPerRound = 2f;
    [Tooltip("1번 키를 한 번 눌렀을 때 요청할 총알 수입니다. 높일수록 한 번에 더 많이 소비하지만, 증기가 부족하면 가능한 발 수만 처리합니다. 0이면 소비하거나 실패 분출하지 않습니다.")]
    [SerializeField, Min(0)] private int _requestedRounds = 10;
    [Tooltip("2번 키 테스트에서 한 번에 소비할 증기량입니다. 높일수록 필요한 잔량이 커지며 부족하면 전혀 소비하지 않습니다. 0이면 소비·분출 없이 성공 처리됩니다.")]
    [SerializeField, Min(0f)] private float _grenadeCost = 40f;
    [Tooltip("3번 키를 한 번 눌렀을 때 회복할 증기량입니다. 높일수록 많이 회복하지만 최대 압력을 넘지 않습니다. 0이면 변화가 없으며, 회복 시 배출구는 분출하지 않습니다.")]
    [SerializeField, Min(0f)] private float _refillAmount = 30f;

    private void Awake()
    {
        // 조작할 탱크가 없으면 입력 처리를 중단하여 키를 누를 때마다 참조 오류가 나지 않게 합니다.
        if (_tank == null)
        {
            Debug.LogError("증기 테스트용 Tank를 연결해 주세요.", this);
            enabled = false;
        }
    }

    private void Update()
    {
        // 프로젝트는 새 Input System 전용입니다. 키보드가 없는 환경에서는 아무 작업도 하지 않습니다.
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            return;
        }
        // 누르고 있는 동안 반복하지 않고, 눌린 프레임에 한 번만 처리합니다.
        // 각 키를 독립적으로 검사하므로 같은 프레임에 여러 키가 눌리면 1 → 2 → 3 순서로 실행됩니다.
        if (keyboard.digit1Key.wasPressedThisFrame)
        {
            // 총알처럼 가능한 개수만 소비합니다. 로그에는 요청 개수가 아닌 실제 처리한 발 수를 남깁니다.
            int rounds = _tank.TryConsumeUnits(_costPerRound, _requestedRounds);
            Debug.Log($"총알 {rounds}발 소비, 증기 {_tank.CurrentPressure}/{_tank.MaxPressure}", this);
        }
        if (keyboard.digit2Key.wasPressedThisFrame)
        {
            // 수류탄처럼 전체 비용이 있어야 성공합니다. 실패 시 잔량이 유지되는지도 로그로 확인합니다.
            bool succeeded = _tank.TryConsume(_grenadeCost);
            Debug.Log($"수류탄 소비 성공: {succeeded}, 증기 {_tank.CurrentPressure}/{_tank.MaxPressure}", this);
        }
        if (keyboard.digit3Key.wasPressedThisFrame)
        {
            // 적 타격 회복을 대신 호출합니다. 게이지·바늘은 바뀌지만 배출구는 반응하지 않아야 합니다.
            _tank.Refill(_refillAmount);
            Debug.Log($"증기 회복: {_tank.CurrentPressure}/{_tank.MaxPressure}", this);
        }
    }
}
