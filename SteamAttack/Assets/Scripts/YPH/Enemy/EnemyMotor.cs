using UnityEngine;
using UnityEngine.AI;

namespace YPH
{
    /// <summary>이동 명령을 받아 FixedUpdate에서만 속도·회전을 적용합니다. 전진 거리 계산도 이곳에서 공유합니다.</summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class EnemyMotor : MonoBehaviour
    {
        [SerializeField, Tooltip("초당 회전 각도입니다. 높이면 목표 방향을 빨리 바라봅니다.")] private float _turnSpeed = 540f;
        [SerializeField, Min(0f), Tooltip("공격 전진 중 플레이어 표면과 남길 간격(m)입니다. 높이면 더 멀리서 멈춥니다.")] private float _lungeStopGap = 0.1f;
        [SerializeField, Tooltip("켜면 배회·추적에 Humanoid NavMesh의 Walkable 영역을 사용합니다. 길이 없으면 멈춥니다. 끄면 기존 직선 이동을 사용합니다.")] private bool _useNavMesh;
        [SerializeField, Min(0.05f), Tooltip("경로를 다시 계산할 간격(초)입니다. 낮추면 움직이는 목표에 빨리 반응하지만 계산량이 늘어납니다. 기본 0.25초입니다.")] private float _repathInterval = 0.25f;
        private const float SampleDistance = 2f;
        private readonly NavMeshQueryFilter _navigationFilter = new NavMeshQueryFilter { agentTypeID = 0, areaMask = 1 };
        private readonly RaycastHit[] _attackLineHits = new RaycastHit[16];
        private NavMeshAgent _agent;
        private NavMeshPath _path;
        private float _nextPathTime;
        private bool _followingPath;
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
            _path = new NavMeshPath();
            _agent = GetComponent<NavMeshAgent>();
            if (_useNavMesh && _agent != null)
            {
                // 길 안내만 맡깁니다. Transform을 자동 갱신하면 Rigidbody와 위치를 서로 덮어씁니다.
                _agent.updatePosition = false;
                _agent.updateRotation = false;
                _agent.updateUpAxis = false;
                _agent.autoTraverseOffMeshLink = false;
            }
            _body.constraints = RigidbodyConstraints.FreezeRotation;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            Facing = transform.forward;
        }

        /// <summary>이동 의도만 저장합니다. 공격 전진은 이 명령보다 우선합니다.</summary>
        public void Move(Vector3 direction, float speed)
        {
            _followingPath = false;
            Direction = direction;
            Speed = speed;
        }

        /// <summary>AI가 정한 목적지까지 길을 찾되 실제 위치 변경은 Rigidbody에만 맡깁니다.</summary>
        public void MoveTo(Vector3 destination, float speed)
        {
            if (!_useNavMesh)
            {
                Move(EnemyAI.Flat(destination - _body.position).normalized, speed);
                Face(Direction);
                return;
            }
            if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            {
                Move(Vector3.zero, 0f);
                return;
            }
            _agent.nextPosition = _body.position;
            _agent.speed = speed;
            if (Time.time >= _nextPathTime)
            {
                RebuildPath(destination);
            }
            _agent.isStopped = false;
            Vector3 velocity = _agent.hasPath && _agent.pathStatus == NavMeshPathStatus.PathComplete
                ? EnemyAI.Flat(_agent.desiredVelocity) : Vector3.zero;
            Move(velocity.normalized, Mathf.Min(speed, velocity.magnitude));
            _followingPath = true;
            if (Direction.sqrMagnitude > 0f)
            {
                Face(Direction);
            }
        }

        /// <summary>도달할 수 없는 목표는 직선 이동으로 대체하지 않고 다음 계산 때까지 기다립니다.</summary>
        private void RebuildPath(Vector3 destination)
        {
            _nextPathTime = Time.time + Mathf.Max(0.05f, _repathInterval);
            if (!NavMesh.SamplePosition(destination, out NavMeshHit end, SampleDistance, _navigationFilter)
                || !_agent.CalculatePath(end.position, _path)
                || _path.status != NavMeshPathStatus.PathComplete)
            {
                _agent.ResetPath();
                return;
            }
            _agent.SetPath(_path);
        }

        /// <summary>공격 거리 조건을 통과한 뒤 호출하며, 플레이어까지 실제 장애물이 있는지 확인합니다.</summary>
        public bool HasClearAttackLine()
        {
            if (!_useNavMesh)
            {
                return true;
            }
            if (_brain.Player == null)
            {
                return false;
            }
            // NavMesh 가장자리의 여백은 벽이 아닙니다. 몸 중심 사이의 실제 충돌체로 공격 차단을 판단합니다.
            Vector3 origin = _bodyCollider != null ? _bodyCollider.bounds.center : _body.position;
            Vector3 target = _brain.PlayerCollider != null ? _brain.PlayerCollider.bounds.center : _brain.Player.position;
            Vector3 direction = target - origin;
            int count = Physics.RaycastNonAlloc(origin, direction.normalized, _attackLineHits, direction.magnitude, ~0, QueryTriggerInteraction.Ignore);
            if (count == _attackLineHits.Length)
            {
                return false; // 결과가 잘렸다면 벽이 없다고 단정하지 않습니다.
            }
            for (int i = 0; i < count; i++)
            {
                Collider hit = _attackLineHits[i].collider;
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(_brain.Player)
                    || hit.GetComponentInParent<EnemyHealth>() != null)
                {
                    continue;
                }
                return false;
            }
            return true;
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
            _nextPathTime = 0f;
            if (_useNavMesh && _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
        }

        internal void ReturnHome(Vector3 position)
        {
            Stop();
            if (_useNavMesh && _agent != null && NavMesh.SamplePosition(position, out NavMeshHit home, SampleDistance, _navigationFilter))
            {
                _agent.enabled = true;
                _agent.Warp(home.position);
            }
            // 부활은 이동 적분이 아니라 위치 복구이므로 물리 프레임을 기다리지 않고 순간 이동합니다.
            transform.position = position;
            _body.position = position;
        }

        private void FixedUpdate()
        {
            bool stopped = _brain == null || !_brain.isActiveAndEnabled || _brain.IsHitStopped || _brain.IsParryStunned;
            if (_useNavMesh && _agent != null && _agent.isActiveAndEnabled && _agent.isOnNavMesh)
            {
                _agent.nextPosition = _body.position;
                _agent.isStopped = stopped || !_followingPath || IsLunging;
                if (_body.isKinematic && _brain != null && !_brain.Health.IsAlive)
                {
                    _agent.enabled = false;
                }
            }
            if (_body.isKinematic)
            {
                return;
            }
            Vector3 velocity = stopped ? Vector3.zero : IsLunging ? GetLungeVelocity() : Direction * Speed;
            if (_useNavMesh && velocity.sqrMagnitude > 0f)
            {
                velocity = LimitToNavMesh(velocity);
            }
            _body.linearVelocity = new Vector3(velocity.x, _body.linearVelocity.y, velocity.z);
            if (!stopped && Facing.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(Facing, Vector3.up);
                _body.MoveRotation(Quaternion.RotateTowards(_body.rotation, target, _turnSpeed * Time.fixedDeltaTime));
            }
        }

        /// <summary>Agent가 경계 안에서 허용한 이동을 Rigidbody 속도로 바꿉니다. 벽 모서리에서는 옆으로 빠져나갈 수 있습니다.</summary>
        private Vector3 LimitToNavMesh(Vector3 velocity)
        {
            if (_agent == null || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
            {
                return Vector3.zero;
            }
            // Raycast의 첫 경계점에서 멈추면 모서리에 고착됩니다. 기본 이동 계산은 경계를 따라 남은 이동을 처리합니다.
            _agent.Move(velocity * Time.fixedDeltaTime);
            Vector3 allowed = EnemyAI.Flat(_agent.nextPosition - _body.position) / Time.fixedDeltaTime;
            return Vector3.ClampMagnitude(allowed, velocity.magnitude);
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
