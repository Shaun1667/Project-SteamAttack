using System;
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
    /// 엔딩 크레딧 (회의 1.7, 기획안 v4 4:40~5:00 — 시연의 끝)
    ///
    ///   보스 처치 문구가 끝나면 → 화면이 먹빛으로 어두워지고, 멈췄던 눈이 다시 내린다 (해빙)
    ///        玄武 · 역류의 어명
    ///          만든 사람들
    ///     기획 · UI · 세계관   나민재      ← 위로 천천히 올라간다
    ///            ...
    ///   마지막 판:   플레이 시간  4분 52초
    ///                 감사합니다
    ///        (아무 키나 누르면 타이틀로)
    ///
    /// - 팀원 이름·역할은 인스펙터 Credits에서 고친다.
    /// - 올라가는 중에 아무 키나 누르면 빨리 감기, ESC는 마지막 판으로 건너뛴다.
    /// - 같은 오브젝트의 BossDefeatBanner가 끝나면 저절로 시작한다. Play()로 직접 시작해도 된다.
    ///
    /// UI는 실행할 때 코드로 만들어진다. 플레이어(NGH_Player)에 붙여 둔다.
    /// (인스펙터 메뉴 '미리 보기'로 바로 볼 수 있다)
    /// </summary>
    [DisallowMultipleComponent]
    public class EndingCredits : MonoBehaviour
    {
        [Serializable]
        public class Credit
        {
            public string role;
            public string[] names = new string[0];
        }

        [Header("만든 사람들 (역할 · 이름)")]
        [SerializeField] private Credit[] credits =
        {
            new Credit { role = "기획 · UI · 세계관", names = new[] { "나민재" } },
            new Credit { role = "플레이어 · 전투 · 시간 역행", names = new[] { "남귀훈" } },
            new Credit { role = "보스 · 현무", names = new[] { "BHS (이름)" } },
            new Credit { role = "주인공 모델 · 화승총 · 받아치기 · 일반 적", names = new[] { "YPH (이름)" } },
        };

        [Header("수화기제 — 처치 문구 뒤, 크레딧 앞의 대사 (세계관 v6 5장 4:40~5:00)")]
        [SerializeField] private NmjDialogue.Line[] epilogue =
        {
            NmjDialogue.Card("수화기제", "물과 불이 제자리를 찾다", 2.4f),
            NmjDialogue.Narrate("무사가 혼천의를 등에서 풀어, 현무의 신핵에 물린다."),
            NmjDialogue.Narrate("증기통의 압력 전부가 혼천의를 거꾸로 돌리고, 현무는 삼킨 시간과 열을 허물처럼 벗는다."),
            NmjDialogue.Narrate("증기통이 터지며 열이 도성으로 돌아온다. 현무는 연못 아래로 가라앉아 잠든다."),
            NmjDialogue.Say("백성", "…방금 귀신불이 깜빡였나?"),
            NmjDialogue.Say("고종", "기억하는 이는 우리뿐이로구나. 그러면 되었다."),
            NmjDialogue.Say("고종", "불은 끄지 않는다. 다만 물을 거스르지 않게 쓰리라.", 2.6f),
        };

        [Header("크레딧 끝 — 멈칫하는 눈송이 (현무는 잠들었을 뿐)")]
        [SerializeField] private bool coda = true;
        [SerializeField] private string codaPlace = "향원정";
        [Tooltip("눈송이가 물에 닿기 직전 멈춰 있는 시간(초)")]
        [SerializeField, Min(0f)] private float codaPause = 1.1f;

        [Header("글자")]
        [SerializeField] private string logo = "玄武";
        [SerializeField] private string subtitle = "역류의 어명";
        [SerializeField] private string creditsTitle = "만든 사람들";
        [SerializeField] private string notice = "이 작품은 실존 인물·사건과 무관한 창작입니다";
        [SerializeField] private string thanks = "감사합니다";

        [Header("연결 (비우면 자동)")]
        [Tooltip("이 문구가 끝나면 크레딧을 시작한다. 비우면 같은 오브젝트에서 찾는다")]
        [SerializeField] private BossDefeatBanner banner;
        [Tooltip("마지막 판에서 키를 누르면 갈 씬. 빌드 설정에 없으면 Title → NMJ_Title 순으로 찾고, 모두 없으면 그 자리에 머문다")]
        [SerializeField] private string titleScene = "NMJ_Title";
        [Tooltip("타이틀로 넘어갈 때의 먹물 화면 전환")]
        [SerializeField] private InkStyle titleTransition = InkStyle.Drip;
        [Tooltip("한글 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("한자(玄武) 전용 고정 폰트. 비우면 에디터에서 NMJ_HudGlyphs SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset glyphFont;

        [Header("시간 (초)")]
        [SerializeField, Min(0.1f)] private float fadeInTime = 2f;
        [Tooltip("올라가는 빠르기 (화면 1080 기준 초당 픽셀)")]
        [SerializeField, Min(10f)] private float scrollSpeed = 70f;
        [Tooltip("마지막 판이 뜬 뒤 키를 받기 시작할 때까지")]
        [SerializeField, Min(0f)] private float inputDelay = 1.5f;

        [Header("색 (오방색)")]
        [SerializeField] private Color backColor = new Color(0.03f, 0.03f, 0.035f, 1f);
        [SerializeField] private Color textColor = new Color(0.96f, 0.95f, 0.92f, 1f);
        [SerializeField] private Color brassColor = new Color(0.94f, 0.73f, 0.16f, 1f);
        [SerializeField] private Color sealColor = new Color(0.82f, 0.18f, 0.10f, 1f);
        [SerializeField] private Color snowColor = new Color(0.83f, 0.93f, 0.95f, 1f);

        [Header("크레딧이 끝난 뒤")]
        public UnityEvent OnFinished = new UnityEvent();

        /// <summary>엔딩 크레딧이 나오는 중</summary>
        public static bool IsPlaying { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsPlaying = false;

        private TMP_FontAsset HanjaFont => glyphFont != null ? glyphFont : font;

        private enum Stage { Idle, Epilogue, FadeIn, Roll, Coda, Final, Leaving }

        private Stage stage = Stage.Idle;
        private float timer;
        private float rollHeight;
        private CanvasGroup group, rollGroup, finalGroup, promptGroup;
        private RectTransform canvasRect, roll;
        private TMP_Text timeText;
        private Texture2D sealTex;

        private struct Flake { public RectTransform rt; public Image img; public float x, speed, sway, phase; }
        private readonly List<Flake> flakes = new List<Flake>();
        private float snowSpeed = 1f, snowClock;

        // 크레딧 끝 한 컷
        private CanvasGroup codaGroup;
        private RectTransform codaFlake, ripple;
        private Image codaHalo, rippleImg;
        private Texture2D glowTex, ringTex;

        // ───────── 시작 ─────────

        private void Awake()
        {
            if (banner == null) banner = GetComponent<BossDefeatBanner>();
            if (banner != null) banner.OnFinished.AddListener(Play);
            BuildUi();
            group.alpha = 0f;
            group.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (banner != null) banner.OnFinished.RemoveListener(Play);
            if (sealTex != null) Destroy(sealTex);
            if (glowTex != null) Destroy(glowTex);
            if (ringTex != null) Destroy(ringTex);
            if (IsPlaying) EndControl();
        }

        /// <summary>엔딩 크레딧을 시작한다</summary>
        public void Play()
        {
            if (stage != Stage.Idle) return;
            IsPlaying = true;
            PlayTimer.Stop();
            timeText.text = PlayTimer.Format(PlayTimer.Seconds);

            // 플레이어 입력·카메라를 멈추고 커서를 보인다 (ESC 메뉴와 같은 기준)
            ControlsOverlay.MenuOpen = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // 먼저 풀린 도성 위에서 수화기제 대사 → 끝나면 크레딧
            if (epilogue != null && epilogue.Length > 0)
            {
                stage = Stage.Epilogue;
                NmjDialogue.Play(epilogue, () => { if (stage == Stage.Epilogue) StartRoll(); });
                return;
            }
            StartRoll();
        }

        private void StartRoll()
        {
            group.gameObject.SetActive(true);
            group.alpha = 0f;
            rollGroup.alpha = 1f;
            finalGroup.alpha = 0f;
            promptGroup.alpha = 0f;
            codaGroup.alpha = 0f;
            snowSpeed = 1f;
            SetRollY(0f);
            stage = Stage.FadeIn;
            timer = 0f;
        }

        [ContextMenu("미리 보기")]
        private void Preview()
        {
            if (!Application.isPlaying) return;
            stage = Stage.Idle;
            Play();
        }

        private void EndControl()
        {
            IsPlaying = false;
            ControlsOverlay.MenuOpen = false;
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            if (stage == Stage.Idle || stage == Stage.Epilogue) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            timer += dt;
            UpdateSnow(dt);

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool esc = kb != null && kb.escapeKey.wasPressedThisFrame;
            bool anyHeld = (kb != null && kb.anyKey.isPressed) || (mouse != null && mouse.leftButton.isPressed);
            bool anyPressed = (kb != null && kb.anyKey.wasPressedThisFrame) || (mouse != null && mouse.leftButton.wasPressedThisFrame);

            switch (stage)
            {
                case Stage.FadeIn:
                    group.alpha = Mathf.Clamp01(timer / fadeInTime);
                    if (timer >= fadeInTime) { stage = Stage.Roll; timer = 0f; }
                    break;

                case Stage.Roll:
                    // 키를 누르고 있으면 4배 빠르게, ESC는 마지막 판으로
                    float y = roll.anchoredPosition.y + scrollSpeed * dt * (anyHeld ? 4f : 1f);
                    SetRollY(y);
                    if (esc || y >= rollHeight + canvasRect.rect.height)
                    {
                        if (coda) ToCoda();
                        else ToFinal();
                    }
                    break;

                case Stage.Coda:
                    UpdateCoda();
                    if (esc) { snowSpeed = 1f; ToFinal(); }
                    break;

                case Stage.Final:
                    finalGroup.alpha = Mathf.Clamp01(timer / 1.2f);
                    promptGroup.alpha = timer >= inputDelay ? 0.35f + 0.25f * Mathf.Sin(timer * 2.5f) : 0f;
                    if (timer >= inputDelay && anyPressed) Leave();
                    break;

                case Stage.Leaving:
                    group.alpha = 1f;
                    finalGroup.alpha = 1f - Mathf.Clamp01(timer / 0.8f);
                    if (timer >= 0.9f) Finish();
                    break;
            }
        }

        private void ToFinal()
        {
            stage = Stage.Final;
            timer = 0f;
            rollGroup.alpha = 0f;
            codaGroup.alpha = 0f;
        }

        // ───────── 크레딧 끝 한 컷: 연못 위로 떨어진 눈송이 하나가 물에 닿기 직전 멈칫했다가 떨어진다 ─────────

        // 들어옴 0~1 → 떨어짐 (fallTime) → 멈칫 (codaPause, 다른 눈도 모두 멈춤) → 닿음 0.3 → 물결 1.6 → 마지막 판
        private const float CodaFall = 4.2f;
        private const float CodaStopAbove = 34f;

        private void ToCoda()
        {
            stage = Stage.Coda;
            timer = 0f;
            rollGroup.alpha = 0f;
            UpdateCoda();
        }

        private void UpdateCoda()
        {
            float h = canvasRect.rect.height;
            float water = h * 0.3f;
            float startY = h + 20f;
            float stopY = water + CodaStopAbove;
            float t = timer;

            codaGroup.alpha = Mathf.Clamp01(t / 1f);
            float pauseStart = CodaFall, pauseEnd = CodaFall + codaPause, touch = pauseEnd + 0.3f;
            float y;
            if (t < pauseStart)
            {
                // 처음엔 빠르게, 물에 가까워질수록 느려지며 내려온다
                float k = t / CodaFall;
                y = Mathf.Lerp(startY, stopY, 1f - (1f - k) * (1f - k));
                snowSpeed = 1f;
            }
            else if (t < pauseEnd)
            {
                // 멈칫: 이 눈송이와 함께 화면의 모든 눈이 멈추고, 푸른 빛(시간)이 한 번 스친다
                y = stopY + Mathf.Sin((t - pauseStart) * 40f) * 0.6f;
                snowSpeed = 0f;
            }
            else
            {
                float k = Mathf.Clamp01((t - pauseEnd) / 0.3f);
                y = Mathf.Lerp(stopY, water, k * k);
                snowSpeed = Mathf.Clamp01((t - pauseEnd) / 0.6f);
            }

            float pause = t >= pauseStart && t < pauseEnd ? Mathf.Sin(Mathf.Clamp01((t - pauseStart) / codaPause) * Mathf.PI) : 0f;
            codaFlake.anchoredPosition = new Vector2(canvasRect.rect.width * 0.5f + Mathf.Sin(t * 0.9f) * 10f * (t < pauseStart ? 1f : 0f), y);
            codaFlake.GetComponent<Image>().color = new Color(snowColor.r, snowColor.g, snowColor.b, t < touch ? 1f : 0f);
            codaHalo.rectTransform.anchoredPosition = codaFlake.anchoredPosition;
            codaHalo.color = new Color(0.45f, 0.72f, 1f, 0.55f * pause);

            // 물결
            float r = Mathf.Clamp01((t - touch) / 1.6f);
            ripple.anchoredPosition = new Vector2(canvasRect.rect.width * 0.5f, water);
            ripple.sizeDelta = new Vector2(Mathf.Lerp(10f, 420f, 1f - (1f - r) * (1f - r)), Mathf.Lerp(3f, 70f, 1f - (1f - r) * (1f - r)));
            rippleImg.color = new Color(snowColor.r, snowColor.g, snowColor.b, t >= touch ? 0.7f * (1f - r) : 0f);

            if (t >= touch + 2.0f) ToFinal();
        }

        private void Leave()
        {
            stage = Stage.Leaving;
            timer = 0f;
        }

        private void Finish()
        {
            stage = Stage.Idle;
            EndControl();
            OnFinished.Invoke();
            string title = NmjBootToTitle.Resolve(titleScene);   // 적힌 씬이 빌드에 없으면 Title → NMJ_Title
            if (title != null)
                InkTransition.Play(titleTransition, () => SceneManager.LoadScene(title));
            else
                stage = Stage.Final;   // 갈 곳이 없으면 마지막 판에 머문다
        }

        private void SetRollY(float y)
        {
            // y = 0 이면 크레딧 맨 윗줄이 화면 아래 바로 밑에 있다
            roll.anchoredPosition = new Vector2(0f, y);
        }

        private void UpdateSnow(float dt)
        {
            // 해빙: 멈췄던 눈송이가 다시 천천히 떨어진다
            float h = canvasRect.rect.height, w = canvasRect.rect.width;
            snowClock += dt * snowSpeed;
            float t = snowClock;
            for (int i = 0; i < flakes.Count; i++)
            {
                Flake f = flakes[i];
                Vector2 p = f.rt.anchoredPosition;
                p.y -= f.speed * dt * snowSpeed;
                if (p.y < -10f) { p.y = h + 10f; }
                p.x = f.x * w + Mathf.Sin(t * 0.6f + f.phase) * f.sway;
                f.rt.anchoredPosition = p;
            }
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) font = FindDefaultFont();
            if (glyphFont == null) glyphFont = FindGlyphFont();
#endif
            Canvas canvas = NewCanvas("NMJ_EndingCreditsCanvas", transform, 85, out group);   // 처치 문구(80) 위, ESC 메뉴(90) 아래
            canvasRect = (RectTransform)canvas.transform;
            group.blocksRaycasts = false;
            group.interactable = false;

            NewImage("Back", canvas.transform, backColor, Vector2.zero, Vector2.zero);

            // 눈송이 (화면 아래 왼쪽 기준 위치)
            var rng = new System.Random(1887);
            for (int i = 0; i < 90; i++)
            {
                float size = 2f + (float)rng.NextDouble() * (rng.NextDouble() < 0.15 ? 5f : 2.5f);
                Image img = NewImage("Flake", canvas.transform, new Color(snowColor.r, snowColor.g, snowColor.b, 0.25f + (float)rng.NextDouble() * 0.4f), Vector2.zero, Vector2.zero);
                RectTransform rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.sizeDelta = new Vector2(size, size);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                rt.anchoredPosition = new Vector2(0f, (float)rng.NextDouble() * 1200f);
                flakes.Add(new Flake { rt = rt, img = img, x = (float)rng.NextDouble(), speed = 20f + (float)rng.NextDouble() * 35f + size * 4f, sway = 6f + (float)rng.NextDouble() * 14f, phase = (float)rng.NextDouble() * 6.28f });
            }

            BuildRoll(canvas.transform);
            BuildCoda(canvas.transform);
            BuildFinal(canvas.transform);
        }

        private void BuildCoda(Transform parent)
        {
            RectTransform root = NewRect("Coda", parent);
            codaGroup = root.gameObject.AddComponent<CanvasGroup>();
            codaGroup.alpha = 0f;

            // 얼음이 풀린 연못: 물 아래는 조금 더 짙게, 수면은 옅은 빛 한 줄
            Image deep = NewImage("Water", root, new Color(0f, 0f, 0f, 0.35f), Vector2.zero, Vector2.zero);
            deep.rectTransform.anchorMax = new Vector2(1f, 0.3f);
            Image surface = NewImage("Surface", root, new Color(snowColor.r, snowColor.g, snowColor.b, 0.22f), Vector2.zero, Vector2.zero);
            RectTransform sr = surface.rectTransform;
            sr.anchorMin = new Vector2(0.18f, 0.3f);
            sr.anchorMax = new Vector2(0.82f, 0.3f);
            sr.sizeDelta = new Vector2(0f, 2f);

            TMP_Text place = NewText("Place", root, font, codaPlace, 22f, brassColor);
            place.characterSpacing = 30f;
            place.alpha = 0.55f;
            RectTransform pr = place.rectTransform;
            pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.2f);
            pr.sizeDelta = new Vector2(400f, 40f);

            glowTex = DrawRadial(64, false);
            ringTex = DrawRadial(128, true);
            codaHalo = NewImage("Halo", root, Color.clear, Vector2.zero, Vector2.zero);
            RectTransform hr = codaHalo.rectTransform;
            hr.anchorMin = hr.anchorMax = Vector2.zero;
            hr.sizeDelta = new Vector2(90f, 90f);
            codaHalo.sprite = Sprite.Create(glowTex, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f));

            Image flake = NewImage("Flake", root, snowColor, Vector2.zero, Vector2.zero);
            codaFlake = flake.rectTransform;
            codaFlake.anchorMin = codaFlake.anchorMax = Vector2.zero;
            codaFlake.sizeDelta = new Vector2(8f, 8f);
            codaFlake.localRotation = Quaternion.Euler(0f, 0f, 45f);

            rippleImg = NewImage("Ripple", root, Color.clear, Vector2.zero, Vector2.zero);
            ripple = rippleImg.rectTransform;
            ripple.anchorMin = ripple.anchorMax = Vector2.zero;
            rippleImg.sprite = Sprite.Create(ringTex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
        }

        /// <summary>둥근 빛(ring = false) 또는 가는 고리(ring = true) — 납작하게 늘리면 물결 타원이 된다</summary>
        private static Texture2D DrawRadial(int S, bool ring)
        {
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = new Vector2(x + 0.5f - S * 0.5f, y + 0.5f - S * 0.5f).magnitude / (S * 0.5f);
                    float a = ring ? Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.06f) : Mathf.Pow(Mathf.Clamp01(1f - d), 2f);
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        private void BuildRoll(Transform parent)
        {
            // 화면 아래 가운데에 매단 긴 줄. 위로 올리며 보여 준다
            roll = NewRect("Roll", parent);
            roll.anchorMin = roll.anchorMax = new Vector2(0.5f, 0f);
            roll.pivot = new Vector2(0.5f, 0f);
            roll.sizeDelta = new Vector2(1200f, 0f);
            rollGroup = roll.gameObject.AddComponent<CanvasGroup>();

            float y = 0f;   // 맨 위에서 아래로 내려가며 쌓는다 (음수)
            Line(roll, HanjaFont, logo, 150f, textColor, ref y, 170f);
            Line(roll, font, subtitle, 42f, brassColor, ref y, 140f).characterSpacing = 30f;
            Line(roll, font, creditsTitle, 30f, brassColor, ref y, 90f).alpha = 0.8f;

            foreach (Credit c in credits)
            {
                Line(roll, font, c.role, 22f, brassColor, ref y, 40f).alpha = 0.75f;
                foreach (string n in c.names) Line(roll, font, n, 40f, textColor, ref y, 56f);
                y -= 44f;
            }

            y -= 40f;
            Line(roll, font, notice, 20f, textColor, ref y, 30f).alpha = 0.5f;

            rollHeight = -y;
            // 0에서 시작해 rollHeight + 화면 높이만큼 올라가면 줄 전체가 화면을 다 지나간다
        }

        /// <summary>줄 하나를 쌓고 y를 내린다. y는 줄 맨 위 기준 (roll의 바닥에서 위로 0 → 아래로 음수)</summary>
        private TMP_Text Line(RectTransform parent, TMP_FontAsset f, string text, float size, Color color, ref float y, float height)
        {
            TMP_Text t = NewText("Line", parent, f, text, size, color);
            RectTransform rt = t.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, y);
            y -= height;
            return t;
        }

        private void BuildFinal(Transform parent)
        {
            RectTransform card = NewRect("Final", parent);
            finalGroup = card.gameObject.AddComponent<CanvasGroup>();

            TMP_Text label = NewText("PlayTimeLabel", card, font, "플레이 시간", 28f, brassColor);
            Place(label.rectTransform, 0.6f, 50f);
            label.characterSpacing = 20f;

            timeText = NewText("PlayTime", card, font, "0초", 96f, textColor);
            Place(timeText.rectTransform, 0.5f, 120f);

            // 붉은 도장으로 마무리
            float sealSize = 70f;
            RectTransform seal = NewRect("Seal", card);
            seal.anchorMin = seal.anchorMax = new Vector2(0.5f, 0.36f);
            seal.sizeDelta = new Vector2(sealSize, sealSize);
            seal.localRotation = Quaternion.Euler(0f, 0f, -5f);
            sealTex = DrawSeal(128, sealColor, textColor, 21f);
            NewRawImage("Ink", seal, sealTex);
            NewText("Glyph", seal, HanjaFont, "玄", sealSize * 0.6f, new Color(textColor.r, textColor.g, textColor.b, 0.94f));

            TMP_Text t = NewText("Thanks", card, font, thanks, 34f, textColor);
            Place(t.rectTransform, 0.27f, 50f);

            TMP_Text prompt = NewText("Prompt", card, font, "아무 키나 누르면 타이틀로", 20f, textColor);
            Place(prompt.rectTransform, 0.1f, 30f);
            promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
        }

        private static void Place(RectTransform rt, float yAnchor, float height)
        {
            rt.anchorMin = new Vector2(0f, yAnchor);
            rt.anchorMax = new Vector2(1f, yAnchor);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = Vector2.zero;
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
