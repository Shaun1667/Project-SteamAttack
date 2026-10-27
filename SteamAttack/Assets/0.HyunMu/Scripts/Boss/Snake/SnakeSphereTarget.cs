using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[DefaultExecutionOrder(100)]
public class SnakeSphereTarget : MonoBehaviour
{
    [Header("IK 타겟 연결")]
    public Transform ikTarget;

    [Header("플레이어 위치에서 추가할 높이")]
    public float playerHeightOffset = 0f;

    [Header("뱀의 기본 앞 방향 보정")]
    public float yawOffset = 0f;

    [Header("콜라이더 위아래 흔들림")]
    [Min(0f)]
    [Tooltip("기본 중심에서 위아래로 움직이는 최대 거리")]
    public float bobHeight = 0.15f;

    [Min(0f)]
    [Tooltip("1이면 1초에 한 번, 0.5면 2초에 한 번 왕복")]
    public float bobFrequency = 0.5f;

    [Header("Idle left-right sway (degrees)")]
    [Min(0f)] public float swayAngle = 5f;
    [Min(0f)] public float swayFrequency = 0.5f;
    private float swayPhase;

    private Transform player;
    private SphereCollider sphere;

    private Vector3 originalCenter;
    private float bobPhase;

    [Header("Breath mouth bones (local rotation offsets)")]
    public Transform mouthHead;
    public Transform jaw;
    public float headOpenX = -10f;
    public float jawOpenX = -60f;
    [Min(0.01f)] public float mouthOpenTime = 0.25f;
    [Min(0.01f)] public float mouthCloseTime = 0.25f;

    private Transform controlledMouthHead;
    private Transform controlledJaw;
    private Quaternion closedHeadRotation;
    private Quaternion closedJawRotation;
    private float mouthAmount;
    private bool mouthControlled;
    private bool mouthOpening;

    public bool IsAttackMouthOpen => mouthControlled && mouthAmount >= 1f;

    public void OpenAttackMouth()
    {
        if (!mouthControlled)
        {
            controlledMouthHead = mouthHead;
            controlledJaw = jaw;
            if (controlledMouthHead != null) closedHeadRotation = controlledMouthHead.localRotation;
            if (controlledJaw != null) closedJawRotation = controlledJaw.localRotation;
            mouthAmount = 0f;
            mouthControlled = true;
        }
        mouthOpening = true;
    }

    private void LateUpdate() => TickAttackMouth(Time.deltaTime);

    public void CloseAttackMouth() => mouthOpening = false;

    public void TickAttackMouth(float deltaTime)
    {
        if (!mouthControlled) return;
        float duration = mouthOpening ? mouthOpenTime : mouthCloseTime;
        mouthAmount = Mathf.MoveTowards(mouthAmount, mouthOpening ? 1f : 0f,
            Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, duration));
        float amount = Mathf.SmoothStep(0f, 1f, mouthAmount);
        if (controlledMouthHead != null)
            controlledMouthHead.localRotation = closedHeadRotation * Quaternion.Euler(headOpenX * amount, 0f, 0f);
        if (controlledJaw != null)
            controlledJaw.localRotation = closedJawRotation * Quaternion.Euler(jawOpenX * amount, 0f, 0f);
        if (!mouthOpening && mouthAmount <= 0f) mouthControlled = false;
    }

    public void RestoreBreathMouth()
    {
        if (!mouthControlled) return;
        if (controlledMouthHead != null) controlledMouthHead.localRotation = closedHeadRotation;
        if (controlledJaw != null) controlledJaw.localRotation = closedJawRotation;
        mouthControlled = false;
        mouthOpening = false;
        mouthAmount = 0f;
    }

    [Min(0.01f)] public float breathReturnTime = 0.5f;
    private bool breathReturning;
    private float breathReturnElapsed;
    private Vector3 breathReturnStart;
    private Quaternion breathReturnRotation;

    private bool breathSweepActive;
    private bool breathSweepComplete;
    private Vector3 breathApproachStart;
    private Vector3 breathStart;
    private Vector3[] breathPath;
    private float[] breathPathDistances;
    private float breathPathLength;
    private Vector3 breathAimPoint;
    private float breathElapsed;
    private float breathApproachTime;
    private float breathSweepTime;

    public bool IsBreathSweeping => breathSweepActive && !breathSweepComplete && breathElapsed >= breathApproachTime;
    public Vector3 BreathAimPoint => breathAimPoint;

    public bool IsBreathSweepComplete => breathSweepActive && breathSweepComplete;

    public bool BeginBreathSweep(Vector3 start, Vector3 end, float approachTime, float sweepTime)
        => BeginBreathSweep(new Vector3[] { start, end }, approachTime, sweepTime);

    public bool BeginBreathSweep(Vector3[] path, float approachTime, float sweepTime)
    {
        if (path == null || path.Length < 2) return false;
        if (!isActiveAndEnabled || ikTarget == null || ikTarget == transform ||
            !ikTarget.IsChildOf(transform)) return false;
        breathReturning = false;
        breathApproachStart = ikTarget.position;
        breathPath = (Vector3[])path.Clone();
        breathPathDistances = new float[path.Length];
        breathPathLength = 0f;
        for (int i = 1; i < path.Length; i++)
        {
            breathPathLength += Vector3.Distance(path[i - 1], path[i]);
            breathPathDistances[i] = breathPathLength;
        }
        breathStart = path[0];
        breathApproachTime = Mathf.Max(0f, approachTime);
        breathSweepTime = Mathf.Max(0.01f, sweepTime);
        breathElapsed = 0f;
        breathAimPoint = breathApproachStart;
        breathSweepComplete = false;
        breathSweepActive = true;
        OpenAttackMouth();
        return true;
    }

    public void EndBreathSweep()
    {
        if (breathSweepActive && ikTarget != null)
        {
            breathReturning = true;
            breathReturnElapsed = 0f;
            breathReturnStart = ikTarget.position;
            breathReturnRotation = transform.rotation;
        }
        mouthOpening = false;
        breathSweepActive = false;
        breathSweepComplete = false;
    }

    private void UpdateBreathSweep()
    {
        if (!breathSweepActive) return;
        breathElapsed += Time.deltaTime;
        if (breathElapsed < breathApproachTime)
        {
            float t = Mathf.SmoothStep(0f, 1f, breathElapsed / breathApproachTime);
            breathAimPoint = Vector3.Lerp(breathApproachStart, breathStart, t);
            return;
        }
        float sweep = Mathf.Clamp01((breathElapsed - breathApproachTime) / breathSweepTime);
        // Constant progress along each decal segment, without cutting across the whole fan.
        float distance = sweep * breathPathLength;
        breathAimPoint = breathPath[breathPath.Length - 1];
        for (int i = 1; i < breathPath.Length; i++)
        {
            if (distance > breathPathDistances[i]) continue;
            float length = breathPathDistances[i] - breathPathDistances[i - 1];
            float t = length > 0.00001f ? (distance - breathPathDistances[i - 1]) / length : 1f;
            breathAimPoint = Vector3.Lerp(breathPath[i - 1], breathPath[i], t);
            break;
        }
        breathSweepComplete = sweep >= 1f;
    }

    private void Awake()
    {
        sphere = GetComponent<SphereCollider>();
        originalCenter = sphere.center;
        sphere.isTrigger = true;

        // 시작할 때 Player 태그로 자동 검색.
        GameObject playerObject = GameObject.FindWithTag("Player");

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
        else
        {
            Debug.LogWarning(
                "Player 태그를 찾지 못했습니다. 콜라이더 흔들림만 실행합니다.",
                this);
        }

        if (ikTarget == null)
        {
            Debug.LogWarning("IK Target을 연결해 주세요.", this);
        }
        else if (ikTarget == transform ||
                 !ikTarget.IsChildOf(transform))
        {
            Debug.LogError(
                "IK Target은 이 SnakePivot 아래 자식이어야 합니다.",
                this);
        }
    }

    private void Update()
    {
        UpdateBreathSweep();
        // 1. 콜라이더 흔들림은 항상 먼저 실행.
        bobPhase = Mathf.Repeat(
            bobPhase + Time.deltaTime * bobFrequency * Mathf.PI * 2f,
            Mathf.PI * 2f);

        float offsetY = Mathf.Sin(bobPhase) * bobHeight;

        // Transform 위치는 건드리지 않고 콜라이더 중심만 이동.
        sphere.center = originalCenter + Vector3.up * offsetY;

        // 연결이 없어도 위의 흔들림은 계속 동작.
        if ((!breathSweepActive && player == null) || ikTarget == null)
            return;

        if (ikTarget == transform ||
            !ikTarget.IsChildOf(transform))
            return;

        // 2. 플레이어 방향으로 뱀 전체를 Y축 회전.
        Vector3 playerPoint =
            breathSweepActive ? breathAimPoint : player.position + Vector3.up * playerHeightOffset;

        Vector3 center =
            transform.TransformPoint(sphere.center);

        float returnBlend = 1f;
        if (breathReturning)
        {
            breathReturnElapsed += Time.deltaTime;
            returnBlend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(breathReturnElapsed / Mathf.Max(0.01f, breathReturnTime)));
        }

        if (!breathSweepActive)
            swayPhase = Mathf.Repeat(swayPhase + Time.deltaTime * Mathf.Max(0f, swayFrequency) * Mathf.PI * 2f, Mathf.PI * 2f);
        float sway = breathSweepActive ? 0f : Mathf.Sin(swayPhase) * Mathf.Max(0f, swayAngle);
        Quaternion swayRotation = Quaternion.Euler(0f, sway, 0f);
        Vector3 horizontalDirection = swayRotation * (playerPoint - center);
        horizontalDirection.y = 0f;

        if (horizontalDirection.sqrMagnitude > 0.000001f)
        {
            float yaw = Mathf.Atan2(
                horizontalDirection.x,
                horizontalDirection.z) * Mathf.Rad2Deg;

            Quaternion facing = Quaternion.Euler(0f, yaw + yawOffset, 0f);
            transform.rotation = breathReturning ? Quaternion.Slerp(breathReturnRotation, facing, returnBlend) : facing;
        }

        // 3. 움직인 구의 표면에 IK 타겟 배치.
        center = transform.TransformPoint(sphere.center);

        Vector3 direction = swayRotation * (playerPoint - center);

        if (direction.sqrMagnitude < 0.000001f)
            return;

        Vector3 scale = transform.lossyScale;

        float largestScale = Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        float worldRadius = sphere.radius * largestScale;

        Vector3 followPosition = center + direction.normalized * worldRadius;
        ikTarget.position = breathReturning ? Vector3.Lerp(breathReturnStart, followPosition, returnBlend) : followPosition;
        if (breathReturning && returnBlend >= 1f) breathReturning = false;
    }

    private void OnDisable()
    {
        EndBreathSweep();
        breathReturning = false;
        RestoreBreathMouth();
        // 껐을 때 콜라이더 중심 복구.
        if (sphere != null)
            sphere.center = originalCenter;

        bobPhase = 0f;
        swayPhase = 0f;
    }
}