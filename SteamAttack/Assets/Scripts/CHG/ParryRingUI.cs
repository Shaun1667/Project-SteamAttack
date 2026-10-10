using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CHG
{
    /// <summary>
    /// 패링 예고 동심원 UI (기획: 게임 제작일지/패링)
    /// - 적이 패링 가능한 공격(도끼 병사 달려들기, 소총 병사 킥)을 하면 적 상체 위에 먹선 원 두 개가 나타난다.
    ///   안쪽 원: 고정, 아이보리 먹선(옅은 그림자) / 바깥 원: 200% → 100% 일정한 속도로 줄어들며 시계 방향으로 점점 빨리 회전 (기본 135°)
    /// - 줄어드는 시간은 모든 적 동일(기본 0.8초). 두 원이 겹치는 순간 = 공격 판정 0.15초 전 = 판정 구간(±0.15초)의 한가운데
    /// - 판정 구간 동안 붉은 먹선으로 발광 (구간 시작 때 효과음)
    /// - 성공: 붉은 먹물 파편이 튀며 빠르게 사라짐 / 실패(헛누름)·공격 명중: 사라짐
    /// - 여러 적이 동시에 공격하면 원은 적마다 뜨고, Ctrl 한 번은 타이밍이 맞는 가장 가까운 적에게 적용 (PlayerParry)
    /// 그림·소리·수치는 Assets/Prefabs/CHG/Resources/ParryRingConfig 에서 바꾼다.
    /// 씬에 따로 놓을 필요 없음: 첫 패링 가능 공격 때 캔버스가 오브젝트 풀(ObjectPool)에서 꺼내진다.
    /// </summary>
    public class ParryRingUI : MonoBehaviour
    {
        private const string ConfigPath = "chg_ParryRingConfig";
        private const int SplatCount = 12;
        private const float SuccessTime = 0.35f;
        private const float FailTime = 0.2f;
        private static readonly Color SplatColor = new Color(0.75f, 0.05f, 0.03f, 1f);

        private class Splat
        {
            public RectTransform rect;
            public RawImage image;
            public Vector2 velocity;
            public float size;
        }

        private class Entry
        {
            public EnemyAI ai;
            public RectTransform root;
            public RectTransform inner;
            public RectTransform outer;
            public RawImage innerBlack, innerRed, outerBlack, outerRed;
            public Splat[] splats;
            public CanvasGroup group;
            public float successTimer;   // > 0: 성공 연출 중
            public float failTimer;      // > 0: 실패 연출 중
            public bool closing;
            public bool sfxPlayed;
            public float alpha;
            public Vector2 screenPos;
        }

        private static ParryRingUI instance;
        private static GameObject canvasTemplate;
        private static GameObject ringTemplate;
        private static ParryRingConfig config;
        private static bool configLoaded;
        private static Texture2D fallbackRing;
        private static Texture2D dotTexture;

        private readonly List<Entry> entries = new List<Entry>();
        private AudioSource audioSource;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            canvasTemplate = null;
            ringTemplate = null;
            config = null;
            configLoaded = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Hook()
        {
            EnemyAI.ParryableAttackStarted -= OnParryableAttackStarted;
            EnemyAI.ParryableAttackStarted += OnParryableAttackStarted;
            PlayerParry.ParryAttempted -= OnParryAttempted;
            PlayerParry.ParryAttempted += OnParryAttempted;
        }

        private static ParryRingConfig Config
        {
            get
            {
                if (!configLoaded)
                {
                    configLoaded = true;
                    config = Resources.Load<ParryRingConfig>(ConfigPath);
                    if (config == null)
                    {
                        Debug.LogWarning("[chg_ParryRingUI] Resources/" + ConfigPath + " 없음 — 임시 원과 기본값 사용");
                        config = ScriptableObject.CreateInstance<ParryRingConfig>();
                    }
                }
                return config;
            }
        }

        private static void OnParryableAttackStarted(EnemyAI ai, float timeUntilHit)
        {
            ParryRingUI ui = GetInstance();
            if (ui != null && ai != null)
            {
                ui.Add(ai);
            }
        }

        // 헛누름(실패) → 떠 있는 원은 사라짐 (쿨타임 때문에 이번 공격은 다시 패링할 수 없음)
        private static void OnParryAttempted(PlayerParry parry, bool success)
        {
            if (success || instance == null)
            {
                return;
            }
            foreach (Entry e in instance.entries)
            {
                if (!e.closing && e.successTimer <= 0f && e.failTimer <= 0f && e.alpha > 0f)
                {
                    e.failTimer = FailTime;
                }
            }
        }

        private void OnEnable()
        {
            instance = this;
        }

        private void OnDisable()
        {
            foreach (Entry e in entries)
            {
                if (e.root != null)
                {
                    ObjectPool.Despawn(e.root.gameObject);
                }
            }
            entries.Clear();
            if (instance == this)
            {
                instance = null;
            }
        }

        // ───────── 원 추가 / 갱신 ─────────

        private void Add(EnemyAI ai)
        {
            // 같은 적의 이전 원은 정리
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i].ai == ai)
                {
                    Release(i);
                }
            }

            GameObject go = ObjectPool.Spawn(GetRingTemplate(), Vector3.zero, Quaternion.identity, transform);
            if (go == null)
            {
                return;
            }
            ParryRingConfig c = Config;
            var e = new Entry { ai = ai, root = (RectTransform)go.transform };
            e.root.localScale = Vector3.one;
            e.root.localRotation = Quaternion.identity;
            e.root.sizeDelta = new Vector2(c.ringSize, c.ringSize);
            e.group = go.GetComponent<CanvasGroup>();
            e.inner = (RectTransform)e.root.Find("Inner");
            e.outer = (RectTransform)e.root.Find("Outer");
            e.innerBlack = e.inner.Find("Black").GetComponent<RawImage>();
            e.innerRed = e.inner.Find("Red").GetComponent<RawImage>();
            e.outerBlack = e.outer.Find("Black").GetComponent<RawImage>();
            e.outerRed = e.outer.Find("Red").GetComponent<RawImage>();
            ApplyTextures(e, c);

            Transform splatRoot = e.root.Find("Splats");
            e.splats = new Splat[splatRoot.childCount];
            for (int i = 0; i < e.splats.Length; i++)
            {
                var rect = (RectTransform)splatRoot.GetChild(i);
                rect.gameObject.SetActive(false);
                e.splats[i] = new Splat { rect = rect, image = rect.GetComponent<RawImage>() };
            }

            e.inner.localScale = Vector3.one;
            e.outer.localScale = Vector3.one * c.outerStartScale;
            e.outer.localRotation = Quaternion.identity;
            SetRed(e, 0f, c);
            e.group.alpha = 0f;
            entries.Add(e);
        }

        private static void ApplyTextures(Entry e, ParryRingConfig c)
        {
            Texture black = c.blackRing != null ? (Texture)c.blackRing : GetFallbackRing();
            Texture red = c.redRing != null ? (Texture)c.redRing : black;
            e.innerBlack.texture = c.whiteRing != null ? (Texture)c.whiteRing : black;   // 안쪽 원: 아이보리 고리
            e.outerBlack.texture = black;
            e.innerRed.texture = red;
            e.outerRed.texture = red;
            // 붉은 그림이 없으면 검은 그림에 붉은 색을 입혀 대신함
            Color redTint = c.redRing != null ? Color.white : new Color(0.85f, 0.1f, 0.05f, 1f);
            e.innerRed.color = redTint;
            e.outerRed.color = redTint;
        }

        // red: 0 = 검은 먹선, 1 = 붉은 먹선
        private static void SetRed(Entry e, float red, ParryRingConfig c)
        {
            SetAlpha(e.innerBlack, c.innerAlpha * (1f - red));
            SetAlpha(e.innerRed, Mathf.Lerp(c.innerAlpha, 1f, 0.5f) * red);
            SetAlpha(e.outerBlack, 1f - red);
            SetAlpha(e.outerRed, red);
        }

        private static void SetAlpha(Graphic g, float a)
        {
            Color col = g.color;
            col.a = a;
            g.color = col;
        }

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            float now = Time.time;
            float dt = Time.unscaledDeltaTime;
            ParryRingConfig c = Config;

            for (int i = entries.Count - 1; i >= 0; i--)
            {
                Entry e = entries[i];
                if (e.root == null)
                {
                    entries.RemoveAt(i);
                    continue;
                }

                bool aiValid = e.ai != null && e.ai.isActiveAndEnabled;
                Vector2 shake = Vector2.zero;

                // 패링 성공 감지: 공격이 끊기고 패링당함 상태가 됨
                if (!e.closing && e.successTimer <= 0f && e.failTimer <= 0f && aiValid
                    && e.ai.CurrentState == EnemyAI.State.Parried)
                {
                    BeginSuccess(e, c);
                }

                if (e.successTimer > 0f)
                {
                    // 성공: 붉은 먹선이 크게 번지며 사라지고, 먹물 파편이 튐
                    e.successTimer -= dt;
                    float k = 1f - Mathf.Clamp01(e.successTimer / SuccessTime);
                    SetRed(e, 1f, c);
                    e.root.localScale = Vector3.one * (1f + 0.6f * (1f - (1f - k) * (1f - k)));
                    e.alpha = 1f - k * k;
                    UpdateSplats(e, dt, k);
                    if (e.successTimer <= 0f)
                    {
                        Release(i);
                        continue;
                    }
                }
                else if (e.failTimer > 0f)
                {
                    // 실패(헛누름): 살짝 떨리며 빠르게 사라짐
                    e.failTimer -= dt;
                    float k = 1f - Mathf.Clamp01(e.failTimer / FailTime);
                    SetRed(e, 0f, c);
                    shake = new Vector2(Random.Range(-1f, 1f), Random.Range(-1f, 1f)) * 6f * (1f - k);
                    e.root.localScale = Vector3.one * (1f - 0.1f * k);
                    e.alpha = 1f - k;
                    if (e.failTimer <= 0f)
                    {
                        Release(i);
                        continue;
                    }
                }
                else if (!aiValid || !e.ai.IsParryWindowOpen)
                {
                    // 공격이 끝났거나 끊김 → 빠르게 사라짐
                    e.closing = true;
                    e.alpha = Mathf.MoveTowards(e.alpha, 0f, dt / 0.12f);
                    if (e.alpha <= 0f)
                    {
                        Release(i);
                        continue;
                    }
                }
                else
                {
                    float overlap = e.ai.ParryOverlapTime;
                    float untilOverlap = overlap - now;
                    if (untilOverlap > c.shrinkTime)
                    {
                        // 아직 나타날 때가 아님 (모든 적이 같은 줄어듦 시간을 쓰도록 대기)
                        e.alpha = 0f;
                    }
                    else
                    {
                        // 바깥 원: 일정한 속도로 줄어듦(Linear), 시계 방향으로 점점 빨리 회전(Ease-In)
                        float p = Mathf.Clamp01(1f - untilOverlap / c.shrinkTime);
                        e.outer.localScale = Vector3.one * Mathf.Lerp(c.outerStartScale, 1f, p);
                        e.outer.localRotation = Quaternion.Euler(0f, 0f, -c.outerRotation * Mathf.Pow(p, c.rotationEaseIn));

                        // 판정 구간(겹치는 순간 ±0.15초): 붉은 먹선으로 발광, 한가운데에서 가장 강함
                        float fromCenter = Mathf.Abs(now - overlap);
                        bool perfect = fromCenter <= EnemyAI.ParryHalfWindow;
                        if (perfect && !e.sfxPlayed)
                        {
                            e.sfxPlayed = true;
                            PlaySfx(c.overlapSfx, c);
                        }
                        float glow = perfect ? 1f - 0.5f * (fromCenter / EnemyAI.ParryHalfWindow) : 0f;
                        SetRed(e, perfect ? 1f : 0f, c);
                        e.root.localScale = Vector3.one * (1f + 0.1f * glow);

                        // 처음 나타날 때 0.1초 동안 서서히
                        e.alpha = Mathf.MoveTowards(e.alpha, 1f, dt / 0.1f);
                    }
                }

                // 위치: 적 상체 위 (화면 밖이면 숨김). 성공 연출 중엔 마지막 위치 유지
                bool onScreen = false;
                if (cam != null && e.ai != null)
                {
                    Vector3 world = e.ai.transform.position + Vector3.up * c.bodyHeight;
                    Vector3 screen = cam.WorldToScreenPoint(world);
                    onScreen = screen.z > 0f && screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
                    if (onScreen)
                    {
                        e.screenPos = new Vector2(screen.x, screen.y);
                    }
                }
                e.root.position = new Vector3(e.screenPos.x + shake.x, e.screenPos.y + shake.y, 0f);
                e.group.alpha = onScreen ? e.alpha : 0f;
            }
        }

        private void BeginSuccess(Entry e, ParryRingConfig c)
        {
            e.successTimer = SuccessTime;
            e.alpha = 1f;
            e.outer.localScale = Vector3.one;
            PlaySfx(c.successSfx, c);

            float scale = c.ringSize / 120f;
            foreach (Splat s in e.splats)
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                s.size = Random.Range(6f, 22f) * scale;
                s.velocity = dir * Random.Range(220f, 520f) * scale;
                s.rect.anchoredPosition = dir * c.ringSize * 0.45f;
                s.rect.sizeDelta = new Vector2(s.size, s.size * Random.Range(0.7f, 1.3f));
                s.rect.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);
                s.image.color = SplatColor;
                s.rect.gameObject.SetActive(true);
            }
        }

        private static void UpdateSplats(Entry e, float dt, float k)
        {
            foreach (Splat s in e.splats)
            {
                if (!s.rect.gameObject.activeSelf)
                {
                    continue;
                }
                s.rect.anchoredPosition += s.velocity * dt;
                s.velocity *= Mathf.Exp(-6f * dt);   // 먹물이 퍼지다 멈추듯 감속
                float grow = 1f + 0.4f * k;
                s.rect.localScale = Vector3.one * grow;
            }
        }

        private void PlaySfx(AudioClip clip, ParryRingConfig c)
        {
            if (clip == null)
            {
                return;
            }
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }
            if (audioSource != null)
            {
                audioSource.PlayOneShot(clip, c.sfxVolume);
            }
        }

        private void Release(int index)
        {
            Entry e = entries[index];
            if (e.root != null)
            {
                e.root.localScale = Vector3.one;
                foreach (Splat s in e.splats)
                {
                    s.rect.gameObject.SetActive(false);
                    s.rect.localScale = Vector3.one;
                }
                ObjectPool.Despawn(e.root.gameObject);
            }
            entries.RemoveAt(index);
        }

        // ───────── 캔버스·원 템플릿 (오브젝트 풀) ─────────

        private static ParryRingUI GetInstance()
        {
            if (instance != null && instance.isActiveAndEnabled)
            {
                return instance;
            }

            if (canvasTemplate == null)
            {
                canvasTemplate = CreateTemplateHolder("chg_ParryRingCanvas");
                Canvas canvas = canvasTemplate.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = -40;   // 적 체력바(-50)보다 위, HUD보다 아래
                CanvasScaler scaler = canvasTemplate.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.matchWidthOrHeight = 0.5f;
                AudioSource audio = canvasTemplate.AddComponent<AudioSource>();
                audio.playOnAwake = false;
                audio.spatialBlend = 0f;   // 화면 효과음 (2D)
                canvasTemplate.AddComponent<ParryRingUI>();
            }

            GameObject go = ObjectPool.Spawn(canvasTemplate, Vector3.zero, Quaternion.identity);
            return go != null ? go.GetComponent<ParryRingUI>() : null;
        }

        private static GameObject GetRingTemplate()
        {
            if (ringTemplate != null)
            {
                return ringTemplate;
            }

            ringTemplate = CreateTemplateHolder("chg_ParryRing");
            RectTransform root = ringTemplate.AddComponent<RectTransform>();
            root.sizeDelta = new Vector2(120f, 120f);
            CanvasGroup group = ringTemplate.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            // 그리는 순서: 안쪽 원 → 바깥 원 → 파편
            RectTransform inner = CreateFill(root, "Inner");
            CreateRawImage(inner, "Black");
            CreateRawImage(inner, "Red");
            RectTransform outer = CreateFill(root, "Outer");
            CreateRawImage(outer, "Black");
            CreateRawImage(outer, "Red");

            RectTransform splats = CreateFill(root, "Splats");
            Texture2D dot = GetDotTexture();
            for (int i = 0; i < SplatCount; i++)
            {
                GameObject go = new GameObject("Splat", typeof(RectTransform), typeof(RawImage));
                var rect = (RectTransform)go.transform;
                rect.SetParent(splats, false);
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
                var image = go.GetComponent<RawImage>();
                image.texture = dot;
                image.raycastTarget = false;
                go.SetActive(false);
            }
            return ringTemplate;
        }

        private static RectTransform CreateFill(RectTransform parent, string childName)
        {
            GameObject go = new GameObject(childName, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static void CreateRawImage(RectTransform parent, string childName)
        {
            RectTransform rect = CreateFill(parent, childName);
            RawImage image = rect.gameObject.AddComponent<RawImage>();
            image.raycastTarget = false;
        }

        // 템플릿은 비활성 상태로 풀 아래에 보관 (풀이 이걸 복제해서 씀)
        private static GameObject CreateTemplateHolder(string templateName)
        {
            GameObject go = new GameObject(templateName);
            go.SetActive(false);
            ObjectPool pool = ObjectPool.Instance;
            if (pool != null)
            {
                go.transform.SetParent(pool.transform, false);
            }
            return go;
        }

        // 먹선 그림이 없을 때 쓰는 임시 고리 (검정, 군데군데 끊김)
        private static Texture2D GetFallbackRing()
        {
            if (fallbackRing != null)
            {
                return fallbackRing;
            }
            const int size = 128;
            fallbackRing = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            float r = size * 0.5f - 1f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dy, dx);
                    float thick = 0.1f + 0.08f * Mathf.Sin(ang * 3f + 0.7f);   // 붓 굵기 변화 (회전이 보이도록)
                    float inner = r * (1f - thick * 2f);
                    float a = Mathf.Clamp01(r - d + 0.5f) * Mathf.Clamp01(d - inner + 0.5f);
                    if (Mathf.Sin(ang * 2f) > 0.93f) a = 0f;   // 끊긴 자리
                    pixels[y * size + x] = new Color32(0, 0, 0, (byte)(a * 255f));
                }
            }
            fallbackRing.SetPixels32(pixels);
            fallbackRing.Apply();
            return fallbackRing;
        }

        // 먹물 파편용 부드러운 점 (흰색, 색은 RawImage.color로)
        private static Texture2D GetDotTexture()
        {
            if (dotTexture != null)
            {
                return dotTexture;
            }
            const int size = 32;
            dotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var pixels = new Color32[size * size];
            float r = size * 0.5f - 1f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
                    float a = Mathf.Clamp01(r - Mathf.Sqrt(dx * dx + dy * dy) + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            dotTexture.SetPixels32(pixels);
            dotTexture.Apply();
            return dotTexture;
        }
    }
}
