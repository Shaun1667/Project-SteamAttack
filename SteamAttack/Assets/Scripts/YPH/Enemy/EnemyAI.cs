using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Profiling;
using Random = UnityEngine.Random;

namespace YPH
{
    /// <summary>인지·상태를 결정하고 이동과 공격을 부품에 맡깁니다. 외부 피해·받아치기 API는 이곳에 유지합니다.</summary>
    [RequireComponent(typeof(EnemyMotor), typeof(EnemyHealth))]
    public class EnemyAI : MonoBehaviour, IParryTarget
    {
        public enum State
        {
            Wander, Chase, Attack, Parried
        }
        [Header("대상·배회")]
        [SerializeField, Tooltip("인지할 대상의 태그입니다. 실제 플레이어 태그와 같아야 찾을 수 있습니다.")] private string _playerTag = "Player";
        [SerializeField, Tooltip("스폰 지점 주변 배회 반경(m)입니다. 높이면 넓게 다니며 0이면 제자리에 머뭅니다.")] private float _wanderRadius = 6f;
        [SerializeField, Tooltip("배회 속도(m/s)입니다. 높이면 빨리 이동합니다.")] private float _wanderSpeed = 1.5f;
        [SerializeField, Tooltip("배회 목적지 도착 후 최소 대기 시간(초)입니다. 높이면 오래 쉽니다.")] private float _wanderWaitMin = 1f;
        [SerializeField, Tooltip("배회 목적지 도착 후 최대 대기 시간(초)입니다. 최소값 이상으로 설정합니다.")] private float _wanderWaitMax = 2.5f;
        [Header("인지·추적")]
        [SerializeField, Tooltip("이 거리(m) 안에 들어온 플레이어를 인지합니다. 높이면 멀리서 발견합니다.")] private float _detectRange = 8f;
        [SerializeField, Tooltip("이 거리(m) 밖에서 놓침 시간을 셉니다. 높이면 더 멀리까지 추적합니다.")] private float _loseRange = 12f;
        [SerializeField, Min(0f), Tooltip("놓침 범위 밖에서 추적을 유지할 시간(초)입니다. 높이면 오래 쫓습니다.")] private float _loseDelay = 3f;
        [SerializeField, Min(0f), Tooltip("멀리서 맞았을 때 놓침 판정을 유예할 최대 시간(초)입니다.")] private float _damagedChaseMaxTime = 10f;
        [SerializeField, Tooltip("추적 속도(m/s)입니다. 높이면 플레이어에게 빨리 접근합니다.")] private float _chaseSpeed = 3.5f;
        [Header("피격·받아치기")]
        [SerializeField, Min(0f), Tooltip("맞을 때 이동·공격·애니메이션을 멈추는 시간(초)입니다. 높이면 정지가 길어집니다.")] private float _hitStopTime = 0.1f;
        [SerializeField, Min(0f), Tooltip("받아치기 성공 후 경직(초)입니다. 높이면 강공격을 넣을 여유가 늘어납니다(D-067). 플레이어 강공격 동작 1.2초를 기준으로 기본 1.8초입니다.")] private float _parriedStunTime = 1.8f;
        [SerializeField, Tooltip("동심원의 기준 위치입니다. 비우면 적 위치에 아래 높이를 더합니다.")] private Transform _parryPoint;
        [SerializeField, Min(0f), Tooltip("기준 Transform이 없을 때 동심원을 올릴 높이(m)입니다.")] private float _parryPointHeight = 1.2f;
        [SerializeField, Tooltip("켜면 상태 전환·공격·인지 이유를 콘솔에 남깁니다.")] private bool _logStateChanges = true;

        // NGH 입력·UI가 공통 규약으로 옮길 때까지 옛 이름과 신호 시점을 유지합니다.
        public const float ParryOverlapLead = 0.15f;
        public const float ParryHalfWindow = 0.15f;
        public const string HitEventName = "OnAttackHit";
        private static readonly List<EnemyAI> _activeEnemies = new List<EnemyAI>();
        private static readonly ProfilerMarker _findPlayerMarker = new ProfilerMarker("YPH.EnemyAI.FindPlayer");
        public static IReadOnlyList<EnemyAI> ActiveEnemies => _activeEnemies;
        public static event Action<EnemyAI, float> ParryableAttackStarted;
        public event Action<bool> AwarenessChanged;
        public event Action Parried;
        public State CurrentState => _state;
        public bool IsAwareOfPlayer => _isAware;
        public bool IsParryWindowOpen => _parryWindowOpen;
        public float ParryHitTime => _parryHitTime;
        public float ParryOverlapTime => _parryHitTime - ParryOverlapLead;
        public bool IsSuperArmor => _superArmor;
        public bool IsParryStunned => _state == State.Parried;
        public Vector3 ParryPoint => _parryPoint != null ? _parryPoint.position : transform.position + Vector3.up * _parryPointHeight;
        internal Transform Player { get; private set; }
        internal Collider PlayerCollider { get; private set; }
        internal float DetectRange => _detectRange;
        internal bool IsHitStopped => _hitStopTimer > 0f;
        internal float ActiveDeltaTime => IsHitStopped ? 0f : Time.deltaTime;
        internal bool UsesRanged => _ranged != null && _ranged.isActiveAndEnabled;
        internal EnemyHealth Health { get; private set; }
        internal EnemyMeleeAttack Melee => _melee;
        internal EnemyRangedAttack Ranged => _ranged;
        private EnemyMotor _motor;
        private EnemyAnimatorLink _animation;
        private EnemyAttack[] _attacks;
        private EnemyAttack _runningAttack;
        private EnemyMeleeAttack _melee;
        private EnemyRangedAttack _ranged;
        private State _state;
        private bool _isAware, _parryWindowOpen, _superArmor, _hasWanderTarget;
        private float _loseTimer, _damagedChaseTimer, _parriedTimer, _hitStopTimer, _parryHitTime, _nextPlayerSearch;
        private float _wanderWaitTimer, _wanderMoveTimer, _wanderGiveUpTime;
        private Vector3 _homePosition, _wanderTarget;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _activeEnemies.Clear();
            ParryableAttackStarted = null;
        }
        private void Awake()
        {
            _motor = GetComponent<EnemyMotor>();
            Health = GetComponent<EnemyHealth>();
            _animation = GetComponent<EnemyAnimatorLink>();
            _attacks = GetComponents<EnemyAttack>();
            _melee = GetComponent<EnemyMeleeAttack>();
            _ranged = GetComponent<EnemyRangedAttack>();
            // 원거리 구성에서도 근접 킥을 먼저 검사하도록 시작 때 한 번만 순서를 고정합니다.
            Array.Sort(_attacks, (a, b) => (a is EnemyMeleeAttack ? 0 : 1).CompareTo(b is EnemyMeleeAttack ? 0 : 1));
            Health.Damaged += OnDamaged;
            _homePosition = transform.position;
        }
        private void Start()
        {
            FindPlayer();
            _wanderWaitTimer = Random.Range(_wanderWaitMin, _wanderWaitMax);
        }
        private void OnEnable()
        {
            if (!_activeEnemies.Contains(this))
            {
                _activeEnemies.Add(this);
            }
            ParryTargets.Register(this);
        }
        private void OnDisable()
        {
            _activeEnemies.Remove(this);
            ParryTargets.Unregister(this);
            CancelAttack();
            if (_state == State.Attack)
            {
                SetState(_isAware && Player != null ? State.Chase : State.Wander);
            }
        }
        private void OnDestroy()
        {
            if (Health != null)
            {
                Health.Damaged -= OnDamaged;
            }
        }
        private void OnValidate()
        {
            _wanderRadius = Mathf.Max(0f, _wanderRadius);
            _wanderWaitMax = Mathf.Max(_wanderWaitMin, _wanderWaitMax);
            _detectRange = Mathf.Max(0.1f, _detectRange);
            _loseRange = Mathf.Max(_detectRange, _loseRange);
            GetComponent<EnemyMeleeAttack>()?.ClampRange(_detectRange);
        }
        private void Update()
        {
            if (IsHitStopped)
            {
                _hitStopTimer -= Time.deltaTime;
                if (_hitStopTimer <= 0f)
                {
                    EndHitStop();
                }
            }
            if (Player == null && Time.time >= _nextPlayerSearch)
            {
                FindPlayer();
            }
            UpdateAwareness();
            _motor.Move(Vector3.zero, 0f);
            if (!IsHitStopped)
            {
                switch (_state)
                {
                    case State.Wander: TickWander(); break;
                    case State.Chase: TickChase(); break;
                    case State.Parried:
                        _parriedTimer -= Time.deltaTime;
                        if (_parriedTimer <= 0f)
                        {
                            SetState(_isAware && Player != null ? State.Chase : State.Wander);
                        }
                        break;
                }
            }
            _animation?.UpdateMovement(_state, _motor.Direction, _motor.Speed, _wanderSpeed, _chaseSpeed, IsHitStopped);
        }

        /// <summary>죽은 순간 공격과 이동을 취소하고 사망 애니메이션으로 바꿉니다.</summary>
        public void StopForDeath()
        {
            CancelAttack();
            SetAware(false);
            _animation?.StopForDeath();
            SetState(State.Wander);
            enabled = false;
        }
        /// <summary>부활 시 원래 위치·공격 대기·애니메이션을 되돌립니다.</summary>
        public void ReviveAtHome()
        {
            _motor.ReturnHome(_homePosition);
            _animation?.Rebind();
            SetAware(false);
            _hasWanderTarget = false;
            _melee?.ResetCooldown();
            _ranged?.ResetCooldown();
            SetState(State.Wander);
            _wanderWaitTimer = Random.Range(_wanderWaitMin, _wanderWaitMax);
            enabled = true;
        }
        private void OnDamaged(float amount, Vector3 from)
        {
            if (!enabled)
            {
                return;
            }
            Aggro(true);
            HitStop(_hitStopTime);
        }
        /// <summary>공격 타이머와 받아치기 예정 시각도 함께 늦춰 표시와 실제 판정을 맞춥니다.</summary>
        public void HitStop(float duration)
        {
            if (duration <= 0f || !enabled)
            {
                return;
            }
            float added = Mathf.Max(0f, duration - _hitStopTimer);
            _hitStopTimer = Mathf.Max(_hitStopTimer, duration);
            _parryHitTime += _parryWindowOpen ? added : 0f;
            _animation?.SetPlaybackSpeed(0f);
        }
        private void EndHitStop()
        {
            _hitStopTimer = 0f;
            _animation?.SetPlaybackSpeed(1f);
        }
        /// <summary>옛 NGH 입력 경로의 타이밍 검사입니다. 새 입력은 검사 후 ApplyParry를 호출합니다.</summary>
        public bool TryParry(float damage = 0f, Vector3 from = default) =>
            enabled && _parryWindowOpen && Mathf.Abs(Time.time - ParryOverlapTime) <= ParryHalfWindow && ApplyParry(damage, from);
        /// <summary>공통 규약으로 받아치기 성공을 적용하며 중복 호출은 닫힌 창에서 거절합니다.</summary>
        public bool ApplyParry(float damage, Vector3 from)
        {
            if (!enabled || !_parryWindowOpen)
            {
                return false;
            }
            ForceParried(damage, from);
            return true;
        }
        /// <summary>공격을 취소하고 경직을 적용합니다. 개발용 5 키도 같은 통로를 사용합니다.</summary>
        public void ForceParried(float damage = 0f, Vector3 from = default)
        {
            if (!enabled || !Health.IsAlive)
            {
                return;
            }
            CancelAttack();
            Aggro(true);
            SetState(State.Parried);
            _parriedTimer = _parriedStunTime;
            _animation?.Play(EnemyAnimatorLink.ParriedHash);
            // 피해로 쓰러지면 사망 처리가 우선이므로 경직 후속 신호는 보내지 않습니다.
            if (damage > 0f)
            {
                Health.TakeDamage(damage, from);
                if (!Health.IsAlive)
                {
                    Log("패링 피해 " + damage + "로 쓰러짐");
                    return;
                }
            }
            Log("패링당함" + (damage > 0f ? " (피해 " + damage + ")" : ""));
            Parried?.Invoke();
        }
        internal void OpenParryWindow(float hitTime)
        {
            _parryWindowOpen = true;
            _parryHitTime = hitTime;
            ParryableAttackStarted?.Invoke(this, hitTime - Time.time);
            ParryTargets.NotifyAttackStarted(this, ParryOverlapTime - Time.time);
        }
        internal void CloseParryWindow() => _parryWindowOpen = false;
        internal void SetSuperArmor(bool value) => _superArmor = value;
        internal void OnAttackFinished(EnemyAttack attack)
        {
            if (_runningAttack != attack)
            {
                return;
            }
            _runningAttack = null;
            _melee?.ResetChoice();
            SetState(_isAware && Player != null ? State.Chase : State.Wander);
        }
        private void CancelAttack()
        {
            if (_runningAttack != null)
            {
                _runningAttack.Cancel();
            }
            _runningAttack = null;
            _melee?.ResetChoice();
            CloseParryWindow();
            _motor.Stop();
            EndHitStop();
        }

        private void FindPlayer()
        {
            // 재생성 대기 중에도 한 프레임마다 씬 전체를 훑지 않도록 탐색 간격을 고정합니다.
            _nextPlayerSearch = Time.time + 0.5f;
            using (_findPlayerMarker.Auto())
            {
                GameObject found = GameObject.FindGameObjectWithTag(_playerTag);
                Player = found != null ? found.transform : null;
                PlayerCollider = Player != null
                    ? Array.Find(Player.GetComponentsInChildren<Collider>(), col => col.enabled && !col.isTrigger) : null;
            }
        }
        /// <summary>피해로 인지하면 멀리 있어도 잠시 추적을 유지합니다.</summary>
        public void Aggro(bool fromDamage)
        {
            if (Player == null && Time.time >= _nextPlayerSearch)
            {
                FindPlayer();
            }
            if (Player == null)
            {
                return;
            }
            _loseTimer = 0f;
            if (fromDamage && Flat(Player.position - transform.position).magnitude > _loseRange)
            {
                _damagedChaseTimer = _damagedChaseMaxTime;
            }
            if (!_isAware)
            {
                SetAware(true);
                Log(fromDamage ? "피격 → 플레이어 인지" : "플레이어 인지");
                if (UsesRanged)
                {
                    _ranged.DelayFirstShot();
                }
            }
            if (_state == State.Wander)
            {
                SetState(State.Chase);
            }
        }
        private void UpdateAwareness()
        {
            if (Player == null)
            {
                if (_isAware)
                {
                    LosePlayer();
                }
                return;
            }
            float distance = Flat(Player.position - transform.position).magnitude;
            if (!_isAware)
            {
                if (distance <= _detectRange)
                {
                    Aggro(false);
                }
                return;
            }
            if (_damagedChaseTimer > 0f)
            {
                _damagedChaseTimer = distance <= _loseRange ? 0f : _damagedChaseTimer - Time.deltaTime;
                _loseTimer = 0f;
                return;
            }
            _loseTimer = distance > _loseRange ? _loseTimer + Time.deltaTime : 0f;
            if (distance > _loseRange && _loseTimer >= _loseDelay)
            {
                LosePlayer();
            }
        }
        private void LosePlayer()
        {
            SetAware(false);
            _melee?.ResetChoice();
            Log("플레이어 놓침");
            if (_state != State.Attack && _state != State.Parried)
            {
                SetState(State.Wander);
            }
        }
        private void SetAware(bool aware)
        {
            _loseTimer = 0f;
            _damagedChaseTimer = aware ? _damagedChaseTimer : 0f;
            if (_isAware == aware)
            {
                return;
            }
            _isAware = aware;
            _animation?.SetCombat(aware);
            AwarenessChanged?.Invoke(aware);
        }
        private void TickWander()
        {
            if (!_hasWanderTarget)
            {
                _wanderWaitTimer -= Time.deltaTime;
                if (_wanderWaitTimer > 0f)
                {
                    return;
                }
                Vector2 offset = Random.insideUnitCircle * _wanderRadius;
                _wanderTarget = _homePosition + new Vector3(offset.x, 0f, offset.y);
                _hasWanderTarget = true;
                _wanderMoveTimer = 0f;
                // 길이 막혀도 한 목표에 영원히 매달리지 않도록 기존 포기 시간을 유지합니다.
                _wanderGiveUpTime = Flat(_wanderTarget - transform.position).magnitude / Mathf.Max(0.01f, _wanderSpeed) * 1.5f + 1f;
            }
            Vector3 toTarget = Flat(_wanderTarget - transform.position);
            _wanderMoveTimer += Time.deltaTime;
            if (toTarget.magnitude <= 0.3f || _wanderMoveTimer >= _wanderGiveUpTime)
            {
                _hasWanderTarget = false;
                _wanderWaitTimer = Random.Range(_wanderWaitMin, _wanderWaitMax);
                return;
            }
            _motor.Move(toTarget.normalized, _wanderSpeed);
            _motor.Face(toTarget.normalized);
        }
        private void TickChase()
        {
            if (!_isAware || Player == null)
            {
                SetState(State.Wander);
                return;
            }
            Vector3 direction = Flat(Player.position - transform.position);
            float distance = direction.magnitude;
            if (UsesRanged)
            {
                _ranged.PrepareChase();
            }
            foreach (EnemyAttack attack in _attacks)
            {
                if (attack.isActiveAndEnabled && attack.CanBegin(distance))
                {
                    BeginAttack(attack);
                    return;
                }
            }
            if (UsesRanged)
            {
                _ranged.MaintainDistance(distance, _chaseSpeed);
                return;
            }
            _motor.Move(direction.normalized, _chaseSpeed);
            _motor.Face(direction.normalized);
        }
        private void BeginAttack(EnemyAttack attack)
        {
            SetState(State.Attack);
            CancelAttack();
            _runningAttack = attack;
            attack.Begin();
        }
        internal void FacePlayer()
        {
            if (Player == null)
            {
                return;
            }
            Vector3 direction = Flat(Player.position - transform.position);
            if (direction.sqrMagnitude > 0.0001f)
            {
                _motor.Face(direction.normalized);
            }
        }
        private void SetState(State next)
        {
            if (_state == next)
            {
                return;
            }
            Log(_state + " → " + next);
            _state = next;
            if (next != State.Attack)
            {
                _motor.Stop();
                CloseParryWindow();
                _superArmor = false;
                _animation?.SetKickSpeed(1f);
            }
            if (next == State.Wander)
            {
                _hasWanderTarget = false;
                _wanderWaitTimer = Random.Range(_wanderWaitMin, _wanderWaitMax);
            }
        }
        internal static Vector3 Flat(Vector3 value) => new Vector3(value.x, 0f, value.z);
        internal void Log(string message)
        {
            if (_logStateChanges)
            {
                Debug.Log("[CHG_EnemyAI:" + name + "] " + message, this);
            }
        }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        internal void DrawDebugLines()
        {
            Vector3 center = transform.position + Vector3.up * 0.05f;
            DebugView.Circle(_homePosition, _wanderRadius, Color.cyan);
            DebugView.Circle(center, _detectRange, _isAware ? new Color(1f, 0.4f, 0.1f) : Color.yellow);
            DebugView.Circle(center, _loseRange, new Color(1f, 0.9f, 0.1f, 0.3f));
            if (_isAware && Player != null)
            {
                DebugView.Line(center, Player.position, Color.yellow);
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
            EnemyDebugTools.DrawCircle(transform.position, _wanderRadius, Color.cyan);
            EnemyDebugTools.DrawCircle(transform.position, _detectRange, Color.yellow);
            EnemyDebugTools.DrawCircle(transform.position, _loseRange, new Color(1f, 0.9f, 0.1f, 0.3f));
        }
#endif
    }
}
