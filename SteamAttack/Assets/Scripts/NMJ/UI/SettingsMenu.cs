using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 환경 설정 화면 (회의 1.6, 기획안 v4 3-5). ESC 메뉴와 타이틀의 '환경 설정'에서 연다.
    ///
    ///                  환경 설정
    ///   소리   마스터 볼륨   ━━━━━━━○──────   70
    ///          배경음        ━━━━━━━━━━━━○  100
    ///          효과음        ━━━━━━━━━━━━○  100
    ///   게임   마우스 감도   ━━━━━━○───────  100%
    ///   화면   해상도            ◀ 1920 × 1080 ▶
    ///          화면 모드         ◀  전체 화면  ▶
    ///                 기본값      돌아가기
    ///
    /// - 바꾸는 즉시 적용·저장된다 (GameSettings). '기본값'은 소리·감도만 처음 값으로.
    /// - 위·아래 키로 줄 고르기, 좌·우 키로 값 바꾸기, 마우스로 끌기·화살표 누르기.
    /// - ESC는 여는 쪽(PauseMenu·TitleScreen)이 받아서 Close()를 부른다 (같은 ESC로 두 번 닫히지 않게).
    ///
    /// UI는 실행할 때 코드로 만들어진다. ESC 메뉴·타이틀과 같은 오브젝트에 붙여 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public class SettingsMenu : MonoBehaviour
    {
        [Tooltip("한글 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;

        [Header("색 (오방색)")]
        [SerializeField] private Color dimColor = new Color(0.06f, 0.06f, 0.07f, 0.6f);
        [SerializeField] private Color panelColor = new Color(0.06f, 0.06f, 0.07f, 1f);
        [SerializeField] private Color brassColor = new Color(0.94f, 0.73f, 0.16f, 1f);
        [SerializeField] private Color textColor = new Color(0.96f, 0.95f, 0.92f, 1f);
        [SerializeField] private Color redColor = new Color(0.82f, 0.18f, 0.10f, 1f);
        [SerializeField] private Color trackColor = new Color(0.22f, 0.22f, 0.24f, 1f);

        /// <summary>환경 설정 화면이 열려 있음</summary>
        public bool IsOpen { get; private set; }

        private class Row
        {
            public Selectable selectable;
            public TMP_Text label;
            public TMP_Text value;
            public Slider slider;
            public Action refresh;
        }

        private const float PanelWidth = 820f;
        private const float RowHeight = 56f;
        private const float SectionGap = 38f;

        private Style style;
        private CanvasGroup group;
        private Image dim;
        private RectTransform panel;
        private float cursorY;
        private readonly List<Row> rows = new List<Row>();
        private readonly List<MenuButton> buttons = new List<MenuButton>();
        private MenuButton backButton;
        private Action onClosed;
        private float alpha;

        private List<Vector2Int> resolutions = new List<Vector2Int>();
        private int resolutionIndex;
        private static readonly string[] ModeNames = { "창 모드", "전체 화면" };

        // ───────── 시작 ─────────

        private void Awake()
        {
            BuildUi();
            SetAlpha(0f);
            group.gameObject.SetActive(false);
        }

        private void OnDisable()
        {
            if (IsOpen) Close();
        }

        /// <summary>환경 설정을 연다. 닫히면 onClosed를 부른다. dimBackground = 뒤 화면을 어둡게 (이미 어두운 ESC 메뉴 위면 false)</summary>
        public void Open(Action closed, bool dimBackground = true)
        {
            if (IsOpen) return;
            EnsureEventSystem();
            IsOpen = true;
            onClosed = closed;
            dim.enabled = dimBackground;
            group.gameObject.SetActive(true);
            group.blocksRaycasts = true;
            group.interactable = true;

            resolutions = GameSettings.Resolutions();
            resolutionIndex = Mathf.Max(0, resolutions.IndexOf(new Vector2Int(Screen.width, Screen.height)));
            foreach (Row r in rows) r.refresh();
            EventSystem.current?.SetSelectedGameObject(rows[0].selectable.gameObject);
        }

        /// <summary>환경 설정을 닫고 연 쪽으로 돌아간다</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            group.blocksRaycasts = false;
            group.interactable = false;
            EventSystem.current?.SetSelectedGameObject(null);
            Action cb = onClosed;
            onClosed = null;
            cb?.Invoke();
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            float target = IsOpen ? 1f : 0f;
            if (!Mathf.Approximately(alpha, target))
            {
                SetAlpha(Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / 0.12f));
                if (alpha <= 0f) group.gameObject.SetActive(false);
            }
            if (!IsOpen) return;

            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            foreach (Row r in rows)
                r.label.color = selected == r.selectable.gameObject ? brassColor : textColor;
            UpdateButtons(buttons, style);
        }

        private void SetAlpha(float a)
        {
            alpha = a;
            group.alpha = a;
        }

        // ───────── 값 ─────────

        private static string Percent(float v) => Mathf.RoundToInt(v * 100f).ToString();

        private void StepResolution(int dir)
        {
            if (resolutions.Count == 0) return;
            resolutionIndex = Mathf.Clamp(resolutionIndex + dir, 0, resolutions.Count - 1);
            GameSettings.SetDisplay(resolutions[resolutionIndex], GameSettings.Fullscreen);
        }

        private void StepMode(int dir)
        {
            Vector2Int size = resolutions.Count > 0 ? resolutions[resolutionIndex] : new Vector2Int(Screen.width, Screen.height);
            GameSettings.SetDisplay(size, !GameSettings.Fullscreen);
        }

        private void ResetDefaults()
        {
            GameSettings.ResetToDefaults();
            foreach (Row r in rows) r.refresh();
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) font = FindDefaultFont();
#endif
            style = new Style { font = font, textColor = textColor, selectedColor = brassColor, underlineColor = redColor, fontSize = 28f, buttonHeight = 56f };

            Canvas canvas = NewCanvas("NMJ_SettingsCanvas", transform, 95, out group);   // ESC 메뉴(90) 위
            dim = NewImage("Dim", canvas.transform, dimColor, Vector2.zero, Vector2.zero);
            dim.raycastTarget = true;

            panel = Panel(canvas.transform, new Vector2(PanelWidth, 740f), panelColor, brassColor);

            TMP_Text title = NewText("Title", panel, font, "환경 설정", 40f, brassColor);
            title.rectTransform.anchorMin = new Vector2(0f, 1f);
            title.rectTransform.anchorMax = new Vector2(1f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);
            title.rectTransform.sizeDelta = new Vector2(0f, 60f);
            title.rectTransform.anchoredPosition = new Vector2(0f, -30f);
            Image rule = NewImage("Rule", panel, new Color(brassColor.r, brassColor.g, brassColor.b, 0.6f), Vector2.zero, Vector2.zero);
            rule.rectTransform.anchorMin = new Vector2(0.1f, 1f);
            rule.rectTransform.anchorMax = new Vector2(0.9f, 1f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);
            rule.rectTransform.anchoredPosition = new Vector2(0f, -98f);

            cursorY = -118f;
            Section("소리");
            SliderRow("마스터 볼륨", 0f, 1f, () => GameSettings.Master, GameSettings.SetMaster, Percent);
            SliderRow("배경음", 0f, 1f, () => GameSettings.Music, GameSettings.SetMusic, Percent);
            SliderRow("효과음", 0f, 1f, () => GameSettings.Sfx, GameSettings.SetSfx, Percent);
            Section("게임");
            SliderRow("마우스 감도", GameSettings.MinMouse, GameSettings.MaxMouse, () => GameSettings.MouseSensitivity, GameSettings.SetMouseSensitivity, v => Percent(v) + "%");
            Section("화면");
            ChoiceRow("해상도", StepResolution, () =>
                resolutions.Count > 0 ? $"{resolutions[resolutionIndex].x} × {resolutions[resolutionIndex].y}" : $"{Screen.width} × {Screen.height}");
            ChoiceRow("화면 모드", StepMode, () => ModeNames[GameSettings.Fullscreen ? 1 : 0]);

            // 아래 버튼 두 개 (가로)
            RectTransform bar = NewRect("Buttons", panel);
            bar.anchorMin = new Vector2(0.2f, 0f);
            bar.anchorMax = new Vector2(0.8f, 0f);
            bar.pivot = new Vector2(0.5f, 0f);
            bar.sizeDelta = new Vector2(0f, 56f);
            bar.anchoredPosition = new Vector2(0f, 62f);
            AddButton(buttons, bar, style, "기본값", ResetDefaults);
            backButton = AddButton(buttons, bar, style, "돌아가기", Close);
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform rt = (RectTransform)buttons[i].button.transform;
                rt.anchorMin = new Vector2(i * 0.5f, 0f);
                rt.anchorMax = new Vector2(i * 0.5f + 0.5f, 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }

            Hint(panel, font, "위·아래 고르기 · 좌·우 바꾸기 · ESC 돌아가기", textColor);
            LinkNavigation();
        }

        private void Section(string name)
        {
            TMP_Text t = NewText("Section_" + name, panel, font, name, 20f, brassColor);
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(-100f, 30f);
            rt.anchoredPosition = new Vector2(0f, cursorY);
            t.alignment = TextAlignmentOptions.Left;
            t.alpha = 0.75f;
            cursorY -= SectionGap - 6f;
        }

        /// <summary>한 줄: 왼쪽 이름, 가운데 조절 칸, 오른쪽 값</summary>
        private RectTransform NewRow(string name, out TMP_Text label, out TMP_Text value, out RectTransform control)
        {
            RectTransform row = NewRect("Row_" + name, panel);
            row.anchorMin = new Vector2(0f, 1f);
            row.anchorMax = new Vector2(1f, 1f);
            row.pivot = new Vector2(0.5f, 1f);
            row.sizeDelta = new Vector2(0f, RowHeight);
            row.anchoredPosition = new Vector2(0f, cursorY);
            cursorY -= RowHeight;

            label = NewText("Label", row, font, name, 26f, textColor);
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(0f, 1f);
            label.rectTransform.pivot = new Vector2(0f, 0.5f);
            label.rectTransform.sizeDelta = new Vector2(220f, 0f);
            label.rectTransform.anchoredPosition = new Vector2(80f, 0f);
            label.alignment = TextAlignmentOptions.Left;

            value = NewText("Value", row, font, "", 24f, textColor);
            value.rectTransform.anchorMin = new Vector2(1f, 0f);
            value.rectTransform.anchorMax = new Vector2(1f, 1f);
            value.rectTransform.pivot = new Vector2(1f, 0.5f);
            value.rectTransform.sizeDelta = new Vector2(100f, 0f);
            value.rectTransform.anchoredPosition = new Vector2(-60f, 0f);
            value.alignment = TextAlignmentOptions.Right;

            control = NewRect("Control", row);
            control.offsetMin = new Vector2(320f, 8f);
            control.offsetMax = new Vector2(-180f, -8f);
            return row;
        }

        private void SliderRow(string name, float min, float max, Func<float> get, Action<float> set, Func<float, string> format)
        {
            NewRow(name, out TMP_Text label, out TMP_Text value, out RectTransform control);

            // 놋쇠로 차오르는 가는 막대 + 백색 손잡이 (←→ 한 번에 10%)
            Image hit = control.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            Slider slider = control.gameObject.AddComponent<Slider>();
            slider.transition = Selectable.Transition.None;
            slider.direction = Slider.Direction.LeftToRight;

            Image track = NewImage("Track", control, trackColor, Vector2.zero, Vector2.zero);
            track.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            track.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(0f, 6f);

            RectTransform fillArea = NewRect("FillArea", control);
            fillArea.anchorMin = new Vector2(0f, 0.5f);
            fillArea.anchorMax = new Vector2(1f, 0.5f);
            fillArea.sizeDelta = new Vector2(0f, 6f);
            Image fill = NewImage("Fill", fillArea, brassColor, Vector2.zero, Vector2.zero);

            RectTransform handleArea = NewRect("HandleArea", control);
            handleArea.offsetMin = new Vector2(8f, 0f);
            handleArea.offsetMax = new Vector2(-8f, 0f);
            Image handle = NewImage("Handle", handleArea, textColor, Vector2.zero, Vector2.zero);
            handle.rectTransform.sizeDelta = new Vector2(14f, 0f);

            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = hit;
            slider.minValue = min;
            slider.maxValue = max;
            slider.onValueChanged.AddListener(v => { set(v); value.text = format(v); });
            SelectOnHover(control.gameObject, slider);

            var row = new Row { selectable = slider, label = label, value = value, slider = slider };
            row.refresh = () => { float v = get(); slider.SetValueWithoutNotify(v); value.text = format(v); };
            rows.Add(row);
        }

        private void ChoiceRow(string name, Action<int> step, Func<string> text)
        {
            NewRow(name, out TMP_Text label, out TMP_Text value, out RectTransform control);
            value.gameObject.SetActive(false);   // 값은 화살표 사이에 쓴다

            Image hit = control.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var chooser = control.gameObject.AddComponent<NmjChoiceSelector>();
            chooser.transition = Selectable.Transition.None;
            chooser.targetGraphic = hit;

            TMP_Text shown = NewText("Shown", control, font, "", 26f, textColor);
            var row = new Row { selectable = chooser, label = label, value = shown };
            row.refresh = () => shown.text = text();
            chooser.OnStep = d => { step(d); row.refresh(); };

            Arrow(control, "〈", 0f, () => { chooser.Select(); chooser.OnStep(-1); });
            Arrow(control, "〉", 1f, () => { chooser.Select(); chooser.OnStep(1); });
            SelectOnHover(control.gameObject, chooser);
            rows.Add(row);
        }

        private void Arrow(RectTransform parent, string glyph, float side, Action onClick)
        {
            RectTransform rt = NewRect("Arrow" + glyph, parent);
            rt.anchorMin = new Vector2(side, 0f);
            rt.anchorMax = new Vector2(side, 1f);
            rt.pivot = new Vector2(side, 0.5f);
            rt.sizeDelta = new Vector2(44f, 0f);
            Image hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            Button b = rt.gameObject.AddComponent<Button>();
            b.transition = Selectable.Transition.None;
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            b.targetGraphic = hit;
            b.onClick.AddListener(() => onClick());
            NewText("Glyph", rt, font, glyph, 22f, brassColor);
        }

        private static void SelectOnHover(GameObject go, Selectable target)
        {
            var trigger = go.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => EventSystem.current?.SetSelectedGameObject(target.gameObject));
            trigger.triggers.Add(enter);
        }

        /// <summary>줄은 위아래로, 아래 버튼 둘은 좌우로. 맨 아래 → 맨 위로 돌아간다</summary>
        private void LinkNavigation()
        {
            Button resetB = buttons[0].button, backB = backButton.button;
            for (int i = 0; i < rows.Count; i++)
            {
                Navigation n = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = i > 0 ? rows[i - 1].selectable : backB,
                    selectOnDown = i < rows.Count - 1 ? rows[i + 1].selectable : backB,
                };
                rows[i].selectable.navigation = n;
            }
            Selectable last = rows[rows.Count - 1].selectable, first = rows[0].selectable;
            resetB.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = backB, selectOnUp = last, selectOnDown = first };
            backB.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = resetB, selectOnUp = last, selectOnDown = first };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (font == null) font = FindDefaultFont();
        }
#endif
    }
}
