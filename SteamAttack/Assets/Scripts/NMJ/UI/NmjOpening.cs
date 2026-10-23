using System.Collections;
using System.Collections.Generic;
using NGH;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using static NMJ.NmjMenuUi;

namespace NMJ
{
    /// <summary>
    /// 오프닝 0:00~0:20 「불이 켜지던 밤」 (세계관 v6 5장, 기획서 v4 4-2)
    ///
    ///   먹빛 화면에 눈이 내리고        고종 24년(1887) 3월 / 경복궁 건청궁
    ///   전등이 켜진다 (따뜻한 빛)  →   땅속 깊은 곳에서, 거대한 숨 한 번   ← 숨소리, 눈송이가 허공에 멈추고 빛이 얼어붙는다
    ///   지하 기관실 → 고종의 어명 세 마디 → 무사는 대답하지 않고 고개를 숙인다
    ///   → 먹물 화면 전환으로 걷히면 육조거리
    ///
    /// 컷씬 모델·카메라가 없어도 되도록 화면 글과 소리로만 만든 판이다. 대사·글은 인스펙터에서 고친다.
    /// ESC = 바로 육조거리로. NMJ_Main 씬에 둔다.
    /// </summary>
    public class NmjOpening : MonoBehaviour
    {
        [Header("켜기")]
        [SerializeField] private bool playOnStart = true;
        [Tooltip("에디터에서 Play를 눌렀을 때도 튼다 (끄면 빌드에서만)")]
        [SerializeField] private bool playInEditor = true;

        [Header("때와 곳")]
        [SerializeField] private string dateLine = "고종 24년(1887) 3월";
        [SerializeField] private string placeLine = "경복궁 건청궁 · 조선 최초의 전등이 켜지던 밤";

        [Header("거대한 숨 (5초 — 현무의 한 호흡)")]
        [SerializeField] private string breathLine = "땅속 깊은 곳에서, 거대한 숨 한 번.";
        [Tooltip("현무 숨소리 (SFX_Hyeonmu_Breath)")]
        [SerializeField] private AudioClip breathClip;
        [SerializeField, Range(0f, 1f)] private float breathVolume = 0.9f;
        [SerializeField, Min(1f)] private float breathTime = 5f;

        [Header("지하 기관실의 어명")]
        [SerializeField] private NmjDialogue.Line[] lines =
        {
            NmjDialogue.Card("지하 기관실", "거꾸로 도는 계측기 곁, 고종과 기기창 정비공", 2.2f),
            NmjDialogue.Say("고종", "저 계측기가 거꾸로 돌고 있다. 시간이 새고 있다는구나."),
            NmjDialogue.Say("고종", "가서, 저것을 다시 잠재우라. 죽이라는 것이 아니다."),
            NmjDialogue.Say("고종", "그 혼천의를 저것의 가슴에 물리면, 너의 5초도 거기서 끝이다."),
            NmjDialogue.Say("고종", "그래도 가겠느냐.", 1.6f),
            NmjDialogue.Narrate("무사는 대답하지 않고 고개를 숙인다.", 1.8f),
        };

        [Header("끝")]
        [SerializeField] private InkStyle revealStyle = InkStyle.Drip;
        [Tooltip("육조거리에 내려선 뒤 아래쪽 자막")]
        [SerializeField] private string arrivalCaption = "육조거리 — 광화문으로";

        [Header("색")]
        [SerializeField] private Color snowColor = new Color(0.83f, 0.93f, 0.95f, 1f);
        [SerializeField] private Color lampWarm = new Color(1f, 0.78f, 0.42f, 1f);
        [SerializeField] private Color lampFrozen = new Color(0.62f, 0.84f, 0.92f, 1f);   // 빙청

        /// <summary>오프닝이 도는 중</summary>
        public static bool IsPlaying { get; private set; }

        /// <summary>이번 판에서 오프닝을 이미 틀었음. 보스 씬에서 Main으로 돌아오거나 Main을 다시 불러와도 다시 틀지 않는다. 타이틀 씬을 열면 지워진다</summary>
        public static bool PlayedThisRun { get; private set; }

        /// <summary>이번에 Main이 열린 것이 새 판의 시작(오프닝을 튼 판)인지. 시작 튜토리얼이 본다</summary>
        public static bool FreshStart { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            IsPlaying = false;
            PlayedThisRun = false;
            FreshStart = false;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnAnySceneLoaded;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnAnySceneLoaded;
        }

        // 타이틀로 돌아가면 새 판 → 다음 Main에서 오프닝을 다시 튼다
        private static void OnAnySceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        {
            if (m != UnityEngine.SceneManagement.LoadSceneMode.Single) return;
            FreshStart = false;
            if (s.name.Contains("Title")) PlayedThisRun = false;
        }

        private struct Flake { public RectTransform rt; public float x, speed, sway, phase; }
        private readonly List<Flake> flakes = new List<Flake>();
        private CanvasGroup group;
        private RectTransform canvasRect;
        private Image lamp, lampHalo;
        private Texture2D glowTex;
        private float snowSpeed = 1f, snowClock;
        private bool skipping, finishing;
        private AudioSource audioSource;
        private Coroutine routine;

        private void Start()
        {
            if (NmjBootToTitle.Redirecting) return;   // 타이틀을 거치지 않고 열려 타이틀로 가는 중
            if (!playOnStart || (Application.isEditor && !playInEditor)) return;
            if (PlayedThisRun) return;   // 보스 씬 → Main 복귀 등: 이미 본 프롤로그는 다시 틀지 않는다
            FreshStart = true;
            Play();
        }

        /// <summary>오프닝을 처음부터 튼다</summary>
        [ContextMenu("미리 보기")]
        public void Play()
        {
            if (!Application.isPlaying || IsPlaying) return;
            IsPlaying = true;
            PlayedThisRun = true;
            skipping = finishing = false;
            ControlsOverlay.MenuOpen = true;
            if (group == null) Build();
            group.alpha = 1f;
            group.gameObject.SetActive(true);
            routine = StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            snowSpeed = 1f;
            SetLamp(0f, lampWarm);

            // 1. 때와 곳 + 전등이 켜진다
            bool cardDone = false;
            NmjDialogue.Play(new[] { NmjDialogue.Card(dateLine, placeLine, 1.8f) }, () => cardDone = true, false, false);
            yield return Lamp(1.2f);
            while (!cardDone) yield return null;

            // 2. 거대한 숨 한 번: 소리가 빨려 들고 눈송이가 멈춘다, 전등 빛도 얼어붙는다
            if (breathClip != null) audioSource.PlayOneShot(breathClip, breathVolume);
            bool breathDone = false;
            NmjDialogue.Play(new[] { NmjDialogue.Narrate(breathLine, breathTime - 1.2f) }, () => breathDone = true, false, false);
            float t = 0f;
            while (t < breathTime)
            {
                t += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
                float k = Mathf.Clamp01(t / (breathTime * 0.7f));
                snowSpeed = 1f - Mathf.SmoothStep(0f, 1f, k);
                SetLamp(1f, Color.Lerp(lampWarm, lampFrozen, k));
                yield return null;
            }
            snowSpeed = 0f;
            while (!breathDone) yield return null;

            // 3. 지하 기관실의 어명
            bool talkDone = false;
            NmjDialogue.Play(lines, () => talkDone = true, false, false);
            while (!talkDone) yield return null;

            Finish();
        }

        private IEnumerator Lamp(float delay)
        {
            float t = 0f;
            while (t < delay) { t += Time.unscaledDeltaTime; yield return null; }
            // 깜빡이다 켜진다
            float[] steps = { 0.6f, 0f, 0.35f, 0f, 1f };
            foreach (float s in steps)
            {
                SetLamp(s, lampWarm);
                float w = 0f;
                while (w < 0.09f) { w += Time.unscaledDeltaTime; yield return null; }
            }
        }

        private void SetLamp(float on, Color c)
        {
            lamp.color = new Color(c.r, c.g, c.b, on);
            lampHalo.color = new Color(c.r, c.g, c.b, on * 0.35f);
        }

        private void Update()
        {
            if (!IsPlaying || group == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            snowClock += dt * snowSpeed;
            UpdateSnow(dt);

            Keyboard kb = Keyboard.current;
            if (!skipping && !finishing && kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                skipping = true;
                if (routine != null) StopCoroutine(routine);
                NmjDialogue.Stop();
                Finish();
            }
        }

        private void Finish()
        {
            if (finishing) return;
            finishing = true;
            // 먹물로 덮고, 덮인 사이에 오프닝 화면을 치운 뒤 육조거리를 드러낸다
            InkTransition.Play(revealStyle, () =>
            {
                NmjDialogue.Stop();
                group.gameObject.SetActive(false);
                ControlsOverlay.MenuOpen = false;
                IsPlaying = false;
            }, 1.2f, 1.4f, 0.3f, () =>
            {
                if (!string.IsNullOrEmpty(arrivalCaption)) NmjSubtitle.Caption(arrivalCaption, 3.2f);
            });
        }

        private void OnDestroy()
        {
            if (IsPlaying) { IsPlaying = false; ControlsOverlay.MenuOpen = false; }
            if (glowTex != null) Destroy(glowTex);
        }

        private void UpdateSnow(float dt)
        {
            float h = canvasRect.rect.height, w = canvasRect.rect.width;
            for (int i = 0; i < flakes.Count; i++)
            {
                Flake f = flakes[i];
                Vector2 p = f.rt.anchoredPosition;
                p.y -= f.speed * dt * snowSpeed;
                if (p.y < -10f) p.y = h + 10f;
                p.x = f.x * w + Mathf.Sin(snowClock * 0.6f + f.phase) * f.sway;
                f.rt.anchoredPosition = p;
            }
        }

        // ───────── 화면 만들기 ─────────

        private void Build()
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f;
            audioSource.ignoreListenerPause = true;

            Canvas canvas = NewCanvas("NMJ_OpeningCanvas", transform, 74, out group);   // HUD 위, 대사(76) 아래
            canvasRect = (RectTransform)canvas.transform;
            group.blocksRaycasts = false;
            group.interactable = false;
            NewImage("Back", canvas.transform, new Color(0.02f, 0.02f, 0.025f, 1f), Vector2.zero, Vector2.zero);

            // 건청궁 전등 하나 (화면 위쪽 가운데) — 둥근 빛 텍스처
            glowTex = DrawGlow(128);
            var halo = NewRect("LampHalo", canvas.transform);
            halo.anchorMin = halo.anchorMax = new Vector2(0.5f, 0.8f);
            halo.sizeDelta = new Vector2(520f, 520f);
            lampHalo = halo.gameObject.AddComponent<Image>();
            lampHalo.sprite = Sprite.Create(glowTex, new Rect(0, 0, 128, 128), new Vector2(0.5f, 0.5f));
            lampHalo.raycastTarget = false;
            var core = NewRect("Lamp", canvas.transform);
            core.anchorMin = core.anchorMax = new Vector2(0.5f, 0.8f);
            core.sizeDelta = new Vector2(70f, 70f);
            lamp = core.gameObject.AddComponent<Image>();
            lamp.sprite = lampHalo.sprite;
            lamp.raycastTarget = false;

            var rng = new System.Random(1887);
            for (int i = 0; i < 110; i++)
            {
                float size = 2f + (float)rng.NextDouble() * (rng.NextDouble() < 0.15 ? 5f : 2.5f);
                bool blue = rng.NextDouble() < 0.08;   // 간간이 푸른 눈송이 (시간 부스러기)
                Color c = blue ? new Color(0.45f, 0.72f, 1f, 0.8f) : new Color(snowColor.r, snowColor.g, snowColor.b, 0.25f + (float)rng.NextDouble() * 0.45f);
                Image img = NewImage("Flake", canvas.transform, c, Vector2.zero, Vector2.zero);
                RectTransform rt = img.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.sizeDelta = new Vector2(size, size);
                rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
                rt.anchoredPosition = new Vector2(0f, (float)rng.NextDouble() * 1200f);
                flakes.Add(new Flake { rt = rt, x = (float)rng.NextDouble(), speed = 30f + (float)rng.NextDouble() * 40f + size * 5f, sway = 6f + (float)rng.NextDouble() * 14f, phase = (float)rng.NextDouble() * 6.28f });
            }
        }

        private static Texture2D DrawGlow(int S)
        {
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float d = new Vector2(x + 0.5f - S * 0.5f, y + 0.5f - S * 0.5f).magnitude / (S * 0.5f);
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * (0.6f + 0.4f * a);
                    px[y * S + x] = new Color(1f, 1f, 1f, a);
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }
    }
}
