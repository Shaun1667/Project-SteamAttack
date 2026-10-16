using System;
using NGH;
using UnityEngine;
using UnityEngine.InputSystem;

namespace YPH
{
    /// <summary>
    /// 플레이어의 받아치기 입력·상태·거리·시각을 검사합니다. 플레이어 루트 또는 자식에 붙이고 증기통을 연결합니다.
    /// 대상의 공격 취소는 IParryTarget, 화면 표시는 ParryRingView, 모션은 ParryAttempted 구독자가 맡습니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class ParryController : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField, Tooltip("상태를 읽을 플레이어입니다. 비우면 시작할 때 부모에서 한 번 찾고, 없으면 받아치기를 끕니다.")]
        private PlayerController _player;
        [SerializeField, Tooltip("증기 소모 옵션에 사용할 스팀백입니다. 비우면 소모 옵션을 켠 시도는 처리하지 못합니다.")]
        private SteamTank _tank;

        [Header("입력·판정")]
        [SerializeField, Tooltip("받아치기 기본 키입니다. None이면 이 입력을 사용하지 않습니다.")]
        private Key _parryKey = Key.LeftCtrl;
        [SerializeField, Tooltip("받아치기 보조 키입니다. None이면 기본 키만 사용합니다.")]
        private Key _altParryKey = Key.RightCtrl;
        [SerializeField, Min(0f), Tooltip("겹침 시각 전후 각각 허용하는 시간입니다. 기본 0.1초(D-084). 높이면 쉬워집니다. 적의 공격 판정 뒤에는 성공하지 않습니다.")]
        private float _judgeHalfWindow = 0.1f;
        [SerializeField, Min(0f), Tooltip("플레이어에서 대상의 ParryPoint까지 허용하는 거리(m)입니다. 높이면 먼 적도 받아칩니다. 높이 차이도 포함합니다.")]
        private float _range = 5f;
        [SerializeField, Min(0f), Tooltip("실제 시도 뒤 다시 시도하기까지의 시간(초)입니다. 높이면 연속 사용이 어려워지며, 0이면 대기하지 않습니다.")]
        private float _cooldown = 2f;
        [SerializeField, Min(0f), Tooltip("성공할 때 대상에게 주는 피해입니다. 0이면 공격 취소와 경직만 적용하며, 높이면 적을 쓰러뜨릴 수도 있습니다.")]
        private float _parryDamage;
        [SerializeField, Tooltip("켜면 약공격·연속 공격·강공격·사격 중 시도를 막습니다. 꺼도 사망·기상·피격·구르기 중에는 막습니다.")]
        private bool _blockDuringAttack = true;

        [Header("증기 옵션 — 기본은 사용 안 함")]
        [SerializeField, Tooltip("켜면 성공 여부와 관계없이 시도 비용을 먼저 냅니다. 부족하면 시도·쿨타임·결과 신호가 발생하지 않습니다.")]
        private bool _consumeSteamOnAttempt;
        [SerializeField, Min(0f), Tooltip("시도 비용입니다. 위 옵션을 켰을 때만 적용되며, 높이면 스팀백을 빨리 소모합니다. 0이면 무료입니다.")]
        private float _attemptSteamCost = 5f;
        [SerializeField, Tooltip("켜면 실제 시도에 실패했을 때 추가 비용을 냅니다. 기본은 꺼짐이며, 부족하면 잔량을 유지하고 스팀백의 부족 신호만 보냅니다.")]
        private bool _consumeSteamOnFail;
        [SerializeField, Min(0f), Tooltip("실패 추가 비용입니다. 높이면 실패 부담이 커집니다. 0이면 차감하지 않으며, 시도 비용과 함께 켜면 두 비용을 모두 냅니다.")]
        private float _failSteamCost = 10f;
        [Header("확인")]
        [SerializeField, Tooltip("켜면 Unity Editor에서 시도 성공·실패·차단을 기록합니다. 플레이어 빌드에는 이 로그가 들어가지 않습니다.")]
        private bool _logParry = true;

        private Transform _origin;
        private float _nextAllowedTime;

        /// <summary>UI도 이 값을 읽어 실제 판정과 붉은 표시의 시간 폭을 맞춥니다.</summary>
        public float JudgeHalfWindow => _judgeHalfWindow;
        /// <summary>다시 시도할 수 있기까지 남은 게임 시간(초)입니다.</summary>
        public float CooldownRemaining => Mathf.Max(0f, _nextAllowedTime - Time.time);
        /// <summary>실제 시도의 성공 여부를 한 번 알립니다. NGH 모션 연결용이며, 차단된 입력에는 발생하지 않습니다.</summary>
        public event Action<bool> ParryAttempted;
        /// <summary>성공한 대상을 결과 신호보다 먼저 알립니다. UI는 이때 성공 연출을 시작합니다.</summary>
        public event Action<IParryTarget> TargetParried;

        /// <summary>자식에 붙여도 실제 플레이어 위치를 쓰도록 상태·위치 참조를 시작 때 한 번 저장합니다.</summary>
        private void Awake()
        {
            if (_player == null)
            {
                _player = GetComponentInParent<PlayerController>();
            }
            if (_player == null)
            {
                Debug.LogError("[ParryController] PlayerController를 연결해야 합니다.", this);
                enabled = false;
                return;
            }
            _origin = _player.transform;
        }

        /// <summary>두 키를 함께 눌러도 한 프레임에 한 번만 시도합니다.</summary>
        private void Update()
        {
            Keyboard keys = Keyboard.current;
            if (keys != null && ((_parryKey != Key.None && keys[_parryKey].wasPressedThisFrame)
                || (_altParryKey != Key.None && keys[_altParryKey].wasPressedThisFrame)))
            {
                TryParry();
            }
        }

        /// <summary>상태·쿨타임·비용을 통과한 시도만 판정합니다. 대상이 받아치기를 적용했을 때만 true입니다.</summary>
        public bool TryParry()
        {
            // 차단된 입력이 자원이나 모션을 소비하지 않도록 시도 비용보다 먼저 검사합니다.
            if (!isActiveAndEnabled || _player == null || IsBlockedByPlayerState() || CooldownRemaining > 0f)
            {
                Log("받아치기 차단: 플레이어 상태 또는 쿨타임");
                return false;
            }
            if (_consumeSteamOnAttempt && (_tank == null || !_tank.TryConsume(_attemptSteamCost)))
            {
                Log("받아치기 차단: 증기 부족 또는 증기통 연결 없음");
                return false;
            }

            _nextAllowedTime = Time.time + _cooldown;
            IParryTarget target = FindClosestTarget();
            bool success = target != null && target.ApplyParry(_parryDamage, _origin.position);
            if (success)
            {
                TargetParried?.Invoke(target);
            }
            else if (_consumeSteamOnFail && _tank != null)
            {
                _tank.TryConsume(_failSteamCost);
            }
            Log(success ? "받아치기 성공" : "받아치기 실패");
            ParryAttempted?.Invoke(success);
            return success;
        }

        /// <summary>NGH의 공개 상태만 읽습니다. 공격 허용 옵션은 구르기·피격·사망 제한을 해제하지 않습니다.</summary>
        private bool IsBlockedByPlayerState()
        {
            if (_player.IsDead || _player.IsStandingUp || _player.IsHitReacting || _player.CurrentAction == PlayerController.ActionState.Roll)
            {
                return true;
            }
            if (!_blockDuringAttack)
            {
                return false;
            }
            switch (_player.CurrentAction)
            {
                case PlayerController.ActionState.LightAttack:
                case PlayerController.ActionState.LightCombo2:
                case PlayerController.ActionState.LightCombo3:
                case PlayerController.ActionState.LightCombo4:
                case PlayerController.ActionState.LightCombo5:
                case PlayerController.ActionState.HeavyAttack:
                case PlayerController.ActionState.Shot:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>창·시간·거리를 모두 만족한 대상 중 한 명만 고릅니다. 적 종류에 의존하지 않아 보스에도 같은 규칙을 적용합니다.</summary>
        private IParryTarget FindClosestTarget()
        {
            IParryTarget closest = null;
            float closestDistance = float.PositiveInfinity;
            float rangeSquared = _range * _range;
            var targets = ParryTargets.Active;
            for (int i = 0; i < targets.Count; i++)
            {
                IParryTarget target = targets[i];
                // interface의 null 비교만으로는 파괴된 Unity 객체를 걸러낼 수 없습니다.
                if ((target as UnityEngine.Object) == null || (target is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                    || !target.IsParryWindowOpen || Mathf.Abs(Time.time - target.ParryOverlapTime) > _judgeHalfWindow)
                {
                    continue;
                }
                float distance = (target.ParryPoint - _origin.position).sqrMagnitude;
                if (distance <= rangeSquared && distance < closestDistance)
                {
                    closest = target;
                    closestDistance = distance;
                }
            }
            return closest;
        }

        /// <summary>개발 확인용 메시지는 빌드에서 호출 자체를 제외합니다.</summary>
        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void Log(string message)
        {
            if (_logParry)
            {
                Debug.Log(message, this);
            }
        }
    }
}
