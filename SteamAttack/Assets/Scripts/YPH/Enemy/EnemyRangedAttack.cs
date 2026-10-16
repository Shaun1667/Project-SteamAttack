using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NGH;

namespace YPH
{
    /// <summary>조준·사격·장전과 거리 유지를 담당합니다. 시야에서 만난 콜라이더의 소유자 판정은 한 번만 조회합니다.</summary>
    [DisallowMultipleComponent]
    public class EnemyRangedAttack : EnemyAttack
    {
        [SerializeField, Tooltip("풀에서 꺼낼 총알입니다. 비우면 사격할 수 없습니다.")] private EnemyBullet _bulletPrefab;
        [SerializeField, Tooltip("총구 Transform입니다. 비우면 아래 로컬 위치에서 발사합니다.")] private Transform _firePoint;
        [SerializeField, Tooltip("총구가 없을 때 발사할 로컬 위치(m)입니다. Y를 높이면 위에서 발사합니다.")] private Vector3 _fallbackFireOffset = new Vector3(0f, 1.45f, 0.6f);
        [SerializeField, Tooltip("총알 한 발의 피해입니다. 높이면 플레이어 HP가 더 많이 줄어듭니다.")] private int _damage = 18;
        [SerializeField, Tooltip("총알에 맞은 플레이어의 경직 단계입니다.")] private HitStagger _stagger = HitStagger.Light;
        [SerializeField, Tooltip("총알 속도(m/s)입니다. 높이면 빨리 도착합니다.")] private float _bulletSpeed = 15f;
        [SerializeField, Tooltip("총알 최대 이동 거리(m)입니다. 높이면 더 멀리 날아갑니다.")] private float _bulletRange = 25f;
        [SerializeField, Tooltip("총알 판정 반지름(m)입니다. 높이면 스쳐도 맞기 쉽습니다.")] private float _bulletRadius = 0.12f;
        [SerializeField, Tooltip("발사부터 다음 조준 시작까지 최소 간격(초)입니다. 높이면 사격 빈도가 줄어듭니다.")] private float _fireInterval = 5f;
        [SerializeField, Tooltip("발사 전 조준 시간(초)입니다. 높이면 피할 여유가 늘어납니다.")] private float _aimTime = 0.8f;
        [SerializeField, Tooltip("발사 동작 시간(초)입니다. 이 시간과 장전 시간 동안 움직이지 않습니다.")] private float _fireAnimTime = 0.27f;
        [SerializeField, Tooltip("발사 동작 뒤 장전 시간(초)입니다. 높이면 정지 시간이 길어집니다.")] private float _reloadTime = 3.3f;
        [SerializeField, Tooltip("처음 인지한 뒤 조준 시작까지 대기 시간(초)입니다.")] private float _firstShotDelay = 1f;
        [SerializeField, Tooltip("이 거리(m)보다 멀면 접근합니다. 높이면 멀리서 멈춥니다.")] private float _preferredMaxDistance = 14f;
        [SerializeField, Tooltip("이 거리(m)보다 가까우면 후퇴합니다. 높이면 더 멀리 떨어지려 합니다.")] private float _retreatDistance = 5f;
        [SerializeField, Tooltip("사격을 시작할 최대 거리(m)입니다. 높이면 멀리서 조준할 수 있습니다.")] private float _maxFireDistance = 18f;
        [SerializeField, Tooltip("후퇴 속도(m/s)입니다. 높이면 더 빨리 물러납니다.")] private float _retreatSpeed = 1.1f;
        [SerializeField, Tooltip("켜면 플레이어 몸 콜라이더 중앙을 겨눕니다. 끄면 아래 높이를 씁니다.")] private bool _aimAtPlayerBody = true;
        [SerializeField, Tooltip("몸 콜라이더를 쓰지 않을 때 플레이어 위치에 더할 조준 높이(m)입니다.")] private float _aimHeight = 1.2f;
        [SerializeField, Tooltip("시야를 막는 장애물 레이어입니다. 제외한 레이어는 사격을 막지 않습니다.")] private LayerMask _lineOfSightMask = ~0;
        private readonly RaycastHit[] _losHits = new RaycastHit[16];
        private readonly Dictionary<Collider, bool> _lineOfSightOwners = new Dictionary<Collider, bool>();
        private float _nextFireTime, _nextLosCheck;
        private bool _hasLineOfSight = true;
        internal bool IsAiming { get; private set; }
        internal Vector3 FirePosition => _firePoint != null ? _firePoint.position : transform.TransformPoint(_fallbackFireOffset);
        internal Vector3 AimPoint
        {
            get
            {
                if (Brain.Player == null)
                {
                    return transform.position + transform.forward;
                }
                if (_aimAtPlayerBody && Brain.PlayerCollider != null && Brain.PlayerCollider.enabled)
                {
                    return Brain.PlayerCollider.bounds.center;
                }
                return Brain.Player.position + Vector3.up * _aimHeight;
            }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        [SerializeField, Tooltip("개발용 표시가 켜진 동안 조준 방향은 빨강, 발사 방향은 노랑으로 그립니다.")] private bool _debugAimLaser = true;
        internal bool ShowLaser => _debugAimLaser && Brain.Player != null && (IsAiming || Time.time < _laserFlashUntil);
        internal Vector3 LaserDirection => IsAiming ? AimPoint - FirePosition : _lastShotDirection;
        internal float LaserRange => _bulletRange;
        internal LayerMask LaserMask => _lineOfSightMask;
        private Vector3 _lastShotDirection;
        private float _laserFlashUntil;
#endif
        private void Start()
        {
            if (_bulletPrefab != null)
            {
                ObjectPool.Prewarm(_bulletPrefab.gameObject, 3);
            }
        }
        private void OnValidate()
        {
            _retreatDistance = Mathf.Max(0f, _retreatDistance);
            _preferredMaxDistance = Mathf.Max(_retreatDistance + 0.5f, _preferredMaxDistance);
            _maxFireDistance = Mathf.Max(_preferredMaxDistance, _maxFireDistance);
            _fireInterval = Mathf.Max(0.1f, _fireInterval);
            _bulletSpeed = Mathf.Max(0.1f, _bulletSpeed);
        }
        public override bool CanBegin(float distanceToPlayer)
        {
            return Time.time >= _nextFireTime && _hasLineOfSight && distanceToPlayer <= _maxFireDistance;
        }
        public override void Begin() => Run(AttackRoutine());
        public override void Cancel()
        {
            StopSequence();
            IsAiming = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _laserFlashUntil = 0f;
#endif
        }
        protected override void OnDisable()
        {
            base.OnDisable();
            _lineOfSightOwners.Clear();
        }
        internal void ResetCooldown() => _nextFireTime = 0f;
        internal void DelayFirstShot() => _nextFireTime = Mathf.Max(_nextFireTime, Time.time + _firstShotDelay);
        internal void PrepareChase()
        {
            if (Time.time >= _nextLosCheck)
            {
                _nextLosCheck = Time.time + 0.2f;
                _hasLineOfSight = CheckLineOfSight();
            }
            Vector3 direction = EnemyAI.Flat(Brain.Player.position - transform.position);
            Motor.Face(direction.magnitude > 0.001f ? direction.normalized : EnemyAI.Flat(transform.forward).normalized);
        }
        internal void MaintainDistance(float distance, float chaseSpeed)
        {
            if (distance > _preferredMaxDistance || !_hasLineOfSight)
            {
                Motor.Move(Motor.Facing, chaseSpeed);
            }
            else if (distance < _retreatDistance)
            {
                Motor.Move(-Motor.Facing, _retreatSpeed);
            }
        }

        private IEnumerator AttackRoutine()
        {
            IsAiming = true;
            yield return WaitActive(_aimTime, true);
            IsAiming = false;
            Animation?.Play(EnemyAnimatorLink.FireHash);
            FireBullet();
            _nextFireTime = Time.time + _fireInterval;
            yield return WaitActive(_fireAnimTime + _reloadTime, true);
            Finish();
        }
        private void FireBullet()
        {
            if (_bulletPrefab == null)
            {
                Debug.LogWarning("[CHG_EnemyAI:" + name + "] 총알 프리팹(Bullet Prefab)이 비어 있어 사격할 수 없습니다.", this);
                return;
            }
            Vector3 origin = FirePosition;
            Vector3 direction = Brain.Player != null ? AimPoint - origin : transform.forward;
            if (direction.sqrMagnitude < 0.0001f)
            {
                direction = transform.forward;
            }
            direction.Normalize();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _lastShotDirection = direction;
            _laserFlashUntil = Time.time + 0.25f;
#endif
            EnemyBullet bullet = ObjectPool.Spawn(_bulletPrefab, origin, Quaternion.LookRotation(direction));
            if (bullet != null)
            {
                bullet.Fire(gameObject, _damage, direction, _bulletSpeed, _bulletRange, _bulletRadius, _stagger);
                Brain.Log("사격 (데미지 " + _damage + ", 경직 " + _stagger + ")");
            }
        }
        private bool CheckLineOfSight()
        {
            if (Brain.Player == null)
            {
                return false;
            }
            Vector3 direction = AimPoint - FirePosition;
            float distance = direction.magnitude;
            if (distance < 0.01f)
            {
                return true;
            }
            int count = Physics.RaycastNonAlloc(FirePosition, direction / distance, _losHits, distance, _lineOfSightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider col = _losHits[i].collider;
                if (col == null || col.transform.IsChildOf(transform) || col.transform.IsChildOf(Brain.Player))
                {
                    continue;
                }
                if (IsCharacterCollider(col))
                {
                    continue;
                }
                return false;
            }
            return true;
        }
        private bool IsCharacterCollider(Collider col)
        {
            if (!_lineOfSightOwners.TryGetValue(col, out bool character))
            {
                // 같은 벽·캐릭터를 0.2초마다 재검사하므로 처음 만났을 때만 부모 컴포넌트를 찾습니다.
                character = col.GetComponentInParent<EnemyHealth>() != null || col.GetComponentInParent<PlayerController>() != null;
                _lineOfSightOwners.Add(col, character);
            }
            return character;
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal void DrawDebugLines()
        {
            Vector3 center = transform.position + Vector3.up * 0.05f;
            DebugView.Circle(center, _retreatDistance, Color.red);
            DebugView.Circle(center, _preferredMaxDistance, Color.green);
            DebugView.Circle(center, _maxFireDistance, new Color(0.2f, 0.9f, 0.3f, 0.4f));
            if (Brain.IsAwareOfPlayer && Brain.Player != null)
            {
                DebugView.Line(FirePosition, AimPoint, _hasLineOfSight ? Color.yellow : Color.gray);
            }
        }
#endif
#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (Application.isPlaying)
            {
                return;
            }
            EnemyDebugTools.DrawCircle(transform.position, _retreatDistance, Color.red);
            EnemyDebugTools.DrawCircle(transform.position, _preferredMaxDistance, Color.green);
            EnemyDebugTools.DrawCircle(transform.position, _maxFireDistance, new Color(0.2f, 0.9f, 0.3f, 0.4f));
            Gizmos.color = new Color(1f, 0.55f, 0.1f);
            Gizmos.DrawWireSphere(FirePosition, 0.06f);
            Gizmos.DrawLine(FirePosition, FirePosition + transform.forward * _maxFireDistance);
        }
#endif
    }
}
