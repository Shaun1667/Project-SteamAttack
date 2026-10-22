using NGH;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 길가의 글 읽기 — 흑수회 괘서, 기기창 정비공의 비망록 (세계관 v6 3-3·5장 육조거리, 회의 B-4)
    ///
    ///   가까이 가면 화면 아래에   [E] 괘서 읽기
    ///   E를 누르면 한지 한 장이 펼쳐진다 → E·ESC·클릭으로 닫는다
    ///
    /// - 읽는 동안 게임이 멈추고(시간 0) 플레이어 입력·ESC 메뉴가 막힌다.
    /// - 이 컴포넌트를 길가의 종이(벽보·바닥의 종이) 오브젝트에 붙이고 글을 적는다. 글은 인스펙터에서 고친다.
    /// - 한 번 읽은 글은 안내가 흐려진다 (다시 읽을 수는 있음).
    /// </summary>
    public class NmjReadable : MonoBehaviour
    {
        public enum Paper
        {
            /// <summary>괘서: 거칠게 붙인 벽보, 큰 붓글씨 한 줄 + 이름</summary>
            Gwaeseo,
            /// <summary>비망록: 손으로 적은 기록 한 장, 작은 글씨 여러 줄</summary>
            Bimangrok,
        }

        [Header("글")]
        [SerializeField] private Paper paper = Paper.Gwaeseo;
        [Tooltip("안내에 나오는 이름 (예: 괘서 → '괘서 읽기')")]
        [SerializeField] private string label = "괘서";
        [Tooltip("종이 맨 위 작은 머리글 (비우면 없음)")]
        [SerializeField] private string heading = "";
        [SerializeField, TextArea(2, 8)] private string body = "북방의 검은 물이 내려와\n귀신불을 끄리라";
        [Tooltip("종이 아래 이름 (예: — 흑수회주)")]
        [SerializeField] private string signature = "— 흑수회주";

        [Header("읽는 거리")]
        [SerializeField, Min(0.5f)] private float radius = 2.4f;
        [SerializeField] private Transform player;

        [Header("글꼴 (비우면 자동)")]
        [SerializeField] private TMP_FontAsset font;

        /// <summary>글을 읽는 중 (닫힌 프레임 다음까지 — 같은 ESC로 메뉴가 열리지 않게)</summary>
        public static bool IsReading => reading != null || Time.frameCount - closedFrame <= 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { reading = null; nearest = null; closedFrame = -10; }

        private static NmjReadable reading;     // 지금 펼친 글
        private static NmjReadable nearest;     // 안내를 띄울 글 (가장 가까운 하나)
        private static int closedFrame = -10;

        private static readonly Color PaperColor = new Color(0.93f, 0.89f, 0.80f, 1f);   // 한지
        private static readonly Color InkText = new Color(0.08f, 0.07f, 0.07f, 1f);

        private bool readOnce;
        private float prevTimeScale = 1f;
        private float openAlpha, promptAlpha;
        private CanvasGroup group, sheetGroup, promptGroup;
        private RectTransform sheet;
        private TMP_Text promptText;
        private Texture2D paperTex;

        // ───────── 매 프레임 ─────────

        private void Update()
        {
            if (player == null)
            {
                var pc = FindAnyObjectByType<PlayerController>();
                if (pc == null) return;
                player = pc.transform;
            }

            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float dist = Vector3.Distance(player.position, transform.position);
            bool blocked = ControlsOverlay.IsOpen && reading != this;   // 메뉴·대사·다른 글이 떠 있음
            bool inRange = dist <= radius && !blocked && !(player.TryGetComponent(out PlayerController p) && p.IsDead);

            // 여러 글이 가까우면 가장 가까운 것만 안내한다
            if (inRange && (nearest == null || nearest == this || !nearest.isActiveAndEnabled ||
                            Vector3.Distance(player.position, nearest.transform.position) > dist)) nearest = this;
            if (!inRange && nearest == this) nearest = null;

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (reading == this)
            {
                bool close = kb != null && (kb.eKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)
                             || (mouse != null && mouse.leftButton.wasPressedThisFrame);
                if (close && openAlpha > 0.5f) Close();
            }
            else if (nearest == this && reading == null && !IsReading && kb != null && kb.eKey.wasPressedThisFrame) Open();

            bool showPrompt = nearest == this && reading == null;
            if (group == null && (showPrompt || reading == this)) Build();
            if (group == null) return;

            promptAlpha = Mathf.MoveTowards(promptAlpha, showPrompt ? 1f : 0f, dt / 0.2f);
            openAlpha = Mathf.MoveTowards(openAlpha, reading == this ? 1f : 0f, dt / 0.25f);
            promptGroup.alpha = promptAlpha * (readOnce ? 0.55f : 1f);
            sheetGroup.alpha = openAlpha;
            // 펼칠 때 살짝 아래에서 올라오며 펴진다
            float e = Mathf.SmoothStep(0f, 1f, openAlpha);
            sheet.anchoredPosition = new Vector2(0f, Mathf.Lerp(-30f, 0f, e));
            sheet.localScale = new Vector3(1f, Mathf.Lerp(0.92f, 1f, e), 1f);
            group.alpha = Mathf.Max(promptAlpha, openAlpha);
        }

        private void Open()
        {
            reading = this;
            readOnce = true;
            ControlsOverlay.MenuOpen = true;
            prevTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }

        private void Close()
        {
            reading = null;
            closedFrame = Time.frameCount;
            ControlsOverlay.MenuOpen = false;
            Time.timeScale = prevTimeScale;
        }

        private void OnDisable()
        {
            if (reading == this) Close();
            if (nearest == this) nearest = null;
        }

        private void OnDestroy()
        {
            if (paperTex != null) Destroy(paperTex);
            if (group != null) Destroy(group.gameObject);
        }

        // ───────── 화면 만들기 ─────────

        private void Build()
        {
#if UNITY_EDITOR
            if (font == null) font = FindDefaultFont();
#endif
            Canvas canvas = NewCanvas("NMJ_ReadableCanvas_" + name, null, 72, out group);   // 자막(70) 위, 대사(76) 아래
            group.blocksRaycasts = false;
            group.interactable = false;
            group.alpha = 0f;

            // 안내: [E] 괘서 읽기
            RectTransform prompt = NewRect("Prompt", canvas.transform);
            prompt.anchorMin = prompt.anchorMax = new Vector2(0.5f, 0.24f);
            prompt.sizeDelta = new Vector2(420f, 56f);
            promptGroup = prompt.gameObject.AddComponent<CanvasGroup>();
            Image band = NewImage("Band", prompt, new Color(0.04f, 0.04f, 0.05f, 0.75f), Vector2.zero, Vector2.zero);
            band.rectTransform.anchorMin = new Vector2(0.08f, 0.08f);
            band.rectTransform.anchorMax = new Vector2(0.92f, 0.92f);
            Image key = NewImage("Key", prompt, White, Vector2.zero, Vector2.zero);
            RectTransform kr = key.rectTransform;
            kr.anchorMin = kr.anchorMax = new Vector2(0.5f, 0.5f);
            kr.sizeDelta = new Vector2(44f, 44f);
            kr.anchoredPosition = new Vector2(-80f, 0f);
            NewText("Glyph", kr, font, "E", 28f, Black);
            promptText = NewText("Text", prompt, font, label + " 읽기", 28f, White);
            RectTransform pt = promptText.rectTransform;
            pt.anchorMin = pt.anchorMax = new Vector2(0.5f, 0.5f);
            pt.pivot = new Vector2(0f, 0.5f);
            pt.sizeDelta = new Vector2(260f, 44f);
            pt.anchoredPosition = new Vector2(-46f, 0f);
            promptText.alignment = TextAlignmentOptions.Left;
            promptText.characterSpacing = 6f;

            // 펼친 종이
            RectTransform sheetRoot = NewRect("Sheet", canvas.transform);
            sheetGroup = sheetRoot.gameObject.AddComponent<CanvasGroup>();
            sheetGroup.alpha = 0f;
            NewImage("Dim", sheetRoot, new Color(0f, 0f, 0f, 0.6f), Vector2.zero, Vector2.zero);

            bool gwaeseo = paper == Paper.Gwaeseo;
            Vector2 size = gwaeseo ? new Vector2(760f, 860f) : new Vector2(980f, 640f);
            sheet = NewRect("Paper", sheetRoot);
            sheet.anchorMin = sheet.anchorMax = new Vector2(0.5f, 0.52f);
            sheet.sizeDelta = size;
            sheet.localRotation = Quaternion.Euler(0f, 0f, gwaeseo ? -1.5f : 0.8f);
            paperTex = DrawPaper(gwaeseo ? 380 : 490, gwaeseo ? 430 : 320, PaperColor, gwaeseo ? 13f : 47f, gwaeseo);
            NewRawImage("Hanji", sheet, paperTex);

            if (!string.IsNullOrEmpty(heading))
            {
                TMP_Text h = NewText("Heading", sheet, font, heading, 24f, new Color(0.45f, 0.36f, 0.22f, 1f));
                h.characterSpacing = 14f;
                RectTransform hr = h.rectTransform;
                hr.anchorMin = new Vector2(0f, 1f);
                hr.anchorMax = new Vector2(1f, 1f);
                hr.pivot = new Vector2(0.5f, 1f);
                hr.sizeDelta = new Vector2(-120f, 40f);
                hr.anchoredPosition = new Vector2(0f, -54f);
            }

            TMP_Text b = NewText("Body", sheet, font, body, gwaeseo ? 64f : 32f, InkText);
            b.textWrappingMode = TextWrappingModes.Normal;
            b.lineSpacing = gwaeseo ? 30f : 24f;
            b.characterSpacing = gwaeseo ? 18f : 2f;
            b.alignment = gwaeseo ? TextAlignmentOptions.Center : TextAlignmentOptions.TopLeft;
            RectTransform br = b.rectTransform;
            br.offsetMin = gwaeseo ? new Vector2(70f, 200f) : new Vector2(80f, 110f);
            br.offsetMax = gwaeseo ? new Vector2(-70f, -120f) : new Vector2(-80f, string.IsNullOrEmpty(heading) ? -70f : -120f);

            if (!string.IsNullOrEmpty(signature))
            {
                TMP_Text s = NewText("Signature", sheet, font, signature, gwaeseo ? 36f : 24f, gwaeseo ? new Color(0.55f, 0.08f, 0.05f, 1f) : InkText);
                s.alignment = TextAlignmentOptions.Right;
                RectTransform sr = s.rectTransform;
                sr.anchorMin = new Vector2(0f, 0f);
                sr.anchorMax = new Vector2(1f, 0f);
                sr.pivot = new Vector2(0.5f, 0f);
                sr.sizeDelta = new Vector2(-140f, 50f);
                sr.anchoredPosition = new Vector2(0f, gwaeseo ? 110f : 60f);
            }

            TMP_Text hint = NewText("Hint", sheetRoot, font, "E · ESC  닫기", 20f, White);
            RectTransform hr2 = hint.rectTransform;
            hr2.anchorMin = hr2.anchorMax = new Vector2(0.5f, 0.06f);
            hr2.sizeDelta = new Vector2(400f, 30f);
            hint.alpha = 0.5f;
        }

        /// <summary>한지: 바탕 얼룩과 닥나무 섬유 결, 거칠게 찢긴 둘레. 괘서는 풀 자국과 얼룩을 더한다</summary>
        private static Texture2D DrawPaper(int W, int H, Color paperColor, float seed, bool rough)
        {
            var px = new Color32[W * H];
            float border = rough ? 0.05f : 0.03f;
            for (int y = 0; y < H; y++)
            {
                for (int x = 0; x < W; x++)
                {
                    float u = (float)x / (W - 1), v = (float)y / (H - 1);
                    // 둘레: 가장자리까지 거리 + 잡음 → 찢긴 모양
                    float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v));
                    edge += (Mathf.PerlinNoise(u * 40f + seed, v * 40f) - 0.5f) * border * 0.9f;
                    float a = Mathf.Clamp01((edge - border * 0.3f) / (border * 0.25f));
                    if (a <= 0f) { px[y * W + x] = new Color32(0, 0, 0, 0); continue; }

                    float blot = Mathf.PerlinNoise(u * 4f + seed, v * 4f + 3f);
                    float fiber = Mathf.PerlinNoise(u * 140f + seed, v * 12f) * Mathf.PerlinNoise(u * 9f, v * 160f + seed);
                    float shade = 0.92f + 0.08f * blot - 0.06f * fiber;
                    if (rough)
                    {
                        // 풀 자국·물 얼룩 (괘서는 거칠게 나붙은 종이)
                        float stain = Mathf.PerlinNoise(u * 2.2f + 11f + seed, v * 2.2f);
                        shade -= Mathf.Clamp01((stain - 0.62f) * 3f) * 0.12f;
                    }
                    // 가장자리로 갈수록 조금 누렇게
                    shade -= Mathf.Clamp01(0.08f - edge) * 1.2f;
                    Color c = paperColor * shade;
                    c.a = a;
                    px[y * W + x] = c;
                }
            }
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.94f, 0.73f, 0.16f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, radius);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (font == null) font = FindDefaultFont();
        }
#endif
    }
}
