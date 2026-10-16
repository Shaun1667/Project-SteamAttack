using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using YPH;

namespace NMJ
{
    /// <summary>
    /// 증기 게이지 + 무기 패 — 놋쇠 띠 두른 쇠 실린더, 유리창 안 흰 증기, 끝에 지금 든 무기 패 (NMJ HUD: 증기·무기)
    ///
    ///       銃      強                 逆
    ///    (◔)═[▓▓|▓▓|▓▓|▓▓|▓▓|▓▓░░░░░░░░░░░░]═╗ [銃]   ← 무기 패 (刀 환도 / 銃 화승총), 총이면 남은 발 수 ×N
    ///     ↑      ↑ 총을 들면 실린더가 증기 5씩 칸으로 나뉨 = 쏠 수 있는 발 수
    ///   압력계
    ///
    /// 기획 v4 (10/15 회의 결정)를 그대로 따른다. 지금 YPH 총의 주머니 충전 방식은 보지 않는다.
    /// - 행동 글자 3개: 銃(화승총 발사·받아치기 5) · 強(강공격 30) · 逆(시간 역행 70)
    ///   증기가 충분하면 짙은 먹, 모자라면 마른 붓(비백)으로 갈라진다. 든 무기에 맞는 글자는 조금 크게.
    /// - 받아치기 성공(적 경직 → 강공격 기회) → 強 글자에 붉은 먹이 번진다. FlashStrongChance()로 직접 부를 수도 있다.
    /// - 칼 적중 등으로 증기가 차면 무기 패 쪽에서 김이 실린더로 빨려 들어간다.
    /// - 증기를 쓰면 밸브에서 쓴 양만큼 김이 뿜어지고, 부족하면 게이지가 덜컹하며 붉게 번쩍인다.
    /// - Tab으로 무기를 바꾸면 패의 글자를 붓으로 다시 쓰듯 왼쪽부터 드러난다. 납도 중에는 패가 흐려진다.
    ///
    /// 데이터는 공개 값만 읽는다: YPH.SteamTank(증기), NGH.PlayerController·WeaponHolder(든 무기, 발도 여부),
    /// NGH.PlayerParry.ParryAttempted(받아치기 성공). 다른 사람 코드는 고치지 않는다.
    /// 그림은 실행할 때 코드로 그린다. 한자는 NMJ 전용 고정 폰트(NMJ_HudGlyphs SDF)를 쓴다.
    ///
    /// 플레이어(YPH_HeroPlayerIntegration 안 NGH_Player)에 InkHealthBar와 함께 붙어 있다.
    /// Test Keys (숫자패드 — F키는 퀘스트 디버그와 겹침):
    ///   1 화승총 · 2 강공격 · 3 시간 역행만큼 쓰기 · 4 칼 적중(+5) · 5 받아치기 성공 · 0 가득 채우기
    /// </summary>
    [DisallowMultipleComponent]
    public class SteamCylinderGauge : MonoBehaviour
    {
        [Header("연결 (비우면 자동으로 찾음)")]
        [SerializeField] private SteamTank tank;
        [SerializeField] private NGH.PlayerController player;
        [Tooltip("HUD 한자 폰트. 비우면 에디터에서 NMJ_HudGlyphs SDF를 자동으로 넣음 (메뉴 SteamAttack/NMJ/HUD 한자 폰트 만들기)")]
        [SerializeField] private TMP_FontAsset glyphFont;
        [Tooltip("마른 붓 표현용 TMP 셰이더 (TextMeshPro/Distance Field). 비우면 에디터에서 자동으로 넣음")]
        [SerializeField] private Shader glyphShader;

        [Header("배치 (화면 1920×1080 기준, 왼쪽 아래 기준점)")]
        [Tooltip("압력계가 체력바 낙관 아래, 실린더가 체력 획 아래에 오는 자리")]
        [SerializeField] private Vector2 position = new Vector2(60f, 22f);
        [Tooltip("게이지 전체 크기 (압력계·무기 패 포함)")]
        [SerializeField] private Vector2 size = new Vector2(600f, 58f);

        [Header("증기 비용 (기획 v4 — 임시값, 플레이 피드백 후 조정)")]
        [Tooltip("화승총 1발 (받아치기도 화승총을 격발하므로 같은 비용)")]
        [SerializeField] private float gunCost = 5f;
        [SerializeField] private float strongCost = 30f;
        [SerializeField] private float rewindCost = 70f;
        [SerializeField] private string gunLabel = "銃";
        [SerializeField] private string strongLabel = "強";
        [SerializeField] private string rewindLabel = "逆";

        [Header("무기 패")]
        [SerializeField] private string meleeGlyph = "刀";
        [SerializeField] private string gunGlyph = "銃";
        [Tooltip("납도 중 패의 진하기")]
        [SerializeField, Range(0f, 1f)] private float sheathedAlpha = 0.45f;
        [Tooltip("받아치기 성공 뒤 強이 붉게 빛나는 시간(초). 적 경직 시간에 맞춘다 (회의 2.1: 미정)")]
        [SerializeField, Min(0.1f)] private float strongChanceTime = 1.2f;

        [Header("색")]
        [SerializeField] private Color ironColor = new Color(0.17f, 0.15f, 0.14f, 1f);
        [SerializeField] private Color brassColor = new Color(0.62f, 0.44f, 0.19f, 1f);
        [SerializeField] private Color inkColor = new Color(0.05f, 0.05f, 0.06f, 1f);
        [SerializeField] private Color dryInkColor = new Color(0.16f, 0.15f, 0.14f, 0.95f);
        [SerializeField] private Color paperColor = new Color(0.88f, 0.83f, 0.72f, 1f);
        [SerializeField] private Color glassColor = new Color(0.10f, 0.11f, 0.12f, 1f);
        [SerializeField] private Color steamColor = new Color(0.95f, 0.95f, 0.93f, 1f);
        [SerializeField] private Color chanceColor = new Color(0.80f, 0.10f, 0.07f, 1f);
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
        [Tooltip("먹이 마르고 젖는 빠르기")]
        [SerializeField, Min(0.1f)] private float inkSpeed = 5f;
        [SerializeField] private int seed = 5;
        [SerializeField] private bool testKeys = true;

        // 내부 배치 (게이지 로컬 좌표, 왼쪽 아래 0,0)
        private const float DialRadius = 27f;
        private const float BodyLeft = 34f;
        private const float BodyPadY = 8f;
        private const float GlassInsetLeft = 26f;
        private const float GlassInsetRight = 32f;
        private const float GlassInsetY = 9f;
        private const float PlaqueW = 46f;
        private const float PlaqueH = 54f;
        private const float PlaqueGap = 8f;
        private const float RevealTime = 0.28f;

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
            public RectTransform rt;
            public TMP_Text wet;
            public TMP_Text dry;
            public Image notch;
            public float wetness = 1f;
            public bool isGun, isStrong;
        }

        private CanvasGroup group;
        private RectTransform root;
        private RectTransform steamMask;
        private RawImage steam;
        private RectTransform frontWisp;
        private Image frontWispImg;
        private Image glassBack;
        private RectTransform needle;
        private Image glow;
        private Vector2 valveTop;
        private Vector2 seamPoint;
        private Vector2 plaqueInlet;
        private float glassWidth;
        private readonly List<Tick> ticks = new List<Tick>();
        private readonly List<Puff> puffs = new List<Puff>();
        private readonly List<Image> cellLines = new List<Image>();
        private Sprite puffSprite;

        // 무기 패
        private RectTransform plaque;
        private CanvasGroup plaqueGroup;
        private RectMask2D plaqueReveal;
        private TMP_Text plaqueGlyph;
        private TMP_Text shotCount;
        private Image shotPaper;
        private NGH.Weapon shownWeapon;
        private bool weaponShownOnce;
        private float revealTimer = RevealTime;

        // 코드로 만든 재질·텍스처 (OnDestroy에서 지움)
        private Material wetMat, dryMat, plaqueMat;
        private Texture2D dryTex;

        private float shown = -1f;
        private float lastPressure = -1f;
        private float gunMode;
        private float strongChance;
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
#if UNITY_EDITOR
            AutoAssign();
#endif
            if (glyphShader == null) glyphShader = Shader.Find("TextMeshPro/Distance Field");
            FindTank();
            if (player == null) player = GetComponentInParent<NGH.PlayerController>();
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

        private void OnEnable()
        {
            NGH.PlayerParry.ParryAttempted += HandleParry;
        }

        private void OnDisable()
        {
            NGH.PlayerParry.ParryAttempted -= HandleParry;
            Unsubscribe();
        }

        // 코드로 만든 텍스처·스프라이트·재질은 직접 지워야 메모리에 남지 않는다
        private void OnDestroy()
        {
            if (root != null)
            {
                var done = new HashSet<Object>();
                foreach (Image img in root.GetComponentsInChildren<Image>(true))
                {
                    if (img.sprite == null || !done.Add(img.sprite)) continue;
                    if (done.Add(img.sprite.texture)) Destroy(img.sprite.texture);
                    Destroy(img.sprite);
                }
            }
            if (steam != null && steam.texture != null) Destroy(steam.texture);
            if (wetMat != null) Destroy(wetMat);
            if (dryMat != null) Destroy(dryMat);
            if (plaqueMat != null) Destroy(plaqueMat);
            if (dryTex != null) Destroy(dryTex);
        }

        /// <summary>강공격을 넣을 기회를 알린다 (強이 seconds 동안 붉게 빛남). 새 받아치기 코드에서 불러도 된다.</summary>
        public void FlashStrongChance(float seconds)
        {
            strongChance = Mathf.Max(strongChance, seconds);
        }

        private void HandleParry(NGH.PlayerParry parry, bool success)
        {
            if (!success || parry == null) return;
            // 다른 플레이어의 받아치기는 무시
            if (player != null && parry.GetComponentInParent<NGH.PlayerController>() != player) return;
            FlashStrongChance(strongChanceTime);
        }

        // ───────── 매 프레임 ─────────

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

            // 증기가 찼으면(칼 적중 등) 무기 패 쪽에서 김이 실린더로 빨려 들어간다
            if (lastPressure >= 0f && current > lastPressure + 0.01f) SpawnInflow(current - lastPressure);
            lastPressure = current;

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

            UpdateWeapon(dt, current);
            UpdateTicks(dt, current);

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

        // ───────── 무기 패 ─────────

        private void UpdateWeapon(float dt, float current)
        {
            NGH.WeaponHolder holder = player != null ? player.weapons : null;
            NGH.Weapon weapon = holder != null ? holder.Current : null;
            bool isGun = weapon != null && !weapon.isMelee;
            bool drawn = player == null || player.IsDrawn;

            // 무기가 바뀌면 글자를 다시 쓴다 (처음 한 번은 연출 없이)
            if (!weaponShownOnce || weapon != shownWeapon)
            {
                plaqueGlyph.text = isGun ? gunGlyph : meleeGlyph;
                revealTimer = weaponShownOnce ? 0f : RevealTime;
                shownWeapon = weapon;
                weaponShownOnce = true;
            }

            // 붓으로 왼쪽부터 쓰듯 드러남 + 패가 살짝 튐
            revealTimer += dt;
            float k = Mathf.Clamp01(revealTimer / RevealTime);
            float ease = 1f - (1f - k) * (1f - k);
            plaqueReveal.padding = new Vector4(0f, 0f, (1f - ease) * PlaqueW, 0f);
            plaque.localScale = Vector3.one * (1f + 0.1f * Mathf.Sin(k * Mathf.PI));

            plaqueGroup.alpha = Mathf.MoveTowards(plaqueGroup.alpha, drawn ? 1f : sheathedAlpha, dt * 4f);

            // 총이면 실린더를 증기 5씩 칸으로 나누고, 패에 쏠 수 있는 발 수
            gunMode = Mathf.MoveTowards(gunMode, isGun ? 1f : 0f, dt * 6f);
            foreach (Image line in cellLines)
                line.color = new Color(inkColor.r, inkColor.g, inkColor.b, 0.6f * gunMode);

            int shots = gunCost > 0f ? Mathf.FloorToInt((current + 0.0001f) / gunCost) : 0;
            string count = "×" + shots;
            if (shotCount.text != count) shotCount.text = count;
            shotCount.color = new Color(shots > 0 ? inkColor.r : chanceColor.r, shots > 0 ? inkColor.g : chanceColor.g,
                shots > 0 ? inkColor.b : chanceColor.b, gunMode);
            shotPaper.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.75f * gunMode);
        }

        // ───────── 행동 글자 (먹의 농담) ─────────

        private void UpdateTicks(float dt, float current)
        {
            if (strongChance > 0f) strongChance -= dt;
            float chance = Mathf.Clamp01(strongChance / 0.3f);   // 끝날 때 0.3초 동안 서서히 꺼짐
            float chancePulse = chance * (0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 14f));

            foreach (Tick t in ticks)
            {
                bool on = current + 0.0001f >= t.cost;
                t.wetness = Mathf.MoveTowards(t.wetness, on ? 1f : 0f, dt * inkSpeed);

                // 든 무기에 맞는 글자를 조금 크게 (총 → 銃, 칼 → 強)
                float emphasis = t.isGun ? gunMode : t.isStrong ? 1f - gunMode : 0f;
                float scale = 1f + 0.18f * emphasis;

                Color wet = inkColor;
                Color dry = dryInkColor;
                if (t.isStrong && chancePulse > 0f)
                {
                    // 받아치기 성공: 붉은 먹이 번지며 크게 맥박친다
                    wet = Color.Lerp(inkColor, chanceColor, chancePulse);
                    dry = Color.Lerp(dryInkColor, chanceColor, chancePulse * 0.8f);
                    scale += 0.25f * chancePulse;
                }
                wet.a *= t.wetness;
                dry.a *= 1f - t.wetness;
                t.wet.color = wet;
                t.dry.color = dry;
                t.rt.localScale = Vector3.one * scale;
                t.notch.color = new Color(inkColor.r, inkColor.g, inkColor.b, Mathf.Lerp(0.35f, 0.9f, t.wetness));
            }
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

        /// <summary>증기가 찼을 때: 무기 패 쪽에서 김이 실린더 안으로 빨려 들어간다</summary>
        private void SpawnInflow(float amount)
        {
            int n = Mathf.Clamp(Mathf.RoundToInt(amount / 2.5f), 2, 6);
            for (int i = 0; i < n; i++)
                SpawnPuff(plaqueInlet, new Vector2(Random.Range(-170f, -110f), Random.Range(-12f, 12f)), Random.Range(9f, 14f), Random.Range(0.35f, 0.55f), 0.8f);
        }

        private void HandleTestKeys()
        {
            if (!testKeys || Keyboard.current == null) return;
            if (Keyboard.current.numpad1Key.wasPressedThisFrame) TestConsume(gunCost);
            if (Keyboard.current.numpad2Key.wasPressedThisFrame) TestConsume(strongCost);
            if (Keyboard.current.numpad3Key.wasPressedThisFrame) TestConsume(rewindCost);
            if (Keyboard.current.numpad4Key.wasPressedThisFrame)
            {
                if (tank != null) tank.Refill(5f);
                else testPressure = Mathf.Min(100f, testPressure + 5f);
            }
            if (Keyboard.current.numpad5Key.wasPressedThisFrame) FlashStrongChance(strongChanceTime);
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
            BuildGlyphMaterials();

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
            Rect body = new Rect(BodyLeft, BodyPadY * 0.5f, W - BodyLeft - 4f - PlaqueW - PlaqueGap, H - BodyPadY);
            Rect glass = new Rect(body.x + GlassInsetLeft, body.y + GlassInsetY, body.width - GlassInsetLeft - GlassInsetRight, body.height - GlassInsetY * 2f);
            Rect plaqueRect = new Rect(body.xMax + PlaqueGap, (H - PlaqueH) * 0.5f, PlaqueW, PlaqueH);
            glassWidth = glass.width;
            valveTop = new Vector2(body.xMax - 14f, body.yMax + 20f);
            seamPoint = new Vector2(body.x + 14f, body.yMax);
            plaqueInlet = new Vector2(plaqueRect.x - 4f, H * 0.5f);

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
            glassBack = NewImage("Glass", root, glassSprite);
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

            // 총을 들었을 때 증기를 발 단위로 나누는 칸막이 (유리창 안)
            BuildCellLines(glass);

            // 유리 반사광 + 먹 테두리
            Image glassTop = NewImage("GlassShine", root, MakeSprite(DrawGlassOverlay(Mathf.RoundToInt(glass.width) + 6, Mathf.RoundToInt(glass.height) + 6)));
            Place(glassTop.rectTransform, new Rect(glass.x - 3f, glass.y - 3f, glass.width + 6f, glass.height + 6f));

            // 행동 글자 3개
            Sprite smudge = MakeSprite(DrawPuff(40));
            AddTick(gunCost, gunLabel, glass, body, smudge, true, false);
            AddTick(strongCost, strongLabel, glass, body, smudge, false, true);
            AddTick(rewindCost, rewindLabel, glass, body, smudge, false, false);

            // 무기 패
            BuildPlaque(plaqueRect, smudge);

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

        private void BuildCellLines(Rect glass)
        {
            float max = tank != null ? tank.MaxPressure : 100f;
            if (gunCost <= 0f || max <= 0f) return;
            int cells = Mathf.FloorToInt(max / gunCost + 0.0001f);
            Sprite line = MakeSprite(DrawCellLine(3, Mathf.RoundToInt(glass.height)));
            for (int i = 1; i < cells; i++)
            {
                Image img = NewImage("Cell_" + i, glassBack.transform, line);
                RectTransform rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(2f, glass.height);
                rt.anchoredPosition = new Vector2(glass.width * (i * gunCost / max), 0f);
                img.color = Color.clear;
                cellLines.Add(img);
            }
        }

        private void AddTick(float cost, string label, Rect glass, Rect body, Sprite smudge, bool isGun, bool isStrong)
        {
            float max = tank != null ? tank.MaxPressure : 100f;
            if (max <= 0f) max = 100f;
            float x = glass.x + glass.width * Mathf.Clamp01(cost / max);

            var t = new Tick { cost = cost, isGun = isGun, isStrong = isStrong };

            // 유리창 위 가장자리의 작은 먹 눈금 (정확한 문턱 위치)
            t.notch = NewImage("Notch_" + label, root, null);
            Place(t.notch.rectTransform, new Rect(x - 1f, glass.yMax - 4f, 2f, 7f));

            t.rt = NewRect("Glyph_" + label, root);
            t.rt.anchorMin = t.rt.anchorMax = Vector2.zero;
            t.rt.pivot = new Vector2(0.5f, 0f);
            t.rt.sizeDelta = new Vector2(30f, 28f);
            t.rt.anchoredPosition = new Vector2(x, body.yMax + 1f);

            // 글자 뒤 한지빛 번짐 — 어두운 배경에서도 먹 글자가 읽히게
            Image back = NewImage("Paper", t.rt, smudge);
            back.color = new Color(paperColor.r, paperColor.g, paperColor.b, 0.6f);
            back.rectTransform.offsetMin = new Vector2(-6f, -4f);
            back.rectTransform.offsetMax = new Vector2(6f, 4f);

            t.wet = NewGlyph("Wet", t.rt, label, 24f, wetMat);
            t.dry = NewGlyph("Dry", t.rt, label, 24f, dryMat);
            // 마른 붓 면 텍스처를 글자마다 0~1로 입힌다
            t.dry.horizontalMapping = TextureMappingOptions.Character;
            t.dry.verticalMapping = TextureMappingOptions.Character;
            ticks.Add(t);
        }

        private void BuildPlaque(Rect r, Sprite smudge)
        {
            plaque = NewRect("WeaponPlaque", root);
            plaque.anchorMin = plaque.anchorMax = Vector2.zero;
            plaque.pivot = new Vector2(0.5f, 0.5f);
            plaque.sizeDelta = r.size;
            plaque.anchoredPosition = r.center;
            plaqueGroup = plaque.gameObject.AddComponent<CanvasGroup>();

            Image face = NewImage("Face", plaque, MakeSprite(DrawPlaque(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height))));
            face.rectTransform.offsetMin = Vector2.zero;
            face.rectTransform.offsetMax = Vector2.zero;

            // 글자는 왼쪽부터 드러나도록 RectMask2D 안에 둔다
            RectTransform reveal = NewRect("Reveal", plaque);
            plaqueReveal = reveal.gameObject.AddComponent<RectMask2D>();
            plaqueReveal.softness = new Vector2Int(8, 0);
            plaqueGlyph = NewGlyph("Glyph", reveal, meleeGlyph, 32f, plaqueMat);
            plaqueGlyph.rectTransform.offsetMin = new Vector2(0f, 6f);
            plaqueGlyph.alignment = TextAlignmentOptions.Center;
            plaqueGlyph.color = inkColor;

            // 쏠 수 있는 발 수는 패 위에 (행동 글자와 같은 줄). 뒤에 한지빛 번짐을 깔아 어두운 배경에서도 읽히게
            shotPaper = NewImage("ShotsPaper", root, smudge);
            Place(shotPaper.rectTransform, new Rect(r.x - 10f, r.yMax - 4f, r.width + 20f, 30f));
            shotPaper.color = Color.clear;
            shotCount = NewGlyph("Shots", root, "×0", 20f, wetMat);
            Place(shotCount.rectTransform, new Rect(r.x - 6f, r.yMax - 2f, r.width + 12f, 26f));
            shotCount.alignment = TextAlignmentOptions.Bottom;
            shotCount.color = Color.clear;
        }

        private TMP_Text NewGlyph(string name, Transform parent, string text, float fontSize, Material mat)
        {
            RectTransform rt = NewRect(name, parent);
            var txt = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (glyphFont != null) txt.font = glyphFont;
            if (mat != null) txt.fontSharedMaterial = mat;
            txt.text = text;
            txt.fontSize = fontSize;
            txt.alignment = TextAlignmentOptions.Bottom;
            txt.raycastTarget = false;
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.overflowMode = TextOverflowModes.Overflow;
            return txt;
        }

        /// <summary>젖은 먹(그대로), 마른 붓(면에 갈라진 붓털 텍스처), 무기 패용 재질을 만든다</summary>
        private void BuildGlyphMaterials()
        {
            if (glyphFont == null)
            {
                Debug.LogWarning("[NMJ] 증기 게이지: HUD 한자 폰트가 없습니다. 메뉴 SteamAttack/NMJ/HUD 한자 폰트 만들기를 실행하세요.", this);
                return;
            }
            dryTex = DrawDryBrush(128, 64);

            // 명조 원본 획이 가늘어서 먹을 머금은 붓처럼 획을 부풀린다
            wetMat = NewGlyphMaterial("NMJ_GlyphWet", 0.32f);
            plaqueMat = NewGlyphMaterial("NMJ_GlyphPlaque", 0.28f);
            dryMat = NewGlyphMaterial("NMJ_GlyphDry", 0.24f);
            if (dryMat.HasProperty("_FaceTex")) dryMat.SetTexture("_FaceTex", dryTex);
        }

        private Material NewGlyphMaterial(string name, float dilate)
        {
            var m = new Material(glyphFont.material) { name = name };
            if (glyphShader != null) m.shader = glyphShader;
            if (m.HasProperty("_FaceDilate")) m.SetFloat("_FaceDilate", dilate);
            ShaderUtilities.UpdateShaderRatios(m);
            return m;
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

        /// <summary>무기 패: 한지 면 + 놋쇠 테두리 + 울퉁불퉁한 먹 테두리 (호패 모양)</summary>
        private Texture2D DrawPlaque(int w, int h)
        {
            var px = new Color[w * h];
            float r = 6f;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float wob = (Mathf.PerlinNoise(x * 0.12f + seed * 7f, y * 0.12f) - 0.5f) * 1.6f;
                    float d = CapsuleSd(x + 0.5f, y + 0.5f, w, h, r) + wob;
                    if (d > 1.5f) { px[y * w + x] = Color.clear; continue; }

                    // 한지 결 (가로로 긴 섬유 + 잔 얼룩)
                    float fiber = Mathf.PerlinNoise(x * 0.05f + seed, y * 0.6f);
                    float spot = Mathf.PerlinNoise(x * 0.2f, y * 0.2f + seed * 2f);
                    Color c = paperColor * (0.88f + 0.12f * fiber + 0.06f * spot);
                    // 위쪽이 살짝 밝은 둥근 음영
                    c *= 0.92f + 0.12f * ((float)y / h);
                    c.a = 1f;

                    // 놋쇠 테두리 (안쪽 띠)
                    float rim = Mathf.Clamp01(1f - (Mathf.Abs(d + 3.6f) - 1.6f));
                    c = Color.Lerp(c, brassColor * (0.9f + 0.3f * ((float)y / h)), rim);

                    // 먹 테두리
                    float ink = Mathf.Clamp01(1f - (Mathf.Abs(d + 0.9f) - 0.9f));
                    c = Color.Lerp(c, inkColor, ink);
                    c.a = d > 0f ? ink : 1f;
                    px[y * w + x] = c;
                }
            return MakeTexture(w, h, px);
        }

        /// <summary>마른 붓 면 텍스처: 가로로 갈라진 붓털 결 (흰색, 알파가 먹이 남은 곳)</summary>
        private Texture2D DrawDryBrush(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                // 붓털 줄마다 먹이 남은 정도가 다르다
                float lane = Mathf.PerlinNoise(seed * 1.7f, y * 0.45f);
                for (int x = 0; x < w; x++)
                {
                    float streak = Mathf.PerlinNoise(x * 0.035f + seed, y * 0.45f);
                    float fine = Mathf.PerlinNoise(x * 0.22f, y * 1.3f + seed * 3f);
                    float v = streak * 0.55f + fine * 0.25f + lane * 0.35f;
                    float a = Mathf.Clamp01((v - 0.42f) / 0.12f);
                    px[y * w + x] = new Color(1f, 1f, 1f, a);
                }
            }
            var tex = MakeTexture(w, h, px);
            tex.wrapMode = TextureWrapMode.Repeat;
            return tex;
        }

        /// <summary>발 단위 칸막이: 가운데가 진하고 위아래로 흐려지는 먹 선</summary>
        private static Texture2D DrawCellLine(int w, int h)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                float ty = (y + 0.5f) / h;
                float a = Mathf.Clamp01(Mathf.Sin(ty * Mathf.PI) * 1.4f);
                for (int x = 0; x < w; x++)
                {
                    float ax = 1f - Mathf.Abs((x + 0.5f) / w - 0.5f) * 1.6f;
                    px[y * w + x] = new Color(1f, 1f, 1f, a * ax);
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
        private void AutoAssign()
        {
            if (glyphFont == null)
                glyphFont = UnityEditor.AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Fonts/NMJ/NMJ_HudGlyphs SDF.asset");
            if (glyphShader == null)
                glyphShader = Shader.Find("TextMeshPro/Distance Field");
        }

        private void OnValidate()
        {
            AutoAssign();
        }
#endif
    }
}
