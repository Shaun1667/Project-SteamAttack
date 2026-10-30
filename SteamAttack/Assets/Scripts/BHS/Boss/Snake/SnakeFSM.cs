using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 뱀 전용 FSM. 기존 BossAttack/BossBreathAttack을 실행합니다.
/// 몸체·뱀 본·IK 타겟을 직접 움직이지 않고 런타임 전용 공격 기준점을 사용합니다.
/// BossAttackCoordinator로 거북이와 공격 실행 권한/공통 간격을 공유합니다.
/// </summary>
[DisallowMultipleComponent]
public class SnakeFSM : MonoBehaviour
{
    public enum State
    {
        Idle, SearchPlayer, SelectAttack, Aim, Attack, Recovery, WaitForPermission
    }

    [Header("현재 상태")]
    [SerializeField] private State currentState;
    [SerializeField] private Transform player;

    [Header("기준점 - 비워 두면 이 오브젝트 위치 사용")]
    [Tooltip("탐색/패턴 선택 거리의 중심. 공통 몸체에 붙인 경우 비워 둬도 됩니다.")]
    public Transform rangeOrigin;
    [Tooltip("브레스 발생 기준 위치. 비우면 Range Origin 사용. 브레스 바닥 Y는 기존 브레스 Ground Y 사용.")]
    public Transform attackOrigin;
    [Tooltip("같은 공격을 두 FSM에서 실행하지 않도록 목록을 확인합니다. 비우면 부모에서 자동 탐색.")]
    public BossFSM bodyFSM;

    [Header("플레이어 탐색 / 공격 방향 조준")]
    [Min(0.1f)] public float detectRange = 8f;
    [Min(0.05f)] public float searchInterval = 0.25f;
    [Min(1f)] public float turnSpeed = 360f;
    [Min(0f)] public float heightTolerance = 2f;

    [Header("뱀 패턴 - 현재는 BossBreathAttack 연결")]
    [Tooltip("현재 거리에서 사용할 수 있는 첫 패턴을 실행합니다. 거북이 Patterns와 중복 등록하지 마세요.")]
    public BossAttack[] patterns = new BossAttack[0];

    [Header("거북이와 공유하는 공격 조율자")]
    [Tooltip("두 FSM에 같은 조율자를 연결하세요. 비우면 자신/부모 또는 Body FSM에서 찾습니다. 없으면 공격을 시작하지 않습니다.")]
    public BossAttackCoordinator attackCoordinator;

    [Header("뱀의 공격 허용 / 공격 사이 간격")]
    [Tooltip("새 공격 시작만 허용/차단합니다. 진행 중인 공격은 끝까지 실행합니다.")]
    public bool allowNewAttacks = true;
    [Tooltip("뱀 공격 종료 후 최소 대기 시간. 실제 대기는 패턴 Recovery Time과 이 값 중 큰 값입니다. 공통 쿨다운은 아닙니다.")]
    [Min(0f)] public float minimumAttackGap = 0.7f;

    [Header("피격 수신 컴포넌트가 없는 대상용 피해 이벤트")]
    [Tooltip("활성 PlayerHitTeleport와 PlayerHealth는 직접 호출합니다. 둘 다 처리하지 않았을 때만 이 이벤트를 호출해 중복 순간이동을 방지합니다.")]
    public UnityEvent<float> onPlayerHit = new UnityEvent<float>();

    public State CurrentState => currentState;
    public Transform Player => player;
    public bool IsAttacking => ownsAttack && activeAttack != null && activeAttack.IsRunning;
    public bool IsBusy => isActiveAndEnabled &&
        (currentState == State.Aim || currentState == State.Attack || currentState == State.Recovery);
    public BossAttack ActiveAttack => activeAttack;

    private BossAttack activeAttack;
    private BossAttackCoordinator acquiredCoordinator;
    private Transform attackPivot;
    private bool ownsAttack;
    private float stateTimer;
    private float recoveryDuration;
    private bool reportedSharedPattern;
    private bool reportedDashPattern;
    private bool reportedMissingCoordinator;

    private Transform RangeOrigin => rangeOrigin != null ? rangeOrigin : transform;
    private Transform AttackOrigin => attackOrigin != null ? attackOrigin : RangeOrigin;

    private void Awake()
    {
        if (bodyFSM == null) bodyFSM = GetComponentInParent<BossFSM>();
    }

    private void OnEnable()
    {
        activeAttack = null;
        acquiredCoordinator = null;
        ownsAttack = false;
        reportedSharedPattern = false;
        reportedDashPattern = false;
        reportedMissingCoordinator = false;
        ChangeState(State.Idle);
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;
        if (!allowNewAttacks && currentState != State.Attack && currentState != State.Recovery)
        {
            activeAttack = null;
            if (currentState != State.WaitForPermission) ChangeState(State.WaitForPermission);
            return;
        }

        switch (currentState)
        {
            case State.Idle:
                if (stateTimer >= searchInterval) ChangeState(State.SearchPlayer);
                break;
            case State.SearchPlayer:
                SearchPlayer();
                break;
            case State.SelectAttack:
                SelectAttack();
                break;
            case State.Aim:
                AimAtPlayer();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Recovery:
                if (stateTimer >= recoveryDuration)
                    ChangeState(allowNewAttacks ? State.Idle : State.WaitForPermission);
                break;
            case State.WaitForPermission:
                if (allowNewAttacks) ChangeState(State.Idle);
                break;
        }
    }

    private void SearchPlayer()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            player = found != null ? found.transform : null;
        }
        ChangeState(IsPlayerInRange() ? State.SelectAttack : State.Idle);
    }

    private bool IsPlayerInRange()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector3 offset = player.position - RangeOrigin.position;
        if (Mathf.Abs(offset.y) > heightTolerance) return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= detectRange * detectRange;
    }

    private float PlayerDistance()
    {
        Vector3 offset = player.position - RangeOrigin.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private bool CanSelect(BossAttack pattern, float distance)
    {
        if (pattern == null) return false;
        // 기존 거북이 돌진은 본체 CharacterController용이므로 뱀 목록에서 제외한다.
        if (pattern is BossDashAttack)
        {
            if (!reportedDashPattern)
            {
                reportedDashPattern = true;
                Debug.LogWarning("SnakeFSM: BossDashAttack은 거북이 본체용입니다. 뱀 Patterns에서 제거하세요.", this);
            }
            return false;
        }
        if (bodyFSM != null && bodyFSM.patterns != null &&
            System.Array.IndexOf(bodyFSM.patterns, pattern) >= 0)
        {
            if (!reportedSharedPattern)
            {
                reportedSharedPattern = true;
                Debug.LogWarning("SnakeFSM: 같은 공격이 거북이 Patterns에도 등록되어 있습니다. 브레스를 거북이 목록에서 제거하세요.", this);
            }
            return false;
        }
        return !pattern.IsRunning && pattern.CanUse(distance, player);
    }

    private void SelectAttack()
    {
        activeAttack = null;
        if (!IsPlayerInRange()) { ChangeState(State.Idle); return; }
        float distance = PlayerDistance();
        if (patterns != null)
        {
            foreach (BossAttack pattern in patterns)
            {
                if (!CanSelect(pattern, distance)) continue;
                activeAttack = pattern;
                break;
            }
        }
        if (activeAttack == null) { ChangeState(State.Idle); return; }
        EnsureAttackPivot();
        attackPivot.position = AttackOrigin.position;
        ChangeState(State.Aim);
    }

    private void EnsureAttackPivot()
    {
        if (attackPivot != null) return;
        // 부모를 두지 않아 몸체의 이동/회전에 따라 공격 기준점이 바뀌지 않는다.
        GameObject pivotObject = new GameObject(name + "_SnakeAttackPivot");
        attackPivot = pivotObject.transform;
        attackPivot.SetPositionAndRotation(AttackOrigin.position,
            Quaternion.Euler(0f, RangeOrigin.eulerAngles.y, 0f));
    }

    private void AimAtPlayer()
    {
        if (!IsPlayerInRange() || activeAttack == null || !CanSelect(activeAttack, PlayerDistance()))
        {
            activeAttack = null;
            ChangeState(State.Idle);
            return;
        }
        EnsureAttackPivot();
        attackPivot.position = AttackOrigin.position;
        Vector3 direction = player.position - attackPivot.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = attackPivot.forward;
        Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
        attackPivot.rotation = Quaternion.RotateTowards(attackPivot.rotation,
            rotation, turnSpeed * Time.deltaTime);
        if (Quaternion.Angle(attackPivot.rotation, rotation) > 1f) return;
        if (!allowNewAttacks)
        {
            activeAttack = null;
            ChangeState(State.WaitForPermission);
            return;
        }
        attackPivot.rotation = rotation;
        // 다른 공격/공통 간격이 끝날 때까지 Aim에서 위치·거리·방향을 계속 확인합니다.
        if (!TryReserveAttack()) return;
        BossAttack attack = activeAttack;
        Transform victim = player;
        ownsAttack = true;
        ChangeState(State.Attack);
        try
        {
            if (attack.BeginAttack(attackPivot, victim, heightTolerance,
                amount => DamagePlayer(victim, amount, attack.IsDirectKnockdownHit))) return;
            Debug.LogWarning($"SnakeFSM: {attack.GetType().Name} 시작 실패. 바로 앞에 출력된 해당 공격의 경고를 확인하세요.", attack);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
        if (!isActiveAndEnabled || activeAttack != attack || !ownsAttack) return;
        CancelOwnedAttack(false);
        recoveryDuration = Mathf.Max(1f, minimumAttackGap);
        ChangeState(State.Recovery);
    }

    private bool TryReserveAttack()
    {
        if (attackCoordinator == null)
            attackCoordinator = GetComponentInParent<BossAttackCoordinator>();
        if (attackCoordinator == null && bodyFSM != null)
            attackCoordinator = bodyFSM.attackCoordinator != null ? bodyFSM.attackCoordinator :
                bodyFSM.GetComponentInParent<BossAttackCoordinator>();
        if (attackCoordinator == null)
        {
            if (!reportedMissingCoordinator)
            {
                reportedMissingCoordinator = true;
                Debug.LogWarning("SnakeFSM: 공통 몸체에 BossAttackCoordinator를 추가하고 두 FSM에 같은 조율자를 연결하세요.", this);
            }
            return false;
        }
        if (!attackCoordinator.TryAcquire(this, activeAttack)) return false;
        acquiredCoordinator = attackCoordinator;
        return true;
    }

    private void UpdateAttack()
    {
        BossAttack attack = activeAttack;
        if (attack != null)
        {
            attack.TickAttack(Time.deltaTime);
            // 피해 처리 도중 비활성화/중단되었다면 상태를 다시 덮어쓰지 않는다.
            if (!isActiveAndEnabled || currentState != State.Attack || activeAttack != attack) return;
            if (attack != null && attack.IsRunning) return;
        }
        recoveryDuration = Mathf.Max(minimumAttackGap,
            attack != null ? Mathf.Max(0f, attack.recoveryTime) : 0f);
        ownsAttack = false;
        activeAttack = null;
        ReleaseReservation();
        ChangeState(State.Recovery);
    }

    private void DamagePlayer(Transform victim, float amount, bool knockdown = false)
    {
        if (victim == null || !victim.gameObject.activeInHierarchy) return;
        bool handled = false;

        // 거북이와 동일하게 테스트 더미의 피격 순간이동을 호출합니다.
        PlayerHitTeleport dummy = victim.GetComponentInParent<PlayerHitTeleport>();
        if (dummy != null && dummy.isActiveAndEnabled)
        {
            dummy.TakeDamage(amount);
            handled = true;
        }

        if (victim == null || !victim.gameObject.activeInHierarchy) return;
        PlayerHealth health = victim.GetComponentInParent<PlayerHealth>();
        if (health != null)
        {
            health.TakeDamage(amount);
            handled = true;
        }

        // 병합 연결 (NGH): 실제 플레이어는 PlayerController.TakeHit 으로 피격 (무적·피격 동작·HP 처리)
        NGH.PlayerController ngh = victim.GetComponentInParent<NGH.PlayerController>();
        if (ngh != null)
        {
            ngh.TakeHit(Mathf.Max(1, Mathf.RoundToInt(amount)), transform.position, knockdown ? HitStagger.Heavy : HitStagger.Light);
            handled = true;
        }

        // Inspector 이벤트에도 같은 더미가 연결되어 있어도 두 번 호출하지 않습니다.
        if (!handled) onPlayerHit?.Invoke(amount);
    }

    private void ChangeState(State next)
    {
        currentState = next;
        stateTimer = 0f;
    }

    // 뱀만 별도로 일시 정지할 때 사용합니다. 진행 중인 공격은 취소하지 않습니다.
    public void SetAttackAllowed(bool allowed)
    {
        allowNewAttacks = allowed;
    }

    public bool ContainsPattern(BossAttack pattern)
    {
        return pattern != null && patterns != null && System.Array.IndexOf(patterns, pattern) >= 0;
    }

    // 사망/전투 중단용. 재개는 SetAttackAllowed(true).
    public void StopCombat()
    {
        allowNewAttacks = false;
        CancelOwnedAttack();
        ChangeState(State.WaitForPermission);
    }

    private void ReleaseReservation(bool applyGap = true)
    {
        BossAttackCoordinator coordinator = acquiredCoordinator;
        acquiredCoordinator = null;
        if (coordinator != null) coordinator.Release(this, applyGap);
    }

    private void CancelOwnedAttack(bool applyGap = true)
    {
        BossAttack attack = activeAttack;
        bool shouldCancel = ownsAttack;
        activeAttack = null;
        ownsAttack = false;
        try
        {
            if (shouldCancel && attack != null) attack.CancelAttack();
        }
        finally
        {
            ReleaseReservation(applyGap);
        }
    }

    private void OnDisable()
    {
        CancelOwnedAttack();
        ChangeState(State.Idle);
    }

    private void OnDestroy()
    {
        if (attackPivot != null) Destroy(attackPivot.gameObject);
    }

    private void OnValidate()
    {
        detectRange = Mathf.Max(0.1f, detectRange);
        searchInterval = Mathf.Max(0.05f, searchInterval);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        heightTolerance = Mathf.Max(0f, heightTolerance);
        minimumAttackGap = Mathf.Max(0f, minimumAttackGap);
    }
}
