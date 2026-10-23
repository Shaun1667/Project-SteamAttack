using NGH;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 쓰러졌을 때 화면 (NGH GameOverScreen을 대신하는 NMJ 스타일)
    ///
    ///   ━━━━━━━━━━━━━━━━━━━━━━━━━━   ← 먹 띠
    ///            쓰러졌다  [命]           ← 붉은 命 도장이 금 간 채 찍힌다
    ///      어명은 아직 끝나지 않았다
    ///    아무 키나 눌러 다시 일어서기       ← 잠시 뒤 깜빡이며 나온다
    ///
    /// - 쓰러지는 동작이 끝나면 화면이 종이 타듯 군데군데 타 들어가며(죽음이 아니라 이 시간선에서 지워짐) 뜬다.
    /// - 키를 누르면 먹물 화면 전환으로 덮고, 그 사이에 그 자리에서 일어난다 (NGH PlayerController.Respawn).
    /// - NGH GameOverScreen은 프리팹에서 꺼 두고, 하던 일(감지·입력·부활)을 여기서 한다.
    ///
    /// UI는 실행할 때 코드로 만들어진다. 플레이어(NGH_Player)에 붙여 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public class DefeatScreen : MonoBehaviour
    {
        [SerializeField] private PlayerController player;
        [Tooltip("한글 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset font;
        [Tooltip("한자(命) 전용 고정 폰트. 비우면 에디터에서 NMJ_HudGlyphs SDF를 자동 연결")]
        [SerializeField] private TMP_FontAsset glyphFont;

        [Header("글자")]
        [SerializeField] private string headline = "쓰러졌다";
        [SerializeField] private string sealText = "命";
        [SerializeField] private string subtitle = "어명은 아직 끝나지 않았다";
        [SerializeField] private string prompt = "아무 키나 눌러 다시 일어서기";

        [Header("시간 (초)")]
        [Tooltip("쓰러지는 동작이 끝난 뒤 화면이 뜰 때까지")]
        [SerializeField, Min(0f)] private float showDelay = 0.3f;
        [Tooltip("화면이 뜬 뒤 키를 받기 시작할 때까지 (연타로 바로 넘어가지 않게)")]
        [SerializeField, Min(0f)] private float inputDelay = 1.2f;
        [Tooltip("화면이 뜬 뒤 이 시간이 지나면 키를 누르지 않아도 저절로 일어난다 (0이면 끔). 시연이 끊기지 않게")]
        [SerializeField, Min(0f)] private float autoRestartAfter = 3f;
        [SerializeField] private InkStyle restartTransition = InkStyle.Drip;

        [Header("종이 타듯 사라짐 (세계관 v6 6-2 — 죽음이 아니라 이 시간선에서 지워짐)")]
        [SerializeField] private bool burnAway = true;
        [Tooltip("화면이 타 들어가는 시간")]
        [SerializeField, Min(0.1f)] private float burnTime = 1.8f;
        [Tooltip("어디까지 탈지 (0~1). 1이면 화면이 다 타서 검게 된다")]
        [SerializeField, Range(0f, 1f)] private float burnAmount = 0.6f;

        [Header("색 (오방색)")]
        [SerializeField] private Color dimColor = new Color(0.03f, 0.03f, 0.035f, 0.62f);
        [SerializeField] private Color inkColor = new Color(0.03f, 0.03f, 0.035f, 0.97f);
        [SerializeField] private Color textColor = new Color(0.96f, 0.95f, 0.92f, 1f);
        [SerializeField] private Color sealColor = new Color(0.82f, 0.18f, 0.10f, 1f);

        /// <summary>쓰러진 화면이 떠 있는 중</summary>
        public static bool IsShowing { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => IsShowing = false;

        private TMP_FontAsset HanjaFont => glyphFont != null ? glyphFont : font;

        private CanvasGroup group, sealGroup, promptGroup;
        private TMP_Text promptText;
        private RectTransform reveal, sealRect, root;
        private TMP_Text headText, subText;
        private Texture2D bandTex, sealTex;
        private float bandWidth = 1500f;
        private Material burnMat;
        private RawImage burnImage;
        private Image dimImage;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int StyleId = Shader.PropertyToID("_Style");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        private float finishedAt = -1f;   // 쓰러지는 동작이 끝난 시각
        private float shownTime;          // 화면이 뜬 뒤 흐른 시간
        private bool restarting;

        // ───────── 시작 ─────────

        private void Awake()
        {
            if (player == null) player = GetComponentInParent<PlayerController>();
            BuildUi();
            Hide();
        }

        private void OnDisable() => Hide();

        private void OnDestroy()
        {
            if (bandTex != null) Destroy(bandTex);
            if (sealTex != null) Destroy(sealTex);
            if (burnMat != null) Destroy(burnMat);
        }

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            if (player == null) return;
            if (!player.IsDead)
            {
                if (IsShowing && !restarting) Hide();
                finishedAt = -1f;
                return;
            }

            if (player.IsDeathAnimFinished && finishedAt < 0f) finishedAt = Time.time;
            if (!IsShowing && finishedAt >= 0f && Time.time - finishedAt >= showDelay) Show();
            if (!IsShowing) return;

            shownTime += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            Layout(shownTime);

            bool auto = autoRestartAfter > 0f && shownTime >= Mathf.Max(autoRestartAfter, inputDelay);
            if (!restarting && shownTime >= inputDelay && (AnyInput() || auto)) Restart();
            if (promptText != null && autoRestartAfter > 0f && !restarting)
                promptText.text = $"{prompt}  <size=80%><alpha=#99>· {Mathf.CeilToInt(Mathf.Max(0f, autoRestartAfter - shownTime))}초 뒤 저절로</size>";
        }

        private void Show()
        {
            IsShowing = true;
            restarting = false;
            shownTime = 0f;
            if (burnMat != null) burnMat.SetFloat(SeedId, Random.Range(0f, 100f));   // 매번 다른 자리부터 탄다
            group.gameObject.SetActive(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Layout(0f);
        }

        private void Hide()
        {
            IsShowing = false;
            restarting = false;
            if (group != null) { group.alpha = 0f; group.gameObject.SetActive(false); }
        }

        private void Restart()
        {
            restarting = true;
            // 먹으로 덮은 사이에 일어난다 → 걷히면 일어나는 모습이 보인다
            InkTransition.Play(restartTransition, () =>
            {
                Hide();
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                player.Respawn();
            }, 1.2f);
        }

        private static bool AnyInput()
        {
            Keyboard kb = Keyboard.current;
            Mouse m = Mouse.current;
            return (kb != null && kb.anyKey.wasPressedThisFrame)
                || (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame));
        }

        // ───────── 연출 ─────────

        // 어둠 0~1.0 → 먹 띠 0.2~0.7 → 글자 0.5~1.1 → 도장 1.1~1.25 → 아래 글 1.2~1.8 → 안내 inputDelay 뒤
        private void Layout(float t)
        {
            // 타는 화면은 처음부터 보이고, 글·띠는 그 위로 서서히
            if (burnImage != null)
            {
                group.alpha = 1f;
                float b = burnAmount * (1f - Mathf.Pow(1f - Mathf.Clamp01(t / burnTime), 2f));
                burnMat.SetFloat(ProgressId, b);
                burnMat.SetFloat(AspectId, Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7778f);
                dimImage.color = new Color(dimColor.r, dimColor.g, dimColor.b, dimColor.a * 0.55f * Mathf.Clamp01(t / 0.6f));
            }
            else group.alpha = Mathf.Clamp01(t / 0.6f);

            float band = EaseOut(Mathf.Clamp01((t - 0.2f) / 0.5f));
            reveal.sizeDelta = new Vector2(bandWidth * band, reveal.sizeDelta.y);

            float h = Mathf.Clamp01((t - 0.5f) / 0.6f);
            headText.alpha = h;
            headText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.08f, 1f, EaseOut(h));
            headText.characterSpacing = Mathf.Lerp(36f, 16f, EaseOut(h));

            float s = Mathf.Clamp01((t - 1.1f) / 0.15f);
            sealGroup.alpha = s * 0.9f;
            sealRect.localScale = Vector3.one * Mathf.Lerp(1.7f, 1f, s * s);
            float shake = t > 1.25f && t < 1.45f ? (1.45f - t) / 0.2f * 3f : 0f;
            root.anchoredPosition = shake > 0f ? new Vector2(Random.Range(-shake, shake), Random.Range(-shake, shake)) : Vector2.zero;

            subText.alpha = Mathf.Clamp01((t - 1.2f) / 0.6f) * 0.85f;
            promptGroup.alpha = t >= inputDelay ? 0.45f + 0.3f * Mathf.Sin((t - inputDelay) * 3f) : 0f;
        }

        private static float EaseOut(float x) => 1f - (1f - x) * (1f - x) * (1f - x);

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) font = FindDefaultFont();
            if (glyphFont == null) glyphFont = FindGlyphFont();
#endif
            Canvas canvas = NewCanvas("NMJ_DefeatCanvas", transform, 82, out group);   // 처치 문구(80) 위, 크레딧(85) 아래
            group.blocksRaycasts = false;
            group.interactable = false;

            dimImage = NewImage("Dim", canvas.transform, dimColor, Vector2.zero, Vector2.zero);

            // 종이 타듯 사라짐: 먹물 전환 셰이더의 Burn 모양을 배경으로 (글·띠 아래)
            var source = burnAway ? Resources.Load<Material>("NMJ/NMJ_InkTransition") : null;
            if (source != null)
            {
                burnMat = new Material(source) { hideFlags = HideFlags.DontSave };
                burnMat.SetFloat(StyleId, (float)InkStyle.Burn);
                burnMat.SetFloat(ProgressId, 0f);
                burnImage = NewRawImage("Burn", canvas.transform, Texture2D.whiteTexture);
                burnImage.material = burnMat;
                burnImage.transform.SetSiblingIndex(dimImage.transform.GetSiblingIndex());   // 어둠 아래
            }

            root = NewRect("Banner", canvas.transform);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.55f);
            root.sizeDelta = new Vector2(bandWidth, 280f);

            // 먹 띠 (왼쪽에서 그어진다)
            reveal = NewRect("Reveal", root);
            reveal.anchorMin = new Vector2(0f, 0f);
            reveal.anchorMax = new Vector2(0f, 1f);
            reveal.pivot = new Vector2(0f, 0.5f);
            reveal.sizeDelta = Vector2.zero;
            reveal.gameObject.AddComponent<RectMask2D>();
            bandTex = DrawBrushBand(1024, 192, inkColor, 31f);
            RawImage band = NewRawImage("Band", reveal, bandTex);
            band.rectTransform.anchorMin = band.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            band.rectTransform.pivot = new Vector2(0f, 0.5f);
            band.rectTransform.sizeDelta = new Vector2(bandWidth, 280f);

            headText = NewText("Headline", root, font, headline, 96f, textColor);
            headText.rectTransform.offsetMin = new Vector2(0f, 60f);
            headText.rectTransform.offsetMax = new Vector2(0f, -20f);

            // 금 간 命 도장 (체력바의 낙관과 같은 글자)
            float sealSize = 86f;
            sealRect = NewRect("Seal", root);
            sealRect.anchorMin = sealRect.anchorMax = new Vector2(0.5f, 0.5f);
            sealRect.sizeDelta = new Vector2(sealSize, sealSize);
            sealRect.anchoredPosition = new Vector2(headText.GetPreferredValues(headline).x * 0.5f + 70f, 44f);
            sealRect.localRotation = Quaternion.Euler(0f, 0f, 8f);
            sealGroup = sealRect.gameObject.AddComponent<CanvasGroup>();
            sealTex = DrawSeal(128, sealColor, textColor, 53f);
            NewRawImage("Ink", sealRect, sealTex);
            NewText("Glyph", sealRect, HanjaFont, sealText, sealSize * 0.6f, new Color(textColor.r, textColor.g, textColor.b, 0.94f));
            Image crack = NewImage("Crack", sealRect, new Color(0.03f, 0.03f, 0.035f, 0.85f), Vector2.zero, Vector2.zero);
            crack.rectTransform.anchorMin = crack.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            crack.rectTransform.sizeDelta = new Vector2(3f, sealSize * 1.1f);
            crack.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -28f);

            subText = NewText("Subtitle", root, font, subtitle, 30f, textColor);
            subText.rectTransform.anchorMin = new Vector2(0f, 0f);
            subText.rectTransform.anchorMax = new Vector2(1f, 0f);
            subText.rectTransform.sizeDelta = new Vector2(0f, 40f);
            subText.rectTransform.anchoredPosition = new Vector2(0f, 48f);

            TMP_Text p = NewText("Prompt", canvas.transform, font, prompt, 22f, textColor);
            p.richText = true;
            promptText = p;
            p.rectTransform.anchorMin = p.rectTransform.anchorMax = new Vector2(0.5f, 0.2f);
            p.rectTransform.sizeDelta = new Vector2(800f, 40f);
            promptGroup = p.gameObject.AddComponent<CanvasGroup>();
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
