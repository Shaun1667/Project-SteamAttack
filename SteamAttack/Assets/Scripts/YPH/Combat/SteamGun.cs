using System;
using UnityEngine;

namespace YPH
{
    /// <summary>약실 한 발과 탄환 주머니를 관리하며 카메라 방향으로 즉시 명중을 판정합니다.</summary>
    public class SteamGun : MonoBehaviour
    {
        /// <summary>발사 → 약실 넣기 → 준비 순서입니다. 주머니가 비면 충전을 기다립니다.</summary>
        public enum State { Ready, Firing, Chambering, Empty, Charging }
        private const int HitBufferSize = 16;

        [SerializeField, Tooltip("주머니 충전 비용을 낼 증기 탱크입니다. 발사와 약실 이동에는 증기를 쓰지 않습니다.")]
        private SteamTank _tank;
        [SerializeField, Tooltip("적 명중을 알릴 회복 입구입니다. 이곳에서 명중당 회복량을 정합니다.")]
        private SteamHitRefill _hitRefill;
        [SerializeField, Tooltip("판정 광선의 시작 위치와 앞 방향입니다. 조준 카메라를 연결합니다.")]
        private Transform _aimOrigin;
        [SerializeField, Tooltip("이 루트 아래의 콜라이더는 광선이 통과합니다. 플레이어 자신을 연결합니다.")]
        private Transform _ignoreRoot;
        [SerializeField, Tooltip("사격선이 시작되는 총구입니다. 실제 명중 판정은 조준 카메라에서 시작합니다.")]
        private Transform _muzzle;
        [SerializeField, Min(1), Tooltip("주머니에 충전할 수 있는 탄 수입니다. 실행 중 줄여도 이미 가진 탄은 버리지 않고, 소모될 때까지 추가 충전을 막습니다.")]
        private int _pouchCapacity = 10;
        [SerializeField, Min(0), Tooltip("시작할 때만 읽는 주머니 탄 수입니다. 용량 이하로 제한되며 약실 한 발은 별도로 지급됩니다.")]
        private int _startPouchRounds = 10;
        [SerializeField, Min(0.001f), Tooltip("주머니 한 발을 충전할 때 소비할 증기입니다. 클수록 같은 증기로 적은 탄을 충전합니다.")]
        private float _steamCostPerRound = 1f;
        [SerializeField, Min(0f), Tooltip("발사 후 약실 이동을 시작하기까지의 초입니다. 현재 경과시간과 비교하므로 실행 중 변경도 반영되고, 0이면 즉시 넘어갑니다.")]
        private float _fireInterval = 0.25f;
        [SerializeField, Min(0f), Tooltip("주머니 한 발을 약실에 넣는 초입니다. 다음 발까지 발사 간격과 이 시간이 필요하며 0이면 즉시 넣습니다.")]
        private float _chamberTime = 0.5f;
        [SerializeField, Min(0f), Tooltip("증기를 먼저 낸 뒤 주머니 탄이 지급될 때까지의 초입니다. 0이면 즉시 완료하며 충전 중에는 발사할 수 없습니다.")]
        private float _pouchChargeTime = 1f;
        [SerializeField, Min(0f), Tooltip("적에게 한 발이 주는 피해입니다. 0이면 피해와 명중 회복을 처리하지 않습니다.")]
        private float _damage = 1f;
        [SerializeField, Min(0.01f), Tooltip("카메라에서 명중을 검사하는 최대 거리(m)입니다. 늘리면 더 먼 적을 맞힐 수 있습니다.")]
        private float _range = 50f;
        [SerializeField, Tooltip("광선이 부딪힐 레이어입니다. 벽을 제외하면 벽 너머까지 맞힐 수 있으므로 장애물도 포함합니다.")]
        private LayerMask _hitMask = ~0;

        private State _state;
        private bool _chamberLoaded;
        private int _pouchRounds;
        private int _pendingPouchRounds;
        private float _stateTimer;
        private RaycastHit[] _hitBuffer;

        /// <summary>현재 동작 단계입니다. 표시 컴포넌트는 이 값을 읽거나 상태 이벤트를 구독합니다.</summary>
        public State CurrentState => _state;
        /// <summary>지금 발사할 약실 한 발이 있는지입니다.</summary>
        public bool ChamberLoaded => _chamberLoaded;
        /// <summary>주머니에 남은 탄 수입니다. 충전 중인 탄은 완료 때 더해집니다.</summary>
        public int PouchRounds => _pouchRounds;
        /// <summary>현재 설정된 주머니 충전 상한입니다.</summary>
        public int PouchCapacity => Mathf.Max(1, _pouchCapacity);
        /// <summary>성공한 발사만 총구, 광선 끝점, 적 명중 여부를 보냅니다. 빗맞아도 발사는 성공입니다.</summary>
        public event Action<Vector3, Vector3, bool> OnFired;
        /// <summary>물체를 맞힌 발사에서 OnFired 다음에 지점·면의 법선·적 피해 여부를 보냅니다. 허공에는 발생하지 않습니다.</summary>
        public event Action<Vector3, Vector3, bool> OnImpact;
        /// <summary>모든 단계 전환을 이전 상태와 새 상태로 알립니다.</summary>
        public event Action<State, State> OnStateChanged;

        private void Awake()
        {
            if (_tank == null || _hitRefill == null || _aimOrigin == null || _ignoreRoot == null || _muzzle == null)
            {
                Debug.LogError("YPH_SteamGun: 탱크·회복·조준·플레이어·총구를 모두 연결하세요.", this);
                enabled = false;
                return;
            }
            _hitBuffer = new RaycastHit[HitBufferSize];
            _pouchRounds = Mathf.Clamp(_startPouchRounds, 0, PouchCapacity);
            _chamberLoaded = true;
            _state = State.Ready;
        }

        private void Update()
        {
            if (_state == State.Ready || _state == State.Empty) return;
            // 남은 시간을 처음에 고정하지 않습니다. Inspector에서 진행 중 시간을 바꿔도 바로 반영합니다.
            // 꺼진 동안 Update가 없으므로 장비를 다시 꺼내면 저장된 경과시간부터 이어집니다.
            _stateTimer += Time.deltaTime;
            FinishElapsedStates();
        }

        /// <summary>준비된 약실 한 발을 사용합니다. 다른 단계이거나 장비가 꺼져 있으면 아무 변화 없이 실패합니다.</summary>
        public bool TryFire()
        {
            if (!isActiveAndEnabled || _state != State.Ready || !_chamberLoaded) return false;
            _chamberLoaded = false;
            SetState(State.Firing);
            bool hitSomething = FindHit(out Vector3 end, out RaycastHit hit);
            bool hitEnemy = hitSomething && ApplyHit(hit);
            OnFired?.Invoke(_muzzle.position, end, hitEnemy);
            if (hitSomething)
            {
                OnImpact?.Invoke(hit.point, hit.normal, hitEnemy);
            }
            FinishElapsedStates(); // 시간이 0인 설정은 다음 프레임을 기다리지 않습니다.
            return true;
        }

        /// <summary>빈 주머니 칸만큼 증기를 먼저 소비합니다. 부족하면 살 수 있는 발 수만 예약합니다.</summary>
        public bool TryChargePouch()
        {
            if (!isActiveAndEnabled || (_state != State.Ready && _state != State.Empty)) return false;
            int missing = PouchCapacity - _pouchRounds;
            if (missing <= 0) return false;
            _pendingPouchRounds = _tank.TryConsumeUnits(_steamCostPerRound, missing);
            if (_pendingPouchRounds == 0) return false;
            SetState(State.Charging);
            FinishElapsedStates();
            return true;
        }

        /// <summary>발사와 같은 광선으로 현재 조준점을 읽습니다. 허공은 사거리 끝을 반환하며 꺼진 총은 false입니다.</summary>
        public bool TryGetAimPoint(out Vector3 point)
        {
            point = default;
            if (!isActiveAndEnabled)
            {
                return false;
            }
            // 조준 연출이 매 프레임 물어도 탄·증기·피해 이벤트에는 손대지 않습니다.
            FindHit(out point, out _);
            return true;
        }

        /// <summary>플레이어 몸을 제외한 가장 가까운 충돌을 찾습니다. 조회와 실제 사격이 이 검사를 공유합니다.</summary>
        private bool FindHit(out Vector3 end, out RaycastHit hit)
        {
            Vector3 origin = _aimOrigin.position;
            Vector3 direction = _aimOrigin.forward;
            end = origin + direction * _range;
            hit = default;
            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, _range, _hitMask, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            float distance = float.PositiveInfinity;
            // ponytail: 16개 넘게 겹치면 가까운 대상을 놓칠 수 있습니다. 전용 레이어가 정해지면 단일 Raycast로 바꿉니다.
            for (int i = 0; i < count; i++)
            {
                if (_hitBuffer[i].collider.transform.IsChildOf(_ignoreRoot) || _hitBuffer[i].distance >= distance)
                {
                    continue;
                }
                nearest = i;
                distance = _hitBuffer[i].distance;
            }
            if (nearest < 0)
            {
                return false;
            }
            hit = _hitBuffer[nearest];
            end = hit.point; // 적이 아닌 벽도 선을 막습니다. 피해 대상 여부와 물리적인 끝점을 구분합니다.
            return true;
        }

        /// <summary>실제 발사 때만 살아 있는 적에게 피해를 준 뒤 명중 회복을 요청합니다.</summary>
        private bool ApplyHit(RaycastHit hit)
        {
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (!CombatTags.IsEnemy(target) || !target.IsAlive || _damage <= 0f)
            {
                return false;
            }
            target.TakeDamage(_damage, _aimOrigin.position);
            _hitRefill.NotifyHit(target);
            return true;
        }

        private void FinishElapsedStates()
        {
            // 최장 경로는 Charging → Chambering → Ready입니다. 순환이 없어 0초 설정도 안전합니다.
            while (_state != State.Ready && _state != State.Empty)
            {
                float duration = _state == State.Firing ? _fireInterval : _state == State.Chambering ? _chamberTime : _pouchChargeTime;
                if (_stateTimer < Mathf.Max(0f, duration)) return;
                float remainder = _stateTimer - Mathf.Max(0f, duration);
                if (_state == State.Chambering)
                {
                    _chamberLoaded = true;
                    SetState(State.Ready);
                }
                else
                {
                    if (_state == State.Charging)
                    {
                        _pouchRounds += _pendingPouchRounds;
                        _pendingPouchRounds = 0;
                    }
                    if (_chamberLoaded) SetState(State.Ready);
                    else if (_pouchRounds > 0) SetState(State.Chambering);
                    else SetState(State.Empty);
                }
                _stateTimer = remainder; // 낮은 프레임률에서도 한 프레임씩 시간이 추가되지 않게 남은 시간을 넘깁니다.
            }
        }

        private void SetState(State next)
        {
            State previous = _state;
            _state = next;
            _stateTimer = 0f;
            if (next == State.Chambering) _pouchRounds--; // 증기는 이미 주머니 충전 때 냈습니다.
            OnStateChanged?.Invoke(previous, next);
        }
    }
}
