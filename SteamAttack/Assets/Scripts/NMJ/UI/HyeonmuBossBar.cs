using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NMJ
{
    /// <summary>
    /// 현무 보스 체력바 — 1페이즈 [북방의 수신] (기획: 세계관 v6 법칙 7·6-2, UI 목록 1번)
    ///
    ///   [ 玄武 현무 | 앞 좌 🔒 / 앞 우 🔒 / 뒤 좌 🔒 / 뒤 우 🔒 ]
    ///                       북방의 수신
    ///
    /// - 다리(BossWallHealth) 하나가 맞을 때마다 그 칸이 깎이고, 칸이 다 비면 놋쇠 자물쇠가 풀리며 칸이 깨진다.
    /// - 다리 4개가 모두 깨지면 OnAllLegsBroken 이벤트를 한 번 보내고 바가 사라진다.
    ///   (2페이즈 체력바, 신핵 QTE는 보류 — 기획 확정 뒤 이어서 만든다)
    ///
    /// BHS 보스 코드는 수정하지 않고 공개 값만 읽는다.
    /// UI는 실행할 때 코드로 만들어진다. 보스 프리팹(BHS_Boss)에 붙여 두면 그 보스를 쓰고, 다른 곳에 붙이면 씬에서 보스를 찾는다.
    /// 보스가 없으면 숨긴다. (Test Keys를 켜면 보스 없이 F5 = 다리 한 대, F7 = 처음으로)
    ///
    /// 코어 현무(BHS_HyunMu, BossCoreController): 다리 칸 대신 보조 코어 4개를 4칸으로 보여 주고,
    /// 본 코어가 드러나면 칸 4개가 사라지고 본 코어 한 칸이 나온다. 본 코어가 깨지면 바가 사라진다.
    /// </summary>
    [DisallowMultipleComponent]
    public class HyeonmuBossBar : MonoBehaviour
    {
        [Header("연결 (비우면 씬에서 자동으로 찾음)")]
        [SerializeField] private BossHealth boss;
        [Tooltip("다리 순서대로. 비우면 보스 아래의 BossWallHealth를 이름순으로 찾는다")]
        [SerializeField] private BossWallHealth[] legs = new BossWallHealth[0];
        [Tooltip("코어 현무 (BHS_HyunMu). BossHealth가 없을 때 쓴다")]
        [SerializeField] private BossCoreController coreController;
        [Tooltip("보조 코어 4개 (칸 순서대로)")]
        [SerializeField] private BossCoreHealth[] subCores = new BossCoreHealth[0];
        [SerializeField] private BossCoreHealth mainCore;
        [Tooltip("한글·한자 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;

        [Header("글자")]
        [SerializeField] private string bossName = "玄武  현무";
        [SerializeField] private string phaseTitle = "북방의 수신";
        [SerializeField] private string[] legLabels = { "앞 좌", "앞 우", "뒤 좌", "뒤 우" };
        [Tooltip("본 코어가 드러났을 때 칸 이름")]
        [SerializeField] private string mainCoreLabel = "본 코어";
        [Tooltip("본 코어가 드러났을 때 바 아래 이름")]
        [SerializeField] private string mainPhaseTitle = "드러난 신핵";

        [Header("배치 (화면 1920×1080 기준)")]
        [SerializeField] private float barWidth = 1000f;
        [SerializeField] private float barHeight = 34f;
        [SerializeField] private float nameWidth = 230f;
        [SerializeField] private float topMargin = 46f;

        [Header("색 (오방색)")]
        [SerializeField] private Color frameColor = new Color(0.06f, 0.06f, 0.07f, 0.92f);   // 흑
        [SerializeField] private Color emptyColor = new Color(0.16f, 0.16f, 0.17f, 0.75f);
        [SerializeField] private Color legColor = new Color(0.24f, 0.27f, 0.30f, 1f);        // 흑철
        [SerializeField] private Color lockColor = new Color(0.94f, 0.73f, 0.16f, 1f);       // 황 (놋쇠 자물쇠)
        [SerializeField] private Color textColor = new Color(0.96f, 0.95f, 0.92f, 1f);       // 백

        [Header("그림 체력바 (칸 잠김 그림을 넣으면 그림으로 그림, 비우면 위의 색 칸)")]
        [Tooltip("사슬로 잠긴 칸")]
        [SerializeField] private Sprite cellLockedSprite;
        [Tooltip("사슬이 끊겨 게이지가 드러난 칸")]
        [SerializeField] private Sprite cellBrokenSprite;
        [Tooltip("깨진 뒤 자물쇠·사슬이 다 떨어져 나간 칸 (깨짐 그림을 잠깐 보여 준 뒤 이 그림으로 바뀜)")]
        [SerializeField] private Sprite cellOpenSprite;
        [Tooltip("깨짐 그림을 보여 주는 시간(초). 이후 열린 칸 그림으로 바뀜")]
        [SerializeField, Min(0f)] private float brokenShowTime = 0.5f;
        [Tooltip("깨짐 → 열린 칸으로 겹쳐 바뀌는 시간(초). 0이면 바로 바뀌며 살짝 튐")]
        [SerializeField, Min(0f)] private float openFadeTime = 0f;
        [Tooltip("본 코어가 드러날 때 풀린 칸 4개가 가운데로 모여 맞물리는 시간(초)")]
        [SerializeField, Min(0.05f)] private float mergeTime = 0.45f;
        [Tooltip("맞물린 뒤 긴 바가 철컥 튀며 흔들리는 시간(초)")]
        [SerializeField, Min(0.05f)] private float slamTime = 0.35f;
        [Tooltip("맞물린 뒤 긴 바 게이지가 본 코어 체력까지 차오르는 시간(초)")]
        [SerializeField, Min(0.05f)] private float chargeTime = 0.6f;
        [Tooltip("본 코어가 드러났을 때의 긴 바 틀 (게이지 홈은 비어 있음)")]
        [SerializeField] private Sprite fullFrameSprite;
        [Tooltip("체력 게이지 (칸 아래 작은 게이지와 긴 바 게이지에 같이 씀)")]
        [SerializeField] private Sprite gaugeSprite;
        [Tooltip("긴 바 틀 그림 안에서 게이지 홈 자리 (0~1 비율, 왼쪽 아래 기준)")]
        [SerializeField] private Rect fullGaugeArea = new Rect(0.05f, 0.45f, 0.9f, 0.1f);
        [Tooltip("그림 체력바 전체 폭 (1920×1080 기준)")]
        [SerializeField] private float artBarWidth = 1000f;
        [Tooltip("이웃 칸끼리 겹치는 폭 (관이 맞물려 보이게)")]
        [SerializeField] private float cellOverlap = 10f;

        [Header("연출")]
        [Tooltip("칸이 줄어드는 빠르기 (초당 칸 전체 대비)")]
        [SerializeField, Min(0.1f)] private float drainSpeed = 4f;
        [Tooltip("자물쇠가 풀리고 칸이 깨지는 시간(초)")]
        [SerializeField, Min(0.05f)] private float breakTime = 0.6f;
        [Tooltip("다리가 모두 깨진 뒤 바가 사라지기까지 기다리는 시간(초)")]
        [SerializeField, Min(0f)] private float hideDelay = 1.2f;

        [Header("다리 4개가 모두 깨졌을 때 (2페이즈 연결용)")]
        public UnityEvent OnAllLegsBroken = new UnityEvent();

        [Header("테스트 (보스 없이 미리 보기)")]
        [Tooltip("켜면 보스가 없어도 바가 보인다. F5 = 다리 한 대, F7 = 처음으로")]
        [SerializeField] private bool testKeys = false;

        /// <summary>깨진 다리 수 (0~다리 수)</summary>
        public int BrokenCount { get; private set; }
        /// <summary>다리가 모두 깨졌는지</summary>
        public bool AllBroken { get; private set; }
        /// <summary>바가 화면에 보이는 중인지 (페이드 중 포함)</summary>
        public bool IsVisible => isActiveAndEnabled && alpha > 0.01f;

        /// <summary>
        /// 화면에 보이는 현무 체력바가 하나라도 있는지 = 보스전 중.
        /// 같은 자리(화면 위 가운데)를 쓰는 퀘스트창 등이 이걸 보고 비켜 준다.
        /// </summary>
        public static bool AnyVisible
        {
            get
            {
                foreach (HyeonmuBossBar b in Instances) if (b.IsVisible) return true;
                return false;
            }
        }

        private static readonly List<HyeonmuBossBar> Instances = new List<HyeonmuBossBar>();

        private class LegCell
        {
            public RectTransform root;
            public RectTransform fill;
            public Image fillImage;
            public RectTransform shackle;
            public Image[] lockParts;
            public TMP_Text label;
            public Image art;            // 그림 체력바: 칸 그림 (없으면 색 칸)
            public Image artOpen;        // 그림 체력바: 깨진 뒤 열린 칸 그림 (art 위에 겹쳐 서서히 나타남)
            public float openBlend;      // 0 = 깨짐 그림, 1 = 열린 칸 그림
            public RectTransform hpBar;  // 그림 체력바: 칸 아래 작은 게이지
            public float shownRatio = 1f;
            public bool broken;
            public float breakTimer;
            public float cellX;
            public float cellY;
            public Vector2 BasePos => new Vector2(cellX, cellY);
        }

        private CanvasGroup group;
        private RectTransform bodyArea;
        private readonly List<LegCell> cells = new List<LegCell>();
        private float alpha;
        private float allBrokenTimer;
        private float[] testLegs;
        private bool bossResolved;
        private LegCell mainCell;
        private TMP_Text titleText;
        private float mainBlend;
        private bool CoreMode => boss == null && coreController != null;
        private bool ArtMode => cellLockedSprite != null;
        private RectTransform titleRect;
        private float artCellW;               // 그림 체력바 칸 하나의 폭
        private float mergeTimer, slamTimer;  // 칸 4개 → 긴 바로 맞물리는 연출
        private bool merged;
        private float titleDrop;   // 본 코어 긴 틀이 칸 줄보다 아래로 더 내려오는 만큼 (페이즈 글자를 같이 내림)

        // ───────── 시작 ─────────

        private void Awake()
        {
            BuildUi();
            ResetBar();
        }

        private void OnEnable() => Instances.Add(this);
        private void OnDisable() => Instances.Remove(this);

        /// <summary>처음 상태로 (모든 다리 온전)</summary>
        public void ResetBar()
        {
            testLegs = new float[cells.Count];
            for (int i = 0; i < testLegs.Length; i++) testLegs[i] = 1f;
            BrokenCount = 0;
            AllBroken = false;
            allBrokenTimer = 0f;
            foreach (LegCell c in cells)
            {
                c.broken = false;
                c.shownRatio = 1f;
                c.breakTimer = 0f;
                c.root.anchoredPosition = c.BasePos;
                c.shackle.anchoredPosition = Vector2.zero;
                c.shackle.localRotation = Quaternion.identity;
                SetFill(c, 1f);
                SetCellAlpha(c, 1f);
                c.root.gameObject.SetActive(true);
                ResetArt(c, cellLockedSprite);
            }
            mainBlend = 0f;
            if (mainCell != null)
            {
                mainCell.broken = false;
                mainCell.shownRatio = 1f;
                mainCell.breakTimer = 0f;
                mainCell.root.anchoredPosition = mainCell.BasePos;
                mainCell.shackle.anchoredPosition = Vector2.zero;
                mainCell.shackle.localRotation = Quaternion.identity;
                SetFill(mainCell, 1f);
                mainCell.root.gameObject.SetActive(false);
                ResetArt(mainCell, fullFrameSprite);
            }
            if (titleText != null) titleText.text = phaseTitle;
            if (titleRect != null) titleRect.anchoredPosition = new Vector2(0f, -8f);
            mergeTimer = slamTimer = 0f;
            merged = false;
        }

        private static void ResetArt(LegCell c, Sprite sprite)
        {
            if (c.art == null) return;
            c.art.sprite = sprite;
            c.openBlend = 0f;
            if (c.artOpen != null) { Color oc = c.artOpen.color; oc.a = 0f; c.artOpen.color = oc; }
            c.root.localScale = Vector3.one;
            if (c.hpBar != null) c.hpBar.gameObject.SetActive(true);
        }

        /// <summary>칸 채움: 그림 체력바는 게이지 그림을 왼쪽부터 잘라 보이고, 색 칸은 폭을 줄인다</summary>
        private static void SetFill(LegCell c, float ratio)
        {
            if (c.art != null) c.fillImage.fillAmount = Mathf.Clamp01(ratio);
            else SetRatio(c.fill, ratio);
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            HandleTestKeys();
            FindBoss();

            if (CoreMode)
            {
                UpdateCoreBoss();
                return;
            }

            if (boss == null && !testKeys)
            {
                FadeTo(0f);
                return;
            }

            UpdateLegs();

            if (!AllBroken && BrokenCount >= cells.Count && cells.Count > 0 && LastBreakFinished())
            {
                AllBroken = true;
                allBrokenTimer = 0f;
                OnAllLegsBroken.Invoke();
            }

            if (AllBroken) allBrokenTimer += Time.unscaledDeltaTime;
            FadeTo(AllBroken && allBrokenTimer >= hideDelay ? 0f : 1f);
        }

        private void FindBoss()
        {
            // 보스와 다리를 한 번 다 정했으면 다시 찾지 않는다.
            // (보스를 인스펙터나 프리팹에서 미리 연결해 둔 경우에도 다리 목록은 여기서 채워야 한다)
            if (bossResolved && (boss != null || coreController != null)) return;

            // 보스에 붙어 있으면 그 보스를 쓰고, 아니면 씬에서 찾는다 (다리 현무 BossHealth → 코어 현무 BossCoreController)
            if (boss == null && coreController == null) boss = GetComponentInParent<BossHealth>();
            if (boss == null && coreController == null) coreController = GetComponentInParent<BossCoreController>();
            if (boss == null && coreController == null) boss = FindAnyObjectByType<BossHealth>();
            if (boss == null && coreController == null) coreController = FindAnyObjectByType<BossCoreController>();
            if (boss == null)
            {
                if (coreController == null) return;
                NGH.ReviewedAudio.PlayUi("Initial/11_UI/BossBar_v03");
                bossResolved = true;
                if (subCores == null || subCores.Length != cells.Count || mainCore == null)
                    Debug.LogWarning($"[NMJ] 현무 체력바: 보조 코어 {cells.Count}개와 본 코어를 연결하세요.", this);
                ResetBar();
                return;
            }
            NGH.ReviewedAudio.PlayUi("Initial/11_UI/BossBar_v03");
            NGH.ReviewedAudio.Play("Revision/41_BossRoar_v02");
            bossResolved = true;

            if (legs == null || legs.Length == 0)
            {
                var found = new List<BossWallHealth>(boss.GetComponentsInChildren<BossWallHealth>(true));
                found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                legs = found.ToArray();
            }
            if (legs.Length != cells.Count)
                Debug.Log($"[NMJ] 현무 체력바: 칸 {cells.Count}개, 보스 다리 {legs.Length}개 — 개수가 다릅니다 (BHS 요청: 보스다리수와체력기준).", this);
            ResetBar();
        }

        // ───────── 값 읽기 ─────────

        /// <summary>다리 i의 남은 비율 (0 = 깨짐, 1 = 온전)</summary>
        private float LegRatio(int i)
        {
            if (CoreMode) return subCores != null && i < subCores.Length ? CoreRatio(subCores[i]) : 0f;
            if (boss != null)
            {
                if (i >= legs.Length || legs[i] == null) return 0f;
                BossWallHealth leg = legs[i];
                if (!leg.gameObject.activeSelf) return 0f;   // BHS: 깨진 다리는 꺼진다
                return leg.maxHp > 0f ? Mathf.Clamp01(leg.currentHp / leg.maxHp) : 0f;
            }
            return i < testLegs.Length ? testLegs[i] : 0f;
        }

        private static float CoreRatio(BossCoreHealth core)
            => core != null && core.MaxHp > 0f ? Mathf.Clamp01(core.CurrentHp / core.MaxHp) : 0f;

        /// <summary>코어 현무: 보조 코어 4칸 → 본 코어 1칸 → 사라짐</summary>
        private void UpdateCoreBoss()
        {
            if (!coreController.gameObject.activeInHierarchy || DefeatScreen.IsShowing || EndingCredits.IsPlaying)
            {
                FadeTo(0f);
                return;
            }

            UpdateLegs();
            if (!AllBroken && BrokenCount >= cells.Count && cells.Count > 0 && LastBreakFinished())
            {
                AllBroken = true;
                OnAllLegsBroken.Invoke();
            }

            float dt = Time.unscaledDeltaTime;
            if (ArtMode)
            {
                UpdateArtMerge(coreController.IsMainCoreExposed, dt);
                allBrokenTimer = coreController.IsDead ? allBrokenTimer + dt : 0f;
                FadeTo(coreController.IsDead && allBrokenTimer >= hideDelay ? 0f : 1f);
                return;
            }

            // 본 코어가 드러나면 보조 코어 칸이 먼저 사라지고 본 코어 칸이 나온다
            mainBlend = Mathf.MoveTowards(mainBlend, coreController.IsMainCoreExposed ? 1f : 0f, dt / 0.65f);
            float legsAlpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(mainBlend * 2f));
            float mainAlpha = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((mainBlend - 0.35f) / 0.65f));
            foreach (LegCell c in cells)
            {
                c.root.gameObject.SetActive(legsAlpha > 0.001f);
                if (legsAlpha < 1f) SetCellAlpha(c, legsAlpha * (c.broken && c.art == null ? 0.35f : 1f));
            }
            mainCell.root.gameObject.SetActive(mainAlpha > 0.001f);
            if (mainAlpha > 0.001f) UpdateCell(mainCell, CoreRatio(mainCore), dt, mainAlpha);
            if (titleText != null) titleText.text = mainBlend > 0.5f ? mainPhaseTitle : phaseTitle;
            if (titleRect != null && titleDrop > 0f) titleRect.anchoredPosition = new Vector2(0f, -8f - titleDrop * Mathf.SmoothStep(0f, 1f, mainBlend));

            allBrokenTimer = coreController.IsDead ? allBrokenTimer + dt : 0f;
            FadeTo(coreController.IsDead && allBrokenTimer >= hideDelay ? 0f : 1f);
        }

        /// <summary>
        /// 그림 체력바: 본 코어가 드러나면 풀린 칸 4개가 가운데로 점점 빨리 미끄러져 맞물리고,
        /// 맞붙는 순간 긴 바로 바뀌며 철컥 튀고 흔들린 뒤 게이지가 본 코어 체력까지 차오른다. (페이드 없음)
        /// </summary>
        private void UpdateArtMerge(bool exposed, float dt)
        {
            if (!exposed)
            {
                mergeTimer = 0f;
                merged = false;
                return;
            }

            int n = cells.Count;
            if (!merged)
            {
                mergeTimer += dt;
                float u = Mathf.Clamp01(mergeTimer / mergeTime);
                float e = u * u * u;   // 점점 빨라져 쾅 맞붙음
                float mainW = mainCell.root.sizeDelta.x;
                float step = n > 1 ? (mainW - artCellW) / (n - 1) : 0f;
                float center = artBarWidth * 0.5f;
                float rattle = u * 1.5f;   // 모이며 덜컹거림
                for (int i = 0; i < n; i++)
                {
                    LegCell c = cells[i];
                    float to = center + (i - (n - 1) * 0.5f) * step;
                    // 가로로 모이면서 세로는 긴 바 가운데 높이로 내려감 (맞붙는 순간 위치가 튀지 않게)
                    c.root.anchoredPosition = new Vector2(Mathf.Lerp(c.cellX, to, e), Mathf.Lerp(c.cellY, mainCell.cellY, e)) + new Vector2(0f, Random.Range(-rattle, rattle));
                }
                if (u < 1f) return;

                // 맞붙음: 칸 4개 → 긴 바
                merged = true;
                slamTimer = 0f;
                foreach (LegCell c in cells) c.root.gameObject.SetActive(false);
                mainCell.root.gameObject.SetActive(true);
                SetCellAlpha(mainCell, 1f);
                mainCell.shownRatio = 0f;
                SetFill(mainCell, 0f);
                if (titleText != null) titleText.text = mainPhaseTitle;
            }

            slamTimer += dt;
            float target = CoreRatio(mainCore);
            if (slamTimer < slamTime)
            {
                float s = slamTimer / slamTime;
                mainCell.root.localScale = Vector3.one * (1f + 0.12f * Mathf.Sin(Mathf.Min(1f, s * 2.5f) * Mathf.PI) * (1f - s));
                float shake = (1f - s) * 8f;
                mainCell.root.anchoredPosition = mainCell.BasePos + new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
            }
            else if (!mainCell.broken)
            {
                mainCell.root.localScale = Vector3.one;
                mainCell.root.anchoredPosition = mainCell.BasePos;
            }
            if (titleRect != null && titleDrop > 0f)
                titleRect.anchoredPosition = new Vector2(0f, -8f - titleDrop * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(slamTimer / slamTime)));

            // 게이지: 증기가 차오르듯 0에서 본 코어 체력까지 채운 뒤, 이후로는 맞을 때마다 줄어듦
            if (slamTimer < chargeTime)
            {
                float k = slamTimer / chargeTime;
                mainCell.shownRatio = target * (1f - (1f - k) * (1f - k));
                SetFill(mainCell, mainCell.shownRatio);
            }
            else UpdateCell(mainCell, target, dt, 1f);
        }

        private bool LastBreakFinished()
        {
            foreach (LegCell c in cells) if (c.broken && c.breakTimer < breakTime) return false;
            return true;
        }

        // ───────── 다리 칸 ─────────

        private void UpdateLegs()
        {
            float dt = Time.unscaledDeltaTime;
            int broken = 0;
            for (int i = 0; i < cells.Count; i++)
            {
                LegCell c = cells[i];
                UpdateCell(c, LegRatio(i), dt, 1f);
                if (c.broken) broken++;
            }
            BrokenCount = broken;
        }

        /// <summary>칸 하나: 맞으면 줄어들고, 다 비면 자물쇠가 풀리며 깨진다 (cellAlpha = 칸 전체 투명도)</summary>
        private void UpdateCell(LegCell c, float target, float dt, float cellAlpha)
        {
            {
                // 칸 채움: 맞으면 빠르게 줄어든다
                c.shownRatio = Mathf.MoveTowards(c.shownRatio, target, dt * drainSpeed);
                SetFill(c, c.shownRatio);

                if (!c.broken && target <= 0f && c.shownRatio <= 0.001f)
                {
                    c.broken = true;
                    c.breakTimer = 0f;
                }

                if (c.broken && c.art != null)
                {
                    // 그림 체력바: 사슬이 끊긴 그림으로 바뀌며 칸이 튀어 올랐다가 크게 흔들린 뒤 가라앉는다
                    c.breakTimer += dt;
                    float t = Mathf.Clamp01(c.breakTimer / breakTime);
                    bool isMain = c == mainCell;
                    if (!isMain && cellBrokenSprite != null && c.art.sprite != cellBrokenSprite) c.art.sprite = cellBrokenSprite;
                    // 깨짐 그림을 잠깐 보여 준 뒤 자물쇠·사슬이 없는 열린 칸으로 바뀐다
                    float sinceOpen = c.breakTimer - brokenShowTime;
                    if (c.artOpen != null) c.openBlend = openFadeTime > 0f ? Mathf.Clamp01(sinceOpen / openFadeTime) : (sinceOpen >= 0f ? 1f : 0f);
                    if (c.hpBar != null && c.hpBar.gameObject.activeSelf) c.hpBar.gameObject.SetActive(false);
                    // 열린 칸으로 바뀌는 순간 한 번 더 살짝 튐 (남은 파편이 떨어져 나가는 느낌)
                    float openBump = c.artOpen != null && sinceOpen >= 0f && sinceOpen < 0.22f ? 0.07f * Mathf.Sin(sinceOpen / 0.22f * Mathf.PI) : 0f;
                    c.root.localScale = Vector3.one * (1f + 0.22f * Mathf.Sin(Mathf.Min(1f, t * 2f) * Mathf.PI) * (1f - t) + openBump);
                    float shake = (1f - t) * 7f;
                    c.root.anchoredPosition = c.BasePos + new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
                    SetCellAlpha(c, (isMain ? Mathf.Lerp(1f, 0.35f, t) : 1f) * cellAlpha);
                }
                else if (c.broken)
                {
                    c.breakTimer += dt;
                    float t = Mathf.Clamp01(c.breakTimer / breakTime);
                    float open = Mathf.Clamp01(t * 2f);
                    // 자물쇠 고리가 튀어 올라 옆으로 젖혀지며 열린다
                    c.shackle.anchoredPosition = new Vector2(0f, Mathf.Sin(open * Mathf.PI * 0.5f) * 7f);
                    c.shackle.localRotation = Quaternion.Euler(0f, 0f, -35f * open);
                    // 칸이 잠깐 흔들린 뒤 흐려진다
                    float shake = (1f - t) * 3f;
                    c.root.anchoredPosition = c.BasePos + new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
                    SetCellAlpha(c, Mathf.Lerp(1f, 0.35f, t) * cellAlpha);
                }
                else SetCellAlpha(c, cellAlpha);
            }
        }

        private void FadeTo(float target)
        {
            alpha = Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime * 3f);
            if (group != null) group.alpha = alpha;
        }

        // ───────── 테스트 키 ─────────

        private void HandleTestKeys()
        {
            if (!testKeys || Keyboard.current == null) return;
            if (Keyboard.current.f7Key.wasPressedThisFrame) { ResetBar(); return; }
            if (boss != null) return;   // 실제 보스가 있으면 테스트 값은 쓰지 않는다

            if (Keyboard.current.f5Key.wasPressedThisFrame)
            {
                for (int i = 0; i < testLegs.Length; i++)
                    if (testLegs[i] > 0f) { testLegs[i] = 0f; break; }   // 다리 한 대 = 한 칸
            }
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) AutoAssignFont();
#endif
            var canvasGo = new GameObject("NMJ_HyeonmuBossBarCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            if (ArtMode)
            {
                BuildArtUi(canvasGo.transform);
                return;
            }

            // 바 전체 (화면 위 가운데)
            RectTransform bar = NewRect("Bar", canvasGo.transform);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(barWidth, barHeight);
            bar.anchoredPosition = new Vector2(0f, -topMargin);
            NewImage("Frame", bar, frameColor, new Vector2(-4f, -4f), new Vector2(4f, 4f));

            // 왼쪽 이름 칸
            RectTransform nameBox = NewRect("NameBox", bar);
            nameBox.anchorMin = new Vector2(0f, 0f);
            nameBox.anchorMax = new Vector2(0f, 1f);
            nameBox.pivot = new Vector2(0f, 0.5f);
            nameBox.sizeDelta = new Vector2(nameWidth, 0f);
            NewImage("NameBg", nameBox, new Color(0.10f, 0.10f, 0.11f, 1f), Vector2.zero, Vector2.zero);
            NewText("Name", nameBox, bossName, 22f);

            // 오른쪽 다리 영역
            bodyArea = NewRect("Legs", bar);
            bodyArea.offsetMin = new Vector2(nameWidth + 4f, 0f);
            bodyArea.offsetMax = Vector2.zero;
            NewImage("Empty", bodyArea, emptyColor, Vector2.zero, Vector2.zero);

            int n = Mathf.Max(1, legLabels.Length);
            float areaWidth = barWidth - nameWidth - 4f;
            const float gap = 6f;
            float cellWidth = (areaWidth - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++) cells.Add(BuildCell(i, cellWidth, gap, i < legLabels.Length ? legLabels[i] : "다리 " + (i + 1)));
            // 본 코어 칸 (코어 현무에서 본 코어가 드러나면 다리 칸 대신 나온다)
            mainCell = BuildCell(0, areaWidth, 0f, mainCoreLabel);
            mainCell.root.name = "MainCore";
            mainCell.root.gameObject.SetActive(false);

            // 바 아래 페이즈 이름
            RectTransform titleRect = NewRect("Title", bar);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 0f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(nameWidth * 0.5f, -6f);
            titleRect.sizeDelta = new Vector2(-nameWidth, 26f);
            titleText = AddText(titleRect.gameObject, phaseTitle, 18f);
            titleText.alpha = 0.85f;
        }

        /// <summary>
        /// 그림 체력바:
        ///   玄武  현무
        ///   [사슬 칸][사슬 칸][사슬 칸][사슬 칸]   ← 칸마다 아래에 작은 게이지
        ///            북방의 수신
        /// 본 코어가 드러나면 칸 4개 대신 긴 바 틀 + 게이지 한 줄
        /// </summary>
        private void BuildArtUi(Transform canvas)
        {
            int n = Mathf.Max(1, legLabels.Length);
            float cellW = (artBarWidth + cellOverlap * (n - 1)) / n;
            float cellH = cellW * cellLockedSprite.rect.height / cellLockedSprite.rect.width;
            artCellW = cellW;
            const float nameH = 30f, hpH = 9f, hpGap = 2f;

            RectTransform bar = NewRect("Bar", canvas);
            bar.anchorMin = bar.anchorMax = new Vector2(0.5f, 1f);
            bar.pivot = new Vector2(0.5f, 1f);
            bar.sizeDelta = new Vector2(artBarWidth, nameH + cellH + hpGap + hpH);
            bar.anchoredPosition = new Vector2(0f, -topMargin);

            // 보스 이름 (맨 위)
            RectTransform nameRect = NewRect("Name", bar);
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.sizeDelta = new Vector2(0f, nameH);
            nameRect.anchoredPosition = Vector2.zero;
            TMP_Text nameText = AddText(nameRect.gameObject, bossName, 24f);
            nameText.outlineWidth = 0.2f;
            nameText.outlineColor = new Color32(0, 0, 0, 200);

            // 칸 줄
            bodyArea = NewRect("Cells", bar);
            bodyArea.anchorMin = new Vector2(0f, 1f);
            bodyArea.anchorMax = new Vector2(1f, 1f);
            bodyArea.pivot = new Vector2(0.5f, 1f);
            bodyArea.sizeDelta = new Vector2(0f, cellH);
            bodyArea.anchoredPosition = new Vector2(0f, -nameH);

            for (int i = 0; i < n; i++)
            {
                var c = new LegCell();
                c.root = NewRect("Cell_" + i, bodyArea);
                c.root.anchorMin = c.root.anchorMax = new Vector2(0f, 0.5f);
                c.root.pivot = new Vector2(0.5f, 0.5f);
                c.root.sizeDelta = new Vector2(cellW, cellH);
                c.cellX = i * (cellW - cellOverlap) + cellW * 0.5f;
                c.root.anchoredPosition = c.BasePos;
                c.art = NewArt("Art", c.root, cellLockedSprite);
                if (cellOpenSprite != null)
                {
                    c.artOpen = NewArt("ArtOpen", c.root, cellOpenSprite);
                    c.artOpen.color = new Color(1f, 1f, 1f, 0f);
                }

                // 칸 아래 작은 게이지 = 이 코어의 남은 체력
                c.hpBar = NewRect("Hp", c.root);
                c.hpBar.anchorMin = new Vector2(0.18f, 0f);
                c.hpBar.anchorMax = new Vector2(0.82f, 0f);
                c.hpBar.pivot = new Vector2(0.5f, 1f);
                c.hpBar.sizeDelta = new Vector2(0f, hpH);
                c.hpBar.anchoredPosition = new Vector2(0f, -hpGap);
                Image hpBack = NewImage("Back", c.hpBar, new Color(0.03f, 0.04f, 0.05f, 0.85f), new Vector2(-1f, -1f), new Vector2(1f, 1f));
                c.fillImage = NewGauge(c.hpBar);
                c.fill = c.fillImage.rectTransform;
                c.shackle = NewRect("Pivot", c.root);
                c.lockParts = new[] { c.art, hpBack };
                cells.Add(c);
            }

            // 본 코어: 긴 바 틀 + 홈 안 게이지
            mainCell = new LegCell();
            mainCell.root = NewRect("MainCore", bodyArea);
            mainCell.root.anchorMin = mainCell.root.anchorMax = new Vector2(0f, 0.5f);
            mainCell.root.pivot = new Vector2(0.5f, 0.5f);
            Sprite frame = fullFrameSprite != null ? fullFrameSprite : cellLockedSprite;
            float mainW = artBarWidth * 0.86f;
            float mainH = mainW * frame.rect.height / frame.rect.width;
            mainCell.root.sizeDelta = new Vector2(mainW, mainH);
            mainCell.cellX = artBarWidth * 0.5f;
            mainCell.cellY = -(mainH - cellH) * 0.5f;   // 위쪽을 칸 줄 위쪽에 맞춤 (이름을 가리지 않게)
            titleDrop = Mathf.Max(0f, mainH - cellH - hpGap - hpH);
            mainCell.root.anchoredPosition = mainCell.BasePos;
            RectTransform channel = NewRect("Channel", mainCell.root);
            channel.anchorMin = fullGaugeArea.min;
            channel.anchorMax = fullGaugeArea.max;
            mainCell.fillImage = NewGauge(channel);
            mainCell.fill = mainCell.fillImage.rectTransform;
            mainCell.art = NewArt("Art", mainCell.root, frame);
            mainCell.art.transform.SetAsFirstSibling();   // 틀 위에 게이지
            mainCell.shackle = NewRect("Pivot", mainCell.root);
            mainCell.lockParts = new[] { mainCell.art };
            mainCell.root.gameObject.SetActive(false);

            // 페이즈 이름 (맨 아래)
            titleRect = NewRect("Title", bar);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 0f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(0f, -8f);
            titleRect.sizeDelta = new Vector2(0f, 24f);
            titleText = AddText(titleRect.gameObject, phaseTitle, 18f);
            titleText.alpha = 0.85f;
            titleText.outlineWidth = 0.2f;
            titleText.outlineColor = new Color32(0, 0, 0, 200);
        }

        private static Image NewArt(string name, Transform parent, Sprite sprite)
        {
            Image img = NewImage(name, parent, Color.white, Vector2.zero, Vector2.zero);
            img.sprite = sprite;
            img.preserveAspect = true;
            return img;
        }

        private Image NewGauge(Transform parent)
        {
            Image img = NewImage("Fill", parent, Color.white, Vector2.zero, Vector2.zero);
            img.sprite = gaugeSprite;
            if (gaugeSprite == null) img.color = new Color(0.35f, 0.95f, 0.9f, 1f);
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
            return img;
        }

        private LegCell BuildCell(int i, float width, float gap, string label)
        {
            var c = new LegCell();
            c.root = NewRect("Leg_" + i, bodyArea);
            c.root.anchorMin = c.root.anchorMax = new Vector2(0f, 0.5f);
            c.root.pivot = new Vector2(0f, 0.5f);
            c.root.sizeDelta = new Vector2(width, barHeight - 8f);
            c.cellX = i * (width + gap);
            c.root.anchoredPosition = c.BasePos;

            NewImage("Back", c.root, new Color(0.08f, 0.08f, 0.09f, 1f), Vector2.zero, Vector2.zero);
            c.fillImage = NewImage("Fill", c.root, legColor, new Vector2(2f, 2f), new Vector2(-2f, -2f));
            c.fill = c.fillImage.rectTransform;

            c.label = NewText("Label", c.root, label, 17f);

            // 놋쇠 자물쇠 (칸 오른쪽): 몸통 + 고리(왼·오른·위 막대)
            RectTransform lockRoot = NewRect("Lock", c.root);
            lockRoot.anchorMin = lockRoot.anchorMax = new Vector2(1f, 0.5f);
            lockRoot.pivot = new Vector2(1f, 0.5f);
            lockRoot.anchoredPosition = new Vector2(-12f, -2f);
            lockRoot.sizeDelta = new Vector2(14f, 18f);
            var parts = new List<Image>();
            Image lockBody = NewImage("LockBody", lockRoot, lockColor, Vector2.zero, Vector2.zero);
            lockBody.rectTransform.anchorMin = new Vector2(0f, 0f);
            lockBody.rectTransform.anchorMax = new Vector2(1f, 0.55f);
            parts.Add(lockBody);

            c.shackle = NewRect("Shackle", lockRoot);
            c.shackle.anchorMin = c.shackle.anchorMax = new Vector2(0.5f, 0.55f);
            c.shackle.pivot = new Vector2(0.15f, 0f);   // 왼쪽 막대를 축으로 열린다
            c.shackle.sizeDelta = new Vector2(10f, 8f);
            parts.Add(LockBar(c.shackle, new Vector2(0f, 0f), new Vector2(0.25f, 1f)));
            parts.Add(LockBar(c.shackle, new Vector2(0.75f, 0f), new Vector2(1f, 1f)));
            parts.Add(LockBar(c.shackle, new Vector2(0f, 0.75f), new Vector2(1f, 1f)));
            c.lockParts = parts.ToArray();
            return c;
        }

        private Image LockBar(RectTransform parent, Vector2 min, Vector2 max)
        {
            Image img = NewImage("Bar", parent, lockColor, Vector2.zero, Vector2.zero);
            img.rectTransform.anchorMin = min;
            img.rectTransform.anchorMax = max;
            return img;
        }

        // ───────── 작은 도우미 ─────────

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            return rt;
        }

        private static Image NewImage(string name, Transform parent, Color color, Vector2 offsetMin, Vector2 offsetMax)
        {
            RectTransform rt = NewRect(name, parent);
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        private TMP_Text NewText(string name, Transform parent, string text, float size)
        {
            return AddText(NewRect(name, parent).gameObject, text, size);
        }

        private TMP_Text AddText(GameObject go, string text, float size)
        {
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = textColor;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
        }

        /// <summary>바(0~1)를 왼쪽에서부터 채운다.</summary>
        private static void SetRatio(RectTransform rect, float ratio)
        {
            if (rect == null) return;
            rect.anchorMax = new Vector2(Mathf.Clamp01(ratio), rect.anchorMax.y);
        }

        private static void SetCellAlpha(LegCell c, float a)
        {
            Color col = c.fillImage.color; col.a = a; c.fillImage.color = col;
            if (c.label != null) c.label.alpha = a;
            foreach (Image p in c.lockParts) { Color pc = p.color; pc.a = a; p.color = pc; }
            if (c.artOpen != null)
            {
                // 깨짐 그림(아래) → 열린 칸 그림(위)으로 겹쳐 바뀜
                Color ac = c.art.color; ac.a = a * (1f - c.openBlend); c.art.color = ac;
                Color oc = c.artOpen.color; oc.a = a * c.openBlend; c.artOpen.color = oc;
            }
        }

#if UNITY_EDITOR
        private void AutoAssignFont()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("ChosunCentennial_otf SDF t:TMP_FontAsset"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Fallback")) continue;
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null) return;
            }
        }

        private void OnValidate()
        {
            if (font == null) AutoAssignFont();
        }
#endif
    }
}
