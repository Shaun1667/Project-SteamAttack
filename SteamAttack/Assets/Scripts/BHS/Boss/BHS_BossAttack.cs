using System;
using UnityEngine;

// 공격 공통 실행부. 스스로 플레이어를 찾거나 다음 공격을 선택하지 않는다.
public abstract class BossAttack : MonoBehaviour
{
    public enum Phase { Finished, Ready, Active }

    [Header("이 패턴을 선택할 거리 - 수평 월드 거리")]
    [Min(0f)] public float minUseDistance = 3f;
    [Min(0f)] public float maxUseDistance = 5f;

    [Header("공격 시간")]
    [Min(0f)] public float readyTime = 1.5f;
    [Tooltip("근거리/브레스의 지속 시간. 돌진은 이동거리와 속도로 자동 계산합니다.")]
    [Min(0.01f)] public float attackTime = 1f;
    [Min(0f)] public float recoveryTime = 2f;

    [Header("공격 효과 / 피해")]
    public ParticleSystem attackEffect;
    [Min(0f)] public float damage = 2f;
    [SerializeField] private Phase currentPhase;

    public bool IsRunning => currentPhase != Phase.Finished;
    // The body can finish its action while an already launched effect continues.
    public virtual bool IsActionComplete => !IsRunning;
    protected virtual bool ControlsOwnerRotation => true;

    // Only a normal action completion may detach its remaining effect.
    // Cancellation, disabling and destruction still use CancelAttack.
    public virtual void ReleaseControl() => CancelAttack();
    protected Transform Owner { get; private set; }
    protected Transform Target { get; private set; }
    protected Action<float> DamageReporter => reportHit;
    protected Vector3 Origin { get; private set; }
    protected Quaternion Direction { get; set; }

    // 돌진처럼 거리에 따라 시간이 달라지는 패턴은 이 값을 재정의한다.
    protected virtual float ActiveDuration => attackTime;
    protected virtual bool ContinueWithoutTarget => false;

    private float timer;
    private float heightTolerance;
    private bool hasHit;
    private Action<float> reportHit;

    // 실제 Collider 판정은 콜라이더의 높이를 사용하므로 피벗 높이로 제한하지 않는다.
    protected bool CanHitCollider => currentPhase == Phase.Active && !hasHit &&
        Target != null && Target.gameObject.activeInHierarchy;

    protected bool CanHit => CanHitCollider &&
        Mathf.Abs(Target.position.y - Origin.y) <= heightTolerance;

    // 공격 선택 조건. 피해 판정은 각 공격 스크립트가 별도로 담당한다.
    public virtual bool CanUse(float distance)
    {
        float minimum = Mathf.Max(0f, minUseDistance);
        float maximum = Mathf.Max(minimum, maxUseDistance);
        return isActiveAndEnabled && distance >= minimum && distance <= maximum;
    }

    // 위치 조건이 필요한 패턴용. 기존 거리 판정과 거리 기즈모는 그대로 사용한다.
    public virtual bool CanUse(float distance, Transform target)
    {
        return CanUse(distance);
    }

    // BossFSM에서만 호출한다. 예고 시작 순간의 위치와 방향을 저장한다.
    public bool BeginAttack(Transform owner, Transform target,
        float allowedHeight, Action<float> onHit)
    {
        CancelAttack();
        if (!isActiveAndEnabled || owner == null || target == null) return false;

        Owner = owner;
        Target = target;
        Origin = owner.position;
        Direction = owner.rotation;
        heightTolerance = Mathf.Max(0f, allowedHeight);
        reportHit = onHit;
        hasHit = false;
        timer = 0f;
        currentPhase = Phase.Ready;

        if (PrepareAttack()) return true;
        CancelAttack();
        return false;
    }

    // FSM advances the owned action; a detached effect may advance its own remaining lifetime.
    public void TickAttack(float deltaTime)
    {
        if (!IsRunning) return;
        bool targetMissing = Target == null || !Target.gameObject.activeInHierarchy;
        if (!isActiveAndEnabled || Owner == null ||
            (targetMissing && (currentPhase == Phase.Ready || !ContinueWithoutTarget)))
        {
            CancelAttack();
            return;
        }

        if (ControlsOwnerRotation) Owner.rotation = Direction;
        deltaTime = Mathf.Max(0f, deltaTime);

        if (currentPhase == Phase.Ready)
        {
            timer += deltaTime;
            if (timer < readyTime) return;
            // 패턴에 따라 예고 이후 애니메이션이 끝날 때까지 판정 시작을 미룬다.
            if (WaitBeforeActive(deltaTime) || !IsRunning) return;

            timer = 0f;
            currentPhase = Phase.Active;
            StartActive();
            if (!IsRunning) return;
            if (attackEffect != null) attackEffect.Play(true);
            TickActive(0f);
            return;
        }

        float duration = Mathf.Max(0.01f, ActiveDuration);
        float step = Mathf.Min(deltaTime, Mathf.Max(0f, duration - timer));
        timer += step;
        TickActive(step);
        if (IsRunning && timer >= duration) CancelAttack();
    }

    protected void HitOnce(bool checkTargetHeight = true)
    {
        HitOnce(damage, checkTargetHeight);
    }

    // 본체 공격과 파동처럼 서로 다른 피해량을 사용하는 단계용.
    protected void HitOnce(float amount, bool checkTargetHeight = true)
    {
        if (checkTargetHeight ? !CanHit : !CanHitCollider) return;
        // 여러 콜라이더가 겹쳐도 같은 피해 단계에서는 한 번만 피해를 준다.
        hasHit = true;
        reportHit?.Invoke(Mathf.Max(0f, amount));
    }

    // 다음 피해 단계가 시작될 때 한 번만 호출한다. 이전 단계의 판정은 먼저 종료해야 한다.
    protected void BeginNextHitPhase()
    {
        if (currentPhase == Phase.Active) hasHit = false;
    }

    public void CancelAttack()
    {
        currentPhase = Phase.Finished;
        EndAttack();
        if (attackEffect != null)
            attackEffect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        reportHit = null;
    }

    protected abstract bool PrepareAttack();
    protected abstract void TickActive(float deltaTime);
    protected virtual bool WaitBeforeActive(float deltaTime) => false;
    protected virtual void StartActive() { }
    protected virtual void EndAttack() { }
    protected virtual void OnDisable() => CancelAttack();

}
