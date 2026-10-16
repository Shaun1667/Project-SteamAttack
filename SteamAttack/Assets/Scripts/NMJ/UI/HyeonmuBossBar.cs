using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using BHS;

namespace NMJ
{
    /// <summary>
    /// 현무 보스 체력바 — 1페이즈 [흑철의 가갑] (기획: 압축 기획안 v3 4-3, UI 목록 1번)
    ///
    ///   [ 玄武 현무 | 앞 좌 🔒 / 앞 우 🔒 / 뒤 좌 🔒 / 뒤 우 🔒 ]
    ///                       흑철의 가갑
    ///
    /// - 다리(BHS.BossWallHealth) 하나가 맞을 때마다 그 칸이 깎이고, 칸이 다 비면 놋쇠 자물쇠가 풀리며 칸이 깨진다.
    /// - 다리 4개가 모두 깨지면 OnAllLegsBroken 이벤트를 한 번 보내고 바가 사라진다.
    ///   (2페이즈 체력바, 신핵 QTE는 보류 — 기획 확정 뒤 이어서 만든다)
    ///
    /// BHS 보스 코드는 수정하지 않고 공개 값만 읽는다.
    /// UI는 실행할 때 코드로 만들어진다. 보스 프리팹(BHS_Boss)에 붙여 두면 그 보스를 쓰고, 다른 곳에 붙이면 씬에서 보스를 찾는다.
    /// 보스가 없으면 숨긴다. (Test Keys를 켜면 보스 없이 F5 = 다리 한 대, F7 = 처음으로)
    /// </summary>
    [DisallowMultipleComponent]
    public class HyeonmuBossBar : MonoBehaviour
    {
        [Header("연결 (비우면 씬에서 자동으로 찾음)")]
        [SerializeField] private BossHealth boss;
        [Tooltip("다리 순서대로. 비우면 보스 아래의 BossWallHealth를 이름순으로 찾는다")]
        [SerializeField] private BossWallHealth[] legs = new BossWallHealth[0];
        [Tooltip("한글·한자 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;

        [Header("글자")]
        [SerializeField] private string bossName = "玄武  현무";
        [SerializeField] private string phaseTitle = "흑철의 가갑";
        [SerializeField] private string[] legLabels = { "앞 좌", "앞 우", "뒤 좌", "뒤 우" };

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
            public float shownRatio = 1f;
            public bool broken;
            public float breakTimer;
            public float cellX;
            public Vector2 BasePos => new Vector2(cellX, 0f);
        }

        private CanvasGroup group;
        private RectTransform bodyArea;
        private readonly List<LegCell> cells = new List<LegCell>();
        private float alpha;
        private float allBrokenTimer;
        private float[] testLegs;
        private bool bossResolved;

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
                SetRatio(c.fill, 1f);
                SetCellAlpha(c, 1f);
            }
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            HandleTestKeys();
            FindBoss();

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
            if (bossResolved && boss != null) return;

            // 보스에 붙어 있으면 그 보스를 쓰고, 아니면 씬에서 찾는다
            if (boss == null) boss = GetComponentInParent<BossHealth>();
            if (boss == null) boss = FindAnyObjectByType<BossHealth>();
            if (boss == null) return;
            bossResolved = true;

            if (legs == null || legs.Length == 0)
            {
                var found = new List<BossWallHealth>(boss.GetComponentsInChildren<BossWallHealth>(true));
                found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                legs = found.ToArray();
            }
            if (legs.Length != cells.Count)
                Debug.LogWarning($"[NMJ] 현무 체력바: 칸 {cells.Count}개, 보스 다리 {legs.Length}개 — 개수가 다릅니다.", this);
            ResetBar();
        }

        // ───────── 값 읽기 ─────────

        /// <summary>다리 i의 남은 비율 (0 = 깨짐, 1 = 온전)</summary>
        private float LegRatio(int i)
        {
            if (boss != null)
            {
                if (i >= legs.Length || legs[i] == null) return 0f;
                BossWallHealth leg = legs[i];
                if (!leg.gameObject.activeSelf) return 0f;   // BHS: 깨진 다리는 꺼진다
                return leg.maxHp > 0f ? Mathf.Clamp01(leg.currentHp / leg.maxHp) : 0f;
            }
            return i < testLegs.Length ? testLegs[i] : 0f;
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
                float target = LegRatio(i);

                // 칸 채움: 맞으면 빠르게 줄어든다
                c.shownRatio = Mathf.MoveTowards(c.shownRatio, target, dt * drainSpeed);
                SetRatio(c.fill, c.shownRatio);

                if (!c.broken && target <= 0f && c.shownRatio <= 0.001f)
                {
                    c.broken = true;
                    c.breakTimer = 0f;
                }

                if (c.broken)
                {
                    broken++;
                    c.breakTimer += dt;
                    float t = Mathf.Clamp01(c.breakTimer / breakTime);
                    float open = Mathf.Clamp01(t * 2f);
                    // 자물쇠 고리가 튀어 올라 옆으로 젖혀지며 열린다
                    c.shackle.anchoredPosition = new Vector2(0f, Mathf.Sin(open * Mathf.PI * 0.5f) * 7f);
                    c.shackle.localRotation = Quaternion.Euler(0f, 0f, -35f * open);
                    // 칸이 잠깐 흔들린 뒤 흐려진다
                    float shake = (1f - t) * 3f;
                    c.root.anchoredPosition = c.BasePos + new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake));
                    SetCellAlpha(c, Mathf.Lerp(1f, 0.35f, t));
                }
            }
            BrokenCount = broken;
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
            for (int i = 0; i < n; i++) cells.Add(BuildCell(i, cellWidth, gap));

            // 바 아래 페이즈 이름
            RectTransform titleRect = NewRect("Title", bar);
            titleRect.anchorMin = new Vector2(0f, 0f);
            titleRect.anchorMax = new Vector2(1f, 0f);
            titleRect.pivot = new Vector2(0.5f, 1f);
            titleRect.anchoredPosition = new Vector2(nameWidth * 0.5f, -6f);
            titleRect.sizeDelta = new Vector2(-nameWidth, 26f);
            TMP_Text title = AddText(titleRect.gameObject, phaseTitle, 18f);
            title.alpha = 0.85f;
        }

        private LegCell BuildCell(int i, float width, float gap)
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

            string label = i < legLabels.Length ? legLabels[i] : "다리 " + (i + 1);
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
