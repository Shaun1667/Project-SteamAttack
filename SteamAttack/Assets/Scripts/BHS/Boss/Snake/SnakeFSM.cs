using UnityEngine;

[DisallowMultipleComponent]
public class SnakeFSM : MonoBehaviour
{
    public enum State { Idle, SelectAttack, LookAtPlayer, Attack, Recovery, WaitForTurtle }

    [Header("현재 상태 - 확인용")]
    [SerializeField] private State currentState;

    [Header("공통 본체 / 뱀 기준점")]
    public BossFSM bodyFSM;
    [Tooltip("뱀 뿌리 위치. 패턴 선택 거리와 탐색 범위의 기준입니다.")]
    public Transform rangeOrigin;
    [Tooltip("뱀 공격 방향만 제어할 별도의 빈 오브젝트. 본체·본·IK 타겟을 연결하지 마세요.")]
    public Transform attackPivot;

    [Header("플레이어 탐색 / 공격 방향 회전")]
    [Min(0.1f)] public float detectRange = 8f;
    [Min(0.05f)] public float searchInterval = 0.25f;
    [Min(1f)] public float turnSpeed = 360f;
    [Min(0f)] public float heightTolerance = 2f;

    [Header("뱀 패턴만 등록 - 지금은 BossBreathAttack")]
    [Tooltip("위에서부터 현재 거리에서 사용할 수 있는 첫 패턴을 선택합니다.")]
    public BossAttack[] patterns = new BossAttack[0];

    // 공격 준비/실행뿐 아니라 회복(추후 물기의 복귀 포함)까지 끝나야 돌진 가능.
    public bool IsBusy => isActiveAndEnabled &&
        (currentState == State.Attack || currentState == State.Recovery);

    private BossAttack activeAttack;
    private float stateTimer;
    private float recoveryDuration;
    private bool reportedSetupError;
    private Transform Player => bodyFSM != null ? bodyFSM.Player : null;

    private void Awake()
    {
        if (bodyFSM == null) bodyFSM = GetComponentInParent<BossFSM>();
    }

    private void OnEnable()
    {
        activeAttack = null;
        reportedSetupError = false;
        ChangeState(State.Idle);
    }

    private bool HasValidSetup()
    {
        bool valid = bodyFSM != null && bodyFSM.snakeFSM == this &&
            rangeOrigin != null && attackPivot != null &&
            attackPivot != rangeOrigin && attackPivot != bodyFSM.transform &&
            !bodyFSM.transform.IsChildOf(attackPivot) &&
            !rangeOrigin.IsChildOf(attackPivot);
        if (!valid && !reportedSetupError)
        {
            reportedSetupError = true;
            Debug.LogError("SnakeFSM: Body FSM과 거북이의 Snake FSM을 서로 연결하고, Range Origin(뿌리)과 별도 Attack Pivot을 연결하세요. Attack Pivot 아래에 본체나 뿌리가 있으면 안 됩니다.", this);
        }
        return valid;
    }

    private void Update()
    {
        if (!HasValidSetup() || !bodyFSM.IsCombatActive)
        {
            StopFromBody();
            return;
        }
        stateTimer += Time.deltaTime;

        // 진행 중인 공격·회복은 계속하고, 시작 전인 공격만 대기시킵니다.
        if (bodyFSM.IsSnakeAttackBlocked && !IsBusy)
        {
            activeAttack = null;
            if (currentState != State.WaitForTurtle) ChangeState(State.WaitForTurtle);
            return;
        }

        switch (currentState)
        {
            case State.Idle:
                if (stateTimer >= searchInterval) ChangeState(State.SelectAttack);
                break;
            case State.SelectAttack:
                SelectAttack();
                break;
            case State.LookAtPlayer:
                LookAtPlayer();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Recovery:
                if (stateTimer >= recoveryDuration)
                    ChangeState(bodyFSM.IsSnakeAttackBlocked ? State.WaitForTurtle : State.Idle);
                break;
            case State.WaitForTurtle:
                ChangeState(State.Idle);
                break;
        }
    }

    public bool ContainsPattern(BossAttack pattern)
    {
        if (patterns == null) return false;
        foreach (BossAttack value in patterns)
            if (value == pattern) return true;
        return false;
    }

    private bool IsPlayerInRange()
    {
        Transform player = Player;
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector3 offset = player.position - rangeOrigin.position;
        if (Mathf.Abs(offset.y) > heightTolerance) return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= detectRange * detectRange;
    }

    private float PlayerDistance()
    {
        Vector3 offset = Player.position - rangeOrigin.position;
        offset.y = 0f;
        return offset.magnitude;
    }

    private void SelectAttack()
    {
        activeAttack = null;
        if (!IsPlayerInRange())
        {
            ChangeState(State.Idle);
            return;
        }
        float distance = PlayerDistance();
        if (patterns != null)
        {
            foreach (BossAttack pattern in patterns)
            {
                // 본체를 움직이는 거북이 돌진은 뱀 패턴으로 실행하지 않습니다.
                if (pattern == null || pattern is BossDashAttack || !pattern.CanUse(distance)) continue;
                activeAttack = pattern;
                break;
            }
        }
        ChangeState(activeAttack != null ? State.LookAtPlayer : State.Idle);
    }

    private void LookAtPlayer()
    {
        if (!IsPlayerInRange() || activeAttack == null || !activeAttack.CanUse(PlayerDistance()))
        {
            activeAttack = null;
            ChangeState(State.Idle);
            return;
        }

        // 사거리는 뿌리에서, 조준 방향은 실제 발생 위치(브레스는 입)에서 계산합니다.
        Vector3 direction = Player.position - activeAttack.GetOriginPosition(attackPivot);
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.ProjectOnPlane(attackPivot.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        attackPivot.rotation = Quaternion.RotateTowards(
            attackPivot.rotation, targetRotation, turnSpeed * Time.deltaTime);
        if (Quaternion.Angle(attackPivot.rotation, targetRotation) > 1f) return;
        attackPivot.rotation = targetRotation;

        // 시작 직전에도 돌진 예약을 확인합니다. 어느 FSM이 먼저 갱신돼도 겹치지 않습니다.
        if (bodyFSM.IsSnakeAttackBlocked)
        {
            activeAttack = null;
            ChangeState(State.WaitForTurtle);
            return;
        }

        Transform victim = Player;
        if (!activeAttack.BeginAttack(attackPivot, victim, heightTolerance,
            amount => bodyFSM.DamagePlayer(victim, amount)))
        {
            Debug.LogWarning("뱀 공격을 시작하지 못했습니다. 브레스 머티리얼과 플레이어 Collider를 확인하세요.", this);
            activeAttack = null;
            recoveryDuration = 1f;
            ChangeState(State.Recovery);
            return;
        }
        ChangeState(State.Attack);
    }

    private void UpdateAttack()
    {
        BossAttack attack = activeAttack;
        if (attack != null)
        {
            attack.TickAttack(Time.deltaTime);
            if (!isActiveAndEnabled || !bodyFSM.IsCombatActive || activeAttack != attack) return;
            if (attack != null && attack.IsRunning) return;
            recoveryDuration = attack != null ? Mathf.Max(0f, attack.recoveryTime) : 0f;
        }
        else recoveryDuration = 0f;
        activeAttack = null;
        ChangeState(State.Recovery);
    }

    private void ChangeState(State next)
    {
        currentState = next;
        stateTimer = 0f;
    }

    // 사망/비활성화 전용. 돌진 예약에서는 이 함수를 호출하지 않습니다.
    public void StopFromBody()
    {
        if (activeAttack != null) activeAttack.CancelAttack();
        activeAttack = null;
        ChangeState(State.Idle);
    }

    private void OnDisable() => StopFromBody();

    private void OnValidate()
    {
        detectRange = Mathf.Max(0.1f, detectRange);
        searchInterval = Mathf.Max(0.05f, searchInterval);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        heightTolerance = Mathf.Max(0f, heightTolerance);
    }
}
