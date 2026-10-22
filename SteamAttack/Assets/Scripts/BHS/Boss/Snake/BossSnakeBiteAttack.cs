using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// SnakeFSM.Patterns에 등록하는 물기 패턴.
/// IK 타겟을 잠시 제어하고, 실제 입의 구형 판정과 이동 경로로 한 번 피해를 줍니다.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(200)]
public class BossSnakeBiteAttack : BossAttack
{
    [Header("연결할 컴포넌트")]
    [Tooltip("평소 뱀의 IK 타겟을 구 표면으로 움직이는 기존 스크립트입니다.")]
    public SnakeSphereTarget targetController;
    [Tooltip("뱀에 사용 중인 Chain IK Constraint입니다. Target은 위 스크립트의 IK Target과 같아야 합니다.")]
    public ChainIKConstraint chainIK;
    [Tooltip("실제 입/머리 본에 새로 만든 물기 전용 SphereCollider입니다. IK Target에 붙이지 마세요. 실행 중에는 물리 충돌 대신 조회용 모양으로 사용합니다.")]
    public SphereCollider mouthCollider;

    [Header("물기 금지 영역 - 공격 범위와 독립")]
    [Tooltip("거북이 본체 또는 등껍질 중심의 Transform을 연결하세요. 이 중심을 따라 물기 금지 원이 이동합니다. 비우면 제한을 사용하지 않습니다.")]
    public Transform biteBlockOrigin;
    [Tooltip("등껍질 주변에서 물 수 없는 수평 월드 반경입니다. 거북이 공격이나 파동 크기와 무관합니다. 0이면 제한을 사용하지 않습니다.")]
    [Min(0f)] public float biteBlockRadius = 4f;

    [Header("물기 동작 - 월드 거리 / 초")]
    [Tooltip("Ready Time 동안 머리를 뒤로 당기는 거리입니다.")]
    [Min(0f)] public float pullbackDistance = 0.35f;
    [Min(0.01f)] public float extendTime = 0.2f;
    [Min(0.01f)] public float holdTime = 0.1f;
    [Min(0.01f)] public float returnTime = 0.35f;
    [Tooltip("플레이어 Transform에서 위로 더한 조준 높이입니다. 발밑이 피벗이면 몸통 높이에 맞추세요.")]
    public float targetHeightOffset = 1f;
    [Tooltip("예고 때 정한 조준점을 이 거리만큼 지나가도록 뻗습니다.")]
    [Min(0f)] public float extraDistance = 0.2f;
    [Tooltip("Chain IK 본 길이 합의 이 비율까지만 뻗습니다. 완전히 일자로 펴지는 것을 피합니다.")]
    [Range(0.5f, 0.99f)] public float reachRatio = 0.98f;

    [Header("확인용 기즈모")]
    public bool showBiteGizmos = true;
    [Tooltip("물기 금지 원을 그리는 월드 Y 좌표입니다. 실제 수평 판정에는 영향을 주지 않습니다.")]
    public float biteBlockGizmoY = 0f;

    // 이 패턴의 공격 시간은 뻗기 + 유지 + 복귀입니다. 공통 Attack Time은 사용하지 않습니다.
    protected override float ActiveDuration => lockedDuration;
    protected override bool ContinueWithoutTarget => true;

    private SnakeSphereTarget controlledDriver;
    private Transform controlledTarget;
    private Transform chainRoot;
    private Transform savedParent;
    private Vector3 savedLocalPosition;
    private Quaternion savedLocalRotation;
    private Vector3 readyStart;
    private Vector3 pulledBack;
    private Vector3 strikePoint;
    private Vector3 aimPoint;
    private Quaternion lockedRotation;
    private Vector3 previousMouthCenter;
    private float previousMouthRadius;
    private float maxReach;
    private float readyStartedAt;
    private float lockedReadyTime;
    private float lockedExtendTime;
    private float lockedHoldTime;
    private float lockedReturnTime;
    private float lockedDuration;
    private float elapsed;
    private bool driverWasEnabled;
    private bool controlsTarget;
    private bool activeStarted;
    private bool canDamage;

    private void Reset()
    {
        minUseDistance = 0f;
        maxUseDistance = 3f;
        readyTime = 0.6f;
        recoveryTime = 1f;
        damage = 2f;
    }

    private void Awake()
    {
        DisablePhysicalMouthCollider();
    }

    public override bool CanUse(float distance, Transform target)
    {
        // FSM이 패턴을 고를 때와 조준 중에 모두 검사한다.
        return base.CanUse(distance, target) && target != null &&
            !IsInsideBlockedArea(target.position);
    }

    private bool IsInsideBlockedArea(Vector3 position)
    {
        float radius = Mathf.Max(0f, biteBlockRadius);
        if (biteBlockOrigin == null || radius <= 0f) return false;
        Vector3 offset = position - biteBlockOrigin.position;
        offset.y = 0f;
        // 경계까지 금지. 보스 Scale과 플레이어 높이는 수평 월드 거리 판정에 영향을 주지 않는다.
        return offset.sqrMagnitude <= radius * radius;
    }

    protected override bool PrepareAttack()
    {
        if (Target == null || !Target.CompareTag("Player")) return false;
        if (IsInsideBlockedArea(Target.position)) return false;
        if (targetController == null || chainIK == null || mouthCollider == null)
            return SetupError("Target Controller, Chain IK, Mouth Collider를 모두 연결하세요.");
        if (!chainIK.isActiveAndEnabled || chainIK.weight <= 0f)
            return SetupError("뱀의 Chain IK를 활성화하고 Weight를 확인하세요.");

        ChainIKConstraintData data = chainIK.data;
        if (data.root == null || data.tip == null || data.target == null ||
            data.root == data.tip || !data.tip.IsChildOf(data.root))
            return SetupError("Chain IK의 Root / Tip / Target 연결을 확인하세요.");
        if (data.target != targetController.ikTarget)
            return SetupError("Chain IK의 Target과 SnakeSphereTarget의 IK Target이 같아야 합니다.");
        if (data.target == targetController.transform || !data.target.IsChildOf(targetController.transform))
            return SetupError("기존 구조처럼 IK Target을 SnakeSphereTarget 오브젝트의 자식으로 두세요.");
        if (!data.target.gameObject.activeInHierarchy || !mouthCollider.gameObject.activeInHierarchy ||
            !targetController.gameObject.activeInHierarchy)
            return SetupError("IK Target과 입 판정 오브젝트가 활성 상태여야 합니다.");
        if (!mouthCollider.transform.IsChildOf(data.tip) ||
            mouthCollider.transform.IsChildOf(data.target) ||
            mouthCollider == targetController.GetComponent<SphereCollider>())
            return SetupError("Mouth Collider는 실제 Chain IK Tip(머리/입 본)의 자식에 새로 만드세요. 타겟 이동 제한용 구를 연결하면 안 됩니다.");
        if (data.maintainTargetPositionOffset)
            return SetupError("Chain IK의 Maintain Target Offset에서 Position을 끄고 IK Target을 머리 끝에 맞춰 주세요.");
        if (data.chainRotationWeight <= 0f)
            return SetupError("Chain IK의 Chain Rotation Weight를 확인하세요.");

        float length = 0f;
        for (Transform bone = data.tip; bone != data.root; bone = bone.parent)
            length += Vector3.Distance(bone.position, bone.parent.position);
        maxReach = length * Mathf.Clamp(reachRatio, 0.5f, 0.99f);
        if (maxReach < 0.01f) return SetupError("뱀의 본 길이가 너무 짧습니다. Root와 Tip을 확인하세요.");

        controlledDriver = targetController;
        controlledTarget = data.target;
        chainRoot = data.root;
        savedParent = controlledTarget.parent;
        savedLocalPosition = controlledTarget.localPosition;
        savedLocalRotation = controlledTarget.localRotation;
        readyStart = controlledTarget.position;
        lockedRotation = controlledTarget.rotation;
        aimPoint = Target.position + Vector3.up * targetHeightOffset;

        Vector3 mouthCenter = GetMouthCenter();
        Vector3 direction = aimPoint - mouthCenter;
        if (direction.sqrMagnitude < 0.000001f) direction = Direction * Vector3.forward;
        direction.Normalize();
        pulledBack = ClampToReach(readyStart - direction * Mathf.Max(0f, pullbackDistance));
        // IK Target은 머리 끝의 목표점이므로 입 중심과 실제 Tip 사이의 차이를 보정합니다.
        Vector3 mouthOffset = mouthCenter - data.tip.position;
        strikePoint = ClampToReach(aimPoint - mouthOffset + direction * Mathf.Max(0f, extraDistance));

        lockedReadyTime = Mathf.Max(0f, readyTime);
        lockedExtendTime = Mathf.Max(0.01f, extendTime);
        lockedHoldTime = Mathf.Max(0.01f, holdTime);
        lockedReturnTime = Mathf.Max(0.01f, returnTime);
        lockedDuration = lockedExtendTime + lockedHoldTime + lockedReturnTime;
        readyStartedAt = Time.time;
        elapsed = 0f;
        activeStarted = false;
        canDamage = false;
        previousMouthCenter = mouthCenter;
        previousMouthRadius = GetMouthRadius();

        driverWasEnabled = controlledDriver.enabled;
        controlsTarget = true;
        controlledDriver.enabled = false;
        DisablePhysicalMouthCollider();
        return true;
    }

    private void Update()
    {
        if (!IsRunning || !controlsTarget) return;
        if (controlledTarget == null || chainRoot == null || mouthCollider == null ||
            controlledTarget.parent != savedParent)
        {
            CancelAttack();
            return;
        }

        if (!activeStarted)
        {
            // 예고 중 금지 영역으로 피하면 뻗기를 시작하지 않고 추적 상태로 복구한다.
            if (Target != null && IsInsideBlockedArea(Target.position))
            {
                CancelAttack();
                return;
            }
            float t = lockedReadyTime > 0f ? (Time.time - readyStartedAt) / lockedReadyTime : 1f;
            SetTargetPose(Vector3.Lerp(readyStart, pulledBack, Smooth(t)), lockedRotation);
            return;
        }

        if (elapsed <= lockedExtendTime)
            SetTargetPose(Vector3.Lerp(pulledBack, strikePoint,
                Smooth(elapsed / lockedExtendTime)), lockedRotation);
        else if (elapsed <= lockedExtendTime + lockedHoldTime)
            SetTargetPose(strikePoint, lockedRotation);
        else
        {
            float t = Smooth((elapsed - lockedExtendTime - lockedHoldTime) / lockedReturnTime);
            Vector3 restPosition = savedParent != null ?
                savedParent.TransformPoint(savedLocalPosition) : savedLocalPosition;
            Quaternion restRotation = savedParent != null ?
                savedParent.rotation * savedLocalRotation : savedLocalRotation;
            SetTargetPose(Vector3.Lerp(strikePoint, restPosition, t),
                Quaternion.Slerp(lockedRotation, restRotation, t));
        }
    }

    protected override void StartActive()
    {
        if (controlledTarget == null || chainRoot == null || mouthCollider == null)
        {
            CancelAttack();
            return;
        }
        // FSM의 TickAttack이 이 컴포넌트의 Update보다 먼저 호출되어도 차단한다.
        if (Target != null && IsInsideBlockedArea(Target.position))
        {
            CancelAttack();
            return;
        }
        // Ready Time이 0이어도 뒤로 순간이동하지 않고 현재 위치에서 뻗습니다.
        pulledBack = ClampToReach(controlledTarget.position);
        previousMouthCenter = GetMouthCenter();
        previousMouthRadius = GetMouthRadius();
        activeStarted = true;
        elapsed = 0f;
        canDamage = true;
    }

    protected override void TickActive(float deltaTime)
    {
        elapsed += deltaTime;
        // 뒤로 회수할 때는 피해를 주지 않습니다.
        canDamage = elapsed <= lockedExtendTime + lockedHoldTime;
    }

    // IK가 평가된 뒤 실제 입 위치를 읽습니다. Animator의 Update Mode는 Normal을 사용합니다.
    private void LateUpdate()
    {
        if (!IsRunning || !controlsTarget || mouthCollider == null) return;
        Vector3 center = GetMouthCenter();
        float radius = GetMouthRadius();
        Vector3 from = previousMouthCenter;
        float sweepRadius = Mathf.Max(radius, previousMouthRadius);
        previousMouthCenter = center;
        previousMouthRadius = radius;
        if (!canDamage || !CanHitCollider || IsInsideBlockedArea(Target.position)) return;

        Physics.SyncTransforms();
        // 시작점 겹침도 검사하므로 입 안에서 공격이 시작된 경우도 놓치지 않습니다.
        if (HitsPlayerAt(from, sweepRadius) || HitsPlayerAt(center, radius))
        {
            HitOnce(checkTargetHeight: false);
            return;
        }

        Vector3 travel = center - from;
        float distance = travel.magnitude;
        if (distance < 0.000001f) return;
        RaycastHit[] hits = Physics.SphereCastAll(from, sweepRadius, travel / distance,
            distance, ~0, QueryTriggerInteraction.Collide);
        foreach (RaycastHit hit in hits)
        {
            if (!IsPlayerCollider(hit.collider)) continue;
            HitOnce(checkTargetHeight: false);
            return;
        }
    }

    private bool HitsPlayerAt(Vector3 center, float radius)
    {
        Collider[] overlaps = Physics.OverlapSphere(center, radius, ~0, QueryTriggerInteraction.Collide);
        foreach (Collider other in overlaps)
            if (IsPlayerCollider(other)) return true;
        return false;
    }

    private bool IsPlayerCollider(Collider other)
    {
        return Target != null && other != null && other.enabled &&
            other.gameObject.activeInHierarchy &&
            (other.transform == Target || other.transform.IsChildOf(Target));
    }

    private void SetTargetPose(Vector3 position, Quaternion rotation)
    {
        controlledTarget.SetPositionAndRotation(ClampToReach(position), rotation);
    }

    private Vector3 ClampToReach(Vector3 position)
    {
        // 몸체가 회전/이동해도 현재 뿌리에서 본 길이 이상으로 타겟을 보내지 않습니다.
        Vector3 offset = position - chainRoot.position;
        if (offset.sqrMagnitude > maxReach * maxReach)
            position = chainRoot.position + offset.normalized * maxReach;
        return position;
    }

    protected override void EndAttack()
    {
        canDamage = false;
        activeStarted = false;
        DisablePhysicalMouthCollider();
        if (!controlsTarget) return;
        controlsTarget = false;

        if (controlledTarget != null && controlledTarget.parent == savedParent)
        {
            controlledTarget.localPosition = savedLocalPosition;
            controlledTarget.localRotation = savedLocalRotation;
        }
        if (controlledDriver != null) controlledDriver.enabled = driverWasEnabled;
        controlledTarget = null;
        controlledDriver = null;
        chainRoot = null;
    }

    private void DisablePhysicalMouthCollider()
    {
        if (mouthCollider == null || chainIK == null || chainIK.data.tip == null ||
            !mouthCollider.transform.IsChildOf(chainIK.data.tip)) return;
        // 콜라이더의 모양으로 직접 검사하므로 Rigidbody / OnTriggerEnter가 필요하지 않습니다.
        mouthCollider.isTrigger = true;
        mouthCollider.enabled = false;
    }

    private Vector3 GetMouthCenter() => mouthCollider.transform.TransformPoint(mouthCollider.center);

    private float GetMouthRadius()
    {
        Vector3 scale = mouthCollider.transform.lossyScale;
        float largest = Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        return Mathf.Max(0.001f, mouthCollider.radius * largest);
    }

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private bool SetupError(string message)
    {
        Debug.LogWarning("BossSnakeBiteAttack: " + message, this);
        return false;
    }

    private void OnDrawGizmosSelected()
    {
        if (!showBiteGizmos) return;
        DrawBlockedAreaGizmo();
        if (mouthCollider == null) return;
        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Gizmos.matrix = Matrix4x4.identity;
        Gizmos.color = canDamage ? Color.red : Color.yellow;
        Gizmos.DrawWireSphere(GetMouthCenter(), GetMouthRadius());
        if (IsRunning && controlsTarget)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(readyStart, pulledBack);
            Gizmos.DrawLine(pulledBack, strikePoint);
            Gizmos.DrawWireSphere(aimPoint, 0.12f);
            if (chainRoot != null) Gizmos.DrawLine(chainRoot.position, aimPoint);
        }
        Gizmos.color = previousColor;
        Gizmos.matrix = previousMatrix;
    }

    private void DrawBlockedAreaGizmo()
    {
#if UNITY_EDITOR
        float radius = Mathf.Max(0f, biteBlockRadius);
        if (biteBlockOrigin == null || radius <= 0f) return;
        Vector3 center = biteBlockOrigin.position;
        center.y = biteBlockGizmoY + 0.06f;
        Color previousColor = UnityEditor.Handles.color;
        Matrix4x4 previousMatrix = UnityEditor.Handles.matrix;
        var previousZTest = UnityEditor.Handles.zTest;
        try
        {
            UnityEditor.Handles.matrix = Matrix4x4.identity;
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            UnityEditor.Handles.color = new Color(1f, 0.5f, 0f, 0.08f);
            UnityEditor.Handles.DrawSolidDisc(center, Vector3.up, radius);
            UnityEditor.Handles.color = new Color(1f, 0.5f, 0f, 1f);
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, radius);
            UnityEditor.Handles.Label(center + Vector3.right * (radius + 0.15f),
                $"물기 금지 · 거북이 중심 {radius:0.##}m 이내");
        }
        finally
        {
            UnityEditor.Handles.color = previousColor;
            UnityEditor.Handles.matrix = previousMatrix;
            UnityEditor.Handles.zTest = previousZTest;
        }
#endif
    }
}
