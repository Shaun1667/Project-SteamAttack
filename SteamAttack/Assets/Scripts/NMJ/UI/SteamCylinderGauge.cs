using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 증기 게이지 — 놋쇠 띠 두른 쇠 실린더 + 유리창 안 흰 증기 (NMJ 새 HUD 2번: 증기)
    ///
    ///       銃      強                 逆          ┳  ← 밸브: 증기를 쓰면 흰 증기가 뿜어져 나감
    ///    (◔)═[▓▓▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░]═╯
    ///     ↑      ↑ 유리창 안 흰 증기(천천히 일렁임), 끝은 부드럽게 흩어짐
    ///   압력계(바늘이 잔량을 가리킴)
    ///
    /// - 눈금 3개 (기획 v4, 10/15 회의 임시값): 銃(화승총 발사 5) · 強(강공격 30) · 逆(시간 역행 70).
    ///   잔량이 그 이상이면 금색, 모자라면 회색. 증기발도는 강공격에 통합돼 눈금 없음.
    /// - YPH.SteamTank의 공개 값·이벤트만 사용한다. 탱크는 부모(플레이어) → 씬 순서로 찾는다.
    ///     OnConsumed      → 쓴 양에 비례해 밸브에서 증기 분출 (50 이상이면 이음새에서도 샘)
    ///     OnConsumeFailed → 게이지가 덜컹 흔들리고 테두리가 붉게 번쩍, 김 빠지는 작은 분출
    /// - 가장 싼 기술(화승총)도 못 쓰는 잔량이면 테두리가 붉게 맥박친다.
    /// - 그림은 실행할 때 코드로 그린다 (텍스처 파일 없음). InkHealthBar 바로 아래에 오도록 배치.
    ///
    /// 플레이어(YPH_HeroPlayerIntegration 안 NGH_Player)에 InkHealthBar와 함께 붙어 있다.
    /// Test Keys (숫자패드 — F키는 퀘스트 디버그와 겹침): 1 = 화승총, 2 = 강공격, 3 = 시간 역행 만큼 쓰기, 0 = 가득 채우기
    /// </summary>
    [DisallowMultipleComponent]
    public class SteamCylinderGauge : MonoBehaviour
    {
        [Header("연결 (비우면 씬에서 자동으로 찾음)")]
        [SerializeField] private SteamTank tank;
        [Tooltip("눈금 글자 폰트. 비우면 에디터에서 ChosunCentennial을 자동으로 넣음")]
        [SerializeField] private TMP_FontAsset font;

        [Header("배치 (화면 1920×1080 기준, 왼쪽 아래 기준점)")]
        [Tooltip("압력계가 체력바 낙관 아래, 실린더가 체력 획 아래에 오는 자리")]
        [SerializeField] private Vector2 position = new Vector2(60f, 22f);
        [Tooltip("게이지 전체 크기 (압력계 포함)")]
        [SerializeField] private Vector2 size = new Vector2(540f, 58f);

        [Header("눈금 (기획 v4 증기 비용 — 임시값, 플레이 피드백 후 조정)")]
        [SerializeField] private float gunCost = 5f;
        [SerializeField] private float strongCost = 30f;
        [SerializeField] private float rewindCost = 70f;
        [SerializeField] private string gunLabel = "銃";
        [SerializeField] private string strongLabel = "強";
        [SerializeField] private string rewindLabel = "逆";

        [Header("색")]
        [SerializeField] private Color ironColor = new Color(0.17f, 0.15f, 0.14f, 1f);
        [SerializeField] private Color brassColor = new Color(0.62f, 0.44f, 0.19f, 1f);
        [SerializeField] private Color inkColor = new Color(0.05f, 0.05f, 0.06f, 1f);
        [SerializeField] private Color glassColor = new Color(0.10f, 0.11f, 0.12f, 1f);
        [SerializeField] private Color steamColor = new Color(0.95f, 0.95f, 0.93f, 1f);
        [SerializeField] private Color tickOnColor = new Color(0.94f, 0.73f, 0.25f, 1f);
        [SerializeField] private Color tickOffColor = new Color(0.43f, 0.38f, 0.32f, 1f);
        [SerializeField] private Color warnColor = new Color(0.85f, 0.15f, 0.10f, 1f);

        [Header("연출")]
        [Tooltip("증기가 줄어드는 속도 (비율/초)")]
        [SerializeField, Min(0.1f)] private float drainSpeed = 1.6f;
        [Tooltip("증기가 차오르는 속도 (비율/초)")]
        [SerializeField, Min(0.1f)] private float refillSpeed = 0.8f;
        [Tooltip("유리창 안 증기가 흐르는 속도")]
        [SerializeField] private float swirlSpeed = 0.04f;
        [Tooltip("증기 10을 쓸 때 나오는 김 덩어리 수 (많이 쓸수록 비례해서 늘어남)")]
        [SerializeField, Min(1)] private int puffsPer10 = 2;
        [SerializeField, Min(0f)] private float failShake = 6f;
        [SerializeField] private int seed = 5;
        [SerializeField] private bool testKeys = true;

        // 내부 배치 (게이지 로컬 좌표, 왼쪽 아래 0,0)
        private const float DialRadius = 27f;
        private const float BodyLeft = 34f;
        private const float BodyPadY = 8f;
        private const float GlassInsetLeft = 26f;
        private const float GlassInsetRight = 32f;
        private const float GlassInsetY = 9f;

        private class Puff
        {
            public RectTransform rt;
            public Image img;
            public Vector2 vel;
            public float life, age, size, alpha;
            public bool active;
        }

        private class Tick
        {
            public float cost;
            public Image mark;
            public TMP_Text label;
        }

        private CanvasGroup group;
        private RectTransform root;
        private RectTransform steamMask;
        private RawImage steam;
        private RectTransform frontWisp;
        private Image frontWispImg;
        private RectTransform needle;
        private Image glow;
        private Vector2 valveTop;
        private Vector2 seamPoint;
        private float glassWidth;
        private readonly List<Tick> ticks = new List<Tick>();
        private readonly List<Puff> puffs = new List<Puff>();
        private Sprite puffSprite;

        private float shown = -1f;
        private float shakeTimer;
        private float flashTimer;
        private float pendingPuffs;
        private float pendingStrength;
        private float pendingTimer;
        private bool subscribed;
        private float testPressure = 100f;   // 탱크가 없을 때 테스트용

        // ───────── 시작 ─────────

        private void Awake()
        {
            FindTank();
            BuildUi();
        }

        // 플레이어에 붙어 있으면 그 플레이어의 탱크를 먼저 쓴다 (씬에 탱크가 여럿일 수 있음)
        private void FindTank()
        {
            if (tank != null) return;
            tank = GetComponentInParent<SteamTank>();
            if (tank == null) tank = transform.root.GetComponentInChildren<SteamTank>(true);
            if (tank == null) tank = FindAnyObjectByType<SteamTank>();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        // 코드로 만든 텍스처·스프라이트는 직접 지워야 메모리에 남지 않는다
        private void OnDestroy()
        {
            if (root == null) return;
            var done = new HashSet<Object>();
            foreach (Image img in root.GetComponentsInChildren<Image>(true))
            {
                if (img.sprite == null || !done.Add(img.sprite)) continue;
                if (done.Add(img.sprite.texture)) Destroy(img.sprite.texture);
                Destroy(img.sprite);
            }
            if (steam != null && steam.texture != null) Destroy(steam.texture);
        }

        private void Update()
        {
            float dt = Time.unscaledDeltaTime;

            if (tank == null) FindTank();
            if (tank != null && !subscribed) Subscribe();

            HandleTestKeys();

            float max = tank != null ? tank.MaxPressure : 100f;
            float current = tank != null ? tank.CurrentPressure : testPressure;
            float target = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            group.alpha = 1f;

            // 잔량 표시: 줄 때는 빠르게, 찰 때는 천천히
            if (shown < 0f) shown = target;
            shown = Mathf.MoveTowards(shown, target, (target < shown ? drainSpeed : refillSpeed) * dt);

            // 유리창 안 증기
            steamMask.sizeDelta = new Vector2(glassWidth * shown, steamMask.sizeDelta.y);
            Rect uv = steam.uvRect;
            uv.x = Mathf.Repeat(uv.x + swirlSpeed * dt, 1f);
            steam.uvRect = uv;
            float bob = Mathf.Sin(Time.unscaledTime * 3.1f) * 2f;
            frontWisp.anchoredPosition = new Vector2(glassWidth * shown - 2f, bob * 0.5f);
            frontWisp.localScale = Vector3.one * (0.9f + 0.12f * Mathf.Sin(Time.unscaledTime * 2.3f));
            frontWispImg.color = new Color(steamColor.r, steamColor.g, steamColor.b, shown > 0.01f ? 0.55f : 0f);

            // 압력계 바늘: 빈 상태 = 왼쪽 아래(135°), 가득 = 오른쪽 아래(-135°)
            float wobble = shown > 0.01f ? Mathf.Sin(Time.unscaledTime * 23f) * 0.6f : 0f;
            needle.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(135f, -135f, shown) + wobble);

            // 눈금 색
            foreach (Tick t in ticks)
            {
                bool on = current + 0.0001f >= t.cost;
                Color c = on ? tickOnColor : tickOffColor;
                t.mark.color = c;
                t.label.color = c;
            }

            // 경고: 가장 싼 기술도 못 쓰면 맥박 / 실패하면 번쩍
            float minCost = Mathf.Min(gunCost, Mathf.Min(strongCost, rewindCost));
            float pulse = current + 0.0001f < minCost ? 0.35f + 0.35f * Mathf.Sin(Time.unscaledTime * 6f) : 0f;
            if (flashTimer > 0f) { flashTimer -= dt; pulse = Mathf.Max(pulse, flashTimer / 0.35f); }
            glow.color = new Color(warnColor.r, warnColor.g, warnColor.b, Mathf.Clamp01(pulse));

            // 흔들림
            Vector2 offset = Vector2.zero;
            if (shakeTimer > 0f)
            {
                shakeTimer -= dt;
                float s = failShake * Mathf.Clamp01(shakeTimer / 0.3f);
                offset = new Vector2(Random.Range(-s, s), Random.Range(-s * 0.5f, s * 0.5f));
            }
            root.anchoredPosition = position + offset;

            UpdatePuffs(dt);
        }

        // ───────── 탱크 이벤트 ─────────

        private void Subscribe()
        {
            tank.OnConsumed += HandleConsumed;
            tank.OnConsumeFailed += HandleConsumeFailed;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (subscribed && tank != null)
            {
                tank.OnConsumed -= HandleConsumed;
                tank.OnConsumeFailed -= HandleConsumeFailed;
            }
            subscribed = false;
        }

        private void HandleConsumed(float amount)
        {
            // 쓴 양만큼 0.3초 동안 밸브에서 김을 뿜는다
            float strength = Mathf.Clamp01(amount / 100f);
            pendingPuffs += Mathf.Max(2f, amount / 10f * puffsPer10);
            pendingStrength = Mathf.Max(pendingStrength, strength);
            pendingTimer = 0.3f + 0.4f * strength;

            // 크게 쓰면 왼쪽 이음새에서도 샌다
            if (amount >= 50f)
                for (int i = 0; i < 3; i++) SpawnPuff(seamPoint, new Vector2(Random.Range(-30f, -10f), Random.Range(30f, 60f)), 14f, 0.7f, 0.5f);
        }

        private void HandleConsumeFailed(float amount)
        {
            shakeTimer = 0.3f;
            flashTimer = 0.35f;
            for (int i = 0; i < 2; i++) SpawnPuff(valveTop, new Vector2(Random.Range(5f, 20f), Random.Range(20f, 40f)), 8f, 0.5f, 0.35f);
        }

        private void HandleTestKeys()
        {
            if (!testKeys || Keyboard.current == null) return;
            if (Keyboard.current.numpad1Key.wasPressedThisFrame) TestConsume(gunCost);
            if (Keyboard.current.numpad2Key.wasPressedThisFrame) TestConsume(strongCost);
            if (Keyboard.current.numpad3Key.wasPressedThisFrame) TestConsume(rewindCost);
            if (Keyboard.current.numpad0Key.wasPressedThisFrame)
            {
                if (tank != null) tank.Refill(tank.MaxPressure);
                else testPressure = 100f;
            }
        }

        private void TestConsume(float amount)
        {
            if (tank != null) { tank.TryConsume(amount); return; }
            if (testPressure + 0.0001f < amount) { HandleConsumeFailed(amount); return; }
            testPressure -= amount;
            HandleConsumed(amount);
        }

        // ───────── 김 분출 ─────────

        private void UpdatePuffs(float dt)
        {
            if (pendingTimer > 0f && pendingPuffs > 0f)
            {
                float rate = pendingPuffs / Mathf.Max(pendingTimer, dt);
                float n = Mathf.Min(pendingPuffs, rate * dt);
                pendingPuffs -= n;
                pendingTimer -= dt;
                int count = Mathf.FloorToInt(n + Random.value);
                float s = pendingStrength;
                for (int i = 0; i < count; i++)
                {
                    Vector2 vel = new Vector2(Random.Range(20f, 55f), Random.Range(70f, 130f)) * (0.8f + 0.6f * s);
                    SpawnPuff(valveTop, vel, Mathf.Lerp(12f, 26f, s), Random.Range(0.9f, 1.6f), Mathf.Lerp(0.6f, 0.95f, s));
                }
                if (pendingTimer <= 0f) { pendingPuffs = 0f; pendingStrength = 0f; }
            }

            foreach (Puff p in puffs)
            {
                if (!p.active) continue;
                p.age += dt;
                float t = p.age / p.life;
                if (t >= 1f) { p.active = false; p.rt.gameObject.SetActive(false); continue; }
                p.vel *= 1f - 1.2f * dt;          // 공기 저항
                p.vel.x += 18f * dt;              // 바람에 살짝 밀림
                p.rt.anchoredPosition += p.vel * dt;
                float sc = p.size * Mathf.Lerp(0.5f, 2.2f, Mathf.Sqrt(t));
                p.rt.sizeDelta = new Vector2(sc, sc);
                Color c = p.img.color;
                c.a = p.alpha * (1f - t) * Mathf.Clamp01(t * 8f);   // 나오자마자 진해졌다 서서히 흩어짐
                p.img.color = c;
            }
        }

        private void SpawnPuff(Vector2 at, Vector2 vel, float size, float life, float alpha)
        {
            Puff p = null;
            foreach (Puff q in puffs) if (!q.active) { p = q; break; }
            if (p == null)
            {
                if (puffs.Count >= 48) return;
                p = new Puff();
                p.img = NewImage("Puff", root, puffSprite);
                p.rt = p.img.rectTransform;
                p.rt.anchorMin = p.rt.anchorMax = Vector2.zero;
                puffs.Add(p);
            }
            p.active = true;
            p.age = 0f;
            p.life = life;
            p.size = size;
            p.vel = vel;
            p.rt.gameObject.SetActive(true);
            p.rt.SetAsLastSibling();
            p.rt.anchoredPosition = at + new Vector2(Random.Range(-3f, 3f), Random.Range(-2f, 2f));
            p.rt.sizeDelta = new Vector2(size * 0.5f, size * 0.5f);
            p.rt.localEulerAngles = new Vector3(0f, 0f, Random.Range(0f, 360f));
            p.alpha = alpha;
            p.img.color = new Color(steamColor.r, steamColor.g, steamColor.b, 0f);
        }

        // ───────── UI 만들기 ─────────

        private void BuildUi()
        {
#if UNITY_EDITOR
            if (font == null) AutoAssignFont();
#endif
            var canvasGo = new GameObject("NMJ_SteamGaugeCanvas", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 41;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            group = canvasGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            root = NewRect("SteamGauge", canvasGo.transform);
            root.anchorMin = root.anchorMax = Vector2.zero;
            root.pivot = Vector2.zero;
            root.sizeDelta = size;
            root.anchoredPosition = position;

            float W = size.x, H = size.y;
            Rect body = new Rect(BodyLeft, BodyPadY * 0.5f, W - BodyLeft - 4f, H - BodyPadY);
            Rect glass = new Rect(body.x + GlassInsetLeft, body.y + GlassInsetY, body.width - GlassInsetLeft - GlassInsetRight, body.height - GlassInsetY * 2f);
            glassWidth = glass.width;
            valveTop = new Vector2(body.xMax - 14f, body.yMax + 20f);
            seamPoint = new Vector2(body.x + 14f, body.yMax);

            // 경고 빛 (몸통 뒤)
            glow = NewImage("WarnGlow", root, MakeSprite(DrawGlow(Mathf.RoundToInt(body.width) + 32, Mathf.RoundToInt(body.height) + 32)));
            Place(glow.rectTransform, new Rect(body.x - 16f, body.y - 16f, body.width + 32f, body.height + 32f));
            glow.color = Color.clear;

            // 밸브 (몸통 뒤에서 위로 솟음)
            Image valve = NewImage("Valve", root, MakeSprite(DrawValve(24, 26)));
            Place(valve.rectTransform, new Rect(valveTop.x - 12f, body.yMax - 8f, 24f, 26f));

            // 쇠 실린더 몸통
            Image bodyImg = NewImage("Body", root, MakeSprite(DrawBody(Mathf.RoundToInt(body.width), Mathf.RoundToInt(body.height))));
            Place(bodyImg.rectTransform, body);

            // 유리창 (둥근 모양으로 증기를 가림)
            Sprite glassSprite = MakeSprite(DrawCapsuleMask(Mathf.RoundToInt(glass.width), Mathf.RoundToInt(glass.height)));
            Image glassBack = NewImage("Glass", root, glassSprite);
            Place(glassBack.rectTransform, glass);
            glassBack.color = glassColor;
            var mask = glassBack.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = true;

            // 증기 (왼쪽부터 차는 RectMask2D 안에서 흐름)
            steamMask = NewRect("SteamLevel", glassBack.transform);
            steamMask.anchorMin = Vector2.zero;
            steamMask.anchorMax = new Vector2(0f, 1f);
            steamMask.pivot = new Vector2(0f, 0.5f);
            steamMask.offsetMin = Vector2.zero;
            steamMask.offsetMax = Vector2.zero;
            steamMask.sizeDelta = new Vector2(glass.width, 0f);
            var rm = steamMask.gameObject.AddComponent<RectMask2D>();
            rm.softness = new Vector2Int(10, 0);

            RectTransform steamRt = NewRect("Steam", steamMask);
            steamRt.anchorMin = Vector2.zero;
            steamRt.anchorMax = new Vector2(0f, 1f);
            steamRt.pivot = new Vector2(0f, 0.5f);
            steamRt.offsetMin = Vector2.zero;
            steamRt.offsetMax = Vector2.zero;
            steamRt.sizeDelta = new Vector2(glass.width, 0f);
            steam = steamRt.gameObject.AddComponent<RawImage>();
            steam.texture = DrawSteam(256, Mathf.RoundToInt(glass.height));
            steam.uvRect = new Rect(0f, 0f, glass.width / 256f, 1f);
            steam.color = steamColor;
            steam.raycastTarget = false;

            // 증기 앞쪽 끝의 흩어지는 김
            puffSprite = MakeSprite(DrawPuff(48));
            frontWispImg = NewImage("FrontWisp", glassBack.transform, puffSprite);
            frontWisp = frontWispImg.rectTransform;
            frontWisp.anchorMin = frontWisp.anchorMax = new Vector2(0f, 0.5f);
            frontWisp.sizeDelta = new Vector2(glass.height * 1.1f, glass.height * 1.1f);

            // 유리 반사광 + 먹 테두리
            Image glassTop = NewImage("GlassShine", root, MakeSprite(DrawGlassOverlay(Mathf.RoundToInt(glass.width) + 6, Mathf.RoundToInt(glass.height) + 6)));
            Place(glassTop.rectTransform, new Rect(glass.x - 3f, glass.y - 3f, glass.width + 6f, glass.height + 6f));

            // 눈금 3개
            AddTick(gunCost, gunLabel, glass, body);
            AddTick(strongCost, strongLabel, glass, body);
            AddTick(rewindCost, rewindLabel, glass, body);

            // 압력계 (몸통 왼쪽 끝 위에 겹침)
            int dialSize = Mathf.RoundToInt(DialRadius * 2f);
            Image dial = NewImage("Dial", root, MakeSprite(DrawDial(dialSize)));
            Place(dial.rectTransform, new Rect(DialRadius - DialRadius + 2f, H * 0.5f - DialRadius, dialSize, dialSize));
            Image needleImg = NewImage("Needle", dial.transform, MakeSprite(DrawNeedle(6, Mathf.RoundToInt(DialRadius - 6f))));
            needle = needleImg.rectTransform;
            needle.anchorMin = needle.anchorMax = new Vector2(0.5f, 0.5f);
            needle.pivot = new Vector2(0.5f, 0.08f);
            needle.sizeDelta = new Vector2(6f, DialRadius - 6f);
            needle.anchoredPosition = Vector2.zero;
            Image hub = NewImage("Hub", dial.transform, puffSprite);
            hub.rectTransform.anchorMin = hub.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hub.rectTransform.sizeDelta = new Vector2(7f, 7f);
            hub.color = inkColor;
        }

        private void AddTick(float cost, string label, Rect glass, Rect body)
        {
            float max = tank != null ? tank.MaxPressure : 100f;
            if (max <= 0f) max = 100f;
            float x = glass.x + glass.width * Mathf.Clamp01(cost / max);
            if (cost >= max) x -= 3f;

            var t = new Tick { cost = cost };
            t.mark = NewImage("Tick_" + label, root, MakeSprite(DrawTriangle(14, 10)));
            Place(t.mark.rectTransform, new Rect(x - 7f, body.yMax - 1f, 14f, 10f));

            var go = new GameObject("TickLabel_" + label, typeof(RectTransform));
            go.transform.SetParent(root, false);
            var txt = go.AddComponent<TextMeshProUGUI>();
            if (font != null) txt.font = font;
            txt.text = label;
            txt.fontSize = 16f;
            txt.alignment = TextAlignmentOptions.Bottom;
            txt.raycastTarget = false;
            txt.outlineWidth = 0.25f;
            txt.outlineColor = inkColor;
            Place(txt.rectTransform, new Rect(x - 14f, body.yMax + 8f, 28f, 20f));
            t.label = txt;
            ticks.Add(t);
        }

        // ───────── 그림 그리기 (코드로 만드는 텍스처) ─────────

        /// <summary>둥근 끝 사각형의 부호 거리 (안쪽 음수)</summary>
        private static float CapsuleSd(float x, float y, float w, float h, float r)
        {
            float cx = Mathf.Clamp(x, r, w - r);
            float cy = Mathf.Clamp(y, r, h - r);
            float dx = x - cx, dy = y - cy;
            return Mathf.Sqrt(dx * dx + dy * dy) - r;
        }

        /// <summary>쇠 실린더: 위아래로 둥근 음영 + 양 끝 놋쇠 띠·리벳 + 울퉁불퉁한 먹 테두리</summary>
        private Texture2D DrawBody(int w, int h)
        {
            var px = new Color[w * h];
            float r = h * 0.42f;
            float[] bands = { 14f, w - 18f };
            float[] rivets = { h * 0.22f, h * 0.5f, h * 0.78f };
            for (int y = 0; y < h; y++)
            {
                float ty = (y + 0.5f) / h;
                float shade = 0.55f + 0.75f * Mathf.Pow(Mathf.Sin(ty * Mathf.PI), 0.8f) - 0.2f * (1f - ty);
                for (int x = 0; x < w; x++)
                {
                    float wob = (Mathf.PerlinNoise(x * 0.09f + seed, y * 0.09f) - 0.5f) * 2.2f;
                    float d = CapsuleSd(x + 0.5f, y + 0.5f, w, h, r) + wob;
                    if (d > 1.5f) { px[y * w + x] = Color.clear; continue; }

                    Color c = ironColor * shade;
                    // 쇠 표면 얼룩
                    c *= 0.85f + 0.3f * Mathf.PerlinNoise(x * 0.06f, y * 0.25f + seed);

                    // 놋쇠 띠
                    foreach (float bx in bands)
                    {
                        float bd = Mathf.Abs(x - bx);
                        if (bd < 5f)
                        {
                            Color b = brassColor * (shade * (bd < 1.5f && x < bx ? 1.35f : 1f));
                            // 리벳
                            foreach (float ry in rivets)
                            {
                                float rd = Mathf.Sqrt((x - bx) * (x - bx) + (y - ry) * (y - ry));
                                if (rd < 2.2f) b = Color.Lerp(b, brassColor * 0.4f, Mathf.Clamp01(2.2f - rd));
                            }
                            c = b;
                        }
                    }
                    c.a = 1f;

                    // 먹 테두리 (굵기가 붓 압력처럼 변함)
                    float thick = 2.2f * (0.75f + 0.6f * Mathf.PerlinNoise(x * 0.04f + seed * 3f, y * 0.04f));
                    float ink = Mathf.Clamp01(1f - (Mathf.Abs(d + thick * 0.5f) - thick * 0.5f));
                    c = Color.Lerp(c, inkColor, ink);
                    c.a = d > 0f ? ink : 1f;
                    px[y * w + x] = c;
                }
            }
            return MakeTexture(w, h, px);
        }

        /// <summary>유리창 모양 (흰색 → Image 색으로 칠함, 증기를 둥글게 가리는 마스크로도 씀)</summary>
        private static Texture2D DrawCapsuleMask(int w, int h)
        {
            var px = new Color[w * h];
            float r = h * 0.5f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = CapsuleSd(x + 0.5f, y + 0.5f, w, h, r);
                    px[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
                }
            return MakeTexture(w, h, px);
        }

        /// <summary>유리 반사광 + 유리창 먹 테두리</summary>
        private Texture2D DrawGlassOverlay(int w, int h)
        {
            var px = new Color[w * h];
            float r = (h - 6f) * 0.5f;
            for (int y = 0; y < h; y++)
            {
                float ty = (y - 3f) / (h - 6f);
                float shine = Mathf.Exp(-Mathf.Pow((ty - 0.75f) / 0.08f, 2f)) * 0.35f;
                for (int x = 0; x < w; x++)
                {
                    float d = CapsuleSd(x - 3f + 0.5f, y - 3f + 0.5f, w - 6f, h - 6f, r);
                    float ink = Mathf.Clamp01(1f - (Mathf.Abs(d) - 1.1f));
                    float inside = Mathf.Clamp01(-d);
                    float fadeX = Mathf.Clamp01((x - r) / 20f) * Mathf.Clamp01((w - r - x) / 20f);
                    Color c = new Color(1f, 1f, 1f, shine * inside * fadeX);
                    c = Color.Lerp(c, inkColor, ink);
                    c.a = Mathf.Max(c.a, ink);
                    px[y * w + x] = c;
                }
            }
            return MakeTexture(w, h, px);
        }

        /// <summary>좌우로 이어지는 흰 증기 (가로 반복, 원통 음영)</summary>
        private Texture2D DrawSteam(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float ty = (y + 0.5f) / h;
                float cyl = 0.7f + 0.35f * Mathf.Sin(ty * Mathf.PI);
                for (int x = 0; x < w; x++)
                {
                    // 가로로 이음매 없이 반복되도록 두 노이즈를 섞음
                    float t = (float)x / w;
                    float a = Fbm(x, y);
                    float b = Fbm(x - w, y);
                    float n = Mathf.Lerp(a, b, t);
                    float v = Mathf.Clamp01((0.62f + 0.5f * (n - 0.5f)) * cyl);
                    px[y * w + x] = new Color(v, v, v, Mathf.Clamp01(0.75f + 0.4f * n));
                }
            }
            var tex = MakeTexture(w, h, px);
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        private float Fbm(float x, float y)
        {
            return 0.6f * Mathf.PerlinNoise(x * 0.035f + seed, y * 0.11f)
                 + 0.4f * Mathf.PerlinNoise(x * 0.09f + seed * 2f, y * 0.25f + 7f);
        }

        /// <summary>부드러운 김 덩어리 (흰색, 가장자리가 흐림)</summary>
        private static Texture2D DrawPuff(int s)
        {
            var px = new Color[s * s];
            float m = s * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = (x + 0.5f - m) / m, dy = (y + 0.5f - m) / m;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float n = Mathf.PerlinNoise(x * 0.18f, y * 0.18f);
                    float a = Mathf.Clamp01(1f - d) * (0.6f + 0.6f * n);
                    px[y * s + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a * a * 1.6f));
                }
            return MakeTexture(s, s, px);
        }

        /// <summary>압력계 판: 한지색 원 + 먹 테두리 + 눈금 9개</summary>
        private Texture2D DrawDial(int s)
        {
            var px = new Color[s * s];
            float m = s * 0.5f;
            Color face = new Color(0.86f, 0.81f, 0.70f, 1f);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float dx = x + 0.5f - m, dy = y + 0.5f - m;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > m) { px[y * s + x] = Color.clear; continue; }
                    Color c = face * (0.9f + 0.15f * Mathf.PerlinNoise(x * 0.2f + seed, y * 0.2f));
                    c.a = 1f;
                    // 안쪽 가는 선
                    if (Mathf.Abs(d - (m - 6f)) < 0.6f) c = Color.Lerp(c, brassColor * 0.7f, 0.8f);
                    // 눈금 (135° → -135°, 즉 아래쪽이 비어 있음)
                    float ang = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                    for (int k = 0; k < 9; k++)
                    {
                        float a = 90f + 135f - 270f * k / 8f;   // 바늘 각도와 같은 기준 (왼쪽 아래 → 오른쪽 아래)
                        float diff = Mathf.Abs(Mathf.DeltaAngle(ang, a));
                        if (d > m - 10f && d < m - 5f && diff < 4f) c = inkColor;
                    }
                    // 바깥 먹 테두리
                    float ink = Mathf.Clamp01(1f - (Mathf.Abs(d - (m - 1.8f)) - 1.4f));
                    c = Color.Lerp(c, inkColor, ink);
                    c.a = Mathf.Clamp01(m - d);
                    px[y * s + x] = c;
                }
            return MakeTexture(s, s, px);
        }

        /// <summary>붉은 바늘 (아래가 축, 위가 끝)</summary>
        private Texture2D DrawNeedle(int w, int h)
        {
            var px = new Color[w * h];
            Color red = new Color(0.72f, 0.12f, 0.08f, 1f);
            for (int y = 0; y < h; y++)
            {
                float half = Mathf.Lerp(w * 0.5f, 0.6f, (float)y / h);
                for (int x = 0; x < w; x++)
                {
                    float a = Mathf.Clamp01(half - Mathf.Abs(x + 0.5f - w * 0.5f) + 0.5f);
                    px[y * w + x] = new Color(red.r, red.g, red.b, a);
                }
            }
            return MakeTexture(w, h, px);
        }

        /// <summary>밸브: 놋쇠 관 + 위 뚜껑</summary>
        private Texture2D DrawValve(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    bool cap = y >= h - 7;
                    float half = cap ? w * 0.5f - 1f : w * 0.27f;
                    float dx = Mathf.Abs(x + 0.5f - w * 0.5f);
                    if (dx > half) { px[y * w + x] = Color.clear; continue; }
                    float shade = 0.8f + 0.5f * (1f - dx / half);
                    Color c = brassColor * shade;
                    c.a = 1f;
                    bool edge = dx > half - 1.6f || y < 1 || y > h - 2 || (cap && y < h - 5.5f);
                    px[y * w + x] = edge ? inkColor : c;
                }
            return MakeTexture(w, h, px);
        }

        /// <summary>눈금 표시 ▼ (흰색 → Image 색으로 칠함, 먹 테두리)</summary>
        private Texture2D DrawTriangle(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float ty = (float)y / (h - 1);       // 0 = 아래 꼭짓점
                    float half = (w * 0.5f - 0.5f) * ty;
                    float dx = Mathf.Abs(x + 0.5f - w * 0.5f);
                    float d = dx - half;
                    if (d > 1f) { px[y * w + x] = Color.clear; continue; }
                    bool edge = d > -1.4f || y > h - 2;
                    px[y * w + x] = edge ? new Color(0f, 0f, 0f, 1f) : Color.white;
                }
            return MakeTexture(w, h, px);
        }

        /// <summary>몸통 뒤 경고 빛 (흰색 흐린 테두리 → 색은 Image에서)</summary>
        private static Texture2D DrawGlow(int w, int h)
        {
            var px = new Color[w * h];
            float r = (h - 32f) * 0.42f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float d = CapsuleSd(x - 16f + 0.5f, y - 16f + 0.5f, w - 32f, h - 32f, r);
                    float a = d < 0f ? 0.6f : Mathf.Exp(-d * d / 60f);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            return MakeTexture(w, h, px);
        }

        // ───────── 작은 도우미 ─────────

        private static void Place(RectTransform rt, Rect r)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = r.size;
            rt.anchoredPosition = r.position;
        }

        private static Texture2D MakeTexture(int w, int h, Color[] px)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixels(px);
            tex.Apply(false, false);
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

#if UNITY_EDITOR
        private void AutoAssignFont()
        {
            foreach (string guid in UnityEditor.AssetDatabase.FindAssets("ChosunCentennial_otf SDF t:TMP_FontAsset"))
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("Fallback")) continue;
                font = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null) return;
            }
        }

        private void OnValidate()
        {
            if (font == null) AutoAssignFont();
        }
#endif
    }
}
