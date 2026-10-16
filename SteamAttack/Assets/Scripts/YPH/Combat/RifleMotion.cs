using NGH;
using UnityEngine;

namespace YPH
{
    /// <summary>
    /// 플레이어 루트에서 총을 든 상체 자세와 사격·받아치기 연출을 연결합니다.
    /// 실제 피해·증기·입력은 기존 담당자가 처리하고, 이 컴포넌트는 신호를 받아 모션만 재생합니다.
    /// </summary>
    [DisallowMultipleComponent]
    public class RifleMotion : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField, Tooltip("상태와 사격 신호를 읽을 플레이어입니다. 비우면 시작할 때 부모에서 찾습니다.")]
        private PlayerController _player;
        [SerializeField, Tooltip("현재 무기와 교체 상태를 읽습니다. 비우면 부모, 자식 순서로 한 번 찾습니다.")]
        private WeaponHolder _weapons;
        [SerializeField, Tooltip("Rifle 레이어가 있는 주인공 Animator입니다. 비우면 자식에서 한 번 찾습니다.")]
        private Animator _animator;
        [SerializeField, Tooltip("성공·실패 시도 신호를 받을 받아치기입니다. 비우면 같은 오브젝트에서 찾습니다.")]
        private ParryController _parry;

        [Header("상체 모션")]
        [SerializeField, Tooltip("상체 레이어 이름입니다. Animator와 다르면 경고 후 이 컴포넌트를 끕니다.")]
        private string _layerName = "Rifle";
        [SerializeField, Tooltip("사격 때 재생할 Rifle 레이어의 상태 이름입니다. Animator의 상태 이름과 같아야 합니다.")]
        private string _shootState = "RifleShoot";
        [SerializeField, Tooltip("받아치기 성공·실패 때 재생할 Rifle 레이어의 상태 이름입니다.")]
        private string _parryState = "RifleParry";
        [SerializeField, Min(0f), Tooltip("초당 상체 가중치 변화량입니다. 높이면 총 자세를 빨리 켜고 끄며, 0이면 전환이 멈춥니다. 기본 10은 연출용 값입니다.")]
        private float _blendSpeed = 10f;
        [SerializeField, Min(0f), Tooltip("사격·받아치기 모션으로 넘어가는 시간(초)입니다. 높이면 천천히 섞이고, 0이면 바로 바뀝니다. 기본 0.08은 연출용 값입니다.")]
        private float _crossFade = 0.08f;
        [SerializeField, Tooltip("총을 들고 받아치기를 시도할 때 처음부터 재생할 총구 파티클입니다. 각 파티클을 한 번씩 연결하며, 비우면 섬광만 생략합니다.")]
        private ParticleSystem[] _parryFlash;

        private int _layerIndex = -1;
        private int _shootHash;
        private int _parryHash;

        /// <summary>매 프레임 찾지 않도록 연결과 상태 번호를 시작할 때 준비합니다.</summary>
        private void Awake()
        {
            if (_player == null)
            {
                _player = GetComponentInParent<PlayerController>();
            }
            if (_weapons == null)
            {
                _weapons = GetComponentInParent<WeaponHolder>();
            }
            if (_weapons == null)
            {
                _weapons = GetComponentInChildren<WeaponHolder>();
            }
            if (_animator == null)
            {
                _animator = GetComponentInChildren<Animator>();
            }
            if (_parry == null)
            {
                _parry = GetComponent<ParryController>();
            }
            if (_animator != null)
            {
                _layerIndex = _animator.GetLayerIndex(_layerName);
            }
            if (_player == null || _weapons == null || _parry == null || _layerIndex < 0)
            {
                Debug.LogWarning("RifleMotion: 플레이어·무기·받아치기 연결과 상체 레이어 이름을 확인하세요.", this);
                enabled = false;
                return;
            }
            _shootHash = Animator.StringToHash(_layerName + "." + _shootState);
            _parryHash = Animator.StringToHash(_layerName + "." + _parryState);
        }

        /// <summary>켜져 있는 동안에만 실제 사격·받아치기 신호를 받습니다.</summary>
        private void OnEnable()
        {
            if (_layerIndex < 0)
            {
                return;
            }
            _player.ShotStarted += OnShotStarted;
            _parry.ParryAttempted += OnParryAttempted;
        }

        /// <summary>교체·납도·피격 등에서는 상체 가중치를 내려 기존 동작이 보이게 합니다.</summary>
        private void Update()
        {
            float target = IsHoldingRifle() ? 1f : 0f;
            _animator.SetLayerWeight(_layerIndex, Mathf.MoveTowards(_animator.GetLayerWeight(_layerIndex), target, _blendSpeed * Time.deltaTime));
        }

        /// <summary>구독을 해제하고 상체 자세를 돌려줘 재활성화 때 중복 재생을 막습니다.</summary>
        private void OnDisable()
        {
            if (_player != null)
            {
                _player.ShotStarted -= OnShotStarted;
            }
            if (_parry != null)
            {
                _parry.ParryAttempted -= OnParryAttempted;
            }
            // 컴포넌트를 껐을 때 마지막 총 자세가 남지 않게 합니다.
            if (_animator != null && _layerIndex >= 0)
            {
                _animator.SetLayerWeight(_layerIndex, 0f);
            }
        }

        /// <summary>총 장착뿐 아니라 발도·교체·행동 상태까지 같은 기준으로 검사합니다.</summary>
        private bool IsHoldingRifle()
        {
            return _player.IsDrawn && !_weapons.IsSwapping && _weapons.Current != null && !_weapons.Current.isMelee
                && !_player.IsDead && !_player.IsStandingUp && !_player.IsHitReacting
                && _player.CurrentAction != PlayerController.ActionState.Roll;
        }

        /// <summary>원거리 사격 신호만 상체 사격으로 바꾸며, 피해 판정은 기존 SteamGun이 맡습니다.</summary>
        private void OnShotStarted(Weapon weapon)
        {
            if (weapon != null && !weapon.isMelee && IsHoldingRifle())
            {
                _animator.CrossFadeInFixedTime(_shootHash, _crossFade, _layerIndex);
            }
        }

        /// <summary>총을 든 실제 시도에만 막기 모션과 섬광을 함께 재생합니다.</summary>
        private void OnParryAttempted(bool success)
        {
            if (!IsHoldingRifle())
            {
                return;
            }
            // 성공 여부는 R1이 처리합니다. 총의 막기·격발 연출은 실패한 시도에도 같습니다.
            _animator.CrossFadeInFixedTime(_parryHash, _crossFade, _layerIndex);
            if (_parryFlash == null)
            {
                return;
            }
            for (int i = 0; i < _parryFlash.Length; i++)
            {
                ParticleSystem flash = _parryFlash[i];
                if (flash == null)
                {
                    continue;
                }
                flash.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                flash.Play(false);
            }
        }
    }
}
