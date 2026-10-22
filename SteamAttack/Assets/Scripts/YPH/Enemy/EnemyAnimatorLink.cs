using System.Collections.Generic;
using UnityEngine;

namespace YPH
{
    /// <summary>기존 클립의 이벤트 시각과 파라미터를 연결합니다. 판정 시각은 미리 읽고 실제 이벤트는 수신만 합니다.</summary>
    [DisallowMultipleComponent]
    public class EnemyAnimatorLink : MonoBehaviour
    {
        [SerializeField, Tooltip("적 Animator입니다. 비우면 시작할 때 자식에서 찾습니다.")] private Animator _animator;
        [SerializeField, Tooltip("켜면 OnAttackHit 이벤트 시각을 판정 시간으로 사용합니다. 이벤트가 없으면 공격 부품의 초 값을 씁니다.")] private bool _useClipHitEvents = true;
        [SerializeField, Tooltip("배회 속도에서 Animator Speed에 넣을 값입니다. 높이면 더 빠른 이동 동작을 고릅니다.")] private float _walkAnimSpeedValue = 0.5f;
        [SerializeField, Tooltip("추적 속도에서 Animator Speed에 넣을 값입니다. 높이면 더 빠른 이동 동작을 고릅니다.")] private float _runAnimSpeedValue = 1f;
        [SerializeField, Tooltip("이동 파라미터를 부드럽게 바꿀 시간(초)입니다. 높이면 동작 전환이 느긋해집니다.")] private float _speedDampTime = 0.1f;
        internal static readonly int AttackHash = Animator.StringToHash("Attack");
        internal static readonly int HeavyAttackHash = Animator.StringToHash("HeavyAttack");
        internal static readonly int FireHash = Animator.StringToHash("Fire");
        internal static readonly int AimHash = Animator.StringToHash("Aim");
        internal static readonly int KickHash = Animator.StringToHash("Kick");
        internal static readonly int ParriedHash = Animator.StringToHash("Parried");
        private static readonly int _dieHash = Animator.StringToHash("Die");
        private static readonly int _speedHash = Animator.StringToHash("Speed");
        private static readonly int _moveXHash = Animator.StringToHash("MoveX");
        private static readonly int _moveZHash = Animator.StringToHash("MoveZ");
        private static readonly int _inCombatHash = Animator.StringToHash("InCombat");
        private static readonly int _kickSpeedHash = Animator.StringToHash("KickSpeed");
        private static readonly int[] _attackTriggers = { AttackHash, HeavyAttackHash, FireHash, KickHash, ParriedHash };
        private readonly HashSet<int> _parameters = new HashSet<int>();
        private EnemyAI _brain;
        internal float AttackHitTime { get; private set; } = -1f;
        internal float HeavyHitTime { get; private set; } = -1f;
        internal float HeavyLength { get; private set; } = -1f;
        internal float KickHitTime { get; private set; } = -1f;
        internal float KickLength { get; private set; } = -1f;

        private void Awake()
        {
            _brain = GetComponent<EnemyAI>();
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
            if (_animator == null || _animator.runtimeAnimatorController == null)
            {
                return;
            }
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
            {
                _parameters.Add(parameter.nameHash);
            }
            ReadClipHitEvents();
        }

        private void ReadClipHitEvents()
        {
            if (!_useClipHitEvents)
            {
                return;
            }
            bool found = false;
            foreach (AnimationClip clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip == null)
                {
                    continue;
                }
                foreach (AnimationEvent hit in clip.events)
                {
                    if (hit.functionName != EnemyAI.HitEventName)
                    {
                        continue;
                    }
                    found = true;
                    switch (hit.stringParameter)
                    {
                        case "Attack": AttackHitTime = hit.time; break;
                        case "HeavyAttack": HeavyHitTime = hit.time; HeavyLength = clip.length; break;
                        case "Kick": KickHitTime = hit.time; KickLength = clip.length; break;
                        default:
                            Debug.LogWarning("[EnemyAI:" + name + "] 클립 " + clip.name + "의 OnAttackHit String은 Attack / HeavyAttack / Kick 중 하나여야 합니다.", this);
                            break;
                    }
                    _brain.Log("타격 이벤트: " + clip.name + " → " + hit.stringParameter + " " + hit.time.ToString("0.00") + "초");
                }
            }
            if (found)
            {
                EnemyAnimationEventReceiver receiver = _animator.GetComponent<EnemyAnimationEventReceiver>();
                if (receiver == null)
                {
                    receiver = _animator.gameObject.AddComponent<EnemyAnimationEventReceiver>();
                }
                receiver.Bind(_brain);
            }
        }

        internal void UpdateMovement(EnemyAI.State state, Vector3 direction, float speed, float walkSpeed, float chaseSpeed, bool stopped)
        {
            if (_animator == null || stopped)
            {
                return;
            }
            bool moving = (state == EnemyAI.State.Wander || state == EnemyAI.State.Chase) && speed > 0.01f;
            float value = 0f;
            if (moving)
            {
                float blend = Mathf.InverseLerp(walkSpeed, Mathf.Max(walkSpeed + 0.01f, chaseSpeed), speed);
                value = speed <= walkSpeed ? _walkAnimSpeedValue * speed / Mathf.Max(0.01f, walkSpeed)
                    : Mathf.Lerp(_walkAnimSpeedValue, _runAnimSpeedValue, blend);
            }
            SetFloat(_speedHash, value, true);
            Vector3 local = moving ? transform.InverseTransformDirection(direction * speed) : Vector3.zero;
            SetFloat(_moveXHash, local.x, true);
            SetFloat(_moveZHash, local.z, true);
        }

        internal void SetPlaybackSpeed(float speed)
        {
            if (_animator != null)
            {
                _animator.speed = speed;
            }
        }

        internal void SetCombat(bool aware)
        {
            if (CanSet(_inCombatHash))
            {
                _animator.SetBool(_inCombatHash, aware);
            }
        }

        internal void SetKickSpeed(float speed) => SetFloat(_kickSpeedHash, speed, false);

        /// <summary>조준 파라미터가 있는 Animator에만 조준 상태를 전달합니다.</summary>
        internal void SetAiming(bool aiming)
        {
            if (CanSet(AimHash))
            {
                _animator.SetBool(AimHash, aiming);
            }
        }

        internal void Play(int trigger)
        {
            ResetAttackTriggers();
            if (CanSet(trigger))
            {
                _animator.SetTrigger(trigger);
            }
        }

        internal void StopForDeath()
        {
            SetAiming(false);
            SetFloat(_speedHash, 0f, false);
            SetFloat(_moveXHash, 0f, false);
            SetFloat(_moveZHash, 0f, false);
            Play(_dieHash);
        }

        internal void Rebind()
        {
            if (_animator == null)
            {
                return;
            }
            _animator.Rebind();
            _animator.speed = 1f;
        }

        private bool CanSet(int hash) => _animator != null && _animator.isActiveAndEnabled && _parameters.Contains(hash);

        private void SetFloat(int hash, float value, bool damp)
        {
            if (!CanSet(hash))
            {
                return;
            }
            if (damp)
            {
                _animator.SetFloat(hash, value, _speedDampTime, Time.deltaTime);
            }
            else
            {
                _animator.SetFloat(hash, value);
            }
        }

        private void ResetAttackTriggers()
        {
            foreach (int hash in _attackTriggers)
            {
                if (CanSet(hash))
                {
                    _animator.ResetTrigger(hash);
                }
            }
        }
    }
}
