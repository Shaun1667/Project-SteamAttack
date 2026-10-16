using UnityEngine;
using UnityEngine.InputSystem;
using YPH;

namespace NGH
{
    /// <summary>
    /// 병합용 연결 (NGH) — CHG 플레이어의 입력·무기 칸과 YPH 화승총·조준·증기를 잇는다. (CombatTestInput 대체)
    /// - 좌클릭(화승총): 쏠 수 있을 때만 사격 동작을 시작하고(ShotGate), 사격 동작이 시작되면(ShotStarted) 발사
    /// - 우클릭(화승총 장착 + 발도 중): 조준. 조준 배율을 CHG 카메라 감도·이동 속도에 반영하고, 끝나면 1로 되돌림
    /// - CHG 근접 명중(OnHitLanded) → YPH 증기 회복 (약공격만 회복, 강공격은 회복 없음)
    /// - 강공격 시작(HeavyAttackGate) → YPH 증기 지불. 모자라면 강공격이 나가지 않음
    /// YPH와 CHG가 서로를 직접 참조하지 않도록 이 컴포넌트가 가운데서만 연결한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatBridge : MonoBehaviour
    {
        [Header("CHG")]
        [SerializeField] private PlayerController player;
        [SerializeField] private WeaponHolder weapons;
        [SerializeField] private ThirdPersonCamera playerCamera;
        [Tooltip("무기 칸에서 화승총으로 쓸 NGH_Weapon")]
        [SerializeField] private Weapon gunWeapon;

        [Header("YPH")]
        [SerializeField] private SteamGun gun;
        [SerializeField] private AimController aim;
        [SerializeField] private SteamHitRefill hitRefill;

        [Header("Debug")]
        [SerializeField] private bool logActions = true;

        private Weapon Equipped => weapons != null && !weapons.IsSwapping ? weapons.Current : null;
        private bool GunReady => player != null && player.IsDrawn && gunWeapon != null && Equipped == gunWeapon;

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (weapons == null && player != null) weapons = player.GetComponent<WeaponHolder>();
            if (playerCamera == null) playerCamera = FindAnyObjectByType<ThirdPersonCamera>();
        }

        private void OnEnable()
        {
            if (player != null)
            {
                player.ShotStarted += HandleShotStarted;
                player.OnHitLanded += HandleMeleeHit;
                player.ShotGate = CanStartShot;
                if (hitRefill != null) player.HeavyAttackGate = hitRefill.TryPayHeavyAttack;
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.ShotStarted -= HandleShotStarted;
                player.OnHitLanded -= HandleMeleeHit;
                player.ShotGate = null;
                player.HeavyAttackGate = null;
                player.MoveSpeedMultiplier = 1f;
            }
            if (playerCamera != null) playerCamera.SensitivityMultiplier = 1f;
            if (aim != null) aim.SetAiming(false);
        }

        private void Update()
        {
            if (player == null) return;

            Mouse mouse = Mouse.current;
            bool canAct = mouse != null && !player.IsDead && !ControlsOverlay.IsOpen;

            // 조준: 화승총 칸 + 발도 중 + 우클릭 유지
            if (aim != null) aim.SetAiming(canAct && GunReady && mouse.rightButton.isPressed);
        }

        private void LateUpdate()
        {
            float sensitivity = aim != null ? aim.SensitivityMultiplier : 1f;
            float moveSpeed = aim != null ? aim.MoveSpeedMultiplier : 1f;
            if (playerCamera != null) playerCamera.SensitivityMultiplier = sensitivity;
            if (player != null) player.MoveSpeedMultiplier = moveSpeed;
        }

        /// <summary>화승총은 조준 중이고 쏠 수 있을 때만 사격 동작을 시작한다. 못 쏘면 YPH가 실패 피드백(딸깍·증기 부족)을 낸다</summary>
        private bool CanStartShot(Weapon weapon)
        {
            if (weapon != gunWeapon || gun == null) return true;      // 화승총이 아니면 관여하지 않음
            bool aiming = GunReady && Mouse.current != null && Mouse.current.rightButton.isPressed;
            if (aiming && gun.CanFire) return true;
            gun.NotifyFireBlocked();
            Log("사격 막힘: 조준 " + aiming + ", 상태 " + gun.CurrentState);
            return false;
        }

        private void HandleShotStarted(Weapon weapon)
        {
            if (weapon == null || weapon != gunWeapon || gun == null) return;
            bool fired = gun.TryFire();
            Log("발사 " + fired + ", 상태 " + gun.CurrentState);
        }

        private void HandleMeleeHit(Damageable target)
        {
            if (hitRefill == null || target == null) return;
            // 적 쪽에 YPH 피해 대상(예: EnemyDamageRelay)이 있으면 그걸로, 없으면 NGH_Damageable을 감싸서 전달
            IDamageable damageable = target.GetComponentInParent<IDamageable>();
            bool heavy = player != null && player.CurrentAction == PlayerController.ActionState.HeavyAttack;
            hitRefill.NotifyHit(damageable ?? new ChgTarget(target), heavy);
        }

        private void Log(string message)
        {
            if (logActions) Debug.Log("[CHG_CombatBridge] " + message, this);
        }

        /// <summary>CHG 표적(Damageable)을 YPH 증기 회복 판정에 넘기기 위한 얇은 포장</summary>
        private sealed class ChgTarget : IDamageable
        {
            private readonly Damageable target;

            public ChgTarget(Damageable target) { this.target = target; }

            public bool IsAlive => target != null && target.IsAlive;
            public void TakeDamage(float amount, Vector3 hitFrom) { if (target != null) target.TakeDamage(amount, hitFrom); }
            public bool CompareTag(string tag) => target != null && target.CompareTag(tag);
        }
    }
}
