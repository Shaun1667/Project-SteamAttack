using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 타이틀 화면 (회의 1.6, 기획안 v4 3-5): 게임 시작 / 환경 설정 / 게임 종료
    ///
    ///               철과 묵  [命]
    ///               STEAM & INK
    ///        1887년 3월 · 시간이 멈춘 한양
    ///   (지금은 그림 모드: UI_TitleBG_SteamInk에 로고·문구가 그려져 있다)
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
    /// - '환경 설정'은 같은 오브젝트의 SettingsMenu를 연다 (없으면 '준비 중'). 열려 있을 때 ESC = 닫기.
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
        [Tooltip("배경 그림에 로고·문구가 이미 그려져 있다 (UI_TitleBG_SteamInk). 켜면 글자 로고·어둠·아래 글을 숨기고 메뉴를 그림 왼쪽에 둔다")]
        [SerializeField] private bool artIncludesLogo;
        [Tooltip("그림 모드에서 여백 없이 화면을 채운다. 화면 비율이 다르면 배경 가장자리만 잘린다")]
        [SerializeField] private bool artFillScreen;
        [Tooltip("그림 모드 메뉴 자리 (그림 안 비율 0~1, 첫 줄 왼쪽 위 기준). 그림이 화면 비율과 달라도 그림을 따라간다")]
        [SerializeField] private Vector2 artMenuAnchor = new Vector2(0.07f, 0.567f);
        [SerializeField] private Color artTextColor = new Color(0.13f, 0.12f, 0.12f, 1f);   // 먹
        [Tooltip("키 아트 모드: 글자 없는 16:9 그림(UI_TitleBG_KeyArt)을 화면 가득 깔고, 로고(Logo Image)를 왼쪽 안개 위에 겹친다. 그림은 천천히 다가오고 로고는 먹처럼 번져 나온다")]
        [SerializeField] private bool keyArtMode;
        [Tooltip("키 아트 모드: 로고 자리 (화면 비율 0~1: x, y = 왼쪽 아래, width, height)")]
        [SerializeField] private Rect keyArtLogoArea = new Rect(0.045f, 0.50f, 0.36f, 0.42f);
        [Tooltip("키 아트 모드: 메뉴 첫 줄 왼쪽 위 (화면 비율 0~1)")]
        [SerializeField] private Vector2 keyArtMenuAnchor = new Vector2(0.085f, 0.45f);
        [Tooltip("키 아트 모드: 왼쪽 아래 짧은 글 (줄바꿈은 엔터)")]
        [SerializeField, TextArea] private string keyArtQuote = "흐르는 것은,\n시간만이 아니다.";
        [Tooltip("키 아트 모드: 그림이 천천히 다가오는 정도 (1 = 그대로)")]
        [SerializeField, Range(1f, 1.2f)] private float keyArtZoom = 1.06f;
        [SerializeField] private AudioClip music;
        [SerializeField, Range(0f, 1f)] private float musicVolume = 0.7f;

        [Header("연결")]
        [Tooltip("'게임 시작'으로 불러올 씬 이름 (빌드 설정에 있어야 함)")]
        [SerializeField] private string gameScene = "NMJ_Main";
        [Tooltip("게임 시작 때 먹물 화면 전환을 쓴다 (끄면 검게 어두워짐)")]
        [SerializeField] private bool inkTransition = true;
        [SerializeField] private InkStyle startTransition = InkStyle.Drip;
        [Tooltip("환경 설정 화면. 비우면 같은 오브젝트에서 찾는다. 없으면 '환경 설정'은 '준비 중'으로 흐리게 보인다")]
        [SerializeField] private SettingsMenu settings;
        [Tooltip("한글·한자 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("한자(玄武·命·鎭) 전용 고정 폰트. 비우면 에디터에서 NMJ_HudGlyphs SDF를 자동 연결. 새 한자를 쓰려면 NmjHudFontBuilder에 글자를 더한다")]
        [SerializeField] private TMP_FontAsset glyphFont;

        private TMP_FontAsset HanjaFont => glyphFont != null ? glyphFont : font;

        [Header("글자 로고 (Logo Image가 없을 때)")]
        [SerializeField] private string logoText = "철과 묵";
        [SerializeField] private string logoSeal = "命";
        [SerializeField] private string subtitle = "STEAM & INK";
        [SerializeField] private string tagline = "1887년 3월 · 시간이 멈춘 한양";

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
        private RectTransform keyArt;      // 키 아트 모드: 천천히 다가오는 그림
        private CanvasGroup keyLogo, keyMenu, keyFooter;
        private RectTransform artRect;   // 그림 모드: 화면 안에 다 보이게 맞춘 그림 자리 (메뉴가 여기에 붙는다)
        private readonly List<Texture2D> owned = new List<Texture2D>();

        private struct Flake { public RectTransform rt; public Image img; public Vector2 basePos; public float phase, speed, size; }
        private readonly List<Flake> flakes = new List<Flake>();

        private float introTimer;
        private float leaveTimer = -1f;
        private string leaveTo;
        private bool leavingByInk;

        // ───────── 시작 ─────────

        private void Awake()
        {
            // ESC 메뉴에서 넘어와도 멈춘 상태가 남지 않게
            Time.timeScale = 1f;
            AudioListener.pause = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            PlayTimer.Stop();   // 타이틀에 있는 동안은 플레이 시간에 넣지 않는다
            NmjBootToTitle.TitleVisited = true;   // 이제 게임 씬이 열려도 다시 타이틀로 보내지 않는다
            BuildUi();
            // 먹물 전환으로 넘어왔으면 먹이 걷히는 것으로 충분하니 검은 막은 건너뛴다
            fade.alpha = InkTransition.IsBusy ? 0f : 1f;
            if (InkTransition.IsBusy) introTimer = 10f;
        }

        private void Start()
        {
            EnsureEventSystem();
            SetEnabled(startButton, !string.IsNullOrEmpty(gameScene) && Application.CanStreamedLevelBeLoaded(gameScene), style);
            if (settings == null) settings = GetComponent<SettingsMenu>();
            SetEnabled(settingsButton, settings != null, style);
            LinkColumn(buttons);
            Select(startButton.enabled ? startButton : buttons.Find(b => b.enabled));

            var reviewedTitle = Resources.Load<AudioClip>("NGH/VARCO_Reviewed/LatestReview/DFT_Title_v02");
            if (reviewedTitle != null) music = reviewedTitle;
            if (music != null)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.clip = music;
                src.loop = true;
                src.playOnAwake = false;
                gameObject.AddComponent<NmjMusic>().Volume = musicVolume;   // 환경 설정의 배경음 볼륨을 따른다
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

            // 환경 설정이 열려 있으면 ESC로 닫는다
            Keyboard kb = Keyboard.current;
            if (settings != null && settings.IsOpen && kb != null && kb.escapeKey.wasPressedThisFrame) settings.Close();

            UpdateButtons(buttons, style);
            UpdateSnow(dt);
            UpdateKeyArt();

            // 게임 시작: 어두워진 뒤 씬을 불러온다
            if (leaveTimer >= 0f && !leavingByInk)
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
            PlayTimer.Restart();
            leaveTimer = 0f;
            group.interactable = false;
            if (inkTransition)
            {
                leavingByInk = true;
                InkTransition.Play(startTransition, () => SceneManager.LoadScene(leaveTo));
            }
        }

        private void OnSettingsPressed()
        {
            if (settings == null || leaveTimer >= 0f) return;
            group.interactable = false;   // 뒤의 타이틀 버튼은 쉬게
            settings.Open(() => { group.interactable = true; Select(settingsButton); });
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
            bool lightArt = (artIncludesLogo || keyArtMode) && backgroundImage != null;
            style = lightArt
                // 밝은 수묵 그림 위: 먹색 글자, 고르면 붉은 글자와 붉은 붓줄
                ? new Style { font = font, textColor = artTextColor, selectedColor = sealColor, underlineColor = sealColor, buttonHeight = 54f, fontSize = 28f }
                : new Style { font = font, textColor = textColor, selectedColor = brassColor, underlineColor = sealColor, buttonHeight = 64f, fontSize = 34f };

            Canvas canvas = NewCanvas("NMJ_TitleCanvas", transform, 10, out group);
            BuildBackground(canvas.transform);
            if (keyArtMode && backgroundImage != null)
            {
                BuildKeyArt(canvas.transform);
            }
            else if (artIncludesLogo && backgroundImage != null)
            {
                BuildArtMenu(canvas.transform);   // 로고·문구는 그림에 있다
            }
            else
            {
                BuildLogo(canvas.transform);
                BuildMenu(canvas.transform);
                BuildFooter(canvas.transform);
            }

            // 맨 위 검은 막 (들어올 때·게임 시작할 때)
            Image black = NewImage("Fade", canvas.transform, Color.black, Vector2.zero, Vector2.zero);
            fade = black.gameObject.AddComponent<CanvasGroup>();
            fade.blocksRaycasts = false;
        }

        private void BuildBackground(Transform parent)
        {
            RectTransform bg = NewRect("Background", parent);
            if (backgroundImage != null && keyArtMode)
            {
                // 글자 없는 16:9 그림을 꽉 채운다 (화면 비율이 다르면 넘치는 쪽만 조금 잘림)
                RawImage art = NewRawImage("KeyArt", bg, backgroundImage);
                var fit = art.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                fit.aspectRatio = (float)backgroundImage.width / backgroundImage.height;
                keyArt = art.rectTransform;
                keyArt.pivot = new Vector2(0.62f, 0.55f);   // 인물·현무 쪽으로 다가간다
                // 왼쪽 안개: 로고·메뉴가 또렷하게 (왼쪽 흰 안개 → 가운데 투명)
                Texture2D mist = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                for (int x = 0; x < 64; x++) mist.SetPixel(x, 0, new Color(0.93f, 0.93f, 0.92f, 0.78f * (1f - Mathf.SmoothStep(0f, 1f, x / 63f))));
                mist.Apply();
                owned.Add(mist);
                RawImage m = NewRawImage("LeftMist", bg, mist);
                m.rectTransform.anchorMax = new Vector2(0.55f, 1f);
                // 아래 가장자리 먹빛 (화면이 붕 뜨지 않게). 왼쪽 아래 글자 자리는 밝게 두려고 왼쪽으로 갈수록 옅게
                Texture2D vig = new Texture2D(32, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 32; x++)
                        vig.SetPixel(x, y, new Color(0.05f, 0.05f, 0.06f, 0.45f * (1f - Mathf.SmoothStep(0f, 1f, y / 63f)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.6f, x / 31f))));
                vig.Apply();
                owned.Add(vig);
                RawImage v = NewRawImage("BottomInk", bg, vig);
                v.rectTransform.anchorMax = new Vector2(1f, 0.16f);
                BuildFlakes(bg, Mathf.Min(snowCount, 90), Color.white);
                return;
            }
            if (backgroundImage != null && artIncludesLogo)
            {
                if (artFillScreen)
                {
                    UnityEngine.UI.RawImage fullArt = NewRawImage("Image", bg, backgroundImage);
                    var fullFit = fullArt.gameObject.AddComponent<UnityEngine.UI.AspectRatioFitter>();
                    fullFit.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.EnvelopeParent;
                    fullFit.aspectRatio = (float)backgroundImage.width / backgroundImage.height;
                    // 메뉴는 화면 안에 고정해, 다른 화면 비율에서도 클릭 영역이 잘리지 않게 한다.
                    artRect = bg;
                    return;
                }
                // 로고가 그림에 있으니 잘리면 안 된다: 그림 전체를 화면 안에 맞추고(FitInParent),
                // 남는 위아래·양옆은 같은 그림을 꽉 채워 깔고 안개색으로 덮어 이어진 것처럼 보이게
                RawImage under = NewRawImage("Fill", bg, backgroundImage);
                var underFit = under.gameObject.AddComponent<AspectRatioFitter>();
                underFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
                underFit.aspectRatio = (float)backgroundImage.width / backgroundImage.height;
                NewImage("Mist", bg, new Color(0.86f, 0.86f, 0.86f, 0.93f), Vector2.zero, Vector2.zero);
                RawImage art = NewRawImage("Image", bg, backgroundImage);
                var fitArt = art.gameObject.AddComponent<AspectRatioFitter>();
                fitArt.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitArt.aspectRatio = (float)backgroundImage.width / backgroundImage.height;
                artRect = art.rectTransform;
                return;
            }
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

            BuildFlakes(bg, snowCount, snowColor);
        }

        private void BuildFlakes(RectTransform bg, int count, Color color)
        {
            // 공중에 멈춘 눈송이
            var rng = new System.Random(1887);
            for (int i = 0; i < count; i++)
            {
                float size = 2f + (float)rng.NextDouble() * (rng.NextDouble() < 0.15 ? 7f : 3f);
                Image img = NewImage("Flake", bg, color, Vector2.zero, Vector2.zero);
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

            TMP_Text main = NewText("Title", logo, font, logoText, 180f, textColor);   // 한글 이름이라 일반 폰트
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

        /// <summary>그림 모드: 그림 왼쪽 안개 자리에 왼쪽 정렬 메뉴 (그림의 NEW GAME 자리)</summary>
        private void BuildArtMenu(Transform parent)
        {
            RectTransform list = NewRect("Menu", artRect != null ? artRect : parent);
            list.anchorMin = list.anchorMax = artMenuAnchor;
            list.pivot = new Vector2(0f, 1f);
            list.sizeDelta = new Vector2(380f, style.buttonHeight * 3f);
            startButton = AddButton(buttons, list, style, "게임 시작", OnStart);
            settingsButton = AddButton(buttons, list, style, "환경 설정", OnSettingsPressed);
            AddButton(buttons, list, style, "게임 종료", OnQuit);
            LayoutColumn(buttons, style.buttonHeight);
            foreach (MenuButton b in buttons)
            {
                b.label.alignment = TextAlignmentOptions.Left;
                b.label.characterSpacing = 12f;
                b.label.rectTransform.offsetMin = new Vector2(28f, 0f);   // 왼쪽에 붉은 마름모 자리
                b.underline.anchorMin = b.underline.anchorMax = new Vector2(0f, 0.5f);
                b.underline.pivot = new Vector2(0f, 0.5f);
                b.underline.anchoredPosition = new Vector2(20f, -style.fontSize * 0.68f);
                b.soonTag.GetComponent<TMP_Text>().alignment = TextAlignmentOptions.Right;
            }
        }

        /// <summary>키 아트 모드: 왼쪽 안개 위에 붓글씨 로고 → 그 아래 메뉴 → 아래 짧은 글</summary>
        private void BuildKeyArt(Transform parent)
        {
            RectTransform logo = NewRect("Logo", parent);
            logo.anchorMin = keyArtLogoArea.min;
            logo.anchorMax = keyArtLogoArea.max;
            logo.offsetMin = logo.offsetMax = Vector2.zero;
            keyLogo = logo.gameObject.AddComponent<CanvasGroup>();
            if (logoImage != null)
            {
                RawImage img = NewRawImage("Image", logo, logoImage);
                var fit = img.gameObject.AddComponent<AspectRatioFitter>();
                fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fit.aspectRatio = (float)logoImage.width / logoImage.height;
            }
            else
            {
                TMP_Text t = NewText("Title", logo, font, logoText, 150f, artTextColor);
                t.characterSpacing = 14f;
            }

            // 메뉴: 화면 전체를 기준으로 한 그림 모드 메뉴를 그대로 쓴다
            artRect = NewRect("MenuArea", parent);
            artMenuAnchor = keyArtMenuAnchor;
            BuildArtMenu(parent);
            keyMenu = buttons[0].button.transform.parent.gameObject.AddComponent<CanvasGroup>();

            RectTransform foot = NewRect("Footer", parent);
            keyFooter = foot.gameObject.AddComponent<CanvasGroup>();
            TMP_Text q = NewText("Quote", foot, font, keyArtQuote.Replace("\\n", "\n"), 24f, artTextColor);
            q.rectTransform.anchorMin = q.rectTransform.anchorMax = new Vector2(0f, 0f);
            q.rectTransform.pivot = new Vector2(0f, 0f);
            q.rectTransform.sizeDelta = new Vector2(600f, 80f);
            q.rectTransform.anchoredPosition = new Vector2(64f, 70f);
            q.alignment = TextAlignmentOptions.BottomLeft;
            q.lineSpacing = 18f;
            q.alpha = 0.85f;
            // 시연판·안내: 오른쪽 아래는 눈밭이라 안 읽혀서 왼쪽 아래 안개 위에 먹색으로
            TMP_Text v = NewText("Version", foot, font, version + "  ·  " + notice, 15f, artTextColor);
            v.rectTransform.anchorMin = v.rectTransform.anchorMax = new Vector2(0f, 0f);
            v.rectTransform.pivot = new Vector2(0f, 0f);
            v.rectTransform.sizeDelta = new Vector2(900f, 26f);
            v.rectTransform.anchoredPosition = new Vector2(64f, 26f);
            v.alignment = TextAlignmentOptions.Left;
            v.alpha = 0.55f;
            keyLogo.alpha = keyMenu.alpha = keyFooter.alpha = 0f;
        }

        private void UpdateKeyArt()
        {
            if (keyArt == null) return;
            // 그림: 80초에 한 번 천천히 다가갔다가 돌아온다 (멈췄던 시간이 다시 흐르는 느낌)
            float k = 0.5f - 0.5f * Mathf.Cos(Time.unscaledTime * Mathf.PI * 2f / 80f);
            float sc = Mathf.Lerp(1.015f, keyArtZoom, k);
            keyArt.localScale = new Vector3(sc, sc, 1f);
            // 로고 → 메뉴 → 아래 글 순서로 먹처럼 번져 나온다
            float Ease(float t0, float dur) { float x = Mathf.Clamp01((introTimer - t0) / dur); return x * x * (3f - 2f * x); }
            float l = Ease(0.6f, 1.6f);
            keyLogo.alpha = l;
            keyLogo.transform.localScale = Vector3.one * Mathf.Lerp(1.06f, 1f, l);
            keyMenu.alpha = Ease(1.8f, 0.8f);
            keyFooter.alpha = Ease(2.4f, 1.2f);
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
