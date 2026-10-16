using UnityEngine;

namespace YPH
{
    /// <summary>이동 명령을 받아 FixedUpdate에서만 속도·회전을 적용합니다. 전진 거리 계산도 이곳에서 공유합니다.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class EnemyMotor : MonoBehaviour
    {
        [SerializeField, Tooltip("초당 회전 각도입니다. 높이면 목표 방향을 빨리 바라봅니다.")] private float _turnSpeed = 540f;
        [SerializeField, Min(0f), Tooltip("공격 전진 중 플레이어 표면과 남길 간격(m)입니다. 높이면 더 멀리서 멈춥니다.")] private float _lungeStopGap = 0.1f;
        private Rigidbody _body;
        private Collider _bodyCollider;
        private EnemyAI _brain;
        private Vector3 _lungeDirection;
        private float _lungeRemaining, _lungeSpeed;
        public bool IsLunging { get; private set; }
        internal Vector3 Direction { get; private set; }
        internal float Speed { get; private set; }
        internal Vector3 Facing { get; private set; }

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _bodyCollider = GetComponent<Collider>();
            _brain = GetComponent<EnemyAI>();
            _body.constraints = RigidbodyConstraints.FreezeRotation;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            Facing = transform.forward;
        }

        /// <summary>이동 의도만 저장합니다. 공격 전진은 이 명령보다 우선합니다.</summary>
        public void Move(Vector3 direction, float speed)
        {
            Direction = direction;
            Speed = speed;
        }

        /// <summary>바라볼 방향을 저장하며 회전 속도 제한은 물리 프레임에서 적용합니다.</summary>
        public void Face(Vector3 direction) => Facing = direction;

        /// <summary>모든 전진은 플레이어 앞에서 멈춥니다. stopAtPlayer가 true인 강공격은 짧아진 거리에도 duration 전체를 사용합니다.</summary>
        public void StartLunge(float distance, float duration, bool stopAtPlayer)
        {
            if (distance <= 0f)
            {
                return;
            }
            _lungeDirection = stopAtPlayer && Facing.sqrMagnitude > 0.0001f
                ? Facing : EnemyAI.Flat(_body.rotation * Vector3.forward).normalized;
            if (_lungeDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }
            // 시작 때 정한 거리를 늘리지 않아 플레이어가 물러나도 추가 추적하지 않습니다.
            float allowed = Mathf.Min(distance, Mathf.Max(0f, GetLungeAllowance()));
            if (allowed <= 0.0001f)
            {
                return;
            }
            _lungeRemaining = allowed;
            _lungeSpeed = (stopAtPlayer ? allowed : distance) / Mathf.Max(0.01f, duration);
            IsLunging = true;
        }

        /// <summary>이동·전진 명령을 지웁니다. 사망으로 Rigidbody가 꺼져도 안전하게 호출할 수 있습니다.</summary>
        public void Stop()
        {
            Move(Vector3.zero, 0f);
            IsLunging = false;
            _lungeRemaining = 0f;
        }

        internal void ReturnHome(Vector3 position)
        {
            // 부활은 이동 적분이 아니라 위치 복구이므로 물리 프레임을 기다리지 않고 순간 이동합니다.
            transform.position = position;
            _body.position = position;
        }

        private void FixedUpdate()
        {
            if (_body.isKinematic)
            {
                return;
            }
            bool stopped = _brain == null || !_brain.isActiveAndEnabled || _brain.IsHitStopped || _brain.IsParryStunned;
            Vector3 velocity = stopped ? Vector3.zero : IsLunging ? GetLungeVelocity() : Direction * Speed;
            _body.linearVelocity = new Vector3(velocity.x, _body.linearVelocity.y, velocity.z);
            if (!stopped && Facing.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(Facing, Vector3.up);
                _body.MoveRotation(Quaternion.RotateTowards(_body.rotation, target, _turnSpeed * Time.fixedDeltaTime));
            }
        }

        private Vector3 GetLungeVelocity()
        {
            float step = Mathf.Min(_lungeSpeed * Time.fixedDeltaTime, _lungeRemaining);
            float allowance = GetLungeAllowance();
            bool blocked = allowance < step;
            if (blocked)
            {
                step = Mathf.Max(0f, allowance);
            }
            _lungeRemaining -= step;
            Vector3 velocity = _lungeDirection * (step / Time.fixedDeltaTime);
            if (blocked || _lungeRemaining <= 0.0001f)
            {
                IsLunging = false;
                _lungeRemaining = 0f;
            }
            return velocity;
        }

        private float GetLungeAllowance()
        {
            if (_brain.Player == null || _brain.PlayerCollider == null)
            {
                return float.PositiveInfinity;
            }
            Collider target = _brain.PlayerCollider;
            // 비볼록 MeshCollider는 ClosestPoint를 지원하지 않아 기존처럼 bounds로 대신합니다.
            Vector3 closest = target is MeshCollider mesh && !mesh.convex
                ? target.bounds.ClosestPoint(_body.position) : target.ClosestPoint(_body.position);
            Vector3 toSurface = EnemyAI.Flat(closest - _body.position);
            if (Vector3.Dot(toSurface, _lungeDirection) <= 0f && toSurface.sqrMagnitude > 0.0001f)
            {
                return float.PositiveInfinity;
            }
            return toSurface.magnitude - GetBodyRadius() - _lungeStopGap;
        }

        private float GetBodyRadius()
        {
            if (_bodyCollider is CapsuleCollider capsule)
            {
                Vector3 scale = transform.lossyScale;
                return capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            }
            if (_bodyCollider != null)
            {
                Vector3 extents = _bodyCollider.bounds.extents;
                return Mathf.Max(extents.x, extents.z);
            }
            return 0.5f;
        }
    }
}
