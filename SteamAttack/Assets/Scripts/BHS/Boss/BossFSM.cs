using UnityEngine;

[DisallowMultipleComponent]
public class BossFSM : MonoBehaviour
{
    public enum State
    {
        Idle, SearchPlayer, LookAtPlayer, SelectAttack, Attack, Recovery,
        WaitForSnake
    }

    [Header("현재 상태 - 확인용")]
    [SerializeField] private State currentState;
    [SerializeField] private Transform player;
    [SerializeField] private bool dashReserved;
    [SerializeField] private bool combatStopped;

    [Header("거북이 거리 기준 / 뱀 FSM")]
    [Tooltip("몸통 중앙의 빈 오브젝트. 비우면 이 오브젝트 위치를 사용합니다.")]
    public Transform rangeOrigin;
    public SnakeFSM snakeFSM;

    [Header("플레이어 탐색 / 회전")]
    [Min(0.1f)] public float detectRange = 8f;
    [Min(0.05f)] public float searchInterval = 0.25f;
    [Min(1f)] public float turnSpeed = 180f;
    [Min(0f)] public float heightTolerance = 2f;

    [Header("거북이 패턴만 등록 - 뱀 브레스는 SnakeFSM에 등록")]
    [Tooltip("위에서부터 현재 거리에서 사용할 수 있는 첫 패턴을 선택합니다.")]
    public BossAttack[] patterns = new BossAttack[0];

    public Transform Player => player;
    public bool IsCombatActive => isActiveAndEnabled && !combatStopped;
    public bool IsSnakeAttackBlocked => !IsCombatActive || dashReserved;

    private BossAttack activeAttack;
    private float stateTimer;
    private float recoveryDuration;
    private float playerSearchTimer;
    private Vector3 RangePosition => rangeOrigin != null ? rangeOrigin.position : transform.position;

    private void Awake()
    {
        if (snakeFSM == null) snakeFSM = GetComponentInChildren<SnakeFSM>(true);
    }

    private void OnEnable()
    {
        activeAttack = null;
        dashReserved = false;
        combatStopped = false;
        playerSearchTimer = 0f;
        ChangeState(State.Idle);
    }

    private void Update()
    {
        if (!IsCombatActive) return;
        RefreshPlayer();
        stateTimer += Time.deltaTime;

        switch (currentState)
        {
            case State.Idle:
                if (stateTimer >= searchInterval) ChangeState(State.SearchPlayer);
                break;
            case State.SearchPlayer:
                ChangeState(IsPlayerInRange() ? State.LookAtPlayer : State.Idle);
                break;
            case State.LookAtPlayer:
                if (!IsPlayerInRange()) ChangeState(State.Idle);
                else if (TurnTowardsPlayer()) SelectAttack();
                break;
            case State.SelectAttack:
                SelectAttack();
                break;
            case State.WaitForSnake:
                UpdateWaitForSnake();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Recovery:
                if (stateTimer >= recoveryDuration) ChangeState(State.SearchPlayer);
                break;
        }
    }

    // 거북이 감지 범위 밖에 있어도 뱀은 같은 플레이어를 자신의 범위로 판단합니다.
    private void RefreshPlayer()
    {
        if (player != null && player.gameObject.activeInHierarchy) return;
        player = null;
        playerSearchTimer -= Time.deltaTime;
        if (playerSearchTimer > 0f) return;
        playerSearchTimer = searchInterval;
        GameObject found = GameObject.FindGameObjectWithTag("Player");
        player = found != null ? found.transform : null;
    }

    private bool IsPlayerInRange()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector3 offset = player.position - RangePosition;
        if (Mathf.Abs(offset.y) > heightTolerance) return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= detectRange * detectRange;
    }

    private float PlayerDistance()
    {
        Vector3 offset = player.position - RangePosition;
        offset.y = 0f;
        return offset.magnitude;
    }

    private bool TurnTowardsPlayer()
    {
        Vector3 direction = player.position - RangePosition;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, targetRotation) > 1f) return false;
        transform.rotation = targetRotation;
        return true;
    }

    private void SelectAttack()
    {
        if (!IsPlayerInRange())
        {
            ChangeState(State.Idle);
            return;
        }

        activeAttack = null;
        float distance = PlayerDistance();
        if (patterns != null)
        {
            foreach (BossAttack pattern in patterns)
            {
                if (pattern == null || !pattern.CanUse(distance)) continue;
                // 같은 공격 컴포넌트를 두 FSM에서 실행하지 않습니다.
                if (snakeFSM != null && snakeFSM.ContainsPattern(pattern)) continue;
                activeAttack = pattern;
                break;
            }
        }

        if (activeAttack == null)
        {
            ChangeState(State.Idle);
            return;
        }

        if (activeAttack is BossDashAttack)
        {
            // 선택 즉시 뱀의 새 공격만 막습니다. 진행 중인 공격은 유지합니다.
            dashReserved = true;
            ChangeState(State.WaitForSnake);
            UpdateWaitForSnake();
            return;
        }
        BeginSelectedAttack();
    }

    private void UpdateWaitForSnake()
    {
        // 대기 중 플레이어가 사라지거나 사거리 밖으로 나가면 예약을 해제합니다.
        if (activeAttack == null || !IsPlayerInRange() || !activeAttack.CanUse(PlayerDistance()))
        {
            activeAttack = null;
            dashReserved = false;
            ChangeState(State.Idle);
            return;
        }
        if (snakeFSM != null && snakeFSM.IsBusy) return;

        // 뱀 복귀 후 방향을 다시 맞춥니다. Ready Time은 BeginAttack부터 시작합니다.
        if (TurnTowardsPlayer()) BeginSelectedAttack();
    }

    private void BeginSelectedAttack()
    {
        Transform victim = player;
        if (!activeAttack.BeginAttack(transform, victim, heightTolerance,
            amount => DamagePlayer(victim, amount)))
        {
            Debug.LogWarning("선택한 공격을 시작하지 못했습니다: " + activeAttack.GetType().Name, this);
            activeAttack = null;
            dashReserved = false;
            recoveryDuration = 1f;
            ChangeState(State.Recovery);
            return;
        }
        ChangeState(State.Attack);
    }

    // 두 FSM의 명중을 이 함수 하나로 전달합니다. 실제 플레이어를 우선 사용합니다.
    public void DamagePlayer(Transform victim, float damage)
    {
        if (!IsCombatActive || victim == null || !victim.gameObject.activeInHierarchy) return;
        PlayerHealth receiver = victim.GetComponentInParent<PlayerHealth>();
        if (receiver != null)
        {
            receiver.TakeDamage(damage);
            return;
        }
        PlayerHitTeleport testReceiver = victim.GetComponentInParent<PlayerHitTeleport>();
        if (testReceiver != null) testReceiver.TakeDamage(damage);
        else Debug.LogWarning("PlayerHealth 또는 테스트용 PlayerHitTeleport가 없습니다.", victim);
    }

    private void UpdateAttack()
    {
        BossAttack attack = activeAttack;
        if (attack != null)
        {
            attack.TickAttack(Time.deltaTime);
            if (!IsCombatActive || activeAttack != attack) return;
            if (attack != null && attack.IsRunning) return;
            recoveryDuration = attack != null ? Mathf.Max(0f, attack.recoveryTime) : 0f;
        }
        else recoveryDuration = 0f;

        activeAttack = null;
        // 돌진 이동 종료 후 뱀은 재개. 거북이 자신의 회복은 별도로 진행합니다.
        dashReserved = false;
        ChangeState(State.Recovery);
    }

    private void ChangeState(State next)
    {
        currentState = next;
        stateTimer = 0f;
    }

    // 보스 체력 스크립트의 사망 처리에서 호출하세요. 체력 계산은 기존 코드를 사용합니다.
    public void StopCombat()
    {
        combatStopped = true;
        CancelCombat();
    }

    private void CancelCombat()
    {
        if (activeAttack != null) activeAttack.CancelAttack();
        activeAttack = null;
        dashReserved = false;
        if (snakeFSM != null) snakeFSM.StopFromBody();
        ChangeState(State.Idle);
    }

    private void OnDisable() => CancelCombat();

    private void OnValidate()
    {
        detectRange = Mathf.Max(0.1f, detectRange);
        searchInterval = Mathf.Max(0.05f, searchInterval);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        heightTolerance = Mathf.Max(0f, heightTolerance);
    }
}
