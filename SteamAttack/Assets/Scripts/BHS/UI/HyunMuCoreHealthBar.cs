using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BHS.UI
{
    /// <summary>Read-only HUD for the four subcores and the exposed main core.</summary>
    [DisallowMultipleComponent]
    public sealed class HyunMuCoreHealthBar : MonoBehaviour
    {
        [Serializable]
        public sealed class CoreCell
        {
            public RectTransform fill;
            public RectTransform damageTrail;
            public Image hitLight;
            public TMP_Text label;
            public Image indicator;
            public CanvasGroup brokenMark;
            [NonSerialized] public float shown = 1f;
            [NonSerialized] public float trail = 1f;
            [NonSerialized] public float previous = 1f;
            [NonSerialized] public float trailHold;
            [NonSerialized] public float flash;
        }

        [Header("실제 보스 코어 연결 (체력은 읽기만 합니다)")]
        [SerializeField] private BossCoreController controller;
        [SerializeField] private BossCoreHealth[] subCores = new BossCoreHealth[4];
        [SerializeField] private BossCoreHealth mainCore;

        [Header("UI 프리팹 연결")]
        [SerializeField] private CanvasGroup visibility;
        [SerializeField] private CanvasGroup subCoreGroup;
        [SerializeField] private CanvasGroup mainCoreGroup;
        [SerializeField] private RectTransform safeArea;
        [SerializeField] private TMP_Text phaseLabel;
        [SerializeField] private TMP_Text statusLabel;
        [SerializeField] private CoreCell[] subCells = new CoreCell[4];
        [SerializeField] private CoreCell mainCell;

        [Header("연출")]
        [SerializeField, Min(0.01f)] private float fillSpeed = 4f;
        [SerializeField, Min(0f)] private float damageTrailDelay = 0.18f;
        [SerializeField, Min(0.01f)] private float damageTrailSpeed = 0.75f;
        [SerializeField, Min(0.05f)] private float phaseTransitionTime = 0.65f;
        [SerializeField, Min(0f)] private float deathHoldTime = 0.6f;
        [SerializeField] private Color liveLabelColor = new Color(0.69f, 0.77f, 0.78f, 1f);
        [SerializeField] private Color brokenLabelColor = new Color(0.39f, 0.44f, 0.46f, 1f);
        [SerializeField] private Color liveIndicatorColor = new Color(0.39f, 0.79f, 0.85f, 1f);

        private static readonly HashSet<HyunMuCoreHealthBar> Instances = new HashSet<HyunMuCoreHealthBar>();
        public static bool AnyVisible
        {
            get
            {
                foreach (var bar in Instances)
                    if (bar != null && bar.IsVisible) return true;
                return false;
            }
        }

        public bool IsVisible => isActiveAndEnabled && visibility != null && visibility.alpha > 0.01f;
        public bool ShowingMainCore => mainBlend >= 0.99f;
        public int DestroyedCount { get; private set; }
        private float mainBlend;
        private float deadTimer;
        private int lastStatus = int.MinValue;
        private int phase = -1;
        private bool ready;
        private Rect lastSafeArea;
        private Vector2Int lastScreen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Instances.Clear();

        private void OnEnable()
        {
            Instances.Add(this);
            if (visibility != null)
            {
                visibility.alpha = 0f;
                visibility.blocksRaycasts = false;
                visibility.interactable = false;
            }
            if (ready) ResetDisplay();
        }

        private void OnDisable()
        {
            Instances.Remove(this);
            if (visibility != null) visibility.alpha = 0f;
        }

        private void Start()
        {
            ready = controller != null && mainCore != null && subCores != null && subCores.Length == 4
                && subCells != null && subCells.Length == 4 && visibility != null
                && subCoreGroup != null && mainCoreGroup != null && ValidCell(mainCell);
            for (int i = 0; ready && i < 4; i++) ready = subCores[i] != null && ValidCell(subCells[i]);
            if (!ready)
            {
                Debug.LogWarning("[HyunMuCoreHealthBar] 코어 및 UI 참조를 연결하세요. 미연결 UI는 숨깁니다.", this);
                return;
            }
            ResetDisplay();
        }

        private static bool ValidCell(CoreCell cell) => cell != null && cell.fill != null && cell.damageTrail != null;
        private static float Ratio(BossCoreHealth core) => core != null && core.MaxHp > 0f ? Mathf.Clamp01(core.CurrentHp / core.MaxHp) : 0f;

        private void ResetDisplay()
        {
            mainBlend = controller.IsMainCoreExposed ? 1f : 0f;
            deadTimer = 0f;
            phase = -1;
            lastStatus = int.MinValue;
            for (int i = 0; i < 4; i++) ResetCell(subCells[i], Ratio(subCores[i]));
            ResetCell(mainCell, Ratio(mainCore));
            ApplyPhase();
            RefreshSafeArea();
        }

        private static void ResetCell(CoreCell cell, float ratio)
        {
            cell.previous = cell.shown = cell.trail = ratio;
            cell.trailHold = cell.flash = 0f;
            SetFill(cell.fill, ratio);
            SetFill(cell.damageTrail, ratio);
        }

        private void LateUpdate()
        {
            if (!ready) return;
            float dt = Time.unscaledDeltaTime;
            bool available = controller != null && controller.gameObject.activeInHierarchy;
            bool dead = available && controller.IsDead;
            if (dead) deadTimer += dt;
            bool hide = !available || (dead && deadTimer >= deathHoldTime)
                || NMJ.DefeatScreen.IsShowing || NMJ.EndingCredits.IsPlaying;
            visibility.alpha = Mathf.MoveTowards(visibility.alpha, hide ? 0f : 1f, dt * 3f);
            RefreshSafeArea();
            if (!available) return;

            DestroyedCount = 0;
            for (int i = 0; i < 4; i++)
            {
                float ratio = Ratio(subCores[i]);
                if (ratio <= 0f) DestroyedCount++;
                UpdateCell(subCells[i], ratio, dt);
            }
            UpdateCell(mainCell, Ratio(mainCore), dt);
            mainBlend = Mathf.MoveTowards(mainBlend, controller.IsMainCoreExposed ? 1f : 0f, dt / Mathf.Max(0.05f, phaseTransitionTime));
            ApplyPhase();

            int nextPhase = dead ? 3 : controller.IsMainCoreExposed ? 2 : DestroyedCount == 4 ? 1 : 0;
            if (phase != nextPhase)
            {
                phase = nextPhase;
                lastStatus = int.MinValue;
                if (phaseLabel != null) phaseLabel.text = phase == 3 ? "코어 정지" : phase == 2 ? "메인코어" : phase == 1 ? "메인코어 노출 중" : "서브코어";
            }
            int status = phase >= 2 ? Mathf.CeilToInt(Ratio(mainCore) * 100f) : 4 - DestroyedCount;
            if (status != lastStatus)
            {
                lastStatus = status;
                if (statusLabel != null) statusLabel.text = phase >= 2 ? status + "%" : status + " / 4";
            }
        }

        private void ApplyPhase()
        {
            // Fade the four cells out before revealing the continuous main-core bar.
            subCoreGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mainBlend * 2f));
            mainCoreGroup.alpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((mainBlend - 0.35f) / 0.65f));
        }

        private void UpdateCell(CoreCell cell, float target, float dt)
        {
            if (target < cell.previous - 0.0001f)
            {
                cell.trailHold = damageTrailDelay;
                cell.flash = 0.2f;
            }
            else if (target > cell.previous + 0.0001f) cell.trail = target;
            cell.previous = target;
            cell.shown = Mathf.MoveTowards(cell.shown, target, dt * fillSpeed);
            cell.trailHold = Mathf.Max(0f, cell.trailHold - dt);
            if (cell.trailHold <= 0f) cell.trail = Mathf.MoveTowards(cell.trail, target, dt * damageTrailSpeed);
            cell.trail = Mathf.Max(cell.trail, cell.shown);
            SetFill(cell.fill, cell.shown);
            SetFill(cell.damageTrail, cell.trail);
            cell.flash = Mathf.Max(0f, cell.flash - dt);
            if (cell.hitLight != null)
            {
                Color color = cell.hitLight.color;
                color.a = cell.flash * 1.4f;
                cell.hitLight.color = color;
            }
            bool broken = target <= 0f;
            if (cell.label != null) cell.label.color = broken ? brokenLabelColor : liveLabelColor;
            if (cell.indicator != null) cell.indicator.color = broken ? brokenLabelColor * new Color(1f, 1f, 1f, 0.6f) : liveIndicatorColor;
            if (cell.brokenMark != null) cell.brokenMark.alpha = Mathf.MoveTowards(cell.brokenMark.alpha, broken ? 1f : 0f, dt * 5f);
        }

        private static void SetFill(RectTransform rect, float value)
        {
            Vector2 anchor = rect.anchorMax;
            anchor.x = Mathf.Clamp01(value);
            if (rect.anchorMax != anchor) rect.anchorMax = anchor;
        }

        private void RefreshSafeArea()
        {
            if (safeArea == null || Screen.width <= 0 || Screen.height <= 0) return;
            Rect safe = Screen.safeArea;
            var resolution = new Vector2Int(Screen.width, Screen.height);
            if (safe == lastSafeArea && resolution == lastScreen) return;
            lastSafeArea = safe;
            lastScreen = resolution;
            safeArea.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            safeArea.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            safeArea.offsetMin = safeArea.offsetMax = Vector2.zero;
        }
    }
}
