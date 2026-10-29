using NGH;
using UnityEngine;
using UnityEngine.InputSystem;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 시연 안내 문구 (기획안 v4 2장 · 통합 기획서 5-1)
    ///
    /// - 첫 받아치기: 받아칠 수 있는 적 공격(YPH EnemyAI 예고)이 처음 시작되면, 시간을 느리게 하고 "CTRL 받아치기"를 띄운다.
    ///   CTRL을 누르거나 공격이 닿을 때가 되면 원래 빠르기로 돌아온다. 한 번만.
    /// - 시간 역행 예고: 체력이 절반 아래로 처음 떨어지면 "Q 시간 역행"을 띄운다. 한 번만.
    /// - 시간 역행으로만 피하는 보스 공격 직전에는 보스 쪽(BHS)에서 TutorialPrompts.RewindWarning()을 부른다.
    ///
    /// 플레이어(NGH_Player)에 붙여 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public class TutorialPrompts : MonoBehaviour
    {
        [SerializeField] private PlayerController player;

        [Header("첫 받아치기")]
        [SerializeField] private bool parryPrompt = true;
        [SerializeField, Range(0.05f, 1f)] private float parrySlowScale = 0.25f;
        [Tooltip("슬로 모션이 가장 길게 이어지는 시간(실제 초)")]
        [SerializeField, Min(0.1f)] private float parrySlowMax = 1.2f;

        [Header("시간 역행 예고")]
        [SerializeField] private bool rewindHint = true;

        private bool parryShown, rewindShown;
        private float slowUntil = -1f;
        private float prevScale = 1f;

        /// <summary>시간 역행으로만 피할 수 있는 공격 직전에 부른다 (BHS 보스 패턴용)</summary>
        public static void RewindWarning(float seconds = 2.5f) => NmjSubtitle.Key("Q", "피할 수 없다, 되감아라", seconds, true);

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
        }

        private void OnEnable()
        {
            EnemyAI.ParryableAttackStarted += OnParryable;
            if (player != null) player.HpChanged += OnHpChanged;
        }

        private void OnDisable()
        {
            EnemyAI.ParryableAttackStarted -= OnParryable;
            if (player != null) player.HpChanged -= OnHpChanged;
            EndSlow();
        }

        private void OnParryable(EnemyAI enemy, float timeToHit)
        {
            if (!parryPrompt || parryShown || PauseMenu.IsOpen) return;
            parryShown = true;
            NGH.ReviewedAudio.PlayUi("Initial/11_UI/Tutorial_v03");
            NmjSubtitle.Key("CTRL", "받아치기", 2.5f);
            // 공격이 닿기 조금 전까지만 느리게 (실제 시간으로 환산)
            prevScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = parrySlowScale;
            float realToHit = Mathf.Max(0.1f, timeToHit) / parrySlowScale;
            slowUntil = Time.unscaledTime + Mathf.Min(parrySlowMax, realToHit);
        }

        private void OnHpChanged(int hp, int max)
        {
            if (!rewindHint || rewindShown || hp <= 0 || hp * 2 > max) return;
            rewindShown = true;
            NGH.ReviewedAudio.PlayUi("Initial/11_UI/Tutorial_v03");
            NmjSubtitle.Key("Q", "시간 역행, 5초 전으로", 3f);
        }

        private void Update()
        {
            if (slowUntil < 0f) return;
            // ESC 메뉴로 멈춘 동안은 슬로 모션 시간도 멈춘다 (메뉴를 닫으면 이어서)
            if (PauseMenu.IsOpen) { slowUntil += Time.unscaledDeltaTime; return; }
            Keyboard kb = Keyboard.current;
            bool pressed = kb != null && (kb.leftCtrlKey.wasPressedThisFrame || kb.rightCtrlKey.wasPressedThisFrame);
            if (pressed || Time.unscaledTime >= slowUntil) EndSlow();
        }

        private void EndSlow()
        {
            if (slowUntil < 0f) return;
            slowUntil = -1f;
            if (!PauseMenu.IsOpen) Time.timeScale = prevScale;
        }
    }
}
