using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(CharacterController))]
public class BossDashAttack : BossAttack
{
    [Header("현무 Animator - Bool 이름은 IsRushing")]
    public Animator animator;

    [Header("기존 HeadTa를 움직이는 목 추적 스크립트")]
    public TurtleHeadSphereTarget headTarget;

    [Header("보스 앞쪽의 공격용 BoxCollider 연결")]
    public BoxCollider attackCollider;

    [Header("돌진 이동")]
    [Min(0.01f)] public float speed = 12f;
    [Min(0f)] public float extraDistance = 2f;
    [Tooltip("출발 후 이동하면서 목표 방향으로 돌아가는 시간입니다. 출발 순간의 플레이어 위치를 향해 회전한 뒤 직진합니다.")]
    [Min(0.01f)] public float turnDuration = 0.35f;

    private CharacterController controller;
    private Collider[] playerColliders;
    private Vector3 forward;
    private float dashSpeed;
    private Vector3 dashAimPoint;
    private float dashStartYaw;
    private float dashTargetYaw;
    private float turnElapsed;
    private float lockedTurnDuration;
    private float lockedExtraDistance;
    private bool turnComplete;
    private float remainingDistance;
    private bool controlsAnimation;

    private static readonly int IsRushingHash =
        Animator.StringToHash("IsRushing");

    private readonly List<(Collider boss, Collider player)> ignoredPairs =
        new List<(Collider boss, Collider player)>();

    // 회전 경로까지 이동한 뒤 남은 직선 거리를 소진하면 직접 종료한다.
    protected override float ActiveDuration => float.PositiveInfinity;
    protected override bool ContinueWithoutTarget => true;

    private void Awake()
    {
        if (attackCollider == null) return;
        attackCollider.isTrigger = true;
        attackCollider.enabled = false;
    }

    private void Reset()
    {
        minUseDistance = 5f;
        maxUseDistance = 20f;
        readyTime = 0.3f;
        recoveryTime = 1f;
        damage = 2f;
    }

    public override bool CanUse(float distance)
    {
        return base.CanUse(distance);
    }


    protected override bool PrepareAttack()
    {
        if (headTarget == null || !headTarget.isActiveAndEnabled)
        {
            Debug.LogWarning("활성 TurtleHeadSphereTarget을 연결해 주세요.", this);
            return false;
        }

        if (animator == null || !animator.isActiveAndEnabled ||
            animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("현무의 활성 Animator와 Controller를 연결해 주세요.", this);
            return false;
        }

        controller = Owner.GetComponent<CharacterController>();
        if (controller == null || !controller.enabled)
        {
            Debug.LogWarning("보스 루트에 활성 CharacterController가 필요합니다.", this);
            return false;
        }

        if (attackCollider == null)
        {
            Debug.LogWarning("Attack Collider에 앞쪽 공격용 BoxCollider를 연결해 주세요.", this);
            return false;
        }

        if (attackCollider.transform != Owner &&
            !attackCollider.transform.IsChildOf(Owner))
        {
            Debug.LogWarning("Attack Collider는 보스 또는 보스 자식에 있어야 합니다.", this);
            return false;
        }

        if (!attackCollider.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("공격용 오브젝트는 켜 두세요. Collider만 코드에서 켜고 끕니다.", this);
            return false;
        }

        if (!Target.CompareTag("Player")) return false;
        playerColliders = Target.GetComponentsInChildren<Collider>(true);
        if (playerColliders.Length == 0)
        {
            Debug.LogWarning("Player 또는 그 자식에 CharacterController나 Collider가 필요합니다.", this);
            return false;
        }

        attackCollider.isTrigger = true;
        attackCollider.enabled = false;

        // Ready Time에는 목 정렬 시간과 RushBe 재생 시간을 모두 포함한다.
        if (!headTarget.BeginRush(PlayRushAnimation)) return false;
        controlsAnimation = true;
        return true;
    }

    private void PlayRushAnimation()
    {
        if (IsRunning && controlsAnimation && animator != null)
            animator.SetBool(IsRushingHash, true);
    }

    protected override void StartActive()
    {
        if (headTarget == null || !headTarget.isActiveAndEnabled ||
            !headTarget.IsAlignedForRush)
        {
            Debug.LogWarning("목 정렬이 끝나지 않았습니다. Ready Time과 목 연결을 확인하세요.", this);
            CancelAttack();
            return;
        }

        if (controller == null || !controller.enabled || attackCollider == null ||
            !attackCollider.gameObject.activeInHierarchy ||
            Target == null || !Target.gameObject.activeInHierarchy)
        {
            CancelAttack();
            return;
        }

        // 출발 시점의 목표 위치만 저장한다. 이후 플레이어가 움직여도 다시 추적하지 않는다.
        dashAimPoint = Target.position;
        Vector3 offset = dashAimPoint - Owner.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        forward = Vector3.ProjectOnPlane(Owner.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;

        // 현재 바라보는 방향으로 먼저 출발하고, 이동 중에 회전한다.
        Direction = Quaternion.LookRotation(forward, Vector3.up);
        dashStartYaw = Direction.eulerAngles.y;
        float aimYaw = distance > 0.0001f
            ? Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg : dashStartYaw;
        dashTargetYaw = dashStartYaw + Mathf.DeltaAngle(dashStartYaw, aimYaw);
        turnElapsed = 0f;
        lockedTurnDuration = Mathf.Max(0.01f, turnDuration);
        lockedExtraDistance = Mathf.Max(0f, extraDistance);
        turnComplete = distance <= 0.0001f ||
            Mathf.Abs(dashTargetYaw - dashStartYaw) <= 0.01f;
        dashSpeed = Mathf.Max(0.01f, speed);
        remainingDistance = distance + lockedExtraDistance;

        IgnorePlayerCollision();
        attackCollider.enabled = true;
    }

    protected override void TickActive(float deltaTime)
    {
        if (controller == null || !controller.enabled || attackCollider == null)
        {
            CancelAttack();
            return;
        }

        // 회전 중 공격 콜라이더도 함께 움직이므로 짧은 구간으로 나눠 이동/충돌 검사한다.
        int steps = Mathf.Clamp(Mathf.CeilToInt(deltaTime * 60f), 1, 32);
        float stepTime = deltaTime / steps;
        for (int i = 0; i < steps && IsRunning; i++)
        {
            Vector3 before = BoxCenter();
            Quaternion rotation = attackCollider.transform.rotation;
            Vector3 halfSize = BoxHalfSize();

            TurnDuringDash(stepTime);
            Owner.rotation = Direction;
            forward = Direction * Vector3.forward;
            float step = dashSpeed * stepTime;
            if (turnComplete) step = Mathf.Min(step, remainingDistance);
            if (step > 0f) controller.Move(forward * step);
            if (!IsRunning) return;
            if (turnComplete)
                remainingDistance = Mathf.Max(0f, remainingDistance - step);

            if (CanHitCollider && IsEnabled(attackCollider))
            {
                Physics.SyncTransforms();
                if (TouchesPlayer(before, BoxCenter(), halfSize, rotation))
                {
                    Debug.Log($"[돌진 명중] {Target.name} / 피해 {damage}", this);
                    attackCollider.enabled = false;
                    HitOnce(checkTargetHeight: false);
                }
            }

            // 맞힌 뒤에도 이동은 계속하고, 회전 종료 후 목표 지점 너머까지 진행한다.
            if (IsRunning && turnComplete && remainingDistance <= 0.00001f)
                CancelAttack();
        }
    }

    private void TurnDuringDash(float deltaTime)
    {
        if (turnComplete) return;
        turnElapsed = Mathf.Min(lockedTurnDuration, turnElapsed + deltaTime);
        Vector3 offset = dashAimPoint - Owner.position;
        offset.y = 0f;
        if (offset.sqrMagnitude > 0.000001f)
        {
            float aimYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            // 180도 경계를 넘어도 회전 방향이 갑자기 반대로 바뀌지 않도록 누적한다.
            dashTargetYaw += Mathf.DeltaAngle(dashTargetYaw, aimYaw);
        }
        float progress = Mathf.SmoothStep(0f, 1f, turnElapsed / lockedTurnDuration);
        Direction = Quaternion.Euler(0f,
            Mathf.Lerp(dashStartYaw, dashTargetYaw, progress), 0f);
        if (turnElapsed < lockedTurnDuration) return;

        turnComplete = true;
        // 곡선으로 돌아온 거리 때문에 정지한 플레이어 앞에서 멈추지 않도록 다시 잰다.
        remainingDistance = offset.magnitude + lockedExtraDistance;
    }

    private Vector3 BoxCenter()
    {
        return attackCollider.transform.TransformPoint(attackCollider.center);
    }

    private Vector3 BoxHalfSize()
    {
        Vector3 scale = attackCollider.transform.lossyScale;
        Vector3 size = attackCollider.size;
        return new Vector3(
            Mathf.Abs(size.x * scale.x),
            Mathf.Abs(size.y * scale.y),
            Mathf.Abs(size.z * scale.z)) * 0.5f;
    }

    private bool TouchesPlayer(Vector3 from, Vector3 to,
        Vector3 halfSize, Quaternion rotation)
    {
        // 출발부터 이미 겹친 경우도 검사한다.
        foreach (Collider other in Physics.OverlapBox(
            from, halfSize, rotation, ~0, QueryTriggerInteraction.Collide))
        {
            if (IsPlayer(other)) return true;
        }

        // 빠른 돌진으로 한 프레임 사이에 플레이어를 지나쳐도 검사한다.
        Vector3 movement = to - from;
        float distance = movement.magnitude;
        if (distance > 0.00001f)
        {
            foreach (RaycastHit hit in Physics.BoxCastAll(
                from, halfSize, movement / distance, rotation,
                distance, ~0, QueryTriggerInteraction.Collide))
            {
                if (IsPlayer(hit.collider)) return true;
            }
        }

        foreach (Collider other in Physics.OverlapBox(
            to, halfSize, attackCollider.transform.rotation,
            ~0, QueryTriggerInteraction.Collide))
        {
            if (IsPlayer(other)) return true;
        }
        return false;
    }

    private bool IsPlayer(Collider other)
    {
        return IsEnabled(other) && Target != null &&
            (other.transform == Target || other.transform.IsChildOf(Target));
    }

    private void IgnorePlayerCollision()
    {
        Collider[] bossColliders = Owner.GetComponentsInChildren<Collider>(true);
        foreach (Collider own in bossColliders)
        {
            if (!IsEnabled(own) || own.isTrigger) continue;
            foreach (Collider other in playerColliders)
            {
                if (!IsEnabled(other) || other.isTrigger || own == other ||
                    Physics.GetIgnoreCollision(own, other)) continue;

                Physics.IgnoreCollision(own, other, true);
                ignoredPairs.Add((own, other));
            }
        }
    }

    protected override void EndAttack()
    {
        // 돌진 종료: RushAf 재생 후, Recovery Time의 마지막 구간에 목 추적 복귀.
        // BeginAttack도 처음에 EndAttack을 호출하므로 이번 공격이 켠 Bool만 끈다.
        if (controlsAnimation)
        {
            controlsAnimation = false;
            if (animator != null)
                animator.SetBool(IsRushingHash, false);

            if (headTarget != null)
                headTarget.EndRush(recoveryTime);
        }

        if (attackCollider != null) attackCollider.enabled = false;
        foreach (var pair in ignoredPairs)
        {
            if (pair.boss != null && pair.player != null)
                Physics.IgnoreCollision(pair.boss, pair.player, false);
        }
        ignoredPairs.Clear();
    }

    private static bool IsEnabled(Collider value)
    {
        return value != null && value.enabled && value.gameObject.activeInHierarchy;
    }

    private void OnDestroy()
    {
        EndAttack();
    }

    private void OnDrawGizmosSelected()
    {
        if (attackCollider == null) return;
        Matrix4x4 oldMatrix = Gizmos.matrix;
        Color oldColor = Gizmos.color;
        Gizmos.matrix = Matrix4x4.TRS(
            BoxCenter(), attackCollider.transform.rotation, Vector3.one);
        Gizmos.color = Color.red;
        Gizmos.DrawWireCube(Vector3.zero, BoxHalfSize() * 2f);
        Gizmos.matrix = oldMatrix;
        Gizmos.color = oldColor;
    }
}
