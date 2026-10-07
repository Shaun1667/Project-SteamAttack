using System.Collections;
using UnityEngine;

/// <summary>
/// 적 기본 AI (NGH)
/// - 배회(Wander): 스폰 지점 주변을 랜덤하게 돌아다닌다.
/// - 인지(Detect): 플레이어가 인지 범위 안에 들어오면 인지한다. (Gizmo로 범위 표시)
/// - 추적(Chase): 인지한 플레이어에게 다가간다.
/// - 공격(Attack): 공격 사거리 안이면 멈춰서 근접 공격.
///   공격 순간 EnemyAttack 히트박스를 오브젝트 풀에서 꺼내 판정하고, 동시에 앞으로 살짝 전진한다.
///   전진 거리는 공격 순간에 확정되고(플레이어 표면 앞까지로 제한), 이후 플레이어가 움직여도 늘어나지 않는다.
///   전진 중 플레이어가 다가와 막히면 그 자리에서 전진을 끝낸다.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CHG_EnemyAI : MonoBehaviour
{
    public enum State
    {
        Wander,
        Chase,
        Attack
    }

    [Header("Target")]
    [Tooltip("인지 대상 태그")]
    [SerializeField] private string playerTag = "Player";

    [Header("Wander (배회)")]
    [Tooltip("스폰 지점 기준 배회 반경")]
    [SerializeField] private float wanderRadius = 6f;
    [SerializeField] private float wanderSpeed = 1.5f;
    [Tooltip("목적지 도착 후 대기 시간 (최소)")]
    [SerializeField] private float wanderWaitMin = 1f;
    [Tooltip("목적지 도착 후 대기 시간 (최대)")]
    [SerializeField] private float wanderWaitMax = 2.5f;

    [Header("Detection (인지)")]
    [Tooltip("이 거리 안에 플레이어가 들어오면 인지")]
    [SerializeField] private float detectRange = 8f;
    [Tooltip("인지 후 이 거리보다 멀어지면 놓침 (인지 범위 이상이어야 함)")]
    [SerializeField] private float loseRange = 12f;

    [Header("Chase (추적)")]
    [SerializeField] private float chaseSpeed = 3.5f;
    [Tooltip("초당 회전 각도")]
    [SerializeField] private float turnSpeed = 540f;

    [Header("Attack (근접 공격)")]
    [Tooltip("이 거리 안이면 공격 시작")]
    [SerializeField] private float attackRange = 1.8f;
    [Tooltip("공격 선딜레이 (판정 생성 전 대기)")]
    [SerializeField] private float attackWindup = 0.4f;
    [Tooltip("히트박스가 유지되는 시간")]
    [SerializeField] private float attackActiveTime = 0.15f;
    [Tooltip("공격 후딜레이")]
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private int attackDamage = 1;
    [Tooltip("공격 판정용 히트박스 프리팹 (오브젝트 풀로 재사용)")]
    [SerializeField] private CHG_EnemyAttackHitbox attackHitboxPrefab;
    [Tooltip("적 기준 히트박스 생성 위치 (로컬)")]
    [SerializeField] private Vector3 attackHitboxOffset = new Vector3(0f, 0f, 1.1f);
    [SerializeField] private Vector3 attackHitboxSize = new Vector3(1.2f, 1.5f, 1.2f);
    [Tooltip("시작할 때 미리 만들어 둘 히트박스 개수")]
    [SerializeField] private int attackHitboxPrewarm = 2;

    [Header("Attack Lunge (공격 시 전진)")]
    [Tooltip("공격하는 순간 앞으로 전진하는 거리 (0이면 전진하지 않음)")]
    [Min(0f)]
    [SerializeField] private float attackLungeDistance = 1f;
    [Tooltip("전진에 걸리는 시간 (짧을수록 빠르게 튀어나감)")]
    [Min(0.01f)]
    [SerializeField] private float attackLungeDuration = 0.12f;
    [Tooltip("전진 중 플레이어 표면과 유지할 최소 간격 (플레이어를 뚫지 않도록)")]
    [Min(0f)]
    [SerializeField] private float lungeStopGap = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = true;

    public State CurrentState => state;
    public bool IsAwareOfPlayer => isAware;

    private Rigidbody rb;
    private Collider bodyCollider;
    private Transform player;
    private Collider playerCollider;
    private State state = State.Wander;
    private bool isAware;

    private Vector3 homePosition;
    private bool hasHome;
    private Vector3 wanderTarget;
    private bool hasWanderTarget;
    private float wanderWaitTimer;
    private float wanderMoveTimer;
    private float wanderGiveUpTime;

    private Coroutine attackRoutine;
    private Vector3 moveDirection;
    private float moveSpeed;
    private Vector3 facingDirection;

    private bool isLunging;
    private Vector3 lungeDirection;
    private float lungeRemaining;
    private float lungeSpeed;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        bodyCollider = GetComponent<Collider>();

        homePosition = transform.position;
        hasHome = true;
        facingDirection = transform.forward;
    }

    private void Start()
    {
        FindPlayer();
        wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);

        if (attackHitboxPrefab != null)
        {
            CHG_ObjectPool.Prewarm(attackHitboxPrefab.gameObject, attackHitboxPrewarm);
        }
    }

    private void OnDisable()
    {
        isLunging = false;
    }

    private void OnValidate()
    {
        wanderRadius = Mathf.Max(0f, wanderRadius);
        wanderWaitMax = Mathf.Max(wanderWaitMin, wanderWaitMax);
        detectRange = Mathf.Max(0.1f, detectRange);
        loseRange = Mathf.Max(detectRange, loseRange);
        attackRange = Mathf.Clamp(attackRange, 0.1f, detectRange);
        attackHitboxPrewarm = Mathf.Max(0, attackHitboxPrewarm);
    }

    private void Update()
    {
        if (player == null)
        {
            FindPlayer();
        }

        UpdateAwareness();

        moveDirection = Vector3.zero;
        moveSpeed = 0f;

        switch (state)
        {
            case State.Wander:
                TickWander();
                break;
            case State.Chase:
                TickChase();
                break;
            case State.Attack:
                // 공격 중 이동은 AttackRoutine + 전진(Lunge)이 담당
                break;
        }
    }

    private void FixedUpdate()
    {
        Vector3 velocity = isLunging ? GetLungeVelocity() : moveDirection * moveSpeed;
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

        if (facingDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
            rb.MoveRotation(Quaternion.RotateTowards(rb.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime));
        }
    }

    #region Death (CHG_EnemyHealth 연동)

    /// <summary>쓰러졌을 때 CHG_EnemyHealth가 호출한다. 공격·전진·이동을 즉시 멈추고 AI를 끈다.</summary>
    public void StopForDeath()
    {
        StopAllCoroutines();
        attackRoutine = null;
        isLunging = false;
        lungeRemaining = 0f;
        isAware = false;
        moveDirection = Vector3.zero;
        moveSpeed = 0f;
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
        }
        SetState(State.Wander);
        enabled = false;
    }

    /// <summary>부활할 때 CHG_EnemyHealth가 호출한다. 스폰 지점으로 돌아가 배회부터 다시 시작한다.</summary>
    public void ReviveAtHome()
    {
        if (hasHome)
        {
            transform.position = homePosition;
            if (rb != null)
            {
                rb.position = homePosition;
            }
        }
        isAware = false;
        hasWanderTarget = false;
        SetState(State.Wander);
        wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
        enabled = true;
    }

    #endregion

    #region Awareness

    private void FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        player = playerObject != null ? playerObject.transform : null;

        playerCollider = null;
        if (player != null)
        {
            foreach (Collider col in player.GetComponentsInChildren<Collider>())
            {
                if (col.enabled && !col.isTrigger)
                {
                    playerCollider = col;
                    break;
                }
            }
        }
    }

    private void UpdateAwareness()
    {
        if (player == null)
        {
            if (isAware)
            {
                LosePlayer();
            }
            return;
        }

        float distance = FlatDistance(transform.position, player.position);

        if (!isAware && distance <= detectRange)
        {
            isAware = true;
            Log("플레이어 인지");
            if (state == State.Wander)
            {
                SetState(State.Chase);
            }
        }
        else if (isAware && distance > loseRange)
        {
            LosePlayer();
        }
    }

    private void LosePlayer()
    {
        isAware = false;
        Log("플레이어 놓침");

        // 공격 중이면 공격이 끝난 뒤 AttackRoutine에서 배회로 돌아간다
        if (state != State.Attack)
        {
            SetState(State.Wander);
        }
    }

    #endregion

    #region Wander

    private void TickWander()
    {
        if (!hasWanderTarget)
        {
            wanderWaitTimer -= Time.deltaTime;
            if (wanderWaitTimer > 0f)
            {
                return;
            }
            PickWanderTarget();
        }

        Vector3 toTarget = Flat(wanderTarget - transform.position);
        wanderMoveTimer += Time.deltaTime;

        if (toTarget.magnitude <= 0.3f || wanderMoveTimer >= wanderGiveUpTime)
        {
            hasWanderTarget = false;
            wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
            return;
        }

        moveDirection = toTarget.normalized;
        moveSpeed = wanderSpeed;
        facingDirection = moveDirection;
    }

    private void PickWanderTarget()
    {
        Vector2 offset = Random.insideUnitCircle * wanderRadius;
        wanderTarget = homePosition + new Vector3(offset.x, 0f, offset.y);
        hasWanderTarget = true;
        wanderMoveTimer = 0f;

        // 막혀서 도착하지 못하는 경우를 대비해 예상 이동 시간의 1.5배가 지나면 포기
        float distance = Flat(wanderTarget - transform.position).magnitude;
        wanderGiveUpTime = distance / Mathf.Max(0.01f, wanderSpeed) * 1.5f + 1f;
    }

    #endregion

    #region Chase

    private void TickChase()
    {
        if (!isAware || player == null)
        {
            SetState(State.Wander);
            return;
        }

        Vector3 toPlayer = Flat(player.position - transform.position);

        if (toPlayer.magnitude <= attackRange)
        {
            StartAttack();
            return;
        }

        moveDirection = toPlayer.normalized;
        moveSpeed = chaseSpeed;
        facingDirection = moveDirection;
    }

    #endregion

    #region Attack

    private void StartAttack()
    {
        SetState(State.Attack);

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }
        attackRoutine = StartCoroutine(AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        // 1) 선딜레이: 멈춘 채로 플레이어를 바라본다
        float timer = 0f;
        while (timer < attackWindup)
        {
            FacePlayer();
            timer += Time.deltaTime;
            yield return null;
        }

        // 2) 판정 + 전진: 이 순간에만 EnemyAttack 히트박스를 꺼내고 앞으로 튀어나간다
        SpawnAttackHitbox();
        StartLunge();
        yield return new WaitForSeconds(attackActiveTime);

        // 3) 후딜레이
        timer = 0f;
        while (timer < attackCooldown)
        {
            FacePlayer();
            timer += Time.deltaTime;
            yield return null;
        }

        attackRoutine = null;

        // 4) 다음 행동 결정
        if (isAware && player != null)
        {
            if (FlatDistance(transform.position, player.position) <= attackRange)
            {
                attackRoutine = StartCoroutine(AttackRoutine());
            }
            else
            {
                SetState(State.Chase);
            }
        }
        else
        {
            SetState(State.Wander);
        }
    }

    private void SpawnAttackHitbox()
    {
        if (attackHitboxPrefab == null)
        {
            Debug.LogWarning("[CHG_EnemyAI:" + name + "] attackHitboxPrefab이 비어 있어 공격 판정을 만들 수 없습니다.", this);
            return;
        }

        Vector3 position = transform.TransformPoint(attackHitboxOffset);
        Quaternion rotation = transform.rotation;

        CHG_EnemyAttackHitbox hitbox = CHG_ObjectPool.Spawn(attackHitboxPrefab, position, rotation);
        if (hitbox != null)
        {
            // 전진하는 동안 히트박스가 적을 따라오도록 follow 지정
            hitbox.Init(gameObject, attackDamage, attackHitboxSize, attackActiveTime, transform, attackHitboxOffset);
            Log("근접 공격");
        }
    }

    private void FacePlayer()
    {
        if (player == null)
        {
            return;
        }

        Vector3 toPlayer = Flat(player.position - transform.position);
        if (toPlayer.sqrMagnitude > 0.0001f)
        {
            facingDirection = toPlayer.normalized;
        }
    }

    #endregion

    #region Lunge

    private void StartLunge()
    {
        if (attackLungeDistance <= 0f)
        {
            return;
        }

        lungeDirection = Flat(rb.rotation * Vector3.forward).normalized;
        if (lungeDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // 전진 거리는 공격 순간에 확정한다.
        // 플레이어가 가까우면 표면 앞까지만 전진하고, 이후 플레이어가 물러나도 더 따라가지 않는다.
        float distance = Mathf.Min(attackLungeDistance, Mathf.Max(0f, GetLungeAllowance()));
        if (distance <= 0.0001f)
        {
            return;
        }

        lungeRemaining = distance;
        lungeSpeed = attackLungeDistance / Mathf.Max(0.01f, attackLungeDuration);
        isLunging = true;
    }

    private Vector3 GetLungeVelocity()
    {
        float deltaTime = Time.fixedDeltaTime;
        float step = Mathf.Min(lungeSpeed * deltaTime, lungeRemaining);

        // 전진 도중 플레이어가 다가와 막히면 표면 앞까지만 가고 전진을 끝낸다
        float allowance = GetLungeAllowance();
        bool blocked = allowance < step;
        if (blocked)
        {
            step = Mathf.Max(0f, allowance);
        }

        lungeRemaining -= step;
        Vector3 velocity = lungeDirection * (step / deltaTime);

        if (blocked || lungeRemaining <= 0.0001f)
        {
            isLunging = false;
            lungeRemaining = 0f;
        }

        return velocity;
    }

    /// <summary>
    /// 전진 방향으로 플레이어와 부딪히기 전까지 이동 가능한 거리.
    /// 플레이어가 전진 방향 뒤쪽이면 제한하지 않는다.
    /// </summary>
    private float GetLungeAllowance()
    {
        if (player == null || playerCollider == null)
        {
            return float.PositiveInfinity;
        }

        Vector3 origin = rb.position;
        Vector3 closest = GetClosestPoint(playerCollider, origin);
        Vector3 toSurface = Flat(closest - origin);

        if (Vector3.Dot(toSurface, lungeDirection) <= 0f && toSurface.sqrMagnitude > 0.0001f)
        {
            return float.PositiveInfinity;
        }

        return toSurface.magnitude - GetBodyRadius() - lungeStopGap;
    }

    private static Vector3 GetClosestPoint(Collider col, Vector3 point)
    {
        // 볼록하지 않은 MeshCollider는 ClosestPoint를 지원하지 않으므로 바운드로 대체
        if (col is MeshCollider meshCollider && !meshCollider.convex)
        {
            return col.bounds.ClosestPoint(point);
        }
        return col.ClosestPoint(point);
    }

    private float GetBodyRadius()
    {
        if (bodyCollider is CapsuleCollider capsule)
        {
            Vector3 scale = transform.lossyScale;
            return capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }

        if (bodyCollider != null)
        {
            Vector3 extents = bodyCollider.bounds.extents;
            return Mathf.Max(extents.x, extents.z);
        }

        return 0.5f;
    }

    #endregion

    #region Utility

    private void SetState(State next)
    {
        if (state == next)
        {
            return;
        }

        Log(state + " → " + next);
        state = next;

        if (next != State.Attack)
        {
            isLunging = false;
        }

        if (next == State.Wander)
        {
            hasWanderTarget = false;
            wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
        }
    }

    private static Vector3 Flat(Vector3 vector)
    {
        vector.y = 0f;
        return vector;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        return Flat(a - b).magnitude;
    }

    private void Log(string message)
    {
        if (logStateChanges)
        {
            Debug.Log("[CHG_EnemyAI:" + name + "] " + message, this);
        }
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmos()
    {
        Vector3 center = transform.position;
        Vector3 home = Application.isPlaying && hasHome ? homePosition : transform.position;

        // 배회 범위 (하늘색)
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        DrawCircle(home, wanderRadius);

        // 인지 범위 (노랑, 인지 중이면 주황)
        Gizmos.color = isAware ? new Color(1f, 0.4f, 0.1f, 1f) : new Color(1f, 0.9f, 0.1f, 1f);
        DrawCircle(center, detectRange);

        // 놓치는 범위 (옅은 노랑)
        Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.25f);
        DrawCircle(center, loseRange);

        // 공격 사거리 (빨강)
        Gizmos.color = Color.red;
        DrawCircle(center, attackRange);

        if (!Application.isPlaying)
        {
            return;
        }

        if (isAware && player != null)
        {
            Gizmos.color = new Color(1f, 0.4f, 0.1f, 1f);
            Gizmos.DrawLine(center, player.position);
        }
        else if (state == State.Wander && hasWanderTarget)
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            Gizmos.DrawLine(center, wanderTarget);
            Gizmos.DrawWireSphere(wanderTarget, 0.2f);
        }
    }

    private void OnDrawGizmosSelected()
    {
        // 공격 전진 거리 미리보기 (보라)
        if (attackLungeDistance > 0f)
        {
            Vector3 start = transform.position;
            Vector3 end = start + Flat(transform.forward).normalized * attackLungeDistance;
            Gizmos.color = new Color(0.8f, 0.3f, 1f, 1f);
            Gizmos.DrawLine(start, end);
            Gizmos.DrawWireSphere(end, 0.15f);
        }

        // 공격 히트박스 미리보기 (빨강)
        Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(attackHitboxOffset), transform.rotation, Vector3.one);
        Gizmos.color = new Color(1f, 0f, 0f, 0.3f);
        Gizmos.DrawCube(Vector3.zero, attackHitboxSize);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(Vector3.zero, attackHitboxSize);
        Gizmos.matrix = Matrix4x4.identity;
    }

    private static void DrawCircle(Vector3 center, float radius, int segments = 48)
    {
        if (radius <= 0f)
        {
            return;
        }

        Vector3 previous = center + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector3 next = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    #endregion
}
