using System;
using UnityEngine;

namespace YPH
{
    /// <summary>총 오브젝트에서 발당 증기 소비와 재장전을 관리합니다. 탱크·조준 기준·플레이어 루트·총구를 연결하며 조준 입력은 외부에서 제한합니다.</summary>
    public class SteamGun : MonoBehaviour
    {
        /// <summary>발사 간격 → 재장전 → 준비 순서입니다. 탄약은 저장하지 않습니다.</summary>
        public enum State { Ready, Firing, Chambering }
        private const int HitBufferSize = 16;

        [SerializeField, Tooltip("한 발마다 증기를 소비할 탱크입니다. 잔량이 발당 비용보다 적으면 발사하지 않습니다.")]
        private SteamTank _tank;
        [SerializeField, Tooltip("판정 광선의 시작 위치와 앞 방향입니다. 조준 카메라를 연결합니다.")]
        private Transform _aimOrigin;
        [SerializeField, Tooltip("이 루트 아래의 콜라이더는 광선이 통과합니다. 플레이어 자신을 연결합니다.")]
        private Transform _ignoreRoot;
        [SerializeField, Tooltip("사격선이 시작되는 총구입니다. 실제 명중 판정은 조준 카메라에서 시작합니다.")]
        private Transform _muzzle;
        // 옛 씬의 탄약 비용·피해·사거리 대신 확정된 기본값을 쓰도록 새 직렬화 이름을 사용합니다.
        [SerializeField, Min(0.001f), Tooltip("한 발마다 쓸 증기입니다. 기본 5(임시, 10.15 회의)이며, 높이면 같은 증기로 쏠 수 있는 횟수가 줄어듭니다. 명중으로 증기를 회복하지 않습니다.")]
        private float _steamCostPerShot = 5f;
        [SerializeField, Min(0f), Tooltip("발사 후 재장전을 시작하기까지의 초입니다. 높이면 다음 발까지 더 오래 기다리며, 0이면 즉시 재장전으로 넘어갑니다. 실행 중 변경도 반영됩니다.")]
        private float _fireInterval = 0.25f;
        [SerializeField, Min(0f), Tooltip("재장전에 걸리는 초입니다. 다음 발까지 발사 간격과 이 시간이 필요합니다. 높이면 연사가 느려지고 0이면 재장전 대기를 생략합니다.")]
        private float _chamberTime = 0.5f;
        [SerializeField, Min(0f), Tooltip("한 발 피해입니다. 기획 기본값은 강공격과 같은 20(약공격 10 × 2)입니다. 높이면 피해가 커지고 0이면 피해를 주지 않습니다. 명중으로 증기를 회복하지 않습니다.")]
        private float _damagePerShot = 20f;
        [SerializeField, Min(0.01f), Tooltip("총구 기준 사거리(m)입니다. 기본 25이며 높이면 먼 표적까지 맞힙니다. 판정은 카메라에서 시작하되 광선 방향의 카메라→총구 거리만큼 더합니다.")]
        private float _maxRange = 25f;
        [SerializeField, Tooltip("광선이 부딪힐 레이어입니다. 벽을 제외하면 벽 너머까지 맞힐 수 있으므로 장애물도 포함합니다.")]
        private LayerMask _hitMask = ~0;

        private State _state;
        private float _stateTimer;
        private RaycastHit[] _hitBuffer;

        /// <summary>현재 동작 단계입니다. 표시 컴포넌트는 이 값을 읽거나 상태 이벤트를 구독합니다.</summary>
        public State CurrentState => _state;
        /// <summary>실제 사격 광선이 나가는 방향입니다. 몸의 조준 회전도 같은 기준을 사용합니다.</summary>
        public Vector3 AimDirection => _aimOrigin != null ? _aimOrigin.forward : Vector3.zero;
        /// <summary>총이 켜져 있고 재장전이 끝났으며 발당 증기가 충분한지 조회합니다. 증기를 소비하지 않으며 조준 여부는 호출자가 검사합니다.</summary>
        public bool CanFire => isActiveAndEnabled && _state == State.Ready && _tank != null && _tank.CanConsume(_steamCostPerShot);
        /// <summary>삭제 예정: NGH CombatBridge 정리 후 제거. 항상 현재 단계가 Ready인지 반환하며 증기·조준 조건은 검사하지 않습니다.</summary>
        public bool ChamberLoaded => _state == State.Ready;
        /// <summary>삭제 예정: NGH CombatBridge 정리 후 제거. 탄약을 저장하지 않아 항상 0입니다.</summary>
        public int PouchRounds => 0;
        /// <summary>삭제 예정: NGH CombatBridge 정리 후 제거. 탄약을 저장하지 않아 항상 0입니다.</summary>
        public int PouchCapacity => 0;
        /// <summary>성공한 발사만 총구, 광선 끝점, 적 명중 여부를 보냅니다. 빗맞아도 발사는 성공입니다.</summary>
        public event Action<Vector3, Vector3, bool> OnFired;
        /// <summary>물체를 맞힌 발사에서 OnFired 다음에 지점·면의 법선·적 피해 여부를 보냅니다. 허공에는 발생하지 않습니다.</summary>
        public event Action<Vector3, Vector3, bool> OnImpact;
        /// <summary>모든 단계 전환을 이전 상태와 새 상태로 알립니다.</summary>
        public event Action<State, State> OnStateChanged;
        /// <summary>입력 담당자가 NotifyFireBlocked를 호출할 때 발생합니다. 증기 부족 알림은 탱크가 별도로 보냅니다.</summary>
        public event Action OnFireBlocked;

        /// <summary>필수 연결을 검사하고 반복 광선 판정에 쓸 버퍼를 한 번만 만듭니다.</summary>
        private void Awake()
        {
            if (_tank == null || _aimOrigin == null || _ignoreRoot == null || _muzzle == null)
            {
                Debug.LogError("SteamGun: 탱크·조준·플레이어·총구를 모두 연결하세요.", this);
                enabled = false;
                return;
            }
            _hitBuffer = new RaycastHit[HitBufferSize];
            _state = State.Ready;
        }

        /// <summary>발사·재장전 중에만 경과 시간을 더합니다. 총을 꺼 두면 진행을 멈추고 다시 켰을 때 이어갑니다.</summary>
        private void Update()
        {
            if (_state == State.Ready)
            {
                return;
            }
            _stateTimer += Time.deltaTime;
            FinishElapsedStates();
        }

        /// <summary>발당 증기를 낸 뒤 한 번 판정합니다. 발사 불가 시 상태·증기·이벤트를 바꾸지 않고 false를 반환합니다.</summary>
        public bool TryFire()
        {
            if (!CanFire || !_tank.TryConsume(_steamCostPerShot))
            {
                return false;
            }
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

        /// <summary>입력 담당자가 사격을 거절할 때 호출합니다. 준비 상태의 증기 부족만 탱크에 알리고, 모든 거절은 OnFireBlocked로 알립니다.</summary>
        public void NotifyFireBlocked()
        {
            if (isActiveAndEnabled && _state == State.Ready && _tank != null && !_tank.CanConsume(_steamCostPerShot))
            {
                // 증기가 모자랄 때만 부르므로 소비되지 않고 실패 신호만 남습니다. 재장전 중에는 탱크를 건드리지 않습니다.
                _tank.TryConsume(_steamCostPerShot);
            }
            OnFireBlocked?.Invoke();
        }

        /// <summary>삭제 예정: NGH CombatBridge 정리 후 제거. 탄 주머니가 없어 항상 false이며 증기와 상태를 바꾸지 않습니다.</summary>
        public bool TryChargePouch() => false;

        /// <summary>발사와 같은 광선으로 현재 조준점을 읽습니다. 허공은 사거리 끝을 반환하며 꺼진 총은 false입니다.</summary>
        public bool TryGetAimPoint(out Vector3 point)
        {
            point = default;
            if (!isActiveAndEnabled)
            {
                return false;
            }
            // 조준 연출이 매 프레임 물어도 증기·피해 이벤트에는 손대지 않습니다.
            FindHit(out point, out _);
            return true;
        }

        /// <summary>플레이어 몸을 제외한 가장 가까운 충돌을 찾습니다. 조회와 실제 사격이 이 검사를 공유합니다.</summary>
        private bool FindHit(out Vector3 end, out RaycastHit hit)
        {
            Vector3 origin = _aimOrigin.position;
            Vector3 direction = _aimOrigin.forward;
            // 카메라 뒤로 빠진 거리 때문에 실제 총구 사거리가 짧아지지 않게 광선 방향 성분만 더합니다.
            float muzzleAlongRay = Mathf.Max(0f, Vector3.Dot(_muzzle.position - origin, direction));
            float maxDistance = muzzleAlongRay + _maxRange;
            end = origin + direction * maxDistance;
            hit = default;
            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, maxDistance, _hitMask, QueryTriggerInteraction.Ignore);
            int nearest = -1;
            float distance = float.PositiveInfinity;
            // shortcut: 16개 넘게 겹치면 가까운 대상을 놓칠 수 있습니다. 전용 레이어가 정해지면 단일 Raycast로 바꿉니다.
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

        /// <summary>실제 발사 때 살아 있는 적에게만 피해를 줍니다. 화승총 명중은 증기 획득 경로가 아닙니다.</summary>
        private bool ApplyHit(RaycastHit hit)
        {
            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (!CombatTags.IsEnemy(target) || !target.IsAlive || _damagePerShot <= 0f)
            {
                return false;
            }
            target.TakeDamage(_damagePerShot, _aimOrigin.position);
            return true;
        }

        /// <summary>끝난 단계의 남은 시간을 다음 단계로 넘겨 낮은 프레임률과 0초 설정에서도 같은 순서로 재장전합니다.</summary>
        private void FinishElapsedStates()
        {
            // Firing → Chambering → Ready에서 끝나므로 두 시간이 0이어도 반복이 끝납니다.
            while (_state != State.Ready)
            {
                float duration = _state == State.Firing ? _fireInterval : _chamberTime;
                if (_stateTimer < Mathf.Max(0f, duration))
                {
                    return;
                }
                float remainder = _stateTimer - Mathf.Max(0f, duration);
                SetState(_state == State.Firing ? State.Chambering : State.Ready);
                _stateTimer = remainder; // 낮은 프레임률에서도 한 프레임씩 시간이 추가되지 않게 남은 시간을 넘깁니다.
            }
        }

        /// <summary>단계의 시작 시간을 초기화하고 표시 컴포넌트에 이전·새 단계를 알립니다.</summary>
        private void SetState(State next)
        {
            State previous = _state;
            _state = next;
            _stateTimer = 0f;
            OnStateChanged?.Invoke(previous, next);
        }
    }
}
