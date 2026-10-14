using UnityEngine;
using UnityEngine.UI;
using NGH;

namespace NMJ
{
    /// <summary>
    /// 플레이어 체력바 — 먹 붓 테두리 + 붉은 톱니 붓 획 (NMJ 새 HUD 1번: 체력)
    ///
    ///   ┌~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~┐   ← 손으로 그은 듯 울퉁불퉁한 먹 테두리
    ///   │/|/|/|/|/|/|/|/|/|/|/|          │   ← 붉은 바탕 위에 톱니 모양 붓 획 (주인의 피, 적색=저항)
    ///   └~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~┘
    ///
    /// - 체력이 줄면 붉은 획이 오른쪽부터 잘려 나가고, 깎인 부분은 옅은 잔상으로 잠시 남았다 줄어든다.
    /// - 맞는 순간 바가 살짝 흔들린다. 체력 30% 이하에서는 붉은 획이 천천히 맥박친다.
    /// - 그림은 실행할 때 코드로 그린다 (텍스처 파일 없음). 같은 Seed면 매번 같은 모양.
    /// - NGH.PlayerController의 공개 값(Hp, maxHp)만 읽는다. 플레이어가 없으면 숨긴다.
    ///
    /// 씬에 빈 오브젝트를 만들고 이 컴포넌트만 붙이면 된다. (증기·무기는 다음 단계에서 같은 스타일로 추가)
    /// 기존 NGH PlayerHUD와 함께 켜면 두 개가 겹치므로, 확인할 때는 기존 HUD를 끈다.
    /// </summary>
    [DisallowMultipleComponent]
    public class InkHealthBar : MonoBehaviour
    {
        [Header("연결 (비우면 씬에서 자동으로 찾음)")]
        [SerializeField] private PlayerController player;

        [Header("배치 (화면 1920×1080 기준, 왼쪽 아래 기준점)")]
        [SerializeField] private Vector2 position = new Vector2(70f, 96f);
        [SerializeField] private Vector2 size = new Vector2(520f, 40f);
        [Tooltip("바 왼쪽의 붉은 + 표시")]
        [SerializeField] private bool showCrossIcon = true;

        [Header("색 (오방색 적·흑)")]
        [SerializeField] private Color washColor = new Color(0.62f, 0.07f, 0.05f, 0.75f);   // 붉은 바탕
        [SerializeField] private Color strokeColor = new Color(0.86f, 0.12f, 0.08f, 1f);   // 톱니 붓 획
        [SerializeField] private Color inkColor = new Color(0.05f, 0.05f, 0.06f, 1f);      // 테두리 먹
        [SerializeField] private Color emptyColor = new Color(0.08f, 0.07f, 0.07f, 0.55f); // 빈 칸
        [SerializeField] private Color trailColor = new Color(1f, 0.82f, 0.78f, 0.75f);    // 깎인 부분 잔상

        [Header("붓 획 모양")]
        [Tooltip("톱니 한 칸 길이 범위 (픽셀)")]
        [SerializeField] private Vector2 toothLength = new Vector2(26f, 48f);
        [SerializeField, Range(1f, 5f)] private float strokeWidth = 1.8f;
        [Tooltip("같은 숫자면 매번 같은 모양")]
        [SerializeField] private int seed = 3;

        [Header("연출")]
        [SerializeField, Min(0f)] private float trailDelay = 0.4f;
        [SerializeField, Min(0.01f)] private float trailSpeed = 0.6f;
        [SerializeField, Min(0f)] private float hitShake = 5f;
        [SerializeField, Range(0f, 1f)] private float lowHpRatio = 0.3f;

        private CanvasGroup group;
        private RectTransform barRoot;
        private Image fill;
        private Image trail;
        private Image cross;
        private float trailRatio = 1f;
        private float trailHold;
        private float shownRatio = 1f;
        private float shakeTimer;
        private Vector2 basePos;

        // ───────── 시작 ─────────

        private void Awake()
        {
            BuildUi();
        }

        private void Update()
        {
            if (player == null)
            {
                player = FindAnyObjectByType<PlayerController>();
                if (player == null) { group.alpha = 0f; return; }
                trailRatio = shownRatio = Ratio();
            }
            group.alpha = 1f;

            float dt = Time.unscaledDeltaTime;
            float ratio = Ratio();

            // 맞았으면 흔들기
            if (ratio < shownRatio - 0.0001f) shakeTimer = 0.25f;
            shownRatio = ratio;

            // 잔상: 회복은 바로, 피해는 잠시 머물렀다 줄어듦
            if (ratio >= trailRatio) { trailRatio = ratio; trailHold = 0f; }
            else if (trailHold < trailDelay) trailHold += dt;
            else
            {
                trailRatio = Mathf.MoveTowards(trailRatio, ratio, trailSpeed * dt);
                if (trailRatio <= ratio) trailHold = 0f;
            }

            fill.fillAmount = ratio;
            trail.fillAmount = trailRatio;

            // 체력이 낮으면 붉은 획이 맥박친다
            float pulse = ratio > 0f && ratio <= lowHpRatio ? 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;
            fill.color = new Color(pulse, pulse, pulse, 1f);
            if (cross != null) cross.color = new Color(strokeColor.r * pulse, strokeColor.g * pulse, strokeColor.b * pulse, 1f);

            // 흔들림
            Vector2 offset = Vector2.zero;
            if (shakeTimer > 0f)
            {
                shakeTimer -= dt;
                float s = hitShake * Mathf.Clamp01(shakeTimer / 0.25f);
                offset = new Vector2(Random.Range(-s, s), Random.Range(-s, s));
            }
            barRoot.anchoredPosition = basePos + offset;
        }

        private float Ratio()
        {
            return player != null && player.maxHp > 0 ? Mathf.Clamp01((float)player.Hp / player.maxHp) : 0f;
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
            var canvasGo = new GameObject("NMJ_InkHealthBarCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            int w = Mathf.RoundToInt(size.x), h = Mathf.RoundToInt(size.y);
            const int pad = 8;   // 테두리가 바 바깥으로 나오는 여유

            barRoot = NewRect("HealthBar", canvasGo.transform);
            barRoot.anchorMin = barRoot.anchorMax = Vector2.zero;
            barRoot.pivot = Vector2.zero;
            barRoot.sizeDelta = size;
            basePos = position;
            barRoot.anchoredPosition = basePos;

            // 빈 칸 (어두운 바탕)
            Image empty = NewImage("Empty", barRoot, MakeSprite(DrawEmpty(w, h)));
            empty.color = emptyColor;

            // 잔상 → 채움 순서로 겹침 (같은 그림을 왼쪽부터 잘라 씀)
            Sprite fillSprite = MakeSprite(DrawFill(w, h));
            trail = NewImage("Trail", barRoot, fillSprite);
            trail.color = trailColor;
            SetFilled(trail);
            fill = NewImage("Fill", barRoot, fillSprite);
            SetFilled(fill);

            // 먹 테두리 (바보다 조금 크게)
            Image frame = NewImage("Frame", barRoot, MakeSprite(DrawFrame(w + pad * 2, h + pad * 2)));
            frame.rectTransform.offsetMin = new Vector2(-pad, -pad);
            frame.rectTransform.offsetMax = new Vector2(pad, pad);

            // 왼쪽 붉은 + 표시
            if (showCrossIcon)
            {
                cross = NewImage("Cross", barRoot, MakeSprite(DrawCross(28)));
                RectTransform rt = cross.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.sizeDelta = new Vector2(28f, 28f);
                rt.anchoredPosition = new Vector2(-pad - 6f, 0f);
            }
        }

        private static void SetFilled(Image img)
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
        }

        // ───────── 그림 그리기 (코드로 만드는 텍스처) ─────────

        /// <summary>붉은 바탕 + 톱니 붓 획. 위아래 가장자리는 붓 끝처럼 살짝 들쭉날쭉.</summary>
        private Texture2D DrawFill(int w, int h)
        {
            var rng = new System.Random(seed);
            var px = new Color[w * h];

            // 톱니 획: 아래 → 오른쪽 위로 비스듬히 긋고, 거의 수직으로 내려옴
            var segs = new System.Collections.Generic.List<Vector4>();
            float x = 0f;
            while (x < w)
            {
                float p = Mathf.Lerp(toothLength.x, toothLength.y, (float)rng.NextDouble());
                float top = 4f + 5f * (float)rng.NextDouble();
                float bottom = h - 4f - 5f * (float)rng.NextDouble();
                segs.Add(new Vector4(x, bottom, x + p, top));
                segs.Add(new Vector4(x + p, top, x + p - 1f - 3f * (float)rng.NextDouble(), bottom));
                x += p;
            }

            float[] edgeTop = RaggedEdge(w, rng, 3f);
            float[] edgeBottom = RaggedEdge(w, rng, 3f);

            for (int yy = 0; yy < h; yy++)
            {
                for (int xx = 0; xx < w; xx++)
                {
                    // 텍스처 y는 아래가 0
                    float y = h - 1 - yy;
                    if (y < edgeTop[xx] || y > h - 1 - edgeBottom[xx]) { px[yy * w + xx] = Color.clear; continue; }

                    // 바탕: 붓으로 칠한 듯 농담이 조금씩 다름
                    float n = Mathf.PerlinNoise(xx * 0.035f + seed, y * 0.12f);
                    Color c = washColor;
                    c.a = Mathf.Clamp01(washColor.a * (0.8f + 0.4f * n));

                    float d = float.MaxValue;
                    foreach (Vector4 s in segs) d = Mathf.Min(d, SegDistance(xx, y, s));
                    float k = Mathf.Clamp01(1f - (d - strokeWidth) / 1.2f);
                    c = Color.Lerp(c, strokeColor, k);
                    c.a = Mathf.Max(c.a, k);
                    px[yy * w + xx] = c;
                }
            }
            return MakeTexture(w, h, px);
        }

        /// <summary>빈 칸 바탕 (채움과 같은 들쭉날쭉한 모양, 흰색 → Image 색으로 칠함)</summary>
        private Texture2D DrawEmpty(int w, int h)
        {
            var rng = new System.Random(seed + 11);
            float[] edgeTop = RaggedEdge(w, rng, 2f);
            float[] edgeBottom = RaggedEdge(w, rng, 2f);
            var px = new Color[w * h];
            for (int yy = 0; yy < h; yy++)
                for (int xx = 0; xx < w; xx++)
                {
                    float y = h - 1 - yy;
                    bool inside = y >= edgeTop[xx] && y <= h - 1 - edgeBottom[xx];
                    px[yy * w + xx] = inside ? Color.white : Color.clear;
                }
            return MakeTexture(w, h, px);
        }

        /// <summary>손으로 그은 듯 울퉁불퉁한 먹 테두리</summary>
        private Texture2D DrawFrame(int w, int h)
        {
            var rng = new System.Random(seed + 7);
            float[] top = Wobble(w, rng, 2.2f, 6f);
            float[] bottom = Wobble(w, rng, 2.2f, h - 6f);
            float[] left = Wobble(h, rng, 1.5f, 6f);
            float[] right = Wobble(h, rng, 1.5f, w - 6f);
            float thick = 1.8f;

            var px = new Color[w * h];
            for (int yy = 0; yy < h; yy++)
            {
                for (int xx = 0; xx < w; xx++)
                {
                    float y = h - 1 - yy;
                    float d = float.MaxValue;
                    if (xx > 3 && xx < w - 4)
                        d = Mathf.Min(Mathf.Abs(y - top[xx]), Mathf.Abs(y - bottom[xx]));
                    if (y > top[xx] - 2f && y < bottom[xx] + 2f)
                        d = Mathf.Min(d, Mathf.Min(Mathf.Abs(xx - left[yy]), Mathf.Abs(xx - right[yy])));
                    // 붓 압력: 획 굵기가 조금씩 달라짐
                    float t = thick * (0.8f + 0.5f * Mathf.PerlinNoise(xx * 0.05f, yy * 0.05f + seed));
                    float a = Mathf.Clamp01(1f - (d - t) / 1.0f);
                    Color c = inkColor; c.a *= a;
                    px[yy * w + xx] = c;
                }
            }
            return MakeTexture(w, h, px);
        }

        /// <summary>붓으로 그은 붉은 + 표시</summary>
        private Texture2D DrawCross(int s)
        {
            var px = new Color[s * s];
            float m = s * 0.5f;
            for (int yy = 0; yy < s; yy++)
                for (int xx = 0; xx < s; xx++)
                {
                    float d = Mathf.Min(
                        SegDistance(xx, yy, new Vector4(3f, m + 0.5f, s - 3f, m - 0.5f)),
                        SegDistance(xx, yy, new Vector4(m - 0.5f, 3f, m + 0.5f, s - 3f)));
                    float a = Mathf.Clamp01(1f - (d - 2.2f) / 1.2f);
                    px[yy * s + xx] = new Color(1f, 1f, 1f, a);
                }
            return MakeTexture(s, s, px);
        }

        // ───────── 작은 도우미 ─────────

        private static float[] RaggedEdge(int n, System.Random rng, float amount)
        {
            var e = new float[n];
            for (int i = 0; i < n; i++) e[i] = (float)rng.NextDouble() * amount;
            return e;
        }

        /// <summary>부드럽게 흔들리는 선 (평균 base 주변으로 amp만큼)</summary>
        private static float[] Wobble(int n, System.Random rng, float amp, float baseValue)
        {
            var raw = new float[n];
            for (int i = 0; i < n; i++) raw[i] = (float)(rng.NextDouble() * 2.0 - 1.0);
            var o = new float[n];
            const int k = 12;
            for (int i = 0; i < n; i++)
            {
                float sum = 0f; int cnt = 0;
                for (int j = i - k; j <= i + k; j++) { if (j < 0 || j >= n) continue; sum += raw[j]; cnt++; }
                o[i] = baseValue + sum / Mathf.Max(1, cnt) * amp * 3f;
            }
            return o;
        }

        private static float SegDistance(float px, float py, Vector4 s)
        {
            float vx = s.z - s.x, vy = s.w - s.y;
            float wx = px - s.x, wy = py - s.y;
            float t = Mathf.Clamp01((wx * vx + wy * vy) / (vx * vx + vy * vy + 1e-6f));
            float dx = px - (s.x + t * vx), dy = py - (s.y + t * vy);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        private static Texture2D MakeTexture(int w, int h, Color[] px)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }

        private static Sprite MakeSprite(Texture2D tex)
        {
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }

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

        private static Image NewImage(string name, Transform parent, Sprite sprite)
        {
            RectTransform rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }
    }
}
