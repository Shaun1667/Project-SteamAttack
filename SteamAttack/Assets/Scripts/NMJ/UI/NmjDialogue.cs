using System;
using NGH;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 대사 연출 (오프닝 어명, 엔딩 수화기제). 화면 위아래에 먹 띠(레터박스)가 내려오고, 아래쪽에 한 줄씩 대사가 나온다.
    ///
    ///   NmjDialogue.Play(new[] {
    ///       NmjDialogue.Card("고종 24년(1887) 3월", "경복궁 건청궁"),     ← 가운데 큰 글씨 (때와 곳)
    ///       NmjDialogue.Say("고종", "가서, 저것을 다시 잠재우라."),         ← 말하는 사람 + 대사
    ///       NmjDialogue.Narrate("무사는 대답하지 않고 고개를 숙인다."),   ← 지문 (흐린 글씨)
    ///   }, onDone);
    ///
    /// - 글자가 한 자씩 써지고, 다 써지면 정해진 시간 뒤 다음 줄로 넘어간다.
    ///   스페이스·엔터·왼쪽 클릭 = 지금 줄 바로 다 쓰기 / 다음 줄, ESC = 모두 건너뛰기(skippable일 때).
    /// - 도는 동안 플레이어 입력을 막고(ESC 메뉴와 같은 기준) ESC 메뉴는 열리지 않는다.
    /// - 멈춘 시간(슬로 모션)과 관계없이 흐른다. 처음 부를 때 그 씬에 캔버스를 만든다.
    /// </summary>
    public class NmjDialogue : MonoBehaviour
    {
        public enum Kind { Line, Narration, Card }

        [Serializable]
        public class Line
        {
            public Kind kind = Kind.Line;
            [Tooltip("말하는 사람 (대사일 때). 카드일 때는 작은 아랫줄")]
            public string speaker;
            [TextArea(1, 3)] public string text;
            [Tooltip("다 써진 뒤 머무는 시간(초). 0이면 글자 수로 정한다")]
            public float hold;
        }

        public static Line Say(string speaker, string text, float hold = 0f) => new Line { kind = Kind.Line, speaker = speaker, text = text, hold = hold };
        public static Line Narrate(string text, float hold = 0f) => new Line { kind = Kind.Narration, text = text, hold = hold };
        public static Line Card(string title, string sub, float hold = 2.6f) => new Line { kind = Kind.Card, text = title, speaker = sub, hold = hold };

        /// <summary>대사 연출이 도는 중 (끝난 프레임 다음까지 — 같은 ESC로 메뉴가 열리지 않게)</summary>
        public static bool IsPlaying => instance != null && (instance.lines != null || Time.frameCount - instance.endFrame <= 1);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        private static NmjDialogue instance;

        // 오방색
        private static readonly Color Ink = new Color(0.02f, 0.02f, 0.025f, 1f);

        private const float CharsPerSecond = 26f;
        private const float FadeTime = 0.3f;

        [NonSerialized] private Line[] lines;   // 실행 중 코드 다시 불러오기 때 빈 배열로 되살아나지 않게
        private int index;
        private float timer, typed, hold;
        private bool skippable, lockedInput;
        private Action onDone;
        private int endFrame = -10;
        private float bars, backdrop, backdropTarget;

        private CanvasGroup group, lineGroup, cardGroup, hintGroup;
        private RectTransform topBar, bottomBar;
        private Image backdropImg;
        private TMP_Text speakerText, bodyText, cardTitle, cardSub, hintText;
        private TMP_FontAsset font;

        // ───────── 부르는 곳 ─────────

        /// <summary>대사를 차례로 보여 준다. blackBackdrop = 뒤를 먹빛으로 가림 (오프닝용)</summary>
        public static void Play(Line[] lines, Action onDone = null, bool blackBackdrop = false, bool skippable = true)
        {
            if (lines == null || lines.Length == 0) { onDone?.Invoke(); return; }
            Get().Begin(lines, onDone, blackBackdrop, skippable);
        }

        /// <summary>도는 대사를 끝낸다 (onDone은 부르지 않음)</summary>
        public static void Stop()
        {
            if (instance != null && instance.lines != null) instance.End(false);
        }

        /// <summary>뒤 먹빛 가림막을 걷거나 덮는다 (오프닝 끝에 쓴다)</summary>
        public static void SetBackdrop(bool on) { if (instance != null) instance.backdropTarget = on ? 1f : 0f; }

        private static NmjDialogue Get()
        {
            if (instance != null) return instance;
            var go = new GameObject("NMJ_Dialogue");
            instance = go.AddComponent<NmjDialogue>();
            instance.Build();
            return instance;
        }

        private void OnDestroy()
        {
            if (lockedInput) ControlsOverlay.MenuOpen = false;
            if (instance == this) instance = null;
        }

        private void Begin(Line[] l, Action done, bool black, bool skip)
        {
            lines = l;
            onDone = done;
            skippable = skip;
            backdropTarget = black ? 1f : 0f;
            if (black) backdrop = 1f;
            // 플레이어 입력을 막는다. 이미 다른 화면(엔딩 크레딧 등)이 막고 있으면 그대로 둔다
            lockedInput = !ControlsOverlay.MenuOpen;
            if (lockedInput) ControlsOverlay.MenuOpen = true;
            hintText.text = skippable ? "SPACE  넘기기    ESC  건너뛰기" : "SPACE  넘기기";
            ShowLine(0);
        }

        private void ShowLine(int i)
        {
            if (i > 0) NGH.ReviewedAudio.PlayUi("Initial/11_UI/DialogueNext_v02");
            index = i;
            timer = 0f;
            typed = 0f;
            Line l = lines[i];
            bool card = l.kind == Kind.Card;
            int len = string.IsNullOrEmpty(l.text) ? 0 : l.text.Length;
            hold = l.hold > 0f ? l.hold : 1.4f + len * 0.06f;

            if (card)
            {
                cardTitle.text = l.text;
                cardSub.text = l.speaker;
                bodyText.text = "";
                speakerText.text = "";
            }
            else
            {
                bool narration = l.kind == Kind.Narration;
                speakerText.text = narration ? "" : l.speaker;
                bodyText.text = l.text;
                bodyText.fontSize = narration ? 32f : 38f;
                bodyText.color = narration ? new Color(White.r, White.g, White.b, 0.7f) : White;
                bodyText.fontStyle = narration ? FontStyles.Italic : FontStyles.Normal;
                bodyText.maxVisibleCharacters = 0;
            }
        }

        private void End(bool callDone)
        {
            lines = null;
            endFrame = Time.frameCount;
            if (lockedInput) { ControlsOverlay.MenuOpen = false; lockedInput = false; }
            Action d = onDone;
            onDone = null;
            if (callDone) d?.Invoke();
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (lines != null && index >= lines.Length) lines = null;
            bool active = lines != null;

            // 먹 띠·가림막·안내는 대사가 끝나도 천천히 걷힌다
            bars = Mathf.MoveTowards(bars, active ? 1f : 0f, dt / 0.6f);
            backdrop = Mathf.MoveTowards(backdrop, active ? backdropTarget : 0f, dt / 0.8f);
            float barH = 118f * Mathf.SmoothStep(0f, 1f, bars);
            topBar.sizeDelta = new Vector2(0f, barH);
            bottomBar.sizeDelta = new Vector2(0f, barH);
            backdropImg.color = new Color(Ink.r, Ink.g, Ink.b, backdrop);
            hintGroup.alpha = active ? Mathf.Min(hintGroup.alpha + dt, 0.45f) : Mathf.Max(hintGroup.alpha - dt * 2f, 0f);
            group.alpha = Mathf.Max(bars, backdrop);

            if (!active)
            {
                lineGroup.alpha = Mathf.MoveTowards(lineGroup.alpha, 0f, dt / FadeTime);
                cardGroup.alpha = Mathf.MoveTowards(cardGroup.alpha, 0f, dt / FadeTime);
                return;
            }

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (skippable && kb != null && kb.escapeKey.wasPressedThisFrame) { End(true); return; }
            bool next = (kb != null && (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
                        || (mouse != null && mouse.leftButton.wasPressedThisFrame);

            Line l = lines[index];
            bool card = l.kind == Kind.Card;
            timer += dt;

            // 들어오기: 카드는 가운데 큰 글씨가 서서히, 대사는 한 자씩 써진다
            int len = string.IsNullOrEmpty(l.text) ? 0 : l.text.Length;
            float typeTime = card ? 0.8f : len / CharsPerSecond;
            bool done = timer >= typeTime;
            if (!card)
            {
                typed = Mathf.Min(len, typed + dt * CharsPerSecond);
                bodyText.maxVisibleCharacters = done ? len : Mathf.FloorToInt(typed);
            }

            if (next)
            {
                if (!done) { timer = typeTime; typed = len; }
                else timer = typeTime + hold;   // 다음 줄로
            }

            float outStart = typeTime + hold;
            float a = timer < FadeTime ? timer / FadeTime : timer > outStart ? 1f - (timer - outStart) / FadeTime : 1f;
            a = Mathf.Clamp01(a);
            if (card)
            {
                cardGroup.alpha = a;
                lineGroup.alpha = 0f;
                cardTitle.characterSpacing = Mathf.Lerp(40f, 18f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(timer / 1.6f)));
            }
            else
            {
                lineGroup.alpha = a;
                cardGroup.alpha = 0f;
            }

            if (timer >= outStart + FadeTime)
            {
                if (index + 1 < lines.Length) ShowLine(index + 1);
                else End(true);
            }
        }

        // ───────── 화면 만들기 ─────────

        private void Build()
        {
#if UNITY_EDITOR
            font = FindDefaultFont();
#else
            foreach (var f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
                if (f.name == "ChosunCentennial_otf SDF") { font = f; break; }
#endif
            Canvas canvas = NewCanvas("Canvas", transform, 76, out group);   // 자막(70)·오프닝 눈(74) 위, 처치 문구(80) 아래
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            backdropImg = NewImage("Backdrop", canvas.transform, new Color(Ink.r, Ink.g, Ink.b, 0f), Vector2.zero, Vector2.zero);

            topBar = Bar("TopBar", canvas.transform, 1f);
            bottomBar = Bar("BottomBar", canvas.transform, 0f);

            // 대사: 아래 먹 띠 바로 위
            RectTransform lineRoot = NewRect("Line", canvas.transform);
            lineRoot.anchorMin = new Vector2(0.5f, 0f);
            lineRoot.anchorMax = new Vector2(0.5f, 0f);
            lineRoot.pivot = new Vector2(0.5f, 0f);
            lineRoot.sizeDelta = new Vector2(1400f, 170f);
            lineRoot.anchoredPosition = new Vector2(0f, 128f);
            lineGroup = lineRoot.gameObject.AddComponent<CanvasGroup>();

            speakerText = NewText("Speaker", lineRoot, font, "", 26f, Brass);
            speakerText.characterSpacing = 24f;
            RectTransform sr = speakerText.rectTransform;
            sr.anchorMin = new Vector2(0f, 1f);
            sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.sizeDelta = new Vector2(0f, 40f);
            sr.anchoredPosition = new Vector2(0f, -4f);

            bodyText = NewText("Body", lineRoot, font, "", 38f, White);
            bodyText.textWrappingMode = TextWrappingModes.Normal;
            bodyText.characterSpacing = 4f;
            bodyText.lineSpacing = 10f;
            RectTransform br = bodyText.rectTransform;
            br.offsetMin = new Vector2(60f, 0f);
            br.offsetMax = new Vector2(-60f, -46f);

            // 카드: 가운데 큰 글씨 + 작은 아랫줄
            RectTransform cardRoot = NewRect("Card", canvas.transform);
            cardRoot.anchorMin = cardRoot.anchorMax = new Vector2(0.5f, 0.54f);
            cardRoot.sizeDelta = new Vector2(1600f, 220f);
            cardGroup = cardRoot.gameObject.AddComponent<CanvasGroup>();
            cardTitle = NewText("Title", cardRoot, font, "", 72f, White);
            cardTitle.rectTransform.offsetMin = new Vector2(0f, 80f);
            cardSub = NewText("Sub", cardRoot, font, "", 28f, Brass);
            cardSub.characterSpacing = 16f;
            cardSub.rectTransform.offsetMax = new Vector2(0f, -150f);

            hintText = NewText("Hint", canvas.transform, font, "", 18f, White);
            RectTransform hr = hintText.rectTransform;
            hr.anchorMin = hr.anchorMax = new Vector2(1f, 0f);
            hr.pivot = new Vector2(1f, 0f);
            hr.sizeDelta = new Vector2(520f, 30f);
            hr.anchoredPosition = new Vector2(-40f, 40f);
            hintText.alignment = TextAlignmentOptions.Right;
            hintGroup = hintText.gameObject.AddComponent<CanvasGroup>();
            hintGroup.alpha = 0f;
        }

        private static RectTransform Bar(string name, Transform parent, float y)
        {
            Image img = NewImage(name, parent, Ink, Vector2.zero, Vector2.zero);
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y);
            rt.pivot = new Vector2(0.5f, y);
            rt.sizeDelta = Vector2.zero;
            return rt;
        }
    }
}
