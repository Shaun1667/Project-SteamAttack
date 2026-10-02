using UnityEngine;

[DisallowMultipleComponent]
public class BossFSM : MonoBehaviour
{
    public enum State
    {
        Idle, SearchPlayer, LookAtPlayer, SelectAttack, Attack, Recovery
    }

    [Header("현재 상태 - 확인용")]
    [SerializeField] private State currentState;
    [SerializeField] private Transform player;

    [Header("플레이어 탐색 / 회전")]
    [Min(0.1f)] public float detectRange = 8f;
    [Min(0.05f)] public float searchInterval = 0.25f;
    [Min(1f)] public float turnSpeed = 180f;
    [Min(0f)] public float heightTolerance = 2f;

    [Header("사용할 패턴 목록 - 지금은 브레스 한 개만 연결")]
    [Tooltip("위에서부터 확인해서 현재 거리에서 사용할 수 있는 첫 패턴을 실행합니다.")]
    public BossAttack[] patterns = new BossAttack[0];

    private BossAttack activeAttack;
    private float stateTimer;
    private float recoveryDuration;

    private void OnEnable()
    {
        player = null;
        activeAttack = null;
        ChangeState(State.Idle);
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;
        switch (currentState)
        {
            case State.Idle:
                if (stateTimer >= searchInterval) ChangeState(State.SearchPlayer);
                break;
            case State.SearchPlayer:
                SearchPlayer();
                break;
            case State.LookAtPlayer:
                LookAtPlayer();
                break;
            case State.SelectAttack:
                SelectAttack();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Recovery:
                if (stateTimer >= recoveryDuration) ChangeState(State.SearchPlayer);
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
        ChangeState(IsPlayerInRange() ? State.LookAtPlayer : State.Idle);
    }

    private bool IsPlayerInRange()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector3 offset = player.position - transform.position;
        if (Mathf.Abs(offset.y) > heightTolerance) return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= detectRange * detectRange;
    }

    private void LookAtPlayer()
    {
        if (!IsPlayerInRange())
        {
            ChangeState(State.Idle);
            return;
        }

        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, targetRotation) <= 1f)
        {
            transform.rotation = targetRotation;
            // 회전이 완료된 순간의 거리로 선택한다.
            ChangeState(State.SelectAttack);
            SelectAttack();
        }
    }

    private void SelectAttack()
    {
        if (!IsPlayerInRange())
        {
            ChangeState(State.Idle);
            return;
        }

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        activeAttack = null;

        // 특정 공격 이름을 알 필요 없이 목록에 등록된 패턴을 확인한다.
        if (patterns != null)
        {
            foreach (BossAttack pattern in patterns)
            {
                if (pattern == null || !pattern.CanUse(distance)) continue;
                activeAttack = pattern;
                break;
            }
        }

        // 현재 거리에서 사용할 패턴이 없으면 대기하며 다시 탐색한다.
        if (activeAttack == null)
        {
            ChangeState(State.Idle);
            return;
        }

        if (!activeAttack.BeginAttack(
            transform, player, heightTolerance, DamagePlayer))
        {
            Debug.LogWarning("선택한 공격을 시작하지 못했습니다: " + activeAttack.GetType().Name +
                ". 공격 컴포넌트와 설정을 확인하세요.", this);
            activeAttack = null;
            recoveryDuration = 1f;
            ChangeState(State.Recovery);
            return;
        }
        ChangeState(State.Attack);
    }

    // 공격이 명중하면 플레이어의 피격 함수를 직접 호출한다.
    private void DamagePlayer(float damage)
    {
        if (player == null) return;
        /*테스트용*/
        PlayerHitTeleport receiverA = player.GetComponentInParent<PlayerHitTeleport>();
        receiverA.TakeDamage(damage);

        PlayerHealth receiver = player.GetComponentInParent<PlayerHealth>();

        receiver.TakeDamage(damage);
    }

    private void UpdateAttack()
    {
        if (activeAttack != null)
        {
            activeAttack.TickAttack(Time.deltaTime);
            if (!isActiveAndEnabled) return;
            if (activeAttack.IsRunning) return;
            recoveryDuration = Mathf.Max(0f, activeAttack.recoveryTime);
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

    private void OnDisable()
    {
        if (activeAttack != null) activeAttack.CancelAttack();
        activeAttack = null;
    }

    private void OnValidate()
    {
        detectRange = Mathf.Max(0.1f, detectRange);
        searchInterval = Mathf.Max(0.05f, searchInterval);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        heightTolerance = Mathf.Max(0f, heightTolerance);
    }
}
