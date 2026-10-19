using UnityEngine;
using UnityEngine.InputSystem;
using YPH;

namespace NGH
{
    /// <summary>
    /// 테스트용 빨리뽑기 패링 (플레이어 동작 없이 판정만). 씬의 플레이어(NGH_Player)에 붙여서 사용.
    /// 플레이어 코드(NGH)는 고치지 않고, 증기는 SteamTank, 적은 CHG_EnemyAI의 공개 함수로 처리한다.
    ///
    /// - Ctrl 입력 → 증기 20 소비 (성공 여부와 관계없이). 증기가 20 미만이면 패링이 나가지 않음
    /// - 쿨타임 2초 (마구 누르기 방지)
    /// - 근처(Parry Range)의 적 중 패링 가능 공격을 하는 적이 있고,
    ///   동심원이 겹치는 순간(공격 판정 0.15초 전) ±0.15초 안에 눌렀으면 성공
    /// - 성공: 적 공격 취소 → 피해 5(빨리뽑기) → 비틀거림 + 약점 노출, 증기 +20
    /// - 실패: 증기만 쓰고 적의 공격은 그대로 들어옴
    /// - 입력 제한: 피격·회피(구르기)·공격(약공격 연타·강공격·사격)·사망·일어나는 중에는 Ctrl을 눌러도 무시 (증기·쿨타임 소모 없음)
    /// 나중에 플레이어 담당이 패링 동작을 만들면 이 판정(TryParryNearby)만 가져가서 쓰면 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlayerParry : MonoBehaviour
    {
        [Header("입력")]
        [Tooltip("왼쪽·오른쪽 Ctrl 모두 사용")]
        [SerializeField] private bool useCtrl = true;

        [Header("증기")]
        [SerializeField, Min(0f)] private float steamCost = 20f;
        [SerializeField, Min(0f)] private float steamOnSuccess = 20f;

        [Header("피해")]
        [Tooltip("패링 성공 시 적에게 주는 피해 (빨리뽑기). 약점 노출 전에 들어가 약점 배율은 붙지 않음")]
        [SerializeField, Min(0f)] private float parryDamage = 5f;
        [Tooltip("비우면 씬에서 찾음")]
        [SerializeField] private SteamTank steamTank;

        [Header("판정")]
        [SerializeField, Min(0f)] private float cooldown = 2f;
        [Tooltip("이 거리 안의 적만 패링 대상")]
        [SerializeField, Min(0.5f)] private float parryRange = 5f;

        [Header("Debug")]
        [SerializeField] private bool logParry = true;

        /// <summary>패링을 시도했을 때 (성공 여부)</summary>
        public static event System.Action<PlayerParry, bool> ParryAttempted;

        public float CooldownRemaining => Mathf.Max(0f, nextAllowedTime - Time.time);

        private PlayerController player;
        private float nextAllowedTime;

        private void Awake()
        {
            player = GetComponentInParent<PlayerController>();
        }

        private void Start()
        {
            if (steamTank == null)
            {
                steamTank = FindAnyObjectByType<SteamTank>();
            }
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null || !useCtrl || ControlsOverlay.IsOpen)   // ESC 메뉴·조작 안내 중엔 받아치기 안 함
            {
                return;
            }
            if (kb.leftCtrlKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame)
            {
                TryParryNearby();
            }
        }

        // 패링을 쓸 수 없는 플레이어 상태 (공개 값만 읽음)
        private bool IsBlockedByPlayerState(out string reason)
        {
            if (player.IsDead) { reason = "사망"; return true; }
            if (player.IsStandingUp) { reason = "일어나는"; return true; }
            if (player.IsHitReacting) { reason = "피격"; return true; }
            if (player.CurrentAction == PlayerController.ActionState.Roll) { reason = "회피"; return true; }
            if (IsAttackAction(player.CurrentAction)) { reason = "공격"; return true; }
            reason = null;
            return false;
        }

        // 공격 동작 (지금은 패링 모션이 없어 공격을 캔슬할 수 없으므로 공격 중 패링 불가)
        private static bool IsAttackAction(PlayerController.ActionState a)
        {
            switch (a)
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

        /// <summary>패링 시도. 성공하면 true.</summary>
        public bool TryParryNearby()
        {
            if (player != null && IsBlockedByPlayerState(out string reason))
            {
                Log("패링 불가 — " + reason + " 중 (증기·쿨타임 소모 없음)");
                return false;
            }
            if (Time.time < nextAllowedTime)
            {
                Log("패링 쿨타임 " + CooldownRemaining.ToString("0.0") + "초 남음");
                return false;
            }

            if (steamTank != null && !steamTank.TryConsume(steamCost))
            {
                Log("증기 부족 — 패링 불가 (필요 " + steamCost + ")");
                return false;
            }
            nextAllowedTime = Time.time + cooldown;

            // 범위 안에서 타이밍이 맞는 적 중 가장 가까운 적
            EnemyAI best = null;
            float bestDistance = float.MaxValue;
            foreach (EnemyAI enemy in EnemyAI.ActiveEnemies)
            {
                if (enemy == null || !enemy.IsParryWindowOpen)
                {
                    continue;
                }
                float d = Vector3.Distance(enemy.transform.position, transform.position);
                if (d > parryRange)
                {
                    continue;
                }
                if (Mathf.Abs(Time.time - enemy.ParryOverlapTime) > EnemyAI.ParryHalfWindow)
                {
                    continue;
                }
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = enemy;
                }
            }

            bool success = best != null && best.TryParry(parryDamage, transform.position);
            if (success)
            {
                if (steamTank != null)
                {
                    steamTank.Refill(steamOnSuccess);
                }
                Log("패링 성공 → " + best.name + " (피해 " + parryDamage + ", 증기 +" + steamOnSuccess + ")");
            }
            else
            {
                Log("패링 실패 (증기 -" + steamCost + ")" + DescribeMiss());
            }

            ParryAttempted?.Invoke(this, success);
            return success;
        }

        // 실패 이유 (가장 가까운 패링 가능 공격 기준으로 빠름/늦음)
        private string DescribeMiss()
        {
            foreach (EnemyAI enemy in EnemyAI.ActiveEnemies)
            {
                if (enemy != null && enemy.IsParryWindowOpen
                    && Vector3.Distance(enemy.transform.position, transform.position) <= parryRange)
                {
                    float diff = Time.time - enemy.ParryOverlapTime;
                    return diff < 0f ? " — " + (-diff).ToString("0.00") + "초 빠름" : " — " + diff.ToString("0.00") + "초 늦음";
                }
            }
            return " — 패링할 공격 없음";
        }

        private void Log(string message)
        {
            if (logParry)
            {
                Debug.Log("[chg_PlayerParry] " + message, this);
            }
        }
    }
}
