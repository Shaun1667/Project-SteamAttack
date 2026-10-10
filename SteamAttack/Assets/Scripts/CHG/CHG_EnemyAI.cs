using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Random = UnityEngine.Random;

/// <summary>
/// 적 기본 AI (NGH)
/// - 배회(Wander): 스폰 지점 주변을 랜덤하게 돌아다닌다.
/// - 인지(Detect): 플레이어가 인지 범위 안에 들어오면 인지한다. (Gizmo로 범위 표시)
///   피해를 받으면(근접·총·수류탄·패링) 거리와 관계없이 바로 인지하고 추적한다.
/// - 추적(Chase): 인지한 플레이어에게 다가간다.
///   놓침 범위 밖에 Lose Delay 초 이상 머물면 추적을 끝낸다.
/// - 공격(Attack): 공격마다 일반공격 / 강공격(Heavy Attack 설정이 켜져 있을 때)을 확률로 고른다.
///   공격 순간 EnemyAttack 히트박스를 오브젝트 풀에서 꺼내 판정하고, 동시에 앞으로 전진한다.
///   강공격은 패링 가능 공격으로, 시작 순간부터 판정이 끝날 때까지 패링 구간이 열린다.
/// - 피격: 맞으면 Hit Stop Time 동안 애니메이션·이동·공격 타이머를 멈춘다. (피격 애니메이션 없음)
/// - 패링당함(Parried): 공격을 취소하고 경직 후 약점 노출 상태가 된다 (CHG_EnemyHealth).
/// - 원거리 모드(Ranged Attack 설정이 켜져 있을 때): 플레이어와 거리를 유지하며 조준 → 사격 → 장전을 반복한다.
///   멀거나 시야가 막히면 다가가고, 너무 가까우면 뒤로 물러난다. 총알은 chg_EnemyBullet(오브젝트 풀)로 날아간다.
/// - Animator가 있으면 Speed / MoveX / MoveZ / InCombat / Attack / HeavyAttack / Fire / Parried / Die 파라미터를 갱신한다 (없는 파라미터는 무시).
///   InCombat(bool): 플레이어를 인지해 전투 중이면 true, 비전투(인지 전·놓친 뒤·사망)면 false.
/// - 개발용 표시(chg_DebugView, 플레이 중 숫자 0 키로 켜고 끔, 기본 꺼짐):
///   범위·판정 Gizmo, 원거리 조준 레이저, 테스트 키 1·2 플레이어 경직/대경직, 3·4 증기 소비/회복, 5 패링당함 / 6 약점 노출 / 7 체력바 (대상: 락온한 적, 없으면 가장 가까운 적), 좌측 상단 설명·대상 정보 상자
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class CHG_EnemyAI : MonoBehaviour
{
    public enum State
    {
        Wander,
        Chase,
        Attack,
        Parried
    }

    [Serializable]
    public class HeavyAttackSettings
    {
        [Tooltip("강공격 사용 여부")]
        public bool enabled = false;
        [Tooltip("공격을 고를 때 강공격이 나올 확률 (0.3 = 30%)")]
        [Range(0f, 1f)] public float chance = 0.3f;
        public int damage = 44;
        [Tooltip("이 거리 안이면 강공격 시작 (달려들며 내려찍기)")]
        public float startRange = 4.5f;
        [Tooltip("시작부터 판정이 생길 때까지 시간")]
        public float windup = 1.62f;
        [Tooltip("히트박스가 유지되는 시간")]
        public float activeTime = 0.15f;
        [Tooltip("판정 후 다음 행동까지 시간")]
        public float recovery = 1.9f;
        [Tooltip("시작하면서 앞으로 달려드는 최대 거리 (플레이어 앞에서 멈춤)")]
        [Min(0f)] public float dashDistance = 4.3f;
        [Tooltip("달려드는 데 걸리는 시간")]
        [Min(0.01f)] public float dashDuration = 1.6f;
        public Vector3 hitboxOffset = new Vector3(0f, 0.9f, 1.1f);
        public Vector3 hitboxSize = new Vector3(1.4f, 1.8f, 1.6f);
        [Tooltip("빨리뽑기 패링으로 막을 수 있는 공격인지")]
        public bool parryable = true;
    }

    [Serializable]
    public class RangedAttackSettings
    {
        [Tooltip("원거리 모드 사용 여부 (켜면 근접 공격 대신 사격)")]
        public bool enabled = false;
        [Tooltip("총알 프리팹 (오브젝트 풀로 재사용)")]
        public chg_EnemyBullet bulletPrefab;
        [Tooltip("총알이 나가는 위치 (총구). 비우면 아래 오프셋 위치에서 발사")]
        public Transform firePoint;
        public Vector3 fallbackFireOffset = new Vector3(0f, 1.45f, 0.6f);
        public int damage = 18;
        [Tooltip("총알 속도 (m/s)")]
        public float bulletSpeed = 15f;
        [Tooltip("총알이 날아가는 최대 거리")]
        public float bulletRange = 25f;
        [Tooltip("총알 판정 반지름")]
        public float bulletRadius = 0.12f;
        [Tooltip("한 발 쏘고 다음 발을 쏘기까지 간격(초)")]
        public float fireInterval = 5f;
        [Tooltip("쏘기 전 멈춰서 조준하는 시간 (예고)")]
        public float aimTime = 0.8f;
        [Tooltip("사격 동작 시간")]
        public float fireAnimTime = 0.27f;
        [Tooltip("사격 후 장전 시간 (이 동안 제자리)")]
        public float reloadTime = 3.3f;
        [Tooltip("인지 후 첫 발까지 대기 시간")]
        public float firstShotDelay = 1f;
        [Tooltip("이 거리보다 멀면 다가감")]
        public float preferredMaxDistance = 14f;
        [Tooltip("이 거리보다 가까우면 뒤로 물러남")]
        public float retreatDistance = 5f;
        [Tooltip("이 거리 안에서만 사격")]
        public float maxFireDistance = 18f;
        [Tooltip("뒤로 물러나는 속도")]
        public float retreatSpeed = 1.1f;
        [Tooltip("플레이어 발밑 기준 조준 높이 (가슴)")]
        public float aimHeight = 1.2f;
        [Tooltip("시야를 막는 레이어 (장애물)")]
        public LayerMask lineOfSightMask = ~0;

        [Header("근접 킥 (패링 가능 강공격)")]
        [Tooltip("플레이어가 가까이 붙으면 물러나는 대신 킥 (조준·장전 중에는 차지 않음)")]
        public bool kickEnabled = false;
        [Tooltip("이 거리 안이면 킥")]
        public float kickRange = 2.5f;
        public int kickDamage = 25;
        [Tooltip("킥 후 다음 킥까지 시간(초)")]
        public float kickCooldown = 4f;
        [Tooltip("차기 전 준비 자세 시간 (패링 예고)")]
        public float kickPrepTime = 0.7f;
        [Tooltip("준비 자세 동안 킥 동작 재생 속도 (작을수록 멈춘 듯이)")]
        [Range(0f, 1f)] public float kickPrepAnimSpeed = 0.15f;
        [Tooltip("킥 클립에서 발이 닿는 시간(초) — 이 순간 판정 생성")]
        public float kickHitClipTime = 0.3f;
        [Tooltip("판정 유지 시간")]
        public float kickActiveTime = 0.13f;
        [Tooltip("킥 클립 전체 길이(초)")]
        public float kickClipLength = 0.8f;
        [Tooltip("차는 순간 앞으로 나가는 거리")]
        public float kickLungeDistance = 0.3f;
        public Vector3 kickHitboxOffset = new Vector3(0f, 1.0f, 0.9f);
        public Vector3 kickHitboxSize = new Vector3(1.0f, 1.0f, 1.2f);
        [Tooltip("빨리뽑기 패링으로 막을 수 있는지")]
        public bool kickParryable = true;
        [Tooltip("킥 방향 보정(도). 킥 동작이 몸 정면보다 왼쪽으로 차므로, 그만큼 몸을 오른쪽(+)으로 돌려 발이 플레이어를 향하게 함. 플레이 중 조절 가능")]
        [Range(-45f, 45f)] public float kickFacingOffset = 10f;
    }

    /// <summary>패링 가능한 공격이 시작될 때 (적, 판정까지 남은 시간) — 패링 UI(동심원)용</summary>
    public static event Action<CHG_EnemyAI, float> ParryableAttackStarted;

    private static readonly List<CHG_EnemyAI> activeEnemies = new List<CHG_EnemyAI>();

    /// <summary>패링 판정: 동심원이 겹치는 순간 = 공격 판정 이 시간(초) 전. 이 순간 ± ParryHalfWindow 안에 누르면 성공</summary>
    public const float ParryOverlapLead = 0.15f;
    /// <summary>패링 성공 허용 범위 (겹치는 순간 기준 ±초, 기획서 0.3초 = ±0.15)</summary>
    public const float ParryHalfWindow = 0.15f;

    /// <summary>지금 살아서 움직이는 적 목록 (패링·UI에서 사용)</summary>
    public static IReadOnlyList<CHG_EnemyAI> ActiveEnemies => activeEnemies;
    private static int debugKeyFrame = -1;

    [Header("Target")]
    [Tooltip("인지 대상 태그")]
    [SerializeField] private string playerTag = "Player";

    [Header("Wander (배회)")]
    [Tooltip("스폰 지점 기준 배회 반경 (0이면 제자리 대기)")]
    [SerializeField] private float wanderRadius = 6f;
    [SerializeField] private float wanderSpeed = 1.5f;
    [Tooltip("목적지 도착 후 대기 시간 (최소)")]
    [SerializeField] private float wanderWaitMin = 1f;
    [Tooltip("목적지 도착 후 대기 시간 (최대)")]
    [SerializeField] private float wanderWaitMax = 2.5f;

    [Header("Detection (인지)")]
    [Tooltip("이 거리 안에 플레이어가 들어오면 인지")]
    [SerializeField] private float detectRange = 8f;
    [Tooltip("인지 후 이 거리보다 멀어지면 놓침 판정 시작 (인지 범위 이상이어야 함)")]
    [SerializeField] private float loseRange = 12f;
    [Tooltip("놓침 범위 밖에 이 시간(초) 이상 있으면 추적 종료")]
    [Min(0f)]
    [SerializeField] private float loseDelay = 3f;
    [Tooltip("놓침 범위 밖에서 맞았을 때, 가까이 올 때까지 놓침 판정 없이 쫓아가는 최대 시간(초)")]
    [Min(0f)]
    [SerializeField] private float damagedChaseMaxTime = 10f;

    [Header("Chase (추적)")]
    [SerializeField] private float chaseSpeed = 3.5f;
    [Tooltip("초당 회전 각도")]
    [SerializeField] private float turnSpeed = 540f;

    [Header("Attack (일반 공격)")]
    [Tooltip("이 거리 안이면 공격 시작")]
    [SerializeField] private float attackRange = 1.8f;
    [Tooltip("공격 선딜레이 (판정 생성 전 대기)")]
    [SerializeField] private float attackWindup = 0.4f;
    [Tooltip("히트박스가 유지되는 시간")]
    [SerializeField] private float attackActiveTime = 0.15f;
    [Tooltip("공격 후딜레이")]
    [SerializeField] private float attackCooldown = 1.2f;
    [SerializeField] private int attackDamage = 22;
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

    [Header("Heavy Attack (강공격, 패링 가능)")]
    [SerializeField] private HeavyAttackSettings heavyAttack = new HeavyAttackSettings();

    [Header("Ranged Attack (원거리 사격)")]
    [SerializeField] private RangedAttackSettings rangedAttack = new RangedAttackSettings();

    [Header("Hit / Parried (피격 · 패링당함)")]
    [Tooltip("맞았을 때 애니메이션·이동을 멈추는 시간(초)")]
    [Min(0f)]
    [SerializeField] private float hitStopTime = 0.1f;
    [Tooltip("패링당한 뒤 움직이지 못하는 시간(초)")]
    [Min(0f)]
    [SerializeField] private float parriedStunTime = 1.8f;

    [Header("Animator (비우면 자식에서 찾음)")]
    [SerializeField] private Animator animator;
    [Tooltip("배회 속도일 때 Speed 값 (걷기)")]
    [SerializeField] private float walkAnimSpeedValue = 0.5f;
    [Tooltip("추적 속도일 때 Speed 값 (달리기)")]
    [SerializeField] private float runAnimSpeedValue = 1f;
    [SerializeField] private float speedDampTime = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool logStateChanges = true;
    [Tooltip("1·2 플레이어 경직, 3·4 증기, 5 패링당함 / 6 약점 노출 / 7 체력바 (락온 대상 또는 가장 가까운 적). 0 키로 개발용 표시를 켰을 때만 동작")]
    [SerializeField] private bool debugKeys = true;
    [Tooltip("원거리: 개발용 표시가 켜져 있을 때 조준 방향 레이저를 보여줌")]
    [SerializeField] private bool debugAimLaser = true;

    public State CurrentState => state;
    public bool IsAwareOfPlayer => isAware;
    /// <summary>패링 가능한 공격의 판정이 끝나기 전인지</summary>
    public bool IsParryWindowOpen => parryWindowOpen;
    /// <summary>패링 가능한 공격의 판정이 생기는 시각 (Time.time 기준, 경직으로 밀리면 함께 밀림)</summary>
    public float ParryHitTime => parryHitTime;
    /// <summary>동심원이 겹치는 시각 = 패링 타이밍의 한가운데</summary>
    public float ParryOverlapTime => parryHitTime - ParryOverlapLead;
    /// <summary>강공격(도끼 달려들기, 소총 킥) 중이라 패링 외에는 끊기지 않는 상태인지</summary>
    public bool IsSuperArmor => superArmor;

    /// <summary>인지 상태가 바뀔 때 (true = 추적 시작, false = 추적 종료)</summary>
    public event Action<bool> AwarenessChanged;
    /// <summary>패링당했을 때</summary>
    public event Action Parried;

    private static readonly int SpeedHash = Animator.StringToHash("Speed");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");
    private static readonly int ParriedHash = Animator.StringToHash("Parried");
    private static readonly int DieHash = Animator.StringToHash("Die");
    private static readonly int FireHash = Animator.StringToHash("Fire");
    private static readonly int KickHash = Animator.StringToHash("Kick");
    private static readonly int KickSpeedHash = Animator.StringToHash("KickSpeed");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveZHash = Animator.StringToHash("MoveZ");
    private static readonly int InCombatHash = Animator.StringToHash("InCombat");
    private static readonly RaycastHit[] losHits = new RaycastHit[16];
    private readonly HashSet<int> animatorParams = new HashSet<int>();

    private Rigidbody rb;
    private Collider bodyCollider;
    private CHG_EnemyHealth health;
    private Transform player;
    private Collider playerCollider;
    private State state = State.Wander;
    private bool isAware;
    private float loseTimer;
    private float damagedChaseTimer;

    private Vector3 homePosition;
    private bool hasHome;
    private Vector3 wanderTarget;
    private bool hasWanderTarget;
    private float wanderWaitTimer;
    private float wanderMoveTimer;
    private float wanderGiveUpTime;

    private Coroutine attackRoutine;
    private bool attackChosen;
    private bool nextIsHeavy;
    private bool parryWindowOpen;
    private float parriedTimer;
    private float hitStopTimer;
    private float nextFireTime;
    private float nextKickTime;
    private float parryHitTime;
    private bool superArmor;
    private float nextLosCheck;
    private bool hasLineOfSight = true;
    private bool isAiming;
    private Vector3 lastShotDirection;
    private float laserFlashTimer;
    private LineRenderer debugLaser;
    private static Material debugLaserMaterial;
    private Action drawDebugLines;

    private Vector3 moveDirection;
    private float moveSpeed;
    private Vector3 facingDirection;

    private bool isLunging;
    private Vector3 lungeDirection;
    private float lungeRemaining;
    private float lungeSpeed;

    private bool IsHitStopped => hitStopTimer > 0f;
    private bool IsRanged => rangedAttack != null && rangedAttack.enabled;
    // 경직 중에는 공격 타이머가 흐르지 않는다
    private float ActiveDeltaTime => IsHitStopped ? 0f : Time.deltaTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        activeEnemies.Clear();
        debugKeyFrame = -1;
        ParryableAttackStarted = null;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        bodyCollider = GetComponent<Collider>();
        health = GetComponent<CHG_EnemyHealth>();
        if (health != null)
        {
            health.Damaged += OnDamaged;
        }

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }
        CacheAnimatorParams();

        homePosition = transform.position;
        hasHome = true;
        facingDirection = transform.forward;
    }

    private void OnDestroy()
    {
        if (health != null)
        {
            health.Damaged -= OnDamaged;
        }
    }

    private void Start()
    {
        FindPlayer();
        wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);

        if (attackHitboxPrefab != null)
        {
            CHG_ObjectPool.Prewarm(attackHitboxPrefab.gameObject, attackHitboxPrewarm);
        }
        if (IsRanged && rangedAttack.bulletPrefab != null)
        {
            CHG_ObjectPool.Prewarm(rangedAttack.bulletPrefab.gameObject, 3);
        }
    }

    private void OnEnable()
    {
        if (!activeEnemies.Contains(this))
        {
            activeEnemies.Add(this);
        }
        if (drawDebugLines == null)
        {
            drawDebugLines = DrawDebugLines;
        }
        chg_DebugView.Register(drawDebugLines);
    }

    private void OnDisable()
    {
        activeEnemies.Remove(this);
        chg_DebugView.Unregister(drawDebugLines);
        isAiming = false;
        laserFlashTimer = 0f;
        if (debugLaser != null)
        {
            debugLaser.enabled = false;
        }
        isLunging = false;
        parryWindowOpen = false;
        EndHitStop();
    }

    private void OnValidate()
    {
        wanderRadius = Mathf.Max(0f, wanderRadius);
        wanderWaitMax = Mathf.Max(wanderWaitMin, wanderWaitMax);
        detectRange = Mathf.Max(0.1f, detectRange);
        loseRange = Mathf.Max(detectRange, loseRange);
        attackRange = Mathf.Clamp(attackRange, 0.1f, detectRange);
        attackHitboxPrewarm = Mathf.Max(0, attackHitboxPrewarm);
        if (rangedAttack != null)
        {
            rangedAttack.retreatDistance = Mathf.Max(0f, rangedAttack.retreatDistance);
            rangedAttack.preferredMaxDistance = Mathf.Max(rangedAttack.retreatDistance + 0.5f, rangedAttack.preferredMaxDistance);
            rangedAttack.maxFireDistance = Mathf.Max(rangedAttack.preferredMaxDistance, rangedAttack.maxFireDistance);
            rangedAttack.fireInterval = Mathf.Max(0.1f, rangedAttack.fireInterval);
            rangedAttack.bulletSpeed = Mathf.Max(0.1f, rangedAttack.bulletSpeed);
        }
        if (heavyAttack != null)
        {
            heavyAttack.startRange = Mathf.Max(0.1f, heavyAttack.startRange);
            heavyAttack.windup = Mathf.Max(0f, heavyAttack.windup);
            heavyAttack.activeTime = Mathf.Max(0.01f, heavyAttack.activeTime);
            heavyAttack.recovery = Mathf.Max(0f, heavyAttack.recovery);
        }
    }

    private void Update()
    {
        chg_DebugView.PollToggleKey();
        if (debugKeys && chg_DebugView.Enabled)
        {
            HandleDebugKeys();
        }

        if (player == null)
        {
            FindPlayer();
        }

        if (IsHitStopped)
        {
            hitStopTimer -= Time.deltaTime;
            if (hitStopTimer <= 0f)
            {
                EndHitStop();
            }
        }

        UpdateAwareness();

        moveDirection = Vector3.zero;
        moveSpeed = 0f;

        if (!IsHitStopped)
        {
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
                case State.Parried:
                    TickParried();
                    break;
            }
        }

        UpdateAnimatorSpeed();
    }

    private void FixedUpdate()
    {
        Vector3 velocity;
        if (IsHitStopped || state == State.Parried)
        {
            velocity = Vector3.zero;
        }
        else
        {
            velocity = isLunging ? GetLungeVelocity() : moveDirection * moveSpeed;
        }
        rb.linearVelocity = new Vector3(velocity.x, rb.linearVelocity.y, velocity.z);

        if (!IsHitStopped && state != State.Parried && facingDirection.sqrMagnitude > 0.0001f)
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
        attackChosen = false;
        parryWindowOpen = false;
        isLunging = false;
        lungeRemaining = 0f;
        EndHitStop();
        SetAware(false);
        moveDirection = Vector3.zero;
        moveSpeed = 0f;
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
        }
        SetAnimFloat(SpeedHash, 0f, false);
        SetAnimFloat(MoveXHash, 0f, false);
        SetAnimFloat(MoveZHash, 0f, false);
        ResetAttackTriggers();
        SetAnimTrigger(DieHash);
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
        if (animator != null)
        {
            animator.Rebind();
            animator.speed = 1f;
        }
        SetAware(false);
        hasWanderTarget = false;
        attackChosen = false;
        nextFireTime = 0f;
        nextKickTime = 0f;
        SetState(State.Wander);
        wanderWaitTimer = Random.Range(wanderWaitMin, wanderWaitMax);
        enabled = true;
    }

    #endregion

    #region Hit / Parried

    // CHG_EnemyHealth.Damaged — 근접·총·수류탄 모두 여기로 들어온다
    private void OnDamaged(float amount, Vector3 hitFrom)
    {
        if (!enabled)
        {
            return;
        }
        Aggro(true);
        HitStop(hitStopTime);
    }

    /// <summary>애니메이션·이동·공격 타이머를 잠깐 멈춘다 (일반 피격 경직)</summary>
    public void HitStop(float duration)
    {
        if (duration <= 0f || !enabled)
        {
            return;
        }
        float added = Mathf.Max(0f, duration - hitStopTimer);
        hitStopTimer = Mathf.Max(hitStopTimer, duration);
        if (parryWindowOpen)
        {
            parryHitTime += added;   // 멈춘 만큼 판정 시각도 밀림
        }
        if (animator != null)
        {
            animator.speed = 0f;
        }
    }

    private void EndHitStop()
    {
        hitStopTimer = 0f;
        if (animator != null)
        {
            animator.speed = 1f;
        }
    }

    /// <summary>
    /// 플레이어 패링 판정에서 호출. 패링 가능한 공격 중이면 패링당함 처리 후 true.
    /// (플레이어 쪽 패링 기능이 생기면 여기에 연결)
    /// </summary>
    public bool TryParry()
    {
        if (!enabled || !parryWindowOpen)
        {
            return false;
        }
        if (Mathf.Abs(Time.time - ParryOverlapTime) > ParryHalfWindow)
        {
            return false;   // 너무 빠르거나 늦음
        }
        ForceParried();
        return true;
    }

    /// <summary>공격 여부와 관계없이 패링당함 상태로 만든다 (테스트용 5 키)</summary>
    public void ForceParried()
    {
        if (!enabled || (health != null && !health.IsAlive))
        {
            return;
        }

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
            attackRoutine = null;
        }
        attackChosen = false;
        parryWindowOpen = false;
        isLunging = false;
        lungeRemaining = 0f;
        EndHitStop();

        Aggro(true);
        SetState(State.Parried);
        parriedTimer = parriedStunTime;
        ResetAttackTriggers();
        SetAnimTrigger(ParriedHash);

        if (health != null)
        {
            health.ExposeWeakness();
        }
        Log("패링당함 → 약점 노출");
        Parried?.Invoke();
    }

    private void TickParried()
    {
        parriedTimer -= Time.deltaTime;
        if (parriedTimer > 0f)
        {
            return;
        }
        SetState(isAware && player != null ? State.Chase : State.Wander);
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

    /// <summary>플레이어를 인지시킨다. fromDamage면 거리와 관계없이 추적을 시작한다.</summary>
    public void Aggro(bool fromDamage)
    {
        if (player == null)
        {
            FindPlayer();
        }
        if (player == null)
        {
            return;
        }

        loseTimer = 0f;
        if (fromDamage && FlatDistance(transform.position, player.position) > loseRange)
        {
            damagedChaseTimer = damagedChaseMaxTime;
        }

        if (!isAware)
        {
            SetAware(true);
            Log(fromDamage ? "피격 → 플레이어 인지" : "플레이어 인지");
            if (IsRanged)
            {
                nextFireTime = Mathf.Max(nextFireTime, Time.time + rangedAttack.firstShotDelay);
            }
        }
        if (state == State.Wander)
        {
            SetState(State.Chase);
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

        if (!isAware)
        {
            if (distance <= detectRange)
            {
                Aggro(false);
            }
            return;
        }

        // 멀리서 맞고 쫓아오는 중: 가까워질 때까지(또는 최대 시간까지) 놓치지 않는다
        if (damagedChaseTimer > 0f)
        {
            damagedChaseTimer = distance <= loseRange ? 0f : damagedChaseTimer - Time.deltaTime;
            loseTimer = 0f;
            return;
        }

        if (distance > loseRange)
        {
            loseTimer += Time.deltaTime;
            if (loseTimer >= loseDelay)
            {
                LosePlayer();
            }
        }
        else
        {
            loseTimer = 0f;
        }
    }

    private void LosePlayer()
    {
        SetAware(false);
        attackChosen = false;
        Log("플레이어 놓침");

        // 공격·패링당함 중이면 끝난 뒤 배회로 돌아간다
        if (state != State.Attack && state != State.Parried)
        {
            SetState(State.Wander);
        }
    }

    private void SetAware(bool aware)
    {
        loseTimer = 0f;
        if (!aware)
        {
            damagedChaseTimer = 0f;
        }
        if (isAware == aware)
        {
            return;
        }
        isAware = aware;
        SetAnimBool(InCombatHash, aware);
        AwarenessChanged?.Invoke(aware);
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

        if (IsRanged)
        {
            TickRangedChase();
            return;
        }

        Vector3 toPlayer = Flat(player.position - transform.position);
        float distance = toPlayer.magnitude;

        // 다음 공격 종류는 공격마다 한 번 고른다 (일반 70% / 강공격 30%)
        if (!attackChosen)
        {
            nextIsHeavy = heavyAttack != null && heavyAttack.enabled && Random.value < heavyAttack.chance;
            attackChosen = true;
        }

        if (nextIsHeavy ? distance <= heavyAttack.startRange : distance <= attackRange)
        {
            StartAttack(nextIsHeavy);
            return;
        }

        moveDirection = toPlayer.normalized;
        moveSpeed = chaseSpeed;
        facingDirection = moveDirection;
    }

    #endregion

    #region Attack

    private void StartAttack(bool heavy)
    {
        SetState(State.Attack);

        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }
        attackRoutine = StartCoroutine(heavy ? HeavyAttackRoutine() : AttackRoutine());
    }

    private IEnumerator AttackRoutine()
    {
        ResetAttackTriggers();
        SetAnimTrigger(AttackHash);

        // 1) 선딜레이: 멈춘 채로 플레이어를 바라본다
        float timer = 0f;
        while (timer < attackWindup)
        {
            FacePlayer();
            timer += ActiveDeltaTime;
            yield return null;
        }

        // 2) 판정 + 전진: 이 순간에만 EnemyAttack 히트박스를 꺼내고 앞으로 튀어나간다
        SpawnAttackHitbox(attackDamage, attackHitboxOffset, attackHitboxSize, attackActiveTime, "근접 공격");
        StartLunge(attackLungeDistance, attackLungeDuration, false);
        yield return WaitActive(attackActiveTime);

        // 3) 후딜레이
        timer = 0f;
        while (timer < attackCooldown)
        {
            FacePlayer();
            timer += ActiveDeltaTime;
            yield return null;
        }

        FinishAttack();
    }

    private IEnumerator HeavyAttackRoutine()
    {
        HeavyAttackSettings h = heavyAttack;
        ResetAttackTriggers();
        SetAnimTrigger(HeavyAttackHash);

        // 1) 플레이어 쪽으로 방향을 정하고 달려든다 (도착 거리는 시작 순간에 확정)
        FacePlayer();
        lungeDirection = facingDirection.sqrMagnitude > 0.0001f ? facingDirection : Flat(transform.forward).normalized;
        StartLunge(h.dashDistance, h.dashDuration, true);

        superArmor = true;
        if (h.parryable)
        {
            parryWindowOpen = true;
            parryHitTime = Time.time + h.windup;
            ParryableAttackStarted?.Invoke(this, h.windup);
        }

        float timer = 0f;
        while (timer < h.windup)
        {
            timer += ActiveDeltaTime;
            yield return null;
        }

        // 2) 내려찍기 판정
        SpawnAttackHitbox(h.damage, h.hitboxOffset, h.hitboxSize, h.activeTime, "강공격");
        yield return WaitActive(h.activeTime);
        parryWindowOpen = false;

        // 3) 후딜레이
        timer = 0f;
        while (timer < h.recovery)
        {
            timer += ActiveDeltaTime;
            yield return null;
        }

        FinishAttack();
    }

    private IEnumerator WaitActive(float duration)
    {
        float timer = 0f;
        while (timer < duration)
        {
            timer += ActiveDeltaTime;
            yield return null;
        }
    }

    private void FinishAttack()
    {
        attackRoutine = null;
        attackChosen = false;
        parryWindowOpen = false;
        superArmor = false;

        // 다음 행동은 추적 상태에서 다시 고른다 (사거리 안이면 바로 다음 공격)
        SetState(isAware && player != null ? State.Chase : State.Wander);
    }

    private void SpawnAttackHitbox(int damage, Vector3 offset, Vector3 size, float activeTime, string label)
    {
        if (attackHitboxPrefab == null)
        {
            Debug.LogWarning("[CHG_EnemyAI:" + name + "] attackHitboxPrefab이 비어 있어 공격 판정을 만들 수 없습니다.", this);
            return;
        }

        Vector3 position = transform.TransformPoint(offset);
        Quaternion rotation = transform.rotation;

        CHG_EnemyAttackHitbox hitbox = CHG_ObjectPool.Spawn(attackHitboxPrefab, position, rotation);
        if (hitbox != null)
        {
            // 전진하는 동안 히트박스가 적을 따라오도록 follow 지정
            hitbox.Init(gameObject, damage, size, activeTime, transform, offset);
            Log(label + " (데미지 " + damage + ")");
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

    #region Ranged

    // 원거리: 거리 유지 + 시야 확인 + 사격 간격이 되면 조준·사격
    private void TickRangedChase()
    {
        RangedAttackSettings r = rangedAttack;
        Vector3 toPlayer = Flat(player.position - transform.position);
        float distance = toPlayer.magnitude;
        Vector3 dir = distance > 0.001f ? toPlayer / distance : Flat(transform.forward).normalized;

        if (Time.time >= nextLosCheck)
        {
            nextLosCheck = Time.time + 0.2f;
            hasLineOfSight = CheckLineOfSight();
        }

        facingDirection = dir;   // 항상 플레이어를 바라봄 (뒤로 물러날 때도)

        // 바짝 붙으면 물러나는 대신 킥 (조준·장전은 Attack 상태라 여기 오지 않음 → 그동안은 차지 않음)
        if (r.kickEnabled && distance <= r.kickRange && Time.time >= nextKickTime)
        {
            StartKick();
            return;
        }

        if (Time.time >= nextFireTime && hasLineOfSight && distance <= r.maxFireDistance)
        {
            StartRangedAttack();
            return;
        }

        if (distance > r.preferredMaxDistance || !hasLineOfSight)
        {
            moveDirection = dir;
            moveSpeed = chaseSpeed;
        }
        else if (distance < r.retreatDistance)
        {
            moveDirection = -dir;
            moveSpeed = r.retreatSpeed;
        }
    }

    private void StartKick()
    {
        SetState(State.Attack);
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }
        attackRoutine = StartCoroutine(KickRoutine());
    }

    // 준비 자세(패링 예고) → 킥 → 복귀. 강공격이라 패링 외에는 끊기지 않음
    private IEnumerator KickRoutine()
    {
        RangedAttackSettings r = rangedAttack;
        superArmor = true;
        nextKickTime = Time.time + r.kickCooldown;

        // 1) 준비 자세: 킥 동작을 아주 느리게 시작하며 플레이어를 바라봄
        ResetAttackTriggers();
        SetAnimFloat(KickSpeedHash, r.kickPrepAnimSpeed, false);
        SetAnimTrigger(KickHash);
        if (r.kickParryable)
        {
            parryWindowOpen = true;
            float untilHit = r.kickPrepTime + Mathf.Max(0f, r.kickHitClipTime - r.kickPrepTime * r.kickPrepAnimSpeed);
            parryHitTime = Time.time + untilHit;
            ParryableAttackStarted?.Invoke(this, untilHit);
        }
        Log("킥 준비");

        float timer = 0f;
        while (timer < r.kickPrepTime)
        {
            FacePlayer();
            facingDirection = Quaternion.Euler(0f, r.kickFacingOffset, 0f) * facingDirection;
            timer += ActiveDeltaTime;
            yield return null;
        }

        // 2) 차기: 원래 속도로 재생, 발이 닿는 순간 판정
        SetAnimFloat(KickSpeedHash, 1f, false);
        float windup = Mathf.Max(0f, r.kickHitClipTime - r.kickPrepTime * r.kickPrepAnimSpeed);
        timer = 0f;
        while (timer < windup)
        {
            timer += ActiveDeltaTime;
            yield return null;
        }

        SpawnAttackHitbox(r.kickDamage, r.kickHitboxOffset, r.kickHitboxSize, r.kickActiveTime, "킥");
        StartLunge(r.kickLungeDistance, 0.12f, false);
        yield return WaitActive(r.kickActiveTime);
        parryWindowOpen = false;

        // 3) 남은 동작
        float recovery = Mathf.Max(0f, r.kickClipLength - r.kickHitClipTime - r.kickActiveTime);
        timer = 0f;
        while (timer < recovery)
        {
            timer += ActiveDeltaTime;
            yield return null;
        }

        FinishAttack();
    }

    private void StartRangedAttack()
    {
        SetState(State.Attack);
        if (attackRoutine != null)
        {
            StopCoroutine(attackRoutine);
        }
        attackRoutine = StartCoroutine(RangedAttackRoutine());
    }

    private IEnumerator RangedAttackRoutine()
    {
        RangedAttackSettings r = rangedAttack;

        // 1) 조준 (예고): 멈춰서 플레이어를 따라 돌린다
        isAiming = true;
        float timer = 0f;
        while (timer < r.aimTime)
        {
            FacePlayer();
            timer += ActiveDeltaTime;
            yield return null;
        }
        isAiming = false;

        // 2) 사격: 이 순간의 플레이어 가슴 쪽으로 총알을 날린다 (이후엔 따라가지 않음)
        ResetAttackTriggers();
        SetAnimTrigger(FireHash);
        FireBullet();
        nextFireTime = Time.time + r.fireInterval;

        // 3) 사격 동작 + 장전 (제자리)
        timer = 0f;
        float lockTime = r.fireAnimTime + r.reloadTime;
        while (timer < lockTime)
        {
            FacePlayer();
            timer += ActiveDeltaTime;
            yield return null;
        }

        FinishAttack();
    }

    private void FireBullet()
    {
        RangedAttackSettings r = rangedAttack;
        if (r.bulletPrefab == null)
        {
            Debug.LogWarning("[CHG_EnemyAI:" + name + "] 총알 프리팹(Bullet Prefab)이 비어 있어 사격할 수 없습니다.", this);
            return;
        }

        Vector3 origin = GetFirePosition();
        Vector3 target = player != null ? player.position + Vector3.up * r.aimHeight : origin + transform.forward;
        Vector3 dir = target - origin;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = transform.forward;
        }
        dir.Normalize();

        lastShotDirection = dir;
        laserFlashTimer = 0.25f;
        chg_EnemyBullet bullet = CHG_ObjectPool.Spawn(r.bulletPrefab, origin, Quaternion.LookRotation(dir));
        if (bullet != null)
        {
            bullet.Fire(gameObject, r.damage, dir, r.bulletSpeed, r.bulletRange, r.bulletRadius);
            Log("사격 (데미지 " + r.damage + ")");
        }
    }

    private Vector3 GetFirePosition()
    {
        RangedAttackSettings r = rangedAttack;
        return r.firePoint != null ? r.firePoint.position : transform.TransformPoint(r.fallbackFireOffset);
    }

    // 총구 높이에서 플레이어 가슴까지 장애물이 있는지 (자기 자신·다른 적·플레이어는 무시)
    private bool CheckLineOfSight()
    {
        if (player == null)
        {
            return false;
        }

        Vector3 from = GetFirePosition();
        Vector3 to = player.position + Vector3.up * rangedAttack.aimHeight;
        Vector3 dir = to - from;
        float distance = dir.magnitude;
        if (distance < 0.01f)
        {
            return true;
        }

        int count = Physics.RaycastNonAlloc(from, dir / distance, losHits, distance, rangedAttack.lineOfSightMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider col = losHits[i].collider;
            if (col == null || col.transform.IsChildOf(transform) || col.transform.IsChildOf(player))
            {
                continue;
            }
            if (col.GetComponentInParent<CHG_EnemyHealth>() != null || col.GetComponentInParent<NGH_PlayerController>() != null)
            {
                continue;
            }
            return false;
        }
        return true;
    }

    #endregion

    #region Debug Aim Laser (개발용)

    private void LateUpdate()
    {
        UpdateDebugLaser();
    }

    // 원거리: 조준 중엔 지금 겨누는 방향(빨강), 쏜 직후엔 실제로 쏜 방향(노랑)을 레이저로 표시
    private void UpdateDebugLaser()
    {
        if (laserFlashTimer > 0f)
        {
            laserFlashTimer -= Time.deltaTime;
        }

        bool show = debugAimLaser && chg_DebugView.Enabled && IsRanged && player != null
                    && (isAiming || laserFlashTimer > 0f);
        if (!show)
        {
            if (debugLaser != null && debugLaser.enabled)
            {
                debugLaser.enabled = false;
            }
            return;
        }

        if (debugLaser == null)
        {
            CreateDebugLaser();
        }

        Vector3 from = GetFirePosition();
        Vector3 dir = isAiming
            ? (player.position + Vector3.up * rangedAttack.aimHeight - from)
            : lastShotDirection;
        if (dir.sqrMagnitude < 0.0001f)
        {
            dir = transform.forward;
        }
        dir.Normalize();

        float length = rangedAttack.bulletRange;
        if (Physics.Raycast(from, dir, out RaycastHit hit, length, rangedAttack.lineOfSightMask, QueryTriggerInteraction.Ignore)
            && !hit.collider.transform.IsChildOf(transform))
        {
            length = hit.distance;
        }

        Color color = isAiming ? new Color(1f, 0.1f, 0.1f, 0.9f) : new Color(1f, 0.85f, 0.2f, 0.9f);
        debugLaser.startColor = color;
        debugLaser.endColor = color;
        debugLaser.SetPosition(0, from);
        debugLaser.SetPosition(1, from + dir * length);
        debugLaser.enabled = true;
    }

    // 플레이 중 개발용 표시(0 키): 범위 원 + 플레이어 연결선 (Game 뷰·Scene 뷰 모두)
    private void DrawDebugLines()
    {
        Vector3 center = transform.position + Vector3.up * 0.05f;
        Vector3 home = (hasHome ? homePosition : transform.position) + Vector3.up * 0.05f;

        chg_DebugView.Circle(home, wanderRadius, new Color(0.2f, 0.8f, 1f, 0.6f));
        chg_DebugView.Circle(center, detectRange, isAware ? new Color(1f, 0.4f, 0.1f, 1f) : new Color(1f, 0.9f, 0.1f, 1f));
        chg_DebugView.Circle(center, loseRange, new Color(1f, 0.9f, 0.1f, 0.3f));

        if (IsRanged)
        {
            chg_DebugView.Circle(center, rangedAttack.retreatDistance, Color.red);
            if (rangedAttack.kickEnabled)
            {
                chg_DebugView.Circle(center, rangedAttack.kickRange, new Color(0.8f, 0.2f, 0.6f, 0.9f));
            }
            chg_DebugView.Circle(center, rangedAttack.preferredMaxDistance, new Color(0.2f, 0.9f, 0.3f, 1f));
            chg_DebugView.Circle(center, rangedAttack.maxFireDistance, new Color(0.2f, 0.9f, 0.3f, 0.4f));
            if (isAware && player != null)
            {
                Color c = hasLineOfSight ? new Color(1f, 0.9f, 0.2f, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.8f);
                chg_DebugView.Line(GetFirePosition(), player.position + Vector3.up * rangedAttack.aimHeight, c);
            }
        }
        else
        {
            chg_DebugView.Circle(center, attackRange, Color.red);
            if (heavyAttack != null && heavyAttack.enabled)
            {
                chg_DebugView.Circle(center, heavyAttack.startRange, new Color(0.8f, 0.2f, 0.6f, 0.8f));
            }
            if (isAware && player != null)
            {
                chg_DebugView.Line(center, player.position + Vector3.up * 0.05f, new Color(1f, 0.4f, 0.1f, 1f));
            }
        }
    }

    // 레이저는 이 적 오브젝트에 컴포넌트로만 붙임 (새 오브젝트를 만들지 않음)
    private void CreateDebugLaser()
    {
        debugLaser = GetComponent<LineRenderer>();
        if (debugLaser == null)
        {
            debugLaser = gameObject.AddComponent<LineRenderer>();
        }
        if (debugLaserMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }
            debugLaserMaterial = new Material(shader);
        }
        debugLaser.sharedMaterial = debugLaserMaterial;
        debugLaser.useWorldSpace = true;
        debugLaser.positionCount = 2;
        debugLaser.widthMultiplier = 0.025f;
        debugLaser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        debugLaser.receiveShadows = false;
        debugLaser.enabled = false;
    }

    #endregion

    #region Lunge

    /// <summary>
    /// 앞으로 전진. 플레이어 표면 앞까지로 거리를 제한한다.
    /// fitDuration이면 실제 이동 거리를 duration 동안 나눠서 이동 (강공격 달려들기용).
    /// </summary>
    private void StartLunge(float maxDistance, float duration, bool fitDuration)
    {
        if (maxDistance <= 0f)
        {
            return;
        }

        if (!fitDuration)
        {
            lungeDirection = Flat(rb.rotation * Vector3.forward).normalized;
        }
        if (lungeDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // 전진 거리는 공격 순간에 확정한다.
        // 플레이어가 가까우면 표면 앞까지만 전진하고, 이후 플레이어가 물러나도 더 따라가지 않는다.
        float distance = Mathf.Min(maxDistance, Mathf.Max(0f, GetLungeAllowance()));
        if (distance <= 0.0001f)
        {
            return;
        }

        lungeRemaining = distance;
        lungeSpeed = (fitDuration ? distance : maxDistance) / Mathf.Max(0.01f, duration);
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

    #region Animator

    private void CacheAnimatorParams()
    {
        animatorParams.Clear();
        if (animator == null || animator.runtimeAnimatorController == null)
        {
            return;
        }
        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            animatorParams.Add(p.nameHash);
        }
    }

    private void UpdateAnimatorSpeed()
    {
        if (animator == null || IsHitStopped)
        {
            return;
        }

        float value = 0f;
        if (state == State.Wander || state == State.Chase)
        {
            if (moveSpeed > 0.01f)
            {
                // 배회 속도 = 걷기 값, 추적 속도 = 달리기 값 (사이는 보간)
                float t = Mathf.InverseLerp(wanderSpeed, Mathf.Max(wanderSpeed + 0.01f, chaseSpeed), moveSpeed);
                value = moveSpeed <= wanderSpeed
                    ? walkAnimSpeedValue * moveSpeed / Mathf.Max(0.01f, wanderSpeed)
                    : Mathf.Lerp(walkAnimSpeedValue, runAnimSpeedValue, t);
            }
        }
        SetAnimFloat(SpeedHash, value, true);

        // 방향 이동 (원거리 병사용 2D 블렌드): 몸 기준 좌우(X)·앞뒤(Z) 속도 m/s
        Vector3 local = Vector3.zero;
        if ((state == State.Wander || state == State.Chase) && moveSpeed > 0.01f)
        {
            local = transform.InverseTransformDirection(moveDirection * moveSpeed);
        }
        SetAnimFloat(MoveXHash, local.x, true);
        SetAnimFloat(MoveZHash, local.z, true);
    }

    private void SetAnimFloat(int hash, float value, bool damp)
    {
        if (animator == null || !animatorParams.Contains(hash) || !animator.isActiveAndEnabled)
        {
            return;
        }
        if (damp)
        {
            animator.SetFloat(hash, value, speedDampTime, Time.deltaTime);
        }
        else
        {
            animator.SetFloat(hash, value);
        }
    }

    private void SetAnimBool(int hash, bool value)
    {
        if (animator != null && animatorParams.Contains(hash) && animator.isActiveAndEnabled)
        {
            animator.SetBool(hash, value);
        }
    }

    private void SetAnimTrigger(int hash)
    {
        if (animator != null && animatorParams.Contains(hash) && animator.isActiveAndEnabled)
        {
            animator.SetTrigger(hash);
        }
    }

    private void ResetAttackTriggers()
    {
        if (animator == null || !animator.isActiveAndEnabled)
        {
            return;
        }
        if (animatorParams.Contains(AttackHash)) animator.ResetTrigger(AttackHash);
        if (animatorParams.Contains(HeavyAttackHash)) animator.ResetTrigger(HeavyAttackHash);
        if (animatorParams.Contains(ParriedHash)) animator.ResetTrigger(ParriedHash);
        if (animatorParams.Contains(FireHash)) animator.ResetTrigger(FireHash);
        if (animatorParams.Contains(KickHash)) animator.ResetTrigger(KickHash);
    }

    #endregion

    #region Debug Keys (개발용, 0 키로 켰을 때만)

    // 다른 파트의 테스트 키(플레이어 1·2, 시간역행 테스터 3·4)는 코드를 고치지 않고
    // 개발용 표시가 켜져 있는 동안만 잠시 꺼 두고, 같은 기능을 여기서 그쪽 공개 함수로 호출한다. 끄면 원래대로 돌려놓는다.
    private static NGH_PlayerController debugPlayer;
    private static bool savedPlayerHitKeys;
    private static readonly List<NGH_TimeRewindTester> disabledTesters = new List<NGH_TimeRewindTester>();
    private static YPH_SteamTank debugSteamTank;
    private const float DebugSteamAmount = 20f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void HookDebugView()
    {
        chg_DebugView.Changed -= OnDebugViewChanged;
        chg_DebugView.Changed += OnDebugViewChanged;
    }

    private static void OnDebugViewChanged(bool on)
    {
        if (on)
        {
            debugPlayer = FindAnyObjectByType<NGH_PlayerController>();
            if (debugPlayer != null)
            {
                savedPlayerHitKeys = debugPlayer.debugHitKeys;
                debugPlayer.debugHitKeys = false;   // 1·2 가 두 번 들어가지 않게 (끄면 복구)
            }
            disabledTesters.Clear();
            foreach (NGH_TimeRewindTester tester in FindObjectsByType<NGH_TimeRewindTester>(FindObjectsSortMode.None))
            {
                if (tester.enabled)
                {
                    tester.enabled = false;         // 3·4 가 두 번 들어가지 않게 (끄면 복구)
                    disabledTesters.Add(tester);
                }
            }
            debugSteamTank = FindAnyObjectByType<YPH_SteamTank>();
        }
        else
        {
            if (debugPlayer != null)
            {
                debugPlayer.debugHitKeys = savedPlayerHitKeys;
            }
            foreach (NGH_TimeRewindTester tester in disabledTesters)
            {
                if (tester != null)
                {
                    tester.enabled = true;
                }
            }
            disabledTesters.Clear();
        }
    }

    private static bool Pressed(Keyboard kb, int number)
    {
        switch (number)
        {
            case 1: return kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame;
            case 2: return kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame;
            case 3: return kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame;
            case 4: return kb.digit4Key.wasPressedThisFrame || kb.numpad4Key.wasPressedThisFrame;
            case 5: return kb.digit5Key.wasPressedThisFrame || kb.numpad5Key.wasPressedThisFrame;
            case 6: return kb.digit6Key.wasPressedThisFrame || kb.numpad6Key.wasPressedThisFrame;
            case 7: return kb.digit7Key.wasPressedThisFrame || kb.numpad7Key.wasPressedThisFrame;
            default: return false;
        }
    }

    // 같은 프레임에 여러 적이 처리하지 않도록 한 번만 실행
    private void HandleDebugKeys()
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || debugKeyFrame == Time.frameCount)
        {
            return;
        }
        debugKeyFrame = Time.frameCount;

        // 1·2: 플레이어 경직 / 대경직 (NGH_PlayerController.TakeHit)
        if (Pressed(kb, 1) || Pressed(kb, 2))
        {
            if (debugPlayer == null)
            {
                debugPlayer = FindAnyObjectByType<NGH_PlayerController>();
            }
            if (debugPlayer != null)
            {
                int damage = Pressed(kb, 1) ? 1 : 2;
                Transform pt = debugPlayer.transform;
                debugPlayer.TakeHit(damage, pt.position + pt.forward);
                Debug.Log("[CHG 테스트] " + damage + " → 플레이어 " + (damage == 1 ? "경직" : "대경직") + " (피해 " + damage + ")", debugPlayer);
            }
            else
            {
                Debug.Log("[CHG 테스트] 플레이어(NGH_PlayerController)를 찾지 못했습니다.");
            }
        }

        // 3·4: 증기 소비 / 회복 (YPH_SteamTank)
        if (Pressed(kb, 3) || Pressed(kb, 4))
        {
            if (debugSteamTank == null)
            {
                debugSteamTank = FindAnyObjectByType<YPH_SteamTank>();
            }
            if (debugSteamTank != null)
            {
                if (Pressed(kb, 3))
                {
                    bool ok = debugSteamTank.TryConsume(DebugSteamAmount);
                    Debug.Log("[CHG 테스트] 3 → 증기 소비 " + DebugSteamAmount + (ok ? " 성공" : " 부족") + " (" + debugSteamTank.CurrentPressure.ToString("0.#") + "/" + debugSteamTank.MaxPressure.ToString("0") + ")", debugSteamTank);
                }
                else
                {
                    debugSteamTank.Refill(DebugSteamAmount);
                    Debug.Log("[CHG 테스트] 4 → 증기 회복 " + DebugSteamAmount + " (" + debugSteamTank.CurrentPressure.ToString("0.#") + "/" + debugSteamTank.MaxPressure.ToString("0") + ")", debugSteamTank);
                }
            }
            else
            {
                Debug.Log("[CHG 테스트] 증기통(YPH_SteamTank)을 찾지 못했습니다.");
            }
        }

        // 5·6·7: 대상 적 (락온 대상, 없으면 가장 가까운 적)
        bool parry = Pressed(kb, 5);
        bool weakness = Pressed(kb, 6);
        bool hpBar = Pressed(kb, 7);
        if (!(parry || weakness || hpBar))
        {
            return;
        }

        CHG_EnemyAI target = FindDebugTarget(out _);
        if (target == null)
        {
            Debug.Log("[CHG 테스트] 살아 있는 적이 없습니다.");
            return;
        }

        if (parry)
        {
            Debug.Log("[CHG 테스트] 5 → " + target.name + " 패링당함", target);
            target.ForceParried();
        }
        if (weakness && target.health != null)
        {
            Debug.Log("[CHG 테스트] 6 → " + target.name + " 약점 노출", target);
            target.health.ExposeWeakness();
        }
        if (hpBar)
        {
            chg_EnemyHpBar bar = target.GetComponent<chg_EnemyHpBar>();
            Debug.Log("[CHG 테스트] 7 → " + target.name + (bar != null ? " 체력바 표시" : " 체력바(chg_EnemyHpBar) 없음"), target);
            if (bar != null)
            {
                bar.Activate();
            }
        }
    }

    // 좌측 상단 개발용 설명 + 대상 정보 상자 — 살아 있는 적 중 한 마리만 그림
    private void OnGUI()
    {
        if (!chg_DebugView.Enabled || activeEnemies.Count == 0 || activeEnemies[0] != this)
        {
            return;
        }

        Rect help = chg_DebugView.DrawBox(
            "<b>개발용 표시 ON</b>  (0: 끄기)\n" +
            "<b>플레이어</b>\n" +
            "  <b>1</b>  경직 (피해 1)      <b>2</b>  대경직 (피해 2)\n" +
            "  <b>3</b>  증기 -" + DebugSteamAmount + "          <b>4</b>  증기 +" + DebugSteamAmount + "\n" +
            "  <b>Ctrl</b>  패링 (chg_PlayerParry 필요 · 증기 10 · 동심원이 겹칠 때)\n" +
            "<b>대상 적</b>  (락온 대상, 없으면 가장 가까운 적)\n" +
            "  <b>5</b>  패링당함 (비틀거림 + 약점 노출)\n" +
            "  <b>6</b>  약점 노출 (다음 공격 1회 3배)\n" +
            "  <b>7</b>  체력바 표시\n" +
            "\n" +
            "<b>바닥 원</b>  노랑 인지 범위(전투 중 주황) · 옅은 노랑 놓침 범위\n" +
            "    근접: 빨강 공격 사거리 · 자주 강공격 시작 거리\n" +
            "    원거리: 빨강 물러남 · 자주 킥 거리 · 초록 유지 거리 · 옅은 초록 사격 가능 거리\n" +
            "<b>선</b>  주황 추적 중인 플레이어 · 노랑/회색 원거리 시야(열림/막힘)\n" +
            "<b>빨간 상자</b>  공격 판정이 살아 있는 순간\n" +
            "<b>레이저</b>  빨강 조준 중 · 노랑 실제 발사 방향",
            new Vector2(0f, 0f));

        // 대상 정보 상자 (설명 상자 오른쪽)
        CHG_EnemyAI target = FindDebugTarget(out bool byLockOn);
        string info;
        if (target == null)
        {
            info = "<b>대상 적</b>\n  없음";
        }
        else
        {
            CHG_EnemyHealth h = target.health;
            info = "<b>대상 적</b>  (" + (byLockOn ? "락온" : "가장 가까움") + ")\n" +
                   "  이름   " + target.name + "\n" +
                   "  종류   " + (target.IsRanged ? "원거리" : "근접") + "\n" +
                   "  상태   " + StateLabel(target.state) + "\n" +
                   (h != null ? "  HP     " + Mathf.CeilToInt(h.CurrentHp) + " / " + Mathf.CeilToInt(h.MaxHp) + "\n" : "") +
                   "  약점   " + (h != null && h.IsWeaknessExposed ? "<color=#ffcc44>노출 중</color>" : "없음");
        }

        NGH_PlayerController pc = debugPlayer != null ? debugPlayer : FindAnyObjectByType<NGH_PlayerController>();
        if (pc != null)
        {
            info += "\n\n<b>플레이어</b>\n  HP     " + pc.Hp + " / " + pc.maxHp;
            if (debugSteamTank != null)
            {
                info += "\n  증기   " + debugSteamTank.CurrentPressure.ToString("0") + " / " + debugSteamTank.MaxPressure.ToString("0");
            }
        }

        chg_DebugView.DrawBox(info, new Vector2(help.xMax, 0f));
    }

    private static string StateLabel(State s)
    {
        switch (s)
        {
            case State.Wander: return "비전투";
            case State.Chase: return "추적";
            case State.Attack: return "공격";
            case State.Parried: return "패링당함";
            default: return s.ToString();
        }
    }

    // 테스트 대상: 플레이어가 락온한 적이면 그 적, 아니면 플레이어와 가장 가까운 살아 있는 적
    private CHG_EnemyAI FindDebugTarget(out bool byLockOn)
    {
        byLockOn = false;
        NGH_PlayerController pc = debugPlayer != null ? debugPlayer : FindAnyObjectByType<NGH_PlayerController>();
        if (pc != null && pc.LockTarget != null)
        {
            CHG_EnemyAI locked = pc.LockTarget.GetComponentInParent<CHG_EnemyAI>();
            if (locked != null && locked.enabled && (locked.health == null || locked.health.IsAlive))
            {
                byLockOn = true;
                return locked;
            }
        }

        Vector3 origin = player != null ? player.position
            : (Camera.main != null ? Camera.main.transform.position : transform.position);

        CHG_EnemyAI best = null;
        float bestDistance = float.MaxValue;
        foreach (CHG_EnemyAI enemy in activeEnemies)
        {
            if (enemy == null || !enemy.enabled || (enemy.health != null && !enemy.health.IsAlive))
            {
                continue;
            }
            float d = (enemy.transform.position - origin).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = enemy;
            }
        }
        return best;
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
            parryWindowOpen = false;
            isAiming = false;
            superArmor = false;
            SetAnimFloat(KickSpeedHash, 1f, false);
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
        if (!chg_DebugView.ShowGizmos)
        {
            return;
        }

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

        if (rangedAttack != null && rangedAttack.enabled)
        {
            // 원거리: 물러나는 거리(빨강), 유지 거리(초록), 사격 가능 거리(옅은 초록)
            Gizmos.color = Color.red;
            DrawCircle(center, rangedAttack.retreatDistance);
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 1f);
            DrawCircle(center, rangedAttack.preferredMaxDistance);
            Gizmos.color = new Color(0.2f, 0.9f, 0.3f, 0.35f);
            DrawCircle(center, rangedAttack.maxFireDistance);
        }
        else
        {
            // 공격 사거리 (빨강)
            Gizmos.color = Color.red;
            DrawCircle(center, attackRange);
        }

        // 강공격 시작 거리 (자주)
        if (heavyAttack != null && heavyAttack.enabled)
        {
            Gizmos.color = new Color(0.8f, 0.2f, 0.6f, 0.8f);
            DrawCircle(center, heavyAttack.startRange);
        }

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
        if (!chg_DebugView.ShowGizmos)
        {
            return;
        }

        if (rangedAttack != null && rangedAttack.enabled)
        {
            DrawRangedGizmos();
            return;
        }

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
        DrawHitboxGizmo(attackHitboxOffset, attackHitboxSize, new Color(1f, 0f, 0f, 0.3f), Color.red);

        // 강공격 히트박스 미리보기 (자주)
        if (heavyAttack != null && heavyAttack.enabled)
        {
            DrawHitboxGizmo(heavyAttack.hitboxOffset, heavyAttack.hitboxSize, new Color(0.8f, 0.2f, 0.6f, 0.25f), new Color(0.8f, 0.2f, 0.6f, 1f));
        }
    }

    // 원거리: 총구 위치(주황 구), 정면 사격 가능 거리(주황 선), 플레이 중엔 플레이어 조준선(시야 열림 노랑 / 막힘 회색)
    private void DrawRangedGizmos()
    {
        Vector3 from = GetFirePosition();
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 1f);
        Gizmos.DrawWireSphere(from, 0.06f);
        Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.5f);
        Gizmos.DrawLine(from, from + Flat(transform.forward).normalized * rangedAttack.maxFireDistance);

        if (rangedAttack.kickEnabled)
        {
            // 킥 판정 미리보기 (자주)
            DrawHitboxGizmo(rangedAttack.kickHitboxOffset, rangedAttack.kickHitboxSize, new Color(0.8f, 0.2f, 0.6f, 0.25f), new Color(0.8f, 0.2f, 0.6f, 1f));
        }

        if (Application.isPlaying && player != null)
        {
            Gizmos.color = hasLineOfSight ? new Color(1f, 0.9f, 0.2f, 0.8f) : new Color(0.5f, 0.5f, 0.5f, 0.8f);
            Gizmos.DrawLine(from, player.position + Vector3.up * rangedAttack.aimHeight);
        }
    }

    private void DrawHitboxGizmo(Vector3 offset, Vector3 size, Color fill, Color wire)
    {
        Gizmos.matrix = Matrix4x4.TRS(transform.TransformPoint(offset), transform.rotation, Vector3.one);
        Gizmos.color = fill;
        Gizmos.DrawCube(Vector3.zero, size);
        Gizmos.color = wire;
        Gizmos.DrawWireCube(Vector3.zero, size);
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
