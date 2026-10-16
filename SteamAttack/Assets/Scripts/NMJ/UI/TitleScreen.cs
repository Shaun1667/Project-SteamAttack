using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 타이틀 화면 (회의 1.6, 기획안 v4 3-5): 게임 시작 / 환경 설정 / 게임 종료
    ///
    ///                 玄武  [命]
    ///               역류의 어명
    ///        1887년 정월 · 시간이 멈춘 한양
    ///
    ///                 게임 시작
    ///                 환경 설정
    ///                 게임 종료
    ///
    ///   실존 인물·사건과 무관한 창작입니다              시연판
    ///
    /// - 그림이 아직 없어도 돌아가게 했다. 아트가 오면 인스펙터에 넣기만 하면 바뀐다.
    ///   Logo Image  : 붓글씨 로고 (UI_TitleLogo, 배경 투명 PNG). 넣으면 글자 로고 대신 보인다.
    ///   Background  : 배경 그림 (UI_TitleBG, 1920×1080). 넣으면 임시 배경(어둠 + 멈춘 눈송이) 대신 보인다.
    ///   Music       : 타이틀 배경음 (반복)
    /// - '게임 시작'은 Game Scene을 불러온다. 빌드 설정에 없으면 '준비 중'으로 흐리게 보인다.
    /// - '환경 설정'은 OnSettings에 연결하기 전까지 '준비 중'.
    ///
    /// UI는 실행할 때 코드로 만들어진다. NMJ_Title 씬의 빈 오브젝트에 붙여 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public class TitleScreen : MonoBehaviour
    {
        [Header("그림 (아트가 오면 넣기)")]
        [Tooltip("붓글씨 로고 (배경 투명). 비우면 글자 로고를 쓴다")]
        [SerializeField] private Texture2D logoImage;
        [Tooltip("배경 그림 (1920×1080). 비우면 임시 배경(어둠 + 멈춘 눈송이)을 쓴다")]
        [SerializeField] private Texture2D backgroundImage;
        [Tooltip("배경 그림 위에 까는 어둠 (글자가 잘 읽히게)")]
        [SerializeField, Range(0f, 1f)] private float backgroundDim = 0.35f;
        [SerializeField] private AudioClip music;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.7f;

        [Header("연결")]
        [Tooltip("'게임 시작'으로 불러올 씬 이름 (빌드 설정에 있어야 함)")]
        [SerializeField] private string gameScene = "NMJ_Main";
        [Tooltip("환경 설정 화면을 여는 곳. 비어 있으면 '환경 설정'은 '준비 중'으로 흐리게 보인다")]
        public UnityEvent OnSettings = new UnityEvent();
        [Tooltip("한글·한자 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("한자(玄武·命·討) 전용 고정 폰트. 비우면 에디터에서 NMJ_HudGlyphs SDF를 자동 연결. 새 한자를 쓰려면 NmjHudFontBuilder에 글자를 더한다")]
        [SerializeField] private TMP_FontAsset glyphFont;

        private TMP_FontAsset HanjaFont => glyphFont != null ? glyphFont : font;

        [Header("글자 로고 (Logo Image가 없을 때)")]
        [SerializeField] private string logoText = "玄武";
        [SerializeField] private string logoSeal = "命";
        [SerializeField] private string subtitle = "역류의 어명";
        [SerializeField] private string tagline = "1887년 정월 · 시간이 멈춘 한양";

        [Header("아래 글")]
        [SerializeField] private string notice = "이 작품은 실존 인물·사건과 무관한 창작입니다";
        [SerializeField] private string version = "시연판";

        [Header("색 (오방색)")]
        [SerializeField] private Color topColor = new Color(0.06f, 0.06f, 0.07f, 1f);        // 흑
        [SerializeField] private Color bottomColor = new Color(0.20f, 0.24f, 0.27f, 1f);     // 회청 #5B6B75를 어둡게
        [SerializeField] private Color snowColor = new Color(0.83f, 0.93f, 0.95f, 1f);       // 서리 #D4ECF1
        [SerializeField] private Color textColor = new Color(0.96f, 0.95f, 0.92f, 1f);       // 백
        [SerializeField] private Color brassColor = new Color(0.94f, 0.73f, 0.16f, 1f);      // 황
        [SerializeField] private Color sealColor = new Color(0.82f, 0.18f, 0.10f, 1f);       // 적

        [Header("임시 배경")]
        [SerializeField, Range(0, 400)] private int snowCount = 140;

        private Style style;
        private CanvasGroup group, fade;
        private readonly List<MenuButton> buttons = new List<MenuButton>();
        private MenuButton startButton, settingsButton;
        private readonly List<Texture2D> owned = new List<Texture2D>();

        private struct Flake { public RectTransform rt; public Image img; public Vector2 basePos; public float phase, speed, size; }
        private readonly List<Flake> flakes = new List<Flake>();

        private float introTimer;
        private float leaveTimer = -1f;
        private string leaveTo;

        // ───────── 시작 ─────────

        private void Awake()
        {
            // ESC 메뉴에서 넘어와도 멈춘 상태가 남지 않게
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            BuildUi();
            fade.alpha = 1f;
        }

        private void Start()
        {
            EnsureEventSystem();
            SetEnabled(startButton, !string.IsNullOrEmpty(gameScene) && Application.CanStreamedLevelBeLoaded(gameScene), style);
            SetEnabled(settingsButton, OnSettings.GetPersistentEventCount() > 0, style);
            LinkColumn(buttons);
            Select(startButton.enabled ? startButton : buttons.Find(b => b.enabled));

            if (music != null)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.clip = music;
                src.loop = true;
                src.volume = musicVolume;
                src.Play();
            }
        }

        private void OnDestroy()
        {
            foreach (Texture2D t in owned) if (t != null) Destroy(t);
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            // 검은 화면에서 천천히 밝아지고, 로고 → 메뉴 순으로 나온다
            introTimer += dt;
            if (leaveTimer < 0f) fade.alpha = 1f - Mathf.Clamp01(introTimer / 1.2f);
            group.alpha = 1f;

            UpdateButtons(buttons, style);
            UpdateSnow(dt);

            // 게임 시작: 어두워진 뒤 씬을 불러온다
            if (leaveTimer >= 0f)
            {
                leaveTimer += dt;
                fade.alpha = Mathf.Clamp01(leaveTimer / 0.7f);
                if (leaveTimer >= 0.8f) { leaveTimer = -1f; SceneManager.LoadScene(leaveTo); }
            }
        }

        private void UpdateSnow(float dt)
        {
            // 시간이 멈춘 도성: 눈송이가 공중에 떠서 아주 조금만 흔들리고, 반짝이듯 밝기가 바뀐다
            float t = Time.unscaledTime;
            foreach (Flake f in flakes)
            {
                Vector2 drift = new Vector2(Mathf.Sin(t * f.speed + f.phase) * 3f, Mathf.Cos(t * f.speed * 0.7f + f.phase) * 2f);
                f.rt.anchoredPosition = f.basePos + drift;
                Color c = f.img.color;
                c.a = (0.35f + 0.35f * Mathf.Sin(t * f.speed * 1.3f + f.phase * 2f)) * Mathf.Clamp01(f.size / 6f + 0.3f);
                f.img.color = c;
            }
        }

        // ───────── 버튼 동작 ─────────

        private void OnStart()
        {
            if (leaveTimer >= 0f) return;
            leaveTo = gameScene;
            leaveTimer = 0f;
            group.interactable = false;
        }

        private void OnSettingsPressed()
        {
            if (OnSettings.GetPersistentEventCount() == 0) return;
            OnSettings.Invoke();
        }

        private void OnQuit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) font = FindDefaultFont();
            if (glyphFont == null) glyphFont = FindGlyphFont();
#endif
            style = new Style { font = font, textColor = textColor, selectedColor = brassColor, underlineColor = sealColor, buttonHeight = 64f, fontSize = 34f };

            Canvas canvas = NewCanvas("NMJ_TitleCanvas", transform, 10, out group);
            BuildBackground(canvas.transform);
            BuildLogo(canvas.transform);
            BuildMenu(canvas.transform);
            BuildFooter(canvas.transform);

            // 맨 위 검은 막 (들어올 때·게임 시작할 때)
            Image black = NewImage("Fade", canvas.transform, Color.black, Vector2.zero, Vector2.zero);
            fade = black.gameObject.AddComponent<CanvasGroup>();
            fade.blocksRaycasts = false;
        }

        private void BuildBackground(Transform parent)
        {
            RectTransform bg = NewRect("Background", parent);
            if (backgroundImage != null)
            {
                RawImage img = NewRawImage("Image", bg, backgroundImage);
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;   // 화면을 꽉 채우고 넘치는 쪽은 잘림
                fit.aspectRatio = (float)backgroundImage.width / backgroundImage.height;
                NewImage("Dim", bg, new Color(0f, 0f, 0f, backgroundDim), Vector2.zero, Vector2.zero);
                return;
            }

            // 임시 배경: 위는 흑, 아래로 갈수록 눈빛 도는 회청
            Texture2D grad = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            for (int y = 0; y < 64; y++) grad.SetPixel(0, y, Color.Lerp(bottomColor, topColor, Mathf.SmoothStep(0f, 1f, y / 63f)));
            grad.Apply();
            owned.Add(grad);
            NewRawImage("Gradient", bg, grad);

            // 공중에 멈춘 눈송이
            var rng = new System.Random(1887);
            for (int i = 0; i < snowCount; i++)
            {
                float size = 2f + (float)rng.NextDouble() * (rng.NextDouble() < 0.15 ? 7f : 3f);
                Image img = NewImage("Flake", bg, snowColor, Vector2.zero, Vector2.zero);
                RectTransform rt = img.rectTransform;
                // 화면 비율이 달라도 고르게 흩어지도록 위치는 앵커(0~1)로 둔다
                rt.anchorMin = rt.anchorMax = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                rt.sizeDelta = new Vector2(size, size);
                rt.anchoredPosition = Vector2.zero;
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                flakes.Add(new Flake { rt = rt, img = img, basePos = Vector2.zero, phase = (float)rng.NextDouble() * 6.28f, speed = 0.3f + (float)rng.NextDouble() * 0.6f, size = size });
            }
        }

        private void BuildLogo(Transform parent)
        {
            RectTransform logo = NewRect("Logo", parent);
            logo.anchorMin = logo.anchorMax = new Vector2(0.5f, 0.66f);
            logo.sizeDelta = new Vector2(1000f, 420f);

            if (logoImage != null)
            {
                RawImage img = NewRawImage("Image", logo, logoImage);
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = (float)logoImage.width / logoImage.height;
                return;
            }

            // 글자 로고: 큰 글자 아래로 먹 띠, 오른쪽 위에 붉은 도장
            Texture2D band = DrawBrushBand(1024, 160, new Color(0f, 0f, 0f, 0.55f), 13f);
            owned.Add(band);
            RawImage bandImg = NewRawImage("Band", logo, band);
            bandImg.rectTransform.anchorMin = new Vector2(0.08f, 0.02f);
            bandImg.rectTransform.anchorMax = new Vector2(0.92f, 0.42f);

            TMP_Text main = NewText("Title", logo, HanjaFont, logoText, 210f, textColor);
            main.rectTransform.offsetMin = new Vector2(0f, 120f);
            main.characterSpacing = 18f;

            float sealSize = 84f;
            RectTransform seal = NewRect("Seal", logo);
            seal.anchorMin = seal.anchorMax = new Vector2(0.5f, 1f);
            seal.sizeDelta = new Vector2(sealSize, sealSize);
            seal.anchoredPosition = new Vector2(main.GetPreferredValues(logoText).x * 0.5f + 40f, -110f);
            seal.localRotation = Quaternion.Euler(0f, 0f, -5f);
            Texture2D sealTex = DrawSeal(128, sealColor, textColor, 7f);
            owned.Add(sealTex);
            NewRawImage("Ink", seal, sealTex);
            NewText("Glyph", seal, HanjaFont, logoSeal, sealSize * 0.6f, new Color(textColor.r, textColor.g, textColor.b, 0.94f));

            TMP_Text sub = NewText("Subtitle", logo, font, subtitle, 52f, brassColor);
            sub.rectTransform.anchorMin = new Vector2(0f, 0f);
            sub.rectTransform.anchorMax = new Vector2(1f, 0f);
            sub.rectTransform.sizeDelta = new Vector2(0f, 70f);
            sub.rectTransform.anchoredPosition = new Vector2(0f, 90f);
            sub.characterSpacing = 30f;

            TMP_Text tag = NewText("Tagline", logo, font, tagline, 24f, textColor);
            tag.rectTransform.anchorMin = new Vector2(0f, 0f);
            tag.rectTransform.anchorMax = new Vector2(1f, 0f);
            tag.rectTransform.sizeDelta = new Vector2(0f, 34f);
            tag.rectTransform.anchoredPosition = new Vector2(0f, 30f);
            tag.alpha = 0.7f;
        }

        private void BuildMenu(Transform parent)
        {
            RectTransform list = NewRect("Menu", parent);
            list.anchorMin = list.anchorMax = new Vector2(0.5f, 0.26f);
            list.sizeDelta = new Vector2(420f, 64f * 3f);
            startButton = AddButton(buttons, list, style, "게임 시작", OnStart);
            settingsButton = AddButton(buttons, list, style, "환경 설정", OnSettingsPressed);
            AddButton(buttons, list, style, "게임 종료", OnQuit);
            LayoutColumn(buttons, 64f);
        }

        private void BuildFooter(Transform parent)
        {
            TMP_Text n = NewText("Notice", parent, font, notice, 18f, textColor);
            n.rectTransform.anchorMin = new Vector2(0f, 0f);
            n.rectTransform.anchorMax = new Vector2(0f, 0f);
            n.rectTransform.pivot = new Vector2(0f, 0f);
            n.rectTransform.sizeDelta = new Vector2(800f, 30f);
            n.rectTransform.anchoredPosition = new Vector2(40f, 28f);
            n.alignment = TextAlignmentOptions.Left;
            n.alpha = 0.5f;

            TMP_Text v = NewText("Version", parent, font, version, 18f, textColor);
            v.rectTransform.anchorMin = new Vector2(1f, 0f);
            v.rectTransform.anchorMax = new Vector2(1f, 0f);
            v.rectTransform.pivot = new Vector2(1f, 0f);
            v.rectTransform.sizeDelta = new Vector2(300f, 30f);
            v.rectTransform.anchoredPosition = new Vector2(-40f, 28f);
            v.alignment = TextAlignmentOptions.Right;
            v.alpha = 0.5f;
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
