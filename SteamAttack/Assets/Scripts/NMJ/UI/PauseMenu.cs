using System.Collections.Generic;
using NGH;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 인게임 ESC 메뉴 (회의 1.6, 기획안 v4 3-5)
    ///
    ///           철과 묵
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
    /// - '환경 설정'은 같은 오브젝트의 SettingsMenu를 연다 (없으면 '준비 중'으로 흐리게).
    /// - '타이틀로'는 타이틀 씬이 빌드 설정에 없으면 흐리게 '준비 중'으로 보인다.
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
        [Tooltip("한자(玄武·命·鎭) 전용 고정 폰트. 비우면 에디터에서 NMJ_HudGlyphs SDF를 자동 연결. 새 한자를 쓰려면 NmjHudFontBuilder에 글자를 더한다")]
        [SerializeField] private TMP_FontAsset glyphFont;

        private TMP_FontAsset HanjaFont => glyphFont != null ? glyphFont : font;
        [Tooltip("조작법 이미지. 비우면 씬에 있는 NGH 조작 안내(ControlsOverlay)의 이미지를 쓴다")]
        [SerializeField] private Texture2D controlsImage;

        [Header("타이틀")]
        [Tooltip("타이틀 씬 이름. 빌드 설정에 없으면 '타이틀로'는 '준비 중'으로 흐리게 보인다")]
        [SerializeField] private string titleScene = "NMJ_Title";
        [Tooltip("타이틀로 돌아갈 때의 먹물 화면 전환")]
        [SerializeField] private InkStyle titleTransition = InkStyle.Drip;

        [Header("환경 설정")]
        [Tooltip("환경 설정 화면. 비우면 같은 오브젝트에서 찾는다. 없으면 '환경 설정'은 '준비 중'으로 흐리게 보인다")]
        [SerializeField] private SettingsMenu settings;

        [Header("글자")]
        [SerializeField] private string gameTitle = "철과 묵";   // 게임 이름 확정 (2026-10-26). 칸 이름을 바꿔 프리팹의 옛 값(玄武)을 쓰지 않게
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

        private enum Page { Closed, Main, Controls, Confirm, Settings }

        private Page page = Page.Closed;
        private float prevTimeScale = 1f;
        private float alpha;
        private int lastGameOverFrame = -10;

        private Style style;
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
            if (settings == null) settings = GetComponent<SettingsMenu>();
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
            if (GameOverScreen.IsShowing || DefeatScreen.IsShowing) lastGameOverFrame = Time.frameCount;

            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) HandleEscape();

            // 열고 닫는 페이드 (멈춘 시간에도 흐르도록 unscaled)
            float target = page == Page.Closed ? 0f : 1f;
            if (!Mathf.Approximately(alpha, target))
            {
                SetVisible(Mathf.MoveTowards(alpha, target, Time.unscaledDeltaTime / fadeTime));
                if (alpha <= 0f) ShowPageObjects();
            }

            if (page == Page.Main) UpdateButtons(mainButtons, style);
            else if (page == Page.Confirm) UpdateButtons(confirmButtons, style);
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
                case Page.Settings:
                    settings.Close();   // 닫히면 메뉴로 돌아온다 (OnSettingsPressed의 콜백)
                    break;
            }
        }

        private bool CanOpen()
        {
            // Game Over 화면이 떠 있거나 방금 닫힌 프레임(같은 ESC로 일어나는 중)에는 열지 않는다
            if (GameOverScreen.IsShowing || DefeatScreen.IsShowing || Time.frameCount - lastGameOverFrame <= 1) return false;
            if (player != null && player.IsDead) return false;
            if (BossDefeatBanner.IsShowing || EndingCredits.IsPlaying) return false;   // 처치 문구 → 엔딩 크레딧 중에는 열지 않는다
            if (NmjOpening.IsPlaying || NmjDialogue.IsPlaying || NmjReadable.IsReading) return false;   // 오프닝·대사·글 읽기는 ESC를 각자 쓴다
            if (NmjDevWindow.BlocksEscape) return false;   // 개발자 창이 ESC로 닫히는 중
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
            if (settings != null && settings.IsOpen) settings.Close();

            ControlsOverlay.MenuOpen = false;
            Time.timeScale = prevTimeScale > 0f ? prevTimeScale : 1f;
            if (pauseAudio) AudioListener.pause = false;
            if (!(GameOverScreen.IsShowing || DefeatScreen.IsShowing))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            Select(null);
            if (group != null) { group.blocksRaycasts = false; group.interactable = false; }
        }

        private void ShowPage(Page next)
        {
            page = next;
            ShowPageObjects();
            if (group != null) { group.blocksRaycasts = true; group.interactable = true; }
            Select(next == Page.Main ? resumeButton : next == Page.Confirm ? confirmNoButton : null);
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
            if (settings == null) return;
            page = Page.Settings;
            ShowPageObjects();   // 메뉴 판은 숨기고 어둠만 남긴다
            settings.Open(() => { if (page == Page.Settings) ShowPage(Page.Main); }, false);
        }

        private void OnTitle()
        {
            Ask("타이틀 화면으로 돌아갈까요?\n<size=70%>지금까지의 진행은 이어지지 않습니다</size>", () =>
            {
                // 먹이 다 덮을 때까지 멈춘 채로 두었다가 넘어간다
                InkTransition.Play(titleTransition, () =>
                {
                    Close();
                    SceneManager.LoadScene(NmjBootToTitle.Resolve(titleScene));
                });
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
            SetEnabled(titleButton, NmjBootToTitle.Resolve(titleScene) != null, style);
            SetEnabled(settingsButton, settings != null, style);
            LinkColumn(mainButtons);
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) font = FindDefaultFont();
            if (glyphFont == null) glyphFont = FindGlyphFont();
#endif
            style = new Style
            {
                font = font,
                textColor = textColor,
                selectedColor = brassColor,
                underlineColor = selectColor,
                disabledAlpha = disabledAlpha,
                buttonHeight = buttonHeight,
                fontSize = buttonFontSize,
            };

            Canvas canvas = NewCanvas("NMJ_PauseMenuCanvas", transform, 90, out group);   // HUD(40~50) 위
            group.blocksRaycasts = false;
            group.interactable = false;

            // 뒤 화면 어둡게 (클릭이 게임으로 새지 않게 막는다)
            Image dim = NewImage("Dim", canvas.transform, dimColor, Vector2.zero, Vector2.zero);
            dim.raycastTarget = true;

            BuildMainPage(canvas.transform);
            BuildControlsPage(canvas.transform);
            BuildConfirmPage(canvas.transform);
            ShowPageObjects();
        }

        private void BuildMainPage(Transform parent)
        {
            mainPage = NewRect("Main", parent);
            RectTransform panel = Panel(mainPage, panelSize, panelColor, brassColor);

            // 현판처럼: 위에 큰 글자, 아래 작은 글자, 놋쇠 줄
            RectTransform head = NewRect("Head", panel);
            head.anchorMin = new Vector2(0f, 1f);
            head.anchorMax = new Vector2(1f, 1f);
            head.pivot = new Vector2(0.5f, 1f);
            head.sizeDelta = new Vector2(0f, 150f);
            head.anchoredPosition = new Vector2(0f, -28f);
            TMP_Text t = NewText("Title", head, font, gameTitle, 64f, brassColor);
            t.rectTransform.offsetMin = new Vector2(0f, 46f);
            TMP_Text s = NewText("Subtitle", head, font, subtitle, 22f, textColor);
            s.rectTransform.offsetMax = new Vector2(0f, -100f);
            s.alpha = 0.7f;
            Image rule = NewImage("Rule", head, new Color(brassColor.r, brassColor.g, brassColor.b, 0.6f), Vector2.zero, Vector2.zero);
            rule.rectTransform.anchorMin = new Vector2(0.18f, 0f);
            rule.rectTransform.anchorMax = new Vector2(0.82f, 0f);
            rule.rectTransform.sizeDelta = new Vector2(0f, 2f);

            // 버튼 줄
            RectTransform list = NewRect("Buttons", panel);
            list.offsetMin = new Vector2(40f, 70f);
            list.offsetMax = new Vector2(-40f, -196f);
            resumeButton = AddButton(mainButtons, list, style, "계속하기", OnResume);
            AddButton(mainButtons, list, style, "조작법", OnControls);
            settingsButton = AddButton(mainButtons, list, style, "환경 설정", OnSettingsPressed);
            titleButton = AddButton(mainButtons, list, style, "타이틀로", OnTitle);
            AddButton(mainButtons, list, style, "게임 종료", OnQuit);
            LayoutColumn(mainButtons, buttonHeight);

            Hint(panel, font, "ESC  계속하기", textColor);
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
            controlsRaw = NewRawImage("Image", area, null);
            controlsFitter = controlsRaw.gameObject.AddComponent<AspectRatioFitter>();
            controlsFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;

            // 이미지가 없을 때 쓰는 글 (기획안 v4 3-1)
            controlsFallback = NewText("Fallback", area, font,
                "WASD  이동      Shift  달리기      Space  구르기\n" +
                "좌클릭  공격 · 사격      우클릭  강공격 · 조준\n" +
                "Tab  환도 ↔ 화승총      C  발도 · 납도      Ctrl  받아치기\n" +
                "Q  시간 역행      F  상호작용      T  락온      ESC  메뉴",
                30f, textColor);
            controlsFallback.lineSpacing = 40f;

            Hint(controlsPage, font, "ESC · 클릭  메뉴로", textColor);
        }

        private void BuildConfirmPage(Transform parent)
        {
            confirmPage = NewRect("Confirm", parent);
            RectTransform panel = Panel(confirmPage, new Vector2(620f, 300f), panelColor, brassColor);

            confirmText = NewText("Question", panel, font, "", 30f, textColor);
            confirmText.rectTransform.offsetMin = new Vector2(30f, 110f);
            confirmText.rectTransform.offsetMax = new Vector2(-30f, -30f);
            confirmText.textWrappingMode = TextWrappingModes.Normal;

            RectTransform row = NewRect("Row", panel);
            row.offsetMin = new Vector2(60f, 30f);
            row.offsetMax = new Vector2(-60f, -200f);
            AddButton(confirmButtons, row, style, "예", () => confirmAction?.Invoke());
            confirmNoButton = AddButton(confirmButtons, row, style, "아니오", () => ShowPage(Page.Main));

            // 가로 두 칸, 왼쪽·오른쪽 키로 오간다
            for (int i = 0; i < confirmButtons.Count; i++)
            {
                RectTransform rt = (RectTransform)confirmButtons[i].button.transform;
                rt.anchorMin = new Vector2(i * 0.5f, 0f);
                rt.anchorMax = new Vector2(i * 0.5f + 0.5f, 1f);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
            confirmButtons[0].button.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = confirmButtons[1].button };
            confirmButtons[1].button.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = confirmButtons[0].button };
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (font == null) font = FindDefaultFont();
            if (glyphFont == null) glyphFont = FindGlyphFont();
        }
#endif
    }
}
