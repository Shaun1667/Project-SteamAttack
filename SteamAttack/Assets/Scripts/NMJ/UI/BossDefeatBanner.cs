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
    /// 보스 처치 문구 (회의 1.7, 기획안 v4 4:40~5:00 엔딩의 첫 장면)
    ///
    ///   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━   ← 먹 띠가 왼쪽에서 오른쪽으로 그어진다
    ///               玄武  [鎭]               ← 글자가 내려앉고, 황색 鎭 도장이 찍힌다 (「玄武 鎭」)
    ///             현무를 잠재웠다             ← 황색
    ///      멈췄던 도성의 시간이 다시 흐른다
    ///
    /// 죽이지 않고 잠재우는 결말이라 "토벌·제거(Eliminated)"가 아니라 진정시킬 진(鎭)이다 (세계관 v6 6-2).
    ///
    /// - 씬의 BHS 보스(BossHealth)가 살아 있다가 쓰러지면 저절로 뜬다. 다른 곳에서 Show()를 불러도 된다.
    /// - 쓰러지는 순간 잠깐 느리게(슬로 모션) 했다가 원래 빠르기로 돌아온다.
    /// - 문구가 사라지면 OnFinished를 보낸다 → 해빙 연출·엔딩 크레딧을 여기에 잇는다.
    ///   (Scene When Done을 적으면 문구가 끝난 뒤 그 씬을 불러온다. 비우면 그대로 둔다)
    /// - 문구가 떠 있는 동안 ESC 메뉴는 열리지 않고, 퀘스트창은 숨는다.
    ///
    /// UI는 실행할 때 코드로 만들어진다. 플레이어(NGH_Player)에 붙여 둔다.
    /// (Test Keys를 켜면 숫자패드 9 = 미리 보기, 인스펙터 메뉴 '미리 보기'도 있음)
    /// </summary>
    [DisallowMultipleComponent]
    public class BossDefeatBanner : MonoBehaviour
    {
        [Header("연결 (비우면 씬에서 자동으로 찾음)")]
        [SerializeField] private BossHealth boss;
        [Tooltip("한글 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("한자(玄武·命·鎭) 전용 고정 폰트. 비우면 에디터에서 NMJ_HudGlyphs SDF를 자동 연결. 새 한자를 쓰려면 NmjHudFontBuilder에 글자를 더한다")]
        [SerializeField] private TMP_FontAsset glyphFont;

        private TMP_FontAsset HanjaFont => glyphFont != null ? glyphFont : font;

        [Header("글자")]
        [Tooltip("한자가 들어 있으면 한자 고정 폰트로 쓴다")]
        [SerializeField] private string headline = "玄武";
        [SerializeField] private string sealText = "鎭";
        [Tooltip("큰 글자 바로 아래 (황색)")]
        [SerializeField] private string subtitle = "현무를 잠재웠다";
        [Tooltip("맨 아래 작은 글 (비우면 없음)")]
        [SerializeField] private string caption = "멈췄던 도성의 시간이 다시 흐른다";

        [Header("시간 (초, 멈춘 시간과 관계없이 흐른다)")]
        [Tooltip("보스가 쓰러진 뒤 문구가 뜨기까지")]
        [SerializeField, Min(0f)] private float startDelay = 0.6f;
        [Tooltip("문구가 다 나온 뒤 머무는 시간")]
        [SerializeField, Min(0f)] private float holdTime = 4f;
        [SerializeField, Min(0.05f)] private float fadeOutTime = 1.5f;

        [Header("슬로 모션")]
        [SerializeField] private bool slowMotion = true;
        [SerializeField, Range(0.05f, 1f)] private float slowScale = 0.3f;
        [Tooltip("느려진 상태로 있는 시간(실제 초). 끝나면 원래 빠르기로 천천히 돌아온다")]
        [SerializeField, Min(0f)] private float slowTime = 1.2f;

        [Header("색 (오방색)")]
        [SerializeField] private Color inkColor = new Color(0.03f, 0.03f, 0.035f, 0.97f);      // 흑 (먹 띠)
        [SerializeField] private Color textColor = new Color(0.96f, 0.95f, 0.92f, 1f);        // 백
        [SerializeField] private Color sealColor = new Color(0.94f, 0.73f, 0.16f, 1f);        // 황 (어명·신핵 — 鎭 도장)
        [SerializeField] private Color captionColor = new Color(0.94f, 0.73f, 0.16f, 1f);     // 황

        [Header("문구가 끝난 뒤 (해빙·엔딩 크레딧 연결용)")]
        public UnityEvent OnFinished = new UnityEvent();
        [Tooltip("문구가 끝나면 불러올 씬 (빌드 설정에 있어야 함). 비우면 그대로 둔다")]
        [SerializeField] private string sceneWhenDone = "";

        [Header("퀘스트")]
        [Tooltip("문구가 뜰 때 보고할 Kill 목표 아이디 (비우면 보고 안 함)")]
        [SerializeField] private string questKillId = "boss_hyeonmu";

        [Header("테스트")]
        [Tooltip("켜면 숫자패드 9 = 미리 보기")]
        [SerializeField] private bool testKeys = false;

        /// <summary>처치 문구가 떠 있는 중 (나오는 중·사라지는 중 포함)</summary>
        public static bool IsShowing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsShowing = false;

        private CanvasGroup group;
        private RectTransform root, reveal, bandRect, sealRect;
        private TMP_Text headText, subText, capText;
        private CanvasGroup sealGroup;
        private Texture2D bandTex, sealTex;

        private bool playing;
        private bool bossSeenAlive;
        private bool shownForBoss;
        private float timer;          // 문구 시간 (startDelay 포함)
        private float slowTimer = -1f;
        private float findTimer;
        private float bandWidth;

        // ───────── 시작 ─────────

        private void Awake()
        {
            BuildUi();
            SetAlpha(0f);
        }

        private void OnDisable()
        {
            if (playing) Stop();
            RestoreTime();
        }

        private void OnDestroy()
        {
            if (bandTex != null) Destroy(bandTex);
            if (sealTex != null) Destroy(sealTex);
        }

        /// <summary>처치 문구를 띄운다 (이미 떠 있으면 무시)</summary>
        public void Show()
        {
            if (playing) return;
            playing = true;
            IsShowing = true;
            timer = 0f;
            if (slowMotion && Time.timeScale > 0f) { Time.timeScale = slowScale; slowTimer = 0f; }
            // 퀘스트 "현무를 잠재운다" (보스 쪽 보고가 없어도 처치 문구가 뜨면 채운다)
            if (!string.IsNullOrEmpty(questKillId) && QuestManager.Instance != null) QuestManager.Instance.ReportKill(questKillId);
            Layout(0f);
        }

        [ContextMenu("미리 보기")]
        private void Preview()
        {
            if (Application.isPlaying) { Stop(); Show(); }
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            if (testKeys && Keyboard.current != null && Keyboard.current.numpad9Key.wasPressedThisFrame) { Stop(); Show(); }

            WatchBoss();
            UpdateSlowMotion();
            if (!playing) return;

            // ESC 메뉴로 멈춘 동안은 문구도 멈춘다. 씬 로딩 같은 긴 멈춤 뒤 한 번에 건너뛰지 않게 한 프레임은 0.1초까지만
            if (!PauseMenu.IsOpen) timer += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Layout(timer - startDelay);

            float end = startDelay + IntroTime + holdTime + fadeOutTime;
            if (timer >= end) Finish();
        }

        /// <summary>(개발자 창 리젠) 지켜보던 보스를 잊고 새 보스를 처음부터 지켜본다. 치운 보스로는 처치 문구가 나오지 않는다</summary>
        public void ForgetBoss()
        {
            boss = null;
            bossSeenAlive = false;
            shownForBoss = false;
            findTimer = 0f;
        }

        private void WatchBoss()
        {
            if (boss == null)
            {
                findTimer -= Time.unscaledDeltaTime;
                if (findTimer > 0f) return;
                findTimer = 0.5f;
                boss = FindAnyObjectByType<BossHealth>();
                if (boss == null) return;
            }

            // 깨어나기 전(체력 0으로 시작)과 구분하려고, 한 번은 살아 있는 것을 본 뒤에만 처치로 친다
            if (boss.IsAlive) { bossSeenAlive = true; return; }
            if (bossSeenAlive && !shownForBoss)
            {
                shownForBoss = true;
                Show();
            }
        }

        private void UpdateSlowMotion()
        {
            if (slowTimer < 0f || PauseMenu.IsOpen) return;
            slowTimer += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (slowTimer < slowTime) return;
            // 0.5초에 걸쳐 원래 빠르기로
            float k = Mathf.Clamp01((slowTimer - slowTime) / 0.5f);
            Time.timeScale = Mathf.Lerp(slowScale, 1f, k);
            if (k >= 1f) slowTimer = -1f;
        }

        private void RestoreTime()
        {
            if (slowTimer >= 0f) { Time.timeScale = 1f; slowTimer = -1f; }
        }

        private void Finish()
        {
            Stop();
            OnFinished.Invoke();
            if (!string.IsNullOrEmpty(sceneWhenDone) && Application.CanStreamedLevelBeLoaded(sceneWhenDone))
                SceneManager.LoadScene(sceneWhenDone);
        }

        private void Stop()
        {
            playing = false;
            IsShowing = false;
            SetAlpha(0f);
        }

        // ───────── 연출 ─────────

        // 먹 띠 0~0.6 → 글자 0.4~1.0 → 도장 1.0~1.15 → 아래 글 1.2~1.8
        private const float IntroTime = 1.8f;

        private void Layout(float t)
        {
            if (t < 0f) { SetAlpha(0f); return; }

            float outT = t - IntroTime - holdTime;
            SetAlpha(outT > 0f ? 1f - Mathf.Clamp01(outT / fadeOutTime) : 1f);

            // 먹 띠: 왼쪽에서 오른쪽으로 빠르게 긋는다 (처음이 빠르고 끝에서 느려짐)
            float band = EaseOut(Mathf.Clamp01(t / 0.6f));
            reveal.sizeDelta = new Vector2(bandWidth * band, reveal.sizeDelta.y);

            // 글자: 크게 떠 있다가 내려앉으며 자간이 좁아진다
            float h = Mathf.Clamp01((t - 0.4f) / 0.6f);
            float he = EaseOut(h);
            headText.alpha = h;
            headText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, he);
            headText.characterSpacing = Mathf.Lerp(42f, 14f, he);

            // 도장: 위에서 꾹 찍힌다 + 찍는 순간 문구 전체가 살짝 흔들림
            float s = Mathf.Clamp01((t - 1.0f) / 0.15f);
            sealGroup.alpha = s;
            sealRect.localScale = Vector3.one * Mathf.Lerp(1.8f, 1f, EaseIn(s));
            float shake = t > 1.15f && t < 1.35f ? (1.35f - t) / 0.2f * 4f : 0f;
            root.anchoredPosition = shake > 0f ? new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake)) : Vector2.zero;

            // 아래 글
            float sub = Mathf.Clamp01((t - 1.2f) / 0.6f);
            subText.alpha = sub * 0.9f;
            capText.alpha = sub * 0.8f;
            capText.characterSpacing = Mathf.Lerp(24f, 8f, EaseOut(sub));
        }

        private void SetAlpha(float a)
        {
            if (group != null) group.alpha = a;
        }

        private static bool HasHanja(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c >= '一' && c <= '鿿') return true;
            return false;
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);
        private static float EaseIn(float x) => x * x;

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) font = FindDefaultFont();
            if (glyphFont == null) glyphFont = FindGlyphFont();
#endif
            Canvas canvas = NewCanvas("NMJ_BossDefeatCanvas", transform, 80, out group);   // HUD 위, ESC 메뉴 아래
            group.blocksRaycasts = false;
            group.interactable = false;

            root = NewRect("Banner", canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.56f);
            root.sizeDelta = new Vector2(1600f, 300f);

            // 먹 띠 (RectMask2D 안에서 왼쪽부터 드러난다)
            bandWidth = 1600f;
            reveal = NewRect("Reveal", root);
            reveal.anchorMin = new Vector2(0f, 0f);
            reveal.anchorMax = new Vector2(0f, 1f);
            reveal.pivot = new Vector2(0f, 0.5f);
            reveal.sizeDelta = new Vector2(0f, 0f);
            reveal.gameObject.AddComponent<RectMask2D>();
            bandTex = DrawBrushBand(1024, 192, inkColor, Random.Range(0f, 100f));
            RawImage bandImg = NewRawImage("Band", reveal, bandTex);
            bandRect = bandImg.rectTransform;
            bandRect.anchorMin = bandRect.anchorMax = new Vector2(0f, 0.5f);
            bandRect.pivot = new Vector2(0f, 0.5f);
            bandRect.sizeDelta = new Vector2(bandWidth, 300f);

            // 큰 글자 (玄武 같은 한자는 한자 고정 폰트로)
            headText = NewText("Headline", root, HasHanja(headline) ? HanjaFont : font, headline, 104f, textColor);
            headText.rectTransform.offsetMin = new Vector2(0f, 70f);
            headText.rectTransform.offsetMax = new Vector2(0f, -20f);

            // 붉은 도장 (큰 글자 오른쪽 위)
            float sealSize = 92f;
            sealRect = NewRect("Seal", root);
            sealRect.anchorMin = sealRect.anchorMax = new Vector2(0.5f, 0.5f);
            sealRect.sizeDelta = new Vector2(sealSize, sealSize);
            sealRect.anchoredPosition = new Vector2(headText.GetPreferredValues(headline).x * 0.5f + 80f, 50f);
            sealRect.localRotation = Quaternion.Euler(0f, 0f, -6f);
            sealGroup = sealRect.gameObject.AddComponent<CanvasGroup>();
            sealTex = DrawSeal(128, sealColor, textColor, Random.Range(0f, 100f));
            NewRawImage("Ink", sealRect, sealTex);
            // 밝은 도장(황)에는 먹 글자, 어두운 도장(적)에는 흰 글자
            float lum = sealColor.r * 0.3f + sealColor.g * 0.59f + sealColor.b * 0.11f;
            Color glyphColor = lum > 0.55f ? new Color(inkColor.r, inkColor.g, inkColor.b, 0.94f) : new Color(textColor.r, textColor.g, textColor.b, 0.94f);
            TMP_Text sealGlyph = NewText("Glyph", sealRect, HanjaFont, sealText, sealSize * 0.6f, glyphColor);
            sealGlyph.rectTransform.anchoredPosition = new Vector2(0f, -sealSize * 0.02f);

            // 아래 글: "현무를 잠재웠다"(황) + 작은 글
            subText = NewText("Subtitle", root, font, subtitle, 34f, captionColor);
            subText.characterSpacing = 10f;
            subText.rectTransform.anchorMin = new Vector2(0f, 0f);
            subText.rectTransform.anchorMax = new Vector2(1f, 0f);
            subText.rectTransform.sizeDelta = new Vector2(0f, 40f);
            subText.rectTransform.anchoredPosition = new Vector2(0f, 62f);

            capText = NewText("Caption", root, font, caption, 22f, textColor);
            capText.rectTransform.anchorMin = new Vector2(0f, 0f);
            capText.rectTransform.anchorMax = new Vector2(1f, 0f);
            capText.rectTransform.sizeDelta = new Vector2(0f, 30f);
            capText.rectTransform.anchoredPosition = new Vector2(0f, 24f);
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
