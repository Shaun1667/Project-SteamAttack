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
    /// - YPH 받아치기(ParryController.ParryAttempted) → 환도를 들고 있으면 CHG 칼 패링 모션. 성공하면 받아친 적을 보고 칼이 부딪히는 불꽃
    ///   (화승총을 들고 있으면 YPH RifleMotion이 따로 재생)
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
        [Tooltip("받아치기 판정·신호. 비우면 부모에서 찾음 (NGH_Player 루트)")]
        [SerializeField] private ParryController parry;

        [Header("칼 받아치기 연출")]
        [Tooltip("받아치기 성공 때 칼이 부딪히는 자리에 만드는 불꽃 (비우면 생략)")]
        [SerializeField] private GameObject parrySparkPrefab;
        [Tooltip("받아치기 시작부터 불꽃이 튀는 시점까지 시간(초). 칼을 쳐올려 부딪히는 순간(클립 약 65%)에 맞춤")]
        [SerializeField, Min(0f)] private float parrySparkDelay = 0.27f;
        [Tooltip("불꽃 위치: 칼 손잡이에서 칼날 방향으로 이만큼(m). 칼날 가운데쯤")]
        [SerializeField] private float parrySparkBladeOffset = 0.55f;
        [Tooltip("칼을 찾지 못했을 때 불꽃 위치: 플레이어 발 기준 높이(m)")]
        [SerializeField] private float parrySparkHeight = 1.45f;
        [Tooltip("칼을 찾지 못했을 때 불꽃 위치: 플레이어 앞쪽 거리(m)")]
        [SerializeField] private float parrySparkForward = 0.75f;
        [Tooltip("불꽃을 없애기까지 시간(초)")]
        [SerializeField, Min(0.1f)] private float parrySparkLifetime = 1.5f;
        [Tooltip("칼이 부딪히는 순간 플레이어 동작을 잠깐 멈추는 시간(초). 0 = 멈추지 않음")]
        [SerializeField, Min(0f)] private float parryHitStop = 0.07f;

        [Header("Debug")]
        [SerializeField] private bool logActions = true;

        private Weapon Equipped => weapons != null && !weapons.IsSwapping ? weapons.Current : null;
        private bool GunReady => player != null && player.IsDrawn && gunWeapon != null && Equipped == gunWeapon;

        private IParryTarget parriedTarget;   // TargetParried → ParryAttempted(true) 사이에만 유효

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            if (weapons == null && player != null) weapons = player.GetComponent<WeaponHolder>();
            if (playerCamera == null) playerCamera = FindAnyObjectByType<ThirdPersonCamera>();
            if (parry == null) parry = GetComponentInParent<ParryController>();
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
            if (parry != null)
            {
                parry.TargetParried += HandleTargetParried;
                parry.ParryAttempted += HandleParryAttempted;
            }
        }

        private void OnDisable()
        {
            if (parry != null)
            {
                parry.TargetParried -= HandleTargetParried;
                parry.ParryAttempted -= HandleParryAttempted;
            }
            parriedTarget = null;
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

        // 성공 때 TargetParried가 ParryAttempted(true)보다 먼저 옴. 받아친 대상을 기억해 두었다가 그쪽을 보게 함
        private void HandleTargetParried(IParryTarget target) => parriedTarget = target;

        /// <summary>환도를 들고 있을 때만 칼 패링 모션. 화승총이면 RifleMotion이 맡으므로 관여하지 않음</summary>
        private void HandleParryAttempted(bool success)
        {
            IParryTarget target = parriedTarget;
            parriedTarget = null;
            if (player == null) return;
            Weapon weapon = Equipped;
            if (weapon != null && !weapon.isMelee) return;

            Vector3? lookAt = success && target != null ? target.ParryPoint : (Vector3?)null;
            bool played = player.PlayParryMotion(success, lookAt);
            Log("칼 받아치기 " + (success ? "성공" : "실패") + ", 모션 " + played);
            if (played && success && parrySparkPrefab != null) StartCoroutine(SpawnParrySpark());
        }

        private System.Collections.IEnumerator SpawnParrySpark()
        {
            if (parrySparkDelay > 0f) yield return new WaitForSeconds(parrySparkDelay);
            if (player == null || player.CurrentAction != PlayerController.ActionState.Parry) yield break;   // 피격 등으로 끊기면 생략
            Transform t = player.transform;
            Transform blade = weapons != null && weapons.Current != null ? weapons.Current.transform : null;
            Vector3 pos = blade != null ? blade.position + blade.forward * parrySparkBladeOffset
                                        : t.position + Vector3.up * parrySparkHeight + t.forward * parrySparkForward;
            GameObject spark = Instantiate(parrySparkPrefab, pos, Quaternion.LookRotation(-t.forward));
            Destroy(spark, parrySparkLifetime);

            // 칼이 부딪히는 손맛: 플레이어 동작만 잠깐 멈춤 (게임 시간은 그대로)
            Animator anim = player.animator;
            if (parryHitStop <= 0f || anim == null) yield break;
            float speed = anim.speed;
            anim.speed = 0f;
            yield return new WaitForSeconds(parryHitStop);
            // 그 사이 피격 등으로 다른 동작이 시작됐으면 그 동작이 정한 속도를 그대로 둠
            if (anim != null && anim.speed == 0f && player.CurrentAction == PlayerController.ActionState.Parry) anim.speed = speed;
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
