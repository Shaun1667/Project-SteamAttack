using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NGH;

namespace NMJ
{
    /// <summary>
    /// 플레이어 체력바 — 붓글씨 한 획 (NMJ 새 HUD 1번: 체력)
    ///
    ///   [命]  ●▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬▬═══≡≡--
    ///   낙관  붓을 눌러 시작한 머리 → 붓털 결이 보이는 몸통 → 먹이 마르며 붓털이 갈라지는 꼬리(비백)
    ///
    /// 획은 그림 파일이 아니라 붓털 수십 가닥을 한 가닥씩 시뮬레이션해서 만든다.
    ///   - 붓털마다 굵기·먹 양·흔들림·끝나는 자리가 다르다. 가장자리 붓털일수록 더 흔들리고 먼저 마른다.
    ///   - 먹이 떨어지는 꼬리에서는 먹이 끊겼다 이어지며 흰 결(비백)이 생긴다.
    ///   - 바깥 붓털 몇 가닥은 검은 먹으로 남아서, 붉은 먹을 자연스러운 먹 테두리가 감싼다.
    ///
    /// 체력: 검은 먹 획 위에 붉은 먹이 얹혀 있고, 체력이 빠지면 붉은 먹이 물러나 검은 먹이 드러난다.
    ///   - 붉은 먹의 끝은 붓털마다 끝나는 자리가 달라 붓 끝처럼 갈라지고, 끝으로 갈수록 듬성듬성하다.
    ///   - 맞는 순간: 바가 흔들리고, 빠진 만큼이 물에 풀린 옅은 붉은 먹으로 남았다가 천천히 걷히고,
    ///     잘린 자리에서 붉은 먹물 방울이 떨어진다.
    ///   - 체력 30% 이하: 붉은 먹과 낙관이 맥박친다. 회복하면 붉은 먹이 서서히 다시 차오른다.
    /// 왼쪽에는 붓글씨 작품처럼 붉은 낙관(도장)을 찍는다. 글자는 sealText (기본 "命").
    ///
    /// 그리기는 전용 셰이더 NMJ/UI/InkBrushBar 가 한다 (Assets/Shaders/NMJ/UI/InkBrushBar.shader).
    /// NGH.PlayerController의 공개 값(Hp, maxHp)만 읽는다. 플레이어가 없으면 숨긴다.
    /// 플레이어 프리팹(NGH_Player 등)에 붙여 둔다. 붙은 플레이어를 먼저 쓰고, 없으면 씬에서 찾는다. (증기·무기는 다음 단계에서 같은 스타일로 추가)
    /// 기존 NGH PlayerHUD와 함께 켜면 두 개가 겹치므로, 확인할 때는 기존 HUD를 끈다.
    /// </summary>
    [DisallowMultipleComponent]
    public class InkHealthBar : MonoBehaviour
    {
        [Header("연결 (비우면 자동으로 찾음)")]
        [SerializeField] private PlayerController player;
        [Tooltip("붓 획 셰이더 NMJ/UI/InkBrushBar. 비우면 에디터에서 자동 연결 (빌드에 포함되도록 씬에 저장된다)")]
        [SerializeField] private Shader brushShader;
        [Tooltip("낙관 글자 폰트. 비우면 에디터에서 ChosunCentennial_otf SDF 를 자동 연결")]
        [SerializeField] private TMP_FontAsset sealFont;

        [Header("배치 (화면 1920×1080 기준, 왼쪽 아래 기준점)")]
        [SerializeField] private Vector2 position = new Vector2(96f, 92f);
        [Tooltip("붓 획이 그려지는 영역 크기")]
        [SerializeField] private Vector2 size = new Vector2(520f, 64f);

        [Header("낙관 (붉은 도장)")]
        [SerializeField] private bool showSeal = true;
        [SerializeField] private string sealText = "命";
        [SerializeField, Range(24f, 72f)] private float sealSize = 48f;
        [SerializeField, Range(-15f, 15f)] private float sealAngle = -5f;

        [Header("붓 획 모양")]
        [Tooltip("몸통 굵기 (픽셀)")]
        [SerializeField, Range(16f, 52f)] private float thickness = 34f;
        [Tooltip("붓을 눌러 시작한 둥근 머리의 길이 (전체 대비)")]
        [SerializeField, Range(0.03f, 0.15f)] private float headLength = 0.07f;
        [Tooltip("머리가 몸통보다 굵은 정도")]
        [SerializeField, Range(1f, 1.4f)] private float headBulge = 1.15f;
        [Tooltip("붓털이 갈라지는 꼬리의 길이 (전체 대비)")]
        [SerializeField, Range(0.05f, 0.35f)] private float tailLength = 0.18f;
        [SerializeField, Range(24, 120)] private int bristleCount = 64;
        [Tooltip("붓털이 흔들리는 정도 (가장자리일수록 더 흔들림)")]
        [SerializeField, Range(0f, 2f)] private float roughness = 1f;
        [Tooltip("마른 붓 자국(비백)의 양. 꼬리에서 가장 많다")]
        [SerializeField, Range(0f, 1f)] private float dryness = 0.9f;
        [Tooltip("획 둘레에 튄 먹 점의 양")]
        [SerializeField, Range(0f, 2f)] private float splatter = 0.6f;
        [Tooltip("위·아래 가장자리가 천천히 굽이치는 정도 (픽셀)")]
        [SerializeField, Range(0f, 6f)] private float waviness = 2.2f;
        [Tooltip("오른쪽으로 갈수록 내려가는 정도 (픽셀)")]
        [SerializeField, Range(0f, 16f)] private float slant = 6f;
        [Tooltip("붉은 먹을 얹지 않고 검게 남기는 바깥 붓털의 비율 (먹 테두리 두께)")]
        [SerializeField, Range(0f, 0.4f)] private float rimBristles = 0.14f;
        [Tooltip("같은 숫자면 매번 같은 모양")]
        [SerializeField] private int seed = 3;

        [Header("색")]
        [SerializeField] private Color inkLight = new Color(0.20f, 0.19f, 0.19f, 1f);
        [SerializeField] private Color inkDark = new Color(0.03f, 0.03f, 0.04f, 1f);
        [SerializeField] private Color redLight = new Color(0.93f, 0.18f, 0.14f, 1f);
        [SerializeField] private Color redDark = new Color(0.42f, 0.02f, 0.03f, 1f);
        [Tooltip("물에 풀린 붉은 먹 (잔상). 알파 = 진하기")]
        [SerializeField] private Color trailColor = new Color(0.97f, 0.70f, 0.64f, 0.9f);
        [SerializeField] private Color sealColor = new Color(0.78f, 0.12f, 0.10f, 1f);
        [SerializeField] private Color paperColor = new Color(0.96f, 0.93f, 0.85f, 1f);
        [Tooltip("어두운 배경에서도 획이 보이도록 뒤에 까는 옅은 종이빛 번짐")]
        [SerializeField, Range(0f, 0.5f)] private float haloAlpha = 0f;

        [Header("먹물이 빠지는 연출")]
        [Tooltip("붉은 먹 끝이 붓털마다 달라지는 폭 (픽셀)")]
        [SerializeField, Range(0f, 40f)] private float tearLength = 16f;
        [Tooltip("붉은 먹 끝이 듬성듬성해지는 구간 (픽셀)")]
        [SerializeField, Range(1f, 40f)] private float fringeLength = 18f;
        [SerializeField, Min(0f)] private float trailDelay = 0.45f;
        [SerializeField, Min(0.01f)] private float trailSpeed = 0.55f;
        [Tooltip("회복할 때 붉은 먹이 차오르는 빠르기 (초당 전체 대비)")]
        [SerializeField, Min(0.05f)] private float refillSpeed = 1.2f;
        [SerializeField, Min(0f)] private float hitShake = 5f;
        [SerializeField, Range(0f, 1f)] private float lowHpRatio = 0.3f;
        [Tooltip("저체력 맥박에서 가장 어두워질 때 얼마나 어두워지는지 (0 = 맥박 없음)")]
        [SerializeField, Range(0f, 0.5f)] private float lowPulseDepth = 0.28f;
        [Tooltip("맞을 때 떨어지는 먹물 방울의 양 (0 = 없음)")]
        [SerializeField, Min(0f)] private float dripAmount = 1f;
        [SerializeField, Min(50f)] private float dripGravity = 520f;

        private const int SS = 2;   // 획 텍스처는 화면보다 2배로 그려서 가장자리를 매끄럽게 한다

        private static readonly int FillId = Shader.PropertyToID("_Fill");
        private static readonly int TrailId = Shader.PropertyToID("_Trail");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");

        private class Drip
        {
            public RectTransform rt;
            public Image img;
            public Vector2 pos, vel;
            public float age, life, width, gravityScale;
            public Color color;
            public bool active, speck;
        }

        private CanvasGroup group;
        private RectTransform barRoot;
        private RectTransform seal;
        private Material brushMat;
        private RectTransform dripLayer;
        private Sprite[] blotSprites;
        private readonly List<Drip> drips = new List<Drip>();
        private readonly List<Object> owned = new List<Object>();   // 직접 만든 텍스처·머티리얼 (파괴용)

        private float[] colCenter, colHalf;   // 화면 픽셀 단위: 각 열에서 획의 중심 높이와 반 굵기

        private float shownRatio = 1f;     // 지금 붉은 먹이 차 있는 비율
        private float trailRatio = 1f;     // 옅은 잔상이 남아 있는 비율
        private float trailHold;
        private float shakeTimer;
        private Vector2 basePos;

        // ───────── 시작 ─────────

        private void Awake()
        {
#if UNITY_EDITOR
            AutoAssign();
#endif
            if (brushShader == null) brushShader = Shader.Find("NMJ/UI/InkBrushBar");
            // 플레이어에 붙어 있으면 그 플레이어를 쓴다 (없으면 Update 에서 씬에서 찾는다)
            if (player == null) player = GetComponentInParent<PlayerController>();
            BuildUi();
        }

        private void OnDestroy()
        {
            foreach (Object o in owned) if (o != null) Destroy(o);
            owned.Clear();
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
            float target = Ratio();

            if (target < shownRatio - 0.0001f)
            {
                // 맞았다: 붉은 먹이 바로 물러나고, 흔들리고, 방울이 떨어진다
                shakeTimer = 0.25f;
                SpawnDrips(shownRatio, target);
                shownRatio = target;
            }
            else
            {
                // 회복: 붉은 먹이 서서히 차오른다
                shownRatio = Mathf.MoveTowards(shownRatio, target, refillSpeed * dt);
            }

            // 잔상: 회복은 바로 따라붙고, 피해는 잠시 머물렀다 걷힌다
            if (shownRatio >= trailRatio) { trailRatio = shownRatio; trailHold = 0f; }
            else if (trailHold < trailDelay) trailHold += dt;
            else trailRatio = Mathf.MoveTowards(trailRatio, shownRatio, trailSpeed * dt);

            // 체력이 낮으면 붉은 먹과 낙관이 맥박친다
            bool low = shownRatio > 0f && shownRatio <= lowHpRatio;
            float beat = low ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f) : 1f;     // 0 ~ 1
            float pulse = low ? 1f - lowPulseDepth * (1f - beat) : 1f;

            if (brushMat != null)
            {
                brushMat.SetFloat(FillId, shownRatio);
                brushMat.SetFloat(TrailId, trailRatio);
                brushMat.SetFloat(PulseId, pulse);
            }
            if (seal != null)
            {
                float s = low ? 1f + 0.07f * beat : 1f;
                seal.localScale = new Vector3(s, s, 1f);
            }

            // 흔들림
            Vector2 offset = Vector2.zero;
            if (shakeTimer > 0f)
            {
                shakeTimer -= dt;
                float k = hitShake * Mathf.Clamp01(shakeTimer / 0.25f);
                offset = new Vector2(Random.Range(-k, k), Random.Range(-k, k));
            }
            barRoot.anchoredPosition = basePos + offset;

            UpdateDrips(dt);
        }

        private float Ratio()
        {
            return player != null && player.maxHp > 0 ? Mathf.Clamp01((float)player.Hp / player.maxHp) : 0f;
        }

        // ───────── 먹물 방울 ─────────

        private void SpawnDrips(float from, float to)
        {
            if (dripAmount <= 0f || colCenter == null) return;

            float lost = from - to;
            int n = Mathf.Clamp(Mathf.RoundToInt((5f + lost * 50f) * dripAmount), 0, 24);
            for (int i = 0; i < n; i++)
            {
                Drip d = GetDrip();
                float x = Mathf.Clamp(Random.Range(to, from) * size.x, 8f, size.x - 8f);
                int col = Mathf.Clamp(Mathf.RoundToInt(x), 0, colCenter.Length - 1);
                // 그 자리의 획 안쪽에서 튄다
                float y = colCenter[col] + Random.Range(-0.6f, 0.6f) * colHalf[col];
                d.pos = new Vector2(x, y);
                d.age = 0f;
                d.speck = Random.value < 0.4f;
                if (d.speck)
                {
                    // 잔 튀김: 작고 옆·위로 튀었다가 금방 사라진다
                    d.vel = new Vector2(Random.Range(-90f, 90f), Random.Range(10f, 100f));
                    d.life = Random.Range(0.3f, 0.6f);
                    d.width = Random.Range(2f, 4f);
                    d.gravityScale = 0.6f;
                }
                else
                {
                    // 먹물 얼룩: 무겁게 아래로 떨어진다
                    d.vel = new Vector2(Random.Range(-25f, 25f), Random.Range(-90f, 10f));
                    d.life = Random.Range(0.5f, 1.0f);
                    d.width = Random.Range(4f, 9f);
                    d.gravityScale = 1f;
                }
                d.img.sprite = blotSprites[Random.Range(0, blotSprites.Length)];
                d.color = Color.Lerp(redDark, redLight, Random.Range(0.25f, 0.85f));
                d.active = true;
                d.rt.gameObject.SetActive(true);
            }
        }

        private Drip GetDrip()
        {
            foreach (Drip d in drips) if (!d.active) return d;

            var go = new GameObject("Drip", typeof(RectTransform));
            go.transform.SetParent(dripLayer, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.AddComponent<Image>();
            img.sprite = blotSprites[0];
            img.raycastTarget = false;
            var d2 = new Drip { rt = rt, img = img };
            drips.Add(d2);
            return d2;
        }

        private void UpdateDrips(float dt)
        {
            foreach (Drip d in drips)
            {
                if (!d.active) continue;
                d.age += dt;
                if (d.age >= d.life) { d.active = false; d.rt.gameObject.SetActive(false); continue; }

                d.vel.y -= dripGravity * d.gravityScale * dt;
                d.pos += d.vel * dt;
                float a = 1f - d.age / d.life;
                d.rt.anchoredPosition = d.pos;
                // 빠르게 떨어질수록 세로로 조금 늘어난다
                float stretch = 1f + Mathf.Clamp01(-d.vel.y / 600f) * 0.7f;
                d.rt.sizeDelta = new Vector2(d.width, d.width * stretch);
                d.img.color = new Color(d.color.r, d.color.g, d.color.b, a * a);
            }
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

            barRoot = NewRect("HealthBar", canvasGo.transform);
            barRoot.anchorMin = barRoot.anchorMax = Vector2.zero;
            barRoot.pivot = Vector2.zero;
            barRoot.sizeDelta = size;
            basePos = position;
            barRoot.anchoredPosition = basePos;

            Texture2D strokeTex, haloTex;
            BuildStroke(out strokeTex, out haloTex);

            // 1) 종이빛 번짐 (어두운 배경에서도 획이 묻히지 않게)
            if (haloAlpha > 0f)
            {
                RawImage halo = NewRaw("Halo", barRoot, haloTex);
                halo.color = new Color(paperColor.r, paperColor.g, paperColor.b, haloAlpha);
            }

            // 2) 붓 획 (검은 먹 + 붉은 먹 + 잔상을 셰이더 하나로)
            RawImage stroke = NewRaw("BrushStroke", barRoot, strokeTex);
            if (brushShader != null)
            {
                brushMat = new Material(brushShader) { name = "NMJ_InkBrushBar (runtime)", hideFlags = HideFlags.DontSave };
                owned.Add(brushMat);
                brushMat.SetColor("_InkLight", inkLight);
                brushMat.SetColor("_InkDark", inkDark);
                brushMat.SetColor("_RedLight", redLight);
                brushMat.SetColor("_RedDark", redDark);
                brushMat.SetColor("_TrailColor", trailColor);
                brushMat.SetFloat("_TearLen", tearLength / size.x);
                brushMat.SetFloat("_FringeLen", fringeLength / size.x);
                brushMat.SetFloat("_Soft", 1.2f / size.x);
                brushMat.SetFloat(FillId, 1f);
                brushMat.SetFloat(TrailId, 1f);
                brushMat.SetFloat(PulseId, 1f);
                stroke.material = brushMat;
            }
            else
            {
                Debug.LogError("[NMJ] InkHealthBar: 셰이더 NMJ/UI/InkBrushBar 를 찾지 못했다. 체력이 표시되지 않는다.", this);
            }

            // 3) 낙관 (붉은 도장 + 새긴 글자)
            if (showSeal) BuildSeal();

            // 4) 먹물 방울 (바 밖으로도 떨어지도록 맨 위)
            dripLayer = NewRect("Drips", barRoot);
            blotSprites = new[] { MakeSprite(DrawBlot(24, 0)), MakeSprite(DrawBlot(24, 1)), MakeSprite(DrawBlot(24, 2)) };
        }

        // ───────── 붓 획 만들기 (붓털 시뮬레이션) ─────────

        /// <summary>
        /// 붓털을 한 가닥씩 종이 위로 끌어서 획을 만든다.
        /// 결과는 셰이더용 데이터 텍스처(A=먹, R=농도, G=붓털 번호, B=붉은 먹 자리)와 뒤에 까는 번짐 텍스처.
        /// </summary>
        private void BuildStroke(out Texture2D data, out Texture2D halo)
        {
            int dispW = Mathf.RoundToInt(size.x), dispH = Mathf.RoundToInt(size.y);
            int W = dispW * SS, H = dispH * SS;
            var cover = new float[W * H];
            var redCover = new float[W * H];
            var densSum = new float[W * H];
            var weight = new float[W * H];
            var best = new float[W * H];
            var hashAt = new float[W * H];
            var pressAt = new float[W];

            var rng = new System.Random(seed);
            float Rand() => (float)rng.NextDouble();

            float x0 = W * 0.03f, x1 = W * 0.985f, len = x1 - x0;
            float p1 = Rand() * Mathf.PI * 2f, p2 = Rand() * Mathf.PI * 2f;

            // 붓털 준비
            int N = Mathf.Max(8, bristleCount);
            var bu = new float[N];     // 붓 안에서의 위치 (-0.5 ~ 0.5)
            var bw = new float[N];     // 굵기 (텍스처 픽셀)
            var bload = new float[N];  // 먹 양
            var bend = new float[N];   // 먹이 다 떨어지는 x
            var bhash = new float[N];
            var btone = new float[N];  // 붓털마다 먹 색이 조금씩 달라 결이 보인다
            var bclump = new int[N];   // 붓털 다발: 한 다발은 함께 말라서 비백이 길게 비어 보인다
            for (int i = 0; i < N; i++)
            {
                float u = ((i + 0.5f) / N - 0.5f) + (Rand() - 0.5f) * 0.6f / N;
                float e = Mathf.Abs(u) * 2f;                                  // 0 = 가운데, 1 = 가장자리
                bu[i] = u;
                bw[i] = (1.4f + 1.6f * Rand()) * SS;
                bload[i] = Mathf.Clamp01(0.8f + 0.2f * Rand() - 0.18f * e);
                // 가장자리 붓털이 먼저 마르지만 가운데도 들쭉날쭉 → 꼬리가 뾰족하지 않고 넓게 갈라진다
                bend[i] = x1 - len * tailLength * (0.10f + 0.55f * Mathf.Pow(e, 1.2f) + 0.35f * Rand());
                // 몇 가닥은 꼬리 구간 중간에서 일찍 끊긴다
                if (Rand() < 0.15f) bend[i] -= len * tailLength * 0.8f * Rand();
                bhash[i] = Rand();
                btone[i] = Rand();
                bclump[i] = Mathf.FloorToInt((u + 0.5f) * 9f + (Rand() - 0.5f) * 0.8f);
            }

            colCenter = new float[dispW];
            colHalf = new float[dispW];
            float rimLimit = 1f - rimBristles;

            for (int x = Mathf.FloorToInt(x0); x < W; x++)
            {
                float t = (x - x0) / len;
                if (t > 1.02f) break;

                // 획의 중심선: 천천히 굽이치고 오른쪽으로 갈수록 살짝 내려간다
                float wave = waviness * SS * (0.6f * Mathf.Sin(t * Mathf.PI * 2f * 1.1f + p1) + 0.3f * Mathf.Sin(t * Mathf.PI * 2f * 2.7f + p2));
                float drift = -slant * SS * (t - 0.5f);
                float c = H * 0.5f + wave + drift;

                // 굵기: 머리(붓을 눌러 둥글게 시작) → 몸통(꼬리로 갈수록 아주 조금 가늘어짐)
                float half = thickness * SS * 0.5f * (1.06f - 0.10f * t);
                if (t < headLength)
                {
                    float k = (headLength - t) / headLength;
                    half *= headBulge * Mathf.Sqrt(Mathf.Max(0f, 1f - k * k));
                }
                else if (t < headLength * 2.2f)
                {
                    half *= Mathf.Lerp(headBulge, 1f, (t - headLength) / (headLength * 1.2f));
                }

                // 붓 누르는 힘이 천천히 변하며 굵기가 살짝 출렁인다
                half *= 1f + 0.05f * Mathf.Sin(t * Mathf.PI * 2f * 1.7f + p2 * 0.5f);

                // 꼬리: 붓털이 벌어지고 붓 누르는 힘이 빠진다
                float tailK = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - tailLength, 1f, t));
                float spread = 1f + 0.35f * tailK;
                float pressure = 1f - 0.5f * tailK;
                // 먹이 마르기 시작하는 정도: 획 후반부터 점점
                float dryRamp = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1.0f, t));
                // 붓을 누른 머리는 먹이 더 진하다
                pressAt[x] = 0.2f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, headLength * 2.5f, t)));

                int dc = Mathf.Clamp(x / SS, 0, dispW - 1);
                colCenter[dc] = c / SS;
                colHalf[dc] = half * spread / SS;

                for (int i = 0; i < N; i++)
                {
                    if (x > bend[i]) continue;
                    float e = Mathf.Abs(bu[i]) * 2f;

                    // 붓털의 흔들림: 느린 굽이 + 잔떨림 (가장자리일수록 크게)
                    float wob = (Mathf.PerlinNoise(x * 0.012f + i * 7.13f, i * 1.31f) - 0.5f) * 2f
                                * (0.4f + 2.2f * e * e) * roughness * SS
                              + (Mathf.PerlinNoise(x * 0.09f + i * 2.7f, i * 0.53f + 20f) - 0.5f) * 1.6f * e * roughness * SS;
                    float y = c + bu[i] * 2f * half * spread + wob;
                    float wdt = bw[i] * pressure * (t < headLength * 1.5f ? 1.15f : 1f);

                    // 먹이 떨어져 가는 정도
                    float fade = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(bend[i] - len * 0.07f, bend[i], x));
                    float ink = bload[i] * fade * (1f - 0.4f * dryRamp);

                    // 비백(飛白): 붓털 다발이 함께 말라 길게 비는 결 + 붓털 하나하나의 잔 끊김
                    float dnClump = Mathf.PerlinNoise(x * 0.010f + bclump[i] * 5.1f, bclump[i] * 0.37f + 9f);
                    float dnFine = Mathf.PerlinNoise(x * 0.06f + i * 3.31f, i * 0.77f + 50f);
                    float dn = Mathf.Clamp01((0.7f * dnClump + 0.3f * dnFine - 0.35f) * 2.5f);
                    float dryT = dryness * (0.1f + 1.6f * dryRamp);
                    float a = Mathf.Clamp01((ink - dn * dryT - 0.05f) * 6f);
                    // 가장자리 붓털은 군데군데 끊겨 거친 가장자리를 만든다
                    if (e > 0.82f && Mathf.PerlinNoise(x * 0.08f + i, 3.3f) > 0.62f) a *= 0.2f;
                    if (a <= 0.001f) continue;

                    float densVal = 0.16f + 0.30f * e * e + 0.22f * (1f - bload[i]) + (btone[i] - 0.5f) * 0.28f;
                    bool redOk = e < rimLimit;

                    int yMin = Mathf.Max(0, Mathf.FloorToInt(y - wdt * 0.5f - 1f));
                    int yMax = Mathf.Min(H - 1, Mathf.CeilToInt(y + wdt * 0.5f + 1f));
                    for (int py = yMin; py <= yMax; py++)
                    {
                        float cv = Mathf.Clamp01(wdt * 0.5f + 0.5f - Mathf.Abs(py + 0.5f - y)) * a;
                        if (cv <= 0f) continue;
                        int idx = py * W + x;
                        cover[idx] = 1f - (1f - cover[idx]) * (1f - cv);
                        if (redOk) redCover[idx] = 1f - (1f - redCover[idx]) * (1f - cv);
                        densSum[idx] += cv * densVal;
                        weight[idx] += cv;
                        if (cv > best[idx]) { best[idx] = cv; hashAt[idx] = bhash[i]; }
                    }
                }
            }

            // 붓을 떼며 튄 먹 점 (꼬리 끝 근처, 획 방향으로)
            int dots = Mathf.RoundToInt(4f * splatter);
            for (int k = 0; k < dots; k++)
            {
                float t = 0.9f + Rand() * 0.10f;
                int dx = Mathf.Clamp(Mathf.RoundToInt(x0 + t * len), 0, W - 1);
                int dcol = Mathf.Clamp(dx / SS, 0, dispW - 1);
                float cy = colCenter[Mathf.Min(dcol, dispW - 1)] * SS + (Rand() - 0.5f) * colHalf[Mathf.Max(0, dcol - 4)] * SS * 2.2f;
                float r = (1.0f + 1.4f * Rand()) * SS;
                // 붓을 떼며 튄 먹은 획 방향으로 길쭉하다
                StampDot(cover, densSum, weight, W, H, dx, cy, r * (1.6f + 1.2f * Rand()), r * 0.75f);
            }

            // 셰이더용 데이터 텍스처로 묶기
            var px = new Color32[W * H];
            for (int py = 0; py < H; py++)
            {
                for (int x = 0; x < W; x++)
                {
                    int idx = py * W + x;
                    float cv = cover[idx];
                    if (cv <= 0.001f) { px[idx] = new Color32(0, 0, 0, 0); continue; }

                    float d = weight[idx] > 0f ? densSum[idx] / weight[idx] : 0.4f;
                    d += pressAt[x];
                    d += (Mathf.PerlinNoise(x * 0.006f + 11f, py * 0.5f) - 0.5f) * 0.12f;   // 붓털 결
                    px[idx] = new Color32(
                        (byte)(Mathf.Clamp01(d) * 255f),
                        (byte)(hashAt[idx] * 255f),
                        (byte)(Mathf.Clamp01(redCover[idx]) * 255f),
                        (byte)(Mathf.Clamp01(cv) * 255f));
                }
            }
            data = new Texture2D(W, H, TextureFormat.RGBA32, true, true)
            {
                name = "NMJ_InkStrokeData",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            data.SetPixels32(px);
            data.Apply(true, true);
            owned.Add(data);

            // 뒤에 까는 번짐: 획 모양을 넓게 흐린 것
            float[] blurred = BoxBlur(cover, W, H, 7 * SS);
            blurred = BoxBlur(blurred, W, H, 5 * SS);
            var hp = new Color32[W * H];
            for (int i = 0; i < hp.Length; i++)
                hp[i] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(blurred[i] * 1.6f) * 255f));
            halo = new Texture2D(W, H, TextureFormat.RGBA32, true)
            {
                name = "NMJ_InkStrokeHalo",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            halo.SetPixels32(hp);
            halo.Apply(true, true);
            owned.Add(halo);
        }

        /// <summary>타원 먹 점 (rx = 가로 반지름, ry = 세로 반지름)</summary>
        private static void StampDot(float[] cover, float[] densSum, float[] weight, int W, int H, float cx, float cy, float rx, float ry)
        {
            int x0 = Mathf.Max(0, Mathf.FloorToInt(cx - rx - 1f)), x1 = Mathf.Min(W - 1, Mathf.CeilToInt(cx + rx + 1f));
            int y0 = Mathf.Max(0, Mathf.FloorToInt(cy - ry - 1f)), y1 = Mathf.Min(H - 1, Mathf.CeilToInt(cy + ry + 1f));
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float dx = (x + 0.5f - cx) / rx, dy = (y + 0.5f - cy) / ry;
                    float cv = Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * ry + 0.5f);
                    if (cv <= 0f) continue;
                    int idx = y * W + x;
                    cover[idx] = 1f - (1f - cover[idx]) * (1f - cv);
                    densSum[idx] += cv * 0.45f;
                    weight[idx] += cv;
                }
        }

        /// <summary>가로·세로로 나눠 하는 상자 흐림 (반경 r)</summary>
        private static float[] BoxBlur(float[] src, int W, int H, int r)
        {
            var tmp = new float[W * H];
            var dst = new float[W * H];
            float inv = 1f / (2 * r + 1);
            for (int y = 0; y < H; y++)
            {
                float acc = 0f;
                for (int x = -r; x <= r; x++) acc += src[y * W + Mathf.Clamp(x, 0, W - 1)];
                for (int x = 0; x < W; x++)
                {
                    tmp[y * W + x] = acc * inv;
                    acc += src[y * W + Mathf.Min(x + r + 1, W - 1)] - src[y * W + Mathf.Max(x - r, 0)];
                }
            }
            for (int x = 0; x < W; x++)
            {
                float acc = 0f;
                for (int y = -r; y <= r; y++) acc += tmp[Mathf.Clamp(y, 0, H - 1) * W + x];
                for (int y = 0; y < H; y++)
                {
                    dst[y * W + x] = acc * inv;
                    acc += tmp[Mathf.Min(y + r + 1, H - 1) * W + x] - tmp[Mathf.Max(y - r, 0) * W + x];
                }
            }
            return dst;
        }

        // ───────── 낙관 (붉은 도장) ─────────

        private void BuildSeal()
        {
            int S = Mathf.RoundToInt(sealSize * SS);
            Texture2D tex = DrawSeal(S);

            seal = NewRect("Seal", barRoot);
            seal.anchorMin = seal.anchorMax = new Vector2(0f, 0f);
            seal.pivot = new Vector2(0.5f, 0.5f);
            seal.sizeDelta = new Vector2(sealSize, sealSize);
            // 획 머리에 살짝 걸치게 찍는다
            int headCol = Mathf.Clamp(Mathf.RoundToInt(size.x * 0.03f), 0, colCenter.Length - 1);
            seal.anchoredPosition = new Vector2(-sealSize * 0.32f, colCenter[headCol] + 1f);
            seal.localRotation = Quaternion.Euler(0f, 0f, sealAngle);
            var img = seal.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;

            if (sealFont != null && !string.IsNullOrEmpty(sealText))
            {
                RectTransform tr = NewRect("SealText", seal);
                var t = tr.gameObject.AddComponent<TextMeshProUGUI>();
                t.font = sealFont;
                t.text = sealText;
                t.fontSize = sealSize * 0.62f;
                t.alignment = TextAlignmentOptions.Center;
                t.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.94f);
                t.raycastTarget = false;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                tr.anchoredPosition = new Vector2(0f, -sealSize * 0.02f);
            }
        }

        /// <summary>붉은 도장: 손으로 찍은 듯 거친 둘레, 군데군데 종이가 비치는 인주, 새긴 테두리선</summary>
        private Texture2D DrawSeal(int S)
        {
            var px = new Color32[S * S];
            float cx = S * 0.5f;
            float inset = S * 0.06f, corner = S * 0.10f;
            float hx = (S - 2f * inset) * 0.5f - corner;

            for (int y = 0; y < S; y++)
            {
                for (int x = 0; x < S; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - cx) - hx;
                    float qy = Mathf.Abs(y + 0.5f - cx) - hx;
                    float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                                    + Mathf.Min(Mathf.Max(qx, qy), 0f) - corner;
                    outside += (Mathf.PerlinNoise(x * 0.18f + seed, y * 0.18f) - 0.5f) * S * 0.05f;   // 거친 둘레
                    float a = Mathf.Clamp01(0.5f - outside);
                    if (a <= 0f) { px[y * S + x] = new Color32(0, 0, 0, 0); continue; }

                    // 안쪽에 새긴 테두리선 (군데군데 끊김)
                    float depth = -outside;
                    float ring = Mathf.Clamp01(1f - Mathf.Abs(depth - S * 0.085f) / (S * 0.022f));
                    if (Mathf.PerlinNoise(x * 0.12f, y * 0.12f + 40f) > 0.68f) ring = 0f;

                    // 인주가 고르지 않고, 종이가 비치는 작은 틈
                    float inkN = Mathf.PerlinNoise(x * 0.06f + 10f, y * 0.06f);
                    float speck = Mathf.PerlinNoise(x * 0.45f + 5f, y * 0.45f + 9f);
                    float paperMix = Mathf.Max(ring, speck > 0.74f ? (speck - 0.74f) / 0.26f * 0.9f : 0f);

                    Color ink = sealColor * (0.88f + 0.2f * inkN);
                    Color c = Color.Lerp(ink, paperColor, paperMix);
                    c.a = a;
                    px[y * S + x] = c;
                }
            }

            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true)
            {
                name = "NMJ_Seal",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            owned.Add(tex);
            return tex;
        }

        /// <summary>먹물 얼룩: 둘레가 불규칙하게 번진 둥근 먹 점 (모양 3가지)</summary>
        private Texture2D DrawBlot(int s, int variant)
        {
            var px = new Color[s * s];
            float c = (s - 1) * 0.5f;
            float r = s * 0.36f;
            float off = seed * 3.7f + variant * 11.3f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x - c, dy = y - c;
                    float ang = Mathf.Atan2(dy, dx);
                    // 둘레가 각도에 따라 들쭉날쭉하게 번진다
                    float edge = r * (1f + 0.22f * (Mathf.PerlinNoise(Mathf.Cos(ang) * 1.6f + off, Mathf.Sin(ang) * 1.6f + off) - 0.5f) * 2f);
                    float inside = edge - Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(inside * 0.8f + 0.5f);
                    // 가운데가 조금 옅고 둘레에 먹이 고인다
                    float rim = Mathf.Clamp01(1f - inside / Mathf.Max(1f, edge * 0.5f));
                    float shade = Mathf.Lerp(0.85f, 1f, rim);
                    px[y * s + x] = new Color(shade, shade, shade, a);
                }
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels(px);
            tex.Apply(false, true);
            owned.Add(tex);
            return tex;
        }

        // ───────── 작은 도우미 ─────────

        private Sprite MakeSprite(Texture2D tex)
        {
            var s = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
            s.hideFlags = HideFlags.DontSave;
            owned.Add(s);
            return s;
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

        private static RawImage NewRaw(string name, Transform parent, Texture tex)
        {
            RectTransform rt = NewRect(name, parent);
            var img = rt.gameObject.AddComponent<RawImage>();
            img.texture = tex;
            img.raycastTarget = false;
            return img;
        }

#if UNITY_EDITOR
        private void AutoAssign()
        {
            if (brushShader == null)
            {
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("InkBrushBar t:Shader"))
                {
                    brushShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(UnityEditor.AssetDatabase.GUIDToAssetPath(guid));
                    if (brushShader != null) break;
                }
            }
            if (sealFont == null)
            {
                foreach (string guid in UnityEditor.AssetDatabase.FindAssets("ChosunCentennial_otf SDF t:TMP_FontAsset"))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (path.Contains("Fallback")) continue;
                    sealFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (sealFont != null) break;
                }
            }
        }

        private void OnValidate()
        {
            AutoAssign();
        }
#endif
    }
}
