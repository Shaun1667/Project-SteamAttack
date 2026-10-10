using UnityEngine;
using UnityEngine.InputSystem;
using NGH;
using YPH;

namespace CHG
{
    /// <summary>
    /// 병합용 연결 (NGH) — CHG 플레이어의 입력·무기 칸과 YPH 총·수류탄·조준을 잇는다. (CombatTestInput 대체)
    /// - 좌클릭: CHG가 원거리 장비로 사격 동작을 시작하면(ShotStarted) 장착한 장비에 따라 총 발사 / 수류탄 투척
    /// - 우클릭(총 장착 + 발도 중): 조준. 조준 배율을 CHG 카메라 감도·이동 속도에 반영하고, 끝나면 1로 되돌림
    /// - R(총 장착 중): 탄 주머니 충전 / G: 수류탄 제작 (장비와 무관)
    /// - CHG 근접 명중(OnHitLanded) → YPH 증기 회복
    /// YPH와 CHG가 서로를 직접 참조하지 않도록 이 컴포넌트가 가운데서만 연결한다.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatBridge : MonoBehaviour
    {
        [Header("CHG")]
        [SerializeField] private PlayerController player;
        [SerializeField] private WeaponHolder weapons;
        [SerializeField] private ThirdPersonCamera playerCamera;
        [Tooltip("무기 칸에서 총으로 쓸 NGH_Weapon")]
        [SerializeField] private Weapon gunWeapon;
        [Tooltip("무기 칸에서 수류탄으로 쓸 NGH_Weapon")]
        [SerializeField] private Weapon grenadeWeapon;

        [Header("YPH")]
        [SerializeField] private SteamGun gun;
        [SerializeField] private SteamGrenadeThrower thrower;
        [SerializeField] private GrenadeHandView grenadeHandView;
        [SerializeField] private AimController aim;
        [SerializeField] private SteamHitRefill hitRefill;

        [Header("키")]
        [Tooltip("총 장착 중 탄 주머니 충전")]
        [SerializeField] private Key chargeKey = Key.R;
        [Tooltip("수류탄 제작 (장비와 무관)")]
        [SerializeField] private Key craftKey = Key.G;

        [Header("Debug")]
        [SerializeField] private bool logActions = true;

        private bool grenadeShown;
        private bool grenadeShownInitialized;

        private Weapon Equipped => weapons != null && !weapons.IsSwapping ? weapons.Current : null;
        private bool GunReady => player != null && player.IsDrawn && gunWeapon != null && Equipped == gunWeapon;
        private bool GrenadeReady => player != null && player.IsDrawn && grenadeWeapon != null && Equipped == grenadeWeapon;

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
            }
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.ShotStarted -= HandleShotStarted;
                player.OnHitLanded -= HandleMeleeHit;
                player.MoveSpeedMultiplier = 1f;
            }
            if (playerCamera != null) playerCamera.SensitivityMultiplier = 1f;
            if (aim != null) aim.SetAiming(false);
        }

        private void Update()
        {
            if (player == null) return;

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool canAct = keyboard != null && mouse != null && !player.IsDead && !ControlsOverlay.IsOpen;

            // 손의 수류탄 표시: 수류탄 칸 + 발도 중일 때만
            bool showGrenade = GrenadeReady;
            if (grenadeHandView != null && (!grenadeShownInitialized || showGrenade != grenadeShown))
            {
                grenadeHandView.SetEquipped(showGrenade);
                grenadeShown = showGrenade;
                grenadeShownInitialized = true;
            }

            // 조준: 총 칸 + 발도 중 + 우클릭 유지
            if (aim != null) aim.SetAiming(canAct && GunReady && mouse.rightButton.isPressed);

            if (!canAct) return;

            if (gun != null && GunReady && keyboard[chargeKey].wasPressedThisFrame)
            {
                bool started = gun.TryChargePouch();
                Log("주머니 충전 " + started + ", 상태 " + gun.CurrentState);
            }
            if (thrower != null && keyboard[craftKey].wasPressedThisFrame)
            {
                bool started = thrower.TryCraft();
                Log("수류탄 제작 " + started + ", 소지 " + thrower.HeldCount + "/" + thrower.MaxHeld);
            }
        }

        private void LateUpdate()
        {
            float sensitivity = aim != null ? aim.SensitivityMultiplier : 1f;
            float moveSpeed = aim != null ? aim.MoveSpeedMultiplier : 1f;
            if (playerCamera != null) playerCamera.SensitivityMultiplier = sensitivity;
            if (player != null) player.MoveSpeedMultiplier = moveSpeed;
        }

        private void HandleShotStarted(Weapon weapon)
        {
            if (weapon == null) return;

            if (weapon == gunWeapon && gun != null)
            {
                bool fired = gun.TryFire();
                Log("발사 " + fired + ", 약실 " + (gun.ChamberLoaded ? 1 : 0) + ", 주머니 " + gun.PouchRounds + "/" + gun.PouchCapacity + ", 상태 " + gun.CurrentState);
            }
            else if (weapon == grenadeWeapon && thrower != null)
            {
                bool thrown = thrower.TryThrow();
                Log("수류탄 투척 " + thrown + ", 소지 " + thrower.HeldCount + "/" + thrower.MaxHeld);
            }
        }

        private void HandleMeleeHit(Damageable target)
        {
            if (hitRefill == null || target == null) return;
            // 적 쪽에 YPH 피해 대상(예: EnemyDamageRelay)이 있으면 그걸로, 없으면 NGH_Damageable을 감싸서 전달
            IDamageable damageable = target.GetComponentInParent<IDamageable>();
            hitRefill.NotifyHit(damageable ?? new ChgTarget(target));
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
