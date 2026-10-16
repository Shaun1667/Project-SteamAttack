using System.Collections.Generic;
using NGH;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace NMJ
{
    /// <summary>
    /// 인게임 ESC 메뉴 (회의 1.6, 기획안 v4 3-5)
    ///
    ///            玄武
    ///          잠시 멈춤
    ///   ─────────────────────
    ///          계속하기
    ///          조작법
    ///          환경 설정
    ///          타이틀로
    ///          게임 종료
    ///
    /// - ESC로 열고 닫는다. 열면 게임이 멈추고(시간·소리) 마우스 커서가 보인다.
    /// - NGH 조작 안내(ControlsOverlay)는 더 이상 ESC로 열리지 않고, 이 메뉴의 '조작법'에서 같은 이미지를 보여 준다.
    /// - 플레이어 입력·카메라는 ControlsOverlay.IsOpen을 보고 멈춘다 (이 메뉴가 ControlsOverlay.MenuOpen을 켠다).
    /// - '타이틀로'는 타이틀 씬이 빌드 설정에 들어오기 전까지 흐리게 '준비 중'으로 보인다. '환경 설정'도 만들기 전까지 '준비 중'.
    /// - 쓰러져 Game Over 화면이 떠 있는 동안에는 열리지 않는다.
    ///
    /// UI는 실행할 때 코드로 만들어진다. 플레이어(NGH_Player)에 붙여 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public class PauseMenu : MonoBehaviour
    {
        [Header("연결 (비우면 자동으로 찾음)")]
        [SerializeField] private PlayerController player;
        [Tooltip("한글·한자 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("조작법 이미지. 비우면 씬에 있는 NGH 조작 안내(ControlsOverlay)의 이미지를 쓴다")]
        [SerializeField] private Texture2D controlsImage;

        [Header("타이틀")]
        [Tooltip("타이틀 씬 이름. 빌드 설정에 없으면 '타이틀로'는 '준비 중'으로 흐리게 보인다")]
        [SerializeField] private string titleScene = "Title";

        [Header("환경 설정")]
        [Tooltip("환경 설정 화면을 여는 곳. 비어 있으면 '환경 설정'은 '준비 중'으로 흐리게 보인다")]
        public UnityEvent OnSettings = new UnityEvent();

        [Header("글자")]
        [SerializeField] private string title = "玄武";
        [SerializeField] private string subtitle = "잠시 멈춤";

        [Header("배치 (화면 1920×1080 기준)")]
        [SerializeField] private Vector2 panelSize = new Vector2(520f, 640f);
        [SerializeField] private float buttonHeight = 66f;
        [SerializeField] private float buttonFontSize = 32f;

        [Header("색 (오방색)")]
        [SerializeField] private Color dimColor = new Color(0.06f, 0.06f, 0.07f, 0.72f);      // 흑
        [SerializeField] private Color panelColor = new Color(0.06f, 0.06f, 0.07f, 0.96f);    // 흑
        [SerializeField] private Color brassColor = new Color(0.94f, 0.73f, 0.16f, 1f);       // 황 (놋쇠 테)
        [SerializeField] private Color textColor = new Color(0.96f, 0.95f, 0.92f, 1f);        // 백
        [SerializeField] private Color selectColor = new Color(0.82f, 0.18f, 0.10f, 1f);      // 적 (고른 줄의 먹 밑줄)
        [SerializeField, Range(0f, 1f)] private float disabledAlpha = 0.35f;

        [Header("동작")]
        [Tooltip("메뉴가 열려 있는 동안 소리도 멈춘다")]
        [SerializeField] private bool pauseAudio = true;
        [Tooltip("열리고 닫히는 페이드 시간(초, 멈춘 시간과 관계없이 흐른다)")]
        [SerializeField, Min(0.01f)] private float fadeTime = 0.12f;

        /// <summary>ESC 메뉴(또는 그 안의 조작법·확인 창)가 열려 있음</summary>
        public static bool IsOpen => ControlsOverlay.MenuOpen;

        private enum Page { Closed, Main, Controls, Confirm }

        private class MenuButton
        {
            public Button button;
            public TMP_Text label;
            public RectTransform underline;
            public bool enabled = true;
            public float line;
        }

        private Page page = Page.Closed;
        private float prevTimeScale = 1f;
        private float alpha;
        private int lastGameOverFrame = -10;

        private CanvasGroup group;
        private RectTransform mainPage, controlsPage, confirmPage;
        private TMP_Text confirmText;
        private RawImage controlsRaw;
        private AspectRatioFitter controlsFitter;
        private TMP_Text controlsFallback;
        private readonly List<MenuButton> mainButtons = new List<MenuButton>();
        private readonly List<MenuButton> confirmButtons = new List<MenuButton>();
        private MenuButton resumeButton, titleButton, settingsButton, confirmNoButton;
        private UnityAction confirmAction;

        // ───────── 시작 ─────────

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            BuildUi();
            SetVisible(0f);
        }

        private void OnEnable() => ControlsOverlay.EscapeHandledByMenu = true;

        private void OnDisable()
        {
            if (page != Page.Closed) Close();
            ControlsOverlay.EscapeHandledByMenu = false;
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            if (GameOverScreen.IsShowing) lastGameOverFrame = Time.frameCount;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) HandleEscape();

            // 열고 닫는 페이드 (멈춘 시간에도 흐르도록 unscaled)
            float target = page == Page.Closed ? 0f : 1f;
            if (!Mathf.Approximately(alpha, target))
            {
                SetVisible(Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeTime));
                if (alpha <= 0f) ShowPageObjects();
            }

            if (page == Page.Main) UpdateButtons(mainButtons);
            else if (page == Page.Confirm) UpdateButtons(confirmButtons);
        }

        private void HandleEscape()
        {
            switch (page)
            {
                case Page.Closed:
                    if (CanOpen()) Open();
                    break;
                case Page.Main:
                    Close();
                    break;
                case Page.Controls:
                case Page.Confirm:
                    ShowPage(Page.Main);
                    break;
            }
        }

        private bool CanOpen()
        {
            // Game Over 화면이 떠 있거나 방금 닫힌 프레임(같은 ESC로 일어나는 중)에는 열지 않는다
            if (GameOverScreen.IsShowing || Time.frameCount - lastGameOverFrame <= 1) return false;
            if (player != null && player.IsDead) return false;
            return !ControlsOverlay.GuideOpen;
        }

        // ───────── 열고 닫기 ─────────

        /// <summary>메뉴를 열고 게임을 멈춘다</summary>
        public void Open()
        {
            if (page != Page.Closed) return;
            EnsureEventSystem();
            RefreshAvailability();

            ControlsOverlay.MenuOpen = true;
            prevTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            if (pauseAudio) AudioListener.pause = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            ShowPage(Page.Main);
        }

        /// <summary>메뉴를 닫고 게임을 이어 간다</summary>
        public void Close()
        {
            if (page == Page.Closed) return;
            page = Page.Closed;   // 화면은 페이드가 끝나면 숨긴다

            ControlsOverlay.MenuOpen = false;
            Time.timeScale = prevTimeScale > 0f ? prevTimeScale : 1f;
            if (pauseAudio) AudioListener.pause = false;
            if (!GameOverScreen.IsShowing)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
            if (group != null) { group.blocksRaycasts = false; group.interactable = false; }
        }

        private void ShowPage(Page next)
        {
            page = next;
            ShowPageObjects();
            if (group != null) { group.blocksRaycasts = true; group.interactable = true; }

            MenuButton first = next == Page.Main ? resumeButton : next == Page.Confirm ? confirmNoButton : null;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first != null ? first.button.gameObject : null);
        }

        private void ShowPageObjects()
        {
            mainPage.gameObject.SetActive(page == Page.Main);
            controlsPage.gameObject.SetActive(page == Page.Controls);
            confirmPage.gameObject.SetActive(page == Page.Confirm);
        }

        private void SetVisible(float a)
        {
            alpha = a;
            if (group != null) group.alpha = a;
        }

        // ───────── 버튼 동작 ─────────

        private void OnResume() => Close();

        private void OnControls()
        {
            Texture2D img = controlsImage;
            if (img == null)
            {
                ControlsOverlay overlay = FindAnyObjectByType<ControlsOverlay>(FindObjectsInactive.Include);
                if (overlay != null) img = overlay.guide;
            }
            controlsRaw.texture = img;
            controlsRaw.enabled = img != null;
            if (img != null) controlsFitter.aspectRatio = (float)img.width / img.height;
            controlsFallback.gameObject.SetActive(img == null);
            ShowPage(Page.Controls);
        }

        private void OnSettingsPressed()
        {
            if (OnSettings.GetPersistentEventCount() == 0) return;
            OnSettings.Invoke();
        }

        private void OnTitle()
        {
            Ask("타이틀 화면으로 돌아갈까요?\n<size=70%>지금까지의 진행은 이어지지 않습니다</size>", () =>
            {
                Close();
                SceneManager.LoadScene(titleScene);
            });
        }

        private void OnQuit()
        {
            Ask("게임을 끝낼까요?", () =>
            {
                Close();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            });
        }

        private void Ask(string question, UnityAction yes)
        {
            confirmText.text = question;
            confirmAction = yes;
            ShowPage(Page.Confirm);
        }

        private void RefreshAvailability()
        {
            SetEnabled(titleButton, !string.IsNullOrEmpty(titleScene) && Application.CanStreamedLevelBeLoaded(titleScene));
            SetEnabled(settingsButton, OnSettings.GetPersistentEventCount() > 0);
            LinkColumn(mainButtons);
        }

        private void SetEnabled(MenuButton b, bool on)
        {
            b.enabled = on;
            b.button.interactable = on;
            b.label.alpha = on ? 1f : disabledAlpha;
            Transform tag = b.button.transform.Find("Soon");
            if (tag != null) tag.gameObject.SetActive(!on);
        }

        // ───────── 고른 줄 표시 ─────────

        private void UpdateButtons(List<MenuButton> buttons)
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            float dt = Time.unscaledDeltaTime;
            foreach (MenuButton b in buttons)
            {
                bool on = b.enabled && selected == b.button.gameObject;
                // 고른 줄은 먹 밑줄이 왼쪽에서 오른쪽으로 그어진다
                b.line = Mathf.MoveTowards(b.line, on ? 1f : 0f, dt * 7f);
                float width = b.label.preferredWidth + 24f;
                b.underline.sizeDelta = new Vector2(width * Mathf.SmoothStep(0f, 1f, b.line), b.underline.sizeDelta.y);
                Color c = on ? brassColor : textColor;
                c.a = b.enabled ? 1f : disabledAlpha;
                b.label.color = c;
            }
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) AutoAssignFont();
#endif
            var canvasGo = new GameObject("NMJ_PauseMenuCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90;   // HUD(40~50) 위
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();
            group = canvasGo.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            // 뒤 화면 어둡게 (클릭이 게임으로 새지 않게 막는다)
            Image dim = NewImage("Dim", canvasGo.transform, dimColor, Vector2.zero, Vector2.zero);
            dim.raycastTarget = true;

            BuildMainPage(canvasGo.transform);
            BuildControlsPage(canvasGo.transform);
            BuildConfirmPage(canvasGo.transform);
            ShowPageObjects();
        }

        private void BuildMainPage(Transform parent)
        {
            mainPage = NewRect("Main", parent);
            RectTransform panel = Panel(mainPage, panelSize);

            // 현판처럼: 위에 큰 글자, 아래 작은 글자, 놋쇠 줄
            RectTransform head = NewRect("Head", panel);
            head.anchorMin = new Vector2(0f, 1f);
            head.anchorMax = new Vector2(1f, 1f);
            head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(0f, 150f);
            head.anchoredPosition = new Vector2(0f, -28f);
            TMP_Text t = NewText("Title", head, title, 64f, brassColor);
            t.rectTransform.offsetMin = new Vector2(0f, 46f);
            TMP_Text s = NewText("Subtitle", head, subtitle, 22f, textColor);
            s.rectTransform.offsetMax = new Vector2(0f, -100f);
            s.alpha = 0.7f;
            Image rule = NewImage("Rule", head, brassColor, Vector2.zero, Vector2.zero);
            rule.rectTransform.anchorMin = new Vector2(0.18f, 0f);
            rule.rectTransform.anchorMax = new Vector2(0.82f, 0f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);
            rule.color = new Color(brassColor.r, brassColor.g, brassColor.b, 0.6f);

            // 버튼 줄
            RectTransform list = NewRect("Buttons", panel);
            list.offsetMin = new Vector2(40f, 70f);
            list.offsetMax = new Vector2(-40f, -196f);
            resumeButton = AddButton(mainButtons, list, "계속하기", OnResume);
            AddButton(mainButtons, list, "조작법", OnControls);
            settingsButton = AddButton(mainButtons, list, "환경 설정", OnSettingsPressed);
            titleButton = AddButton(mainButtons, list, "타이틀로", OnTitle);
            AddButton(mainButtons, list, "게임 종료", OnQuit);
            LayoutColumn(mainButtons);

            Hint(panel, "ESC  계속하기");
        }

        private void BuildControlsPage(Transform parent)
        {
            controlsPage = NewRect("Controls", parent);

            // 화면 85% 안에 비율을 지켜 보여 준다. 아무 곳이나 누르면 메뉴로
            Button back = controlsPage.gameObject.AddComponent<Button>();
            back.transition = Selectable.Transition.None;
            Image hit = controlsPage.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            back.targetGraphic = hit;
            back.onClick.AddListener(() => ShowPage(Page.Main));

            RectTransform area = NewRect("Area", controlsPage);
            area.anchorMin = new Vector2(0.075f, 0.1f);
            area.anchorMax = new Vector2(0.925f, 0.95f);
            RectTransform img = NewRect("Image", area);
            controlsRaw = img.gameObject.AddComponent<RawImage>();
            controlsRaw.raycastTarget = false;
            controlsFitter = img.gameObject.AddComponent<AspectRatioFitter>();
            controlsFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            // 이미지가 없을 때 쓰는 글 (기획안 v4 3-1)
            controlsFallback = NewText("Fallback", area,
                "WASD  이동      Shift  달리기      Space  구르기\n" +
                "좌클릭  공격 · 사격      우클릭  강공격 · 조준\n" +
                "Tab  환도 ↔ 화승총      C  발도 · 납도      Ctrl  받아치기\n" +
                "Q  시간 역행      F  상호작용      T  락온      ESC  메뉴",
                30f, textColor);
            controlsFallback.lineSpacing = 40f;

            Hint(controlsPage, "ESC · 클릭  메뉴로");
        }

        private void BuildConfirmPage(Transform parent)
        {
            confirmPage = NewRect("Confirm", parent);
            RectTransform panel = Panel(confirmPage, new Vector2(620f, 300f));

            confirmText = NewText("Question", panel, "", 30f, textColor);
            confirmText.rectTransform.offsetMin = new Vector2(30f, 110f);
            confirmText.rectTransform.offsetMax = new Vector2(-30f, -30f);
            confirmText.textWrappingMode = TextWrappingModes.Normal;

            RectTransform row = NewRect("Row", panel);
            row.offsetMin = new Vector2(60f, 30f);
            row.offsetMax = new Vector2(-60f, -200f);
            AddButton(confirmButtons, row, "예", () => confirmAction?.Invoke());
            confirmNoButton = AddButton(confirmButtons, row, "아니오", () => ShowPage(Page.Main));

            // 가로 두 칸, 왼쪽·오른쪽 키로 오간다
            for (int i = 0; i < confirmButtons.Count; i++)
            {
                RectTransform rt = (RectTransform)confirmButtons[i].button.transform;
                rt.anchorMin = new Vector2(i * 0.5f, 0f);
                rt.anchorMax = new Vector2(i * 0.5f + 0.5f, 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            Navigation a = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = confirmButtons[1].button };
            Navigation b = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = confirmButtons[0].button };
            confirmButtons[0].button.navigation = a;
            confirmButtons[1].button.navigation = b;
        }

        /// <summary>흑 바탕 + 놋쇠 겹테 판</summary>
        private RectTransform Panel(RectTransform parent, Vector2 size)
        {
            RectTransform panel = NewRect("Panel", parent);
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = size;
            Image bg = NewImage("Bg", panel, panelColor, Vector2.zero, Vector2.zero);
            bg.raycastTarget = true;
            Frame(panel, 0f, 2f, 1f);
            Frame(panel, 8f, 1f, 0.45f);
            return panel;
        }

        private void Frame(RectTransform parent, float inset, float thickness, float a)
        {
            Color c = new Color(brassColor.r, brassColor.g, brassColor.b, a);
            Edge(parent, c, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(inset, -inset - thickness), new Vector2(-inset, -inset));
            Edge(parent, c, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(inset, inset), new Vector2(-inset, inset + thickness));
            Edge(parent, c, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(inset, inset), new Vector2(inset + thickness, -inset));
            Edge(parent, c, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-inset - thickness, inset), new Vector2(-inset, -inset));
        }

        private static void Edge(RectTransform parent, Color c, Vector2 min, Vector2 max, Vector2 offMin, Vector2 offMax)
        {
            Image e = NewImage("Edge", parent, c, offMin, offMax);
            e.rectTransform.anchorMin = min;
            e.rectTransform.anchorMax = max;
        }

        private MenuButton AddButton(List<MenuButton> list, RectTransform parent, string text, UnityAction onClick)
        {
            var b = new MenuButton();
            RectTransform rt = NewRect("Btn_" + text, parent);
            Image hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;   // 누를 자리만 잡는다
            b.button = rt.gameObject.AddComponent<Button>();
            b.button.targetGraphic = hit;
            b.button.transition = Selectable.Transition.None;
            b.button.onClick.AddListener(onClick);

            // 마우스를 올리면 그 줄을 고른다 → 키보드와 마우스가 같은 표시를 쓴다
            var trigger = rt.gameObject.AddComponent<EventTrigger>();
            var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
            enter.callback.AddListener(_ => { if (b.enabled && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(rt.gameObject); });
            trigger.triggers.Add(enter);

            b.label = NewText("Label", rt, text, buttonFontSize, textColor);

            b.underline = NewRect("Underline", rt);
            b.underline.anchorMin = b.underline.anchorMax = new Vector2(0.5f, 0.5f);
            b.underline.sizeDelta = new Vector2(0f, 3f);
            b.underline.anchoredPosition = new Vector2(0f, -buttonFontSize * 0.62f);
            Image ink = b.underline.gameObject.AddComponent<Image>();
            ink.color = selectColor;
            ink.raycastTarget = false;

            TMP_Text soon = NewText("Soon", rt, "준비 중", 16f, textColor);
            soon.alignment = TextAlignmentOptions.Right;
            soon.alpha = 0.5f;
            soon.gameObject.SetActive(false);

            list.Add(b);
            return b;
        }

        /// <summary>버튼을 위에서부터 한 줄씩 둔다</summary>
        private void LayoutColumn(List<MenuButton> buttons)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                RectTransform rt = (RectTransform)buttons[i].button.transform;
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(0.5f, 1f);
                rt.sizeDelta = new Vector2(0f, buttonHeight);
                rt.anchoredPosition = new Vector2(0f, -i * buttonHeight);
            }
            LinkColumn(buttons);
        }

        /// <summary>위아래 키로 쓸 수 있는 버튼만 돌아가며 오가게 잇는다 ('준비 중'은 건너뜀)</summary>
        private static void LinkColumn(List<MenuButton> buttons)
        {
            var usable = buttons.FindAll(b => b.enabled);
            foreach (MenuButton b in buttons) b.button.navigation = new Navigation { mode = Navigation.Mode.None };
            for (int i = 0; i < usable.Count; i++)
            {
                usable[i].button.navigation = new Navigation
                {
                    mode = Navigation.Mode.Explicit,
                    selectOnUp = usable[(i - 1 + usable.Count) % usable.Count].button,
                    selectOnDown = usable[(i + 1) % usable.Count].button,
                };
            }
        }

        private void Hint(RectTransform parent, string text)
        {
            TMP_Text h = NewText("Hint", parent, text, 18f, textColor);
            RectTransform rt = h.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(0f, 30f);
            rt.anchoredPosition = new Vector2(0f, 22f);
            h.alpha = 0.5f;
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
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

        private TMP_Text NewText(string name, Transform parent, string text, float size, Color color)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = color;
            t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            return t;
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
