using UnityEngine;

/// <summary>
/// 같은 몸체의 FSM들이 공격 예고부터 공격 종료까지 하나의 실행 권한을 공유합니다.
/// 일반 이동/회전과 개별 FSM의 Recovery는 막지 않습니다.
/// </summary>
[DisallowMultipleComponent]
public class BossAttackCoordinator : MonoBehaviour
{
    [Header("두 FSM이 공유하는 공격 간격")]
    [Tooltip("공격 종료/취소부터 다음 공격 예고 시작까지의 최소 시간. 각 패턴의 Recovery Time과 동시에 흐릅니다.")]
    [Min(0f)] public float commonAttackGap = 0.7f;

    [Header("현재 공격 권한 - 실행 중 확인용")]
    [SerializeField] private MonoBehaviour currentOwner;
    [SerializeField] private BossAttack currentAttack;

    public MonoBehaviour CurrentOwner => currentOwner;
    public BossAttack CurrentAttack => currentAttack;
    public bool IsReserved => hasReservation;
    public float GapRemaining => Mathf.Max(0f, nextAttackTime - Time.time);

    private bool hasReservation;
    private bool releasing;
    private float nextAttackTime;

    // 반드시 BeginAttack보다 먼저 호출합니다. 먼저 성공한 FSM만 예고를 시작합니다.
    public bool TryAcquire(MonoBehaviour requester, BossAttack attack)
    {
        if (!isActiveAndEnabled || requester == null || !requester.isActiveAndEnabled ||
            attack == null || !attack.isActiveAndEnabled || releasing) return false;

        // 소유 FSM/공격이 제거된 경우에도 영구 대기하지 않도록 정리합니다.
        if (hasReservation && (currentOwner == null ||
            !currentOwner.isActiveAndEnabled || currentAttack == null))
            ClearReservation(true);

        if (hasReservation || Time.time < nextAttackTime || attack.IsRunning) return false;

        hasReservation = true;
        currentOwner = requester;
        currentAttack = attack;
        return true;
    }

    // 실제로 획득한 FSM만 반납할 수 있습니다. 시작 실패 시에만 applyGap=false.
    public void Release(MonoBehaviour requester, bool applyGap = true)
    {
        if (!hasReservation || currentOwner != requester) return;
        ClearReservation(applyGap);
    }

    private void ClearReservation(bool applyGap)
    {
        if (!hasReservation || releasing) return;
        releasing = true;
        try
        {
            // 권한을 반납하면서 진행 중인 판정이 남지 않도록 합니다.
            if (currentAttack != null && currentAttack.IsRunning)
                currentAttack.CancelAttack();
        }
        finally
        {
            currentOwner = null;
            currentAttack = null;
            hasReservation = false;
            if (applyGap) nextAttackTime = Time.time + Mathf.Max(0f, commonAttackGap);
            releasing = false;
        }
    }

    // 일시 비활성화만으로 진행 중인 권한을 풀지는 않습니다.
    // 그동안 새 공격은 차단되고, 기존 FSM은 공격 종료 후 정상 반납할 수 있습니다.
    private void OnDestroy()
    {
        ClearReservation(true);
    }

    private void OnValidate()
    {
        commonAttackGap = Mathf.Max(0f, commonAttackGap);
    }
}
