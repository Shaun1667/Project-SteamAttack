using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace NMJ
{
    /// <summary>먹물 화면 전환 모양 (시안 3가지)</summary>
    public enum InkStyle
    {
        /// <summary>A. 먹 번짐 — 먹방울이 떨어져 한지에 번지듯 퍼진다</summary>
        Bleed = 0,
        /// <summary>B. 붓질 — 굵은 붓 세 획이 번갈아 화면을 가로지른다</summary>
        Brush = 1,
        /// <summary>C. 먹 흘러내림 — 위에서 먹이 배어 내려오고 먹줄기가 흘러내린다</summary>
        Drip = 2,
        /// <summary>종이 타듯 사라짐 — 쓰러짐 화면 배경 (세계관 v6 6-2). 화면 전환에는 쓰지 않는다</summary>
        Burn = 3,
    }

    /// <summary>
    /// 먹물 화면 전환. 화면을 먹으로 덮고 → 그 사이에 할 일(씬 불러오기·순간이동 등) → 다시 걷어 낸다.
    ///
    ///   InkTransition.Play(InkStyle.Drip, () => SceneManager.LoadScene("NMJ_Main"));
    ///
    /// 게임 전체의 화면 전환은 C 먹 흘러내림으로 통일했다 (2026-10-17). A·B는 시안으로 남겨 둠 (숫자패드 6/7로 비교).
    ///
    /// - 씬이 바뀌어도 이어지도록 따로 떨어진 오브젝트(DontDestroyOnLoad)에서 돈다. 처음 부를 때 저절로 만든다.
    /// - 걷어 낼 때는 다른 모양으로 물러난다 (덮인 순간에 잡음 씨앗을 바꿈).
    /// - 멈춘 시간(ESC 메뉴, 슬로 모션)과 관계없이 흐르고, 씬 로딩처럼 긴 멈춤 뒤에도 한 번에 건너뛰지 않는다.
    /// - 모양은 셰이더 NMJ/UI/InkTransition (Assets/Resources/NMJ/NMJ_InkTransition 머티리얼로 빌드에 포함).
    /// - 시안 비교용: 에디터·개발 빌드에서 숫자패드 6 / 7 / 8 = A 먹 번짐 / B 붓질 / C 먹 흘러내림 (덮었다 걷기만 함).
    /// </summary>
    public class InkTransition : MonoBehaviour
    {
        private const string MaterialResource = "NMJ/NMJ_InkTransition";

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int StyleId = Shader.PropertyToID("_Style");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");

        private enum Phase { Idle, Covering, Hold, Revealing }

        private static InkTransition instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => instance = null;

        // 시안 비교 키를 바로 쓸 수 있게 미리 만들어 둔다 (에디터·개발 빌드만)
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateForTestKeys()
        {
            if (Debug.isDebugBuild) Get();
        }

        /// <summary>전환이 돌고 있다 (덮는 중·덮임·걷는 중)</summary>
        public static bool IsBusy => instance != null && instance.phase != Phase.Idle;

        private Material material;
        private RawImage image;
        private Phase phase = Phase.Idle;
        private float progress;
        private float coverTime, holdTime, revealTime, holdTimer;
        private Action atMiddle, onDone;
        private int framesSinceMiddle;

        // ───────── 부르는 곳 ─────────

        /// <summary>먹으로 덮고 → atMiddle → 걷어 낸다</summary>
        public static void Play(InkStyle style, Action atMiddle, float coverTime = 1.2f, float revealTime = 1.1f, float holdTime = 0.2f, Action onDone = null)
        {
            InkTransition t = Get();
            if (t == null) { atMiddle?.Invoke(); onDone?.Invoke(); return; }
            t.Begin(style, atMiddle, coverTime, revealTime, holdTime, onDone);
        }

        /// <summary>(미리 보기용) 진행도를 직접 정해 멈춰 둔다. progress &lt; 0 이면 지운다</summary>
        public static void Preview(InkStyle style, float progress, float seed)
        {
            InkTransition t = Get();
            if (t == null) return;
            t.phase = Phase.Idle;
            t.material.SetFloat(StyleId, (float)style);
            t.material.SetFloat(SeedId, seed);
            t.SetProgress(Mathf.Max(0f, progress));
        }

        private static InkTransition Get()
        {
            if (instance != null) return instance;
            var mat = Resources.Load<Material>(MaterialResource);
            if (mat == null)
            {
                Debug.LogWarning($"[NMJ] 먹물 화면 전환 머티리얼이 없습니다: Resources/{MaterialResource}");
                return null;
            }
            var go = new GameObject("NMJ_InkTransition");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<InkTransition>();
            instance.Build(mat);
            return instance;
        }

        // ───────── 진행 ─────────

        private void Begin(InkStyle style, Action middle, float cover, float reveal, float hold, Action done)
        {
            // 이미 덮인 상태면 이어서, 아니면 처음부터 덮는다
            // 덮는 중에 또 불리면 앞의 예약을 지우지 않고 함께 실행한다
            // (예: 오프닝이 끝나며 부른 전환이 안개문 순간이동 예약을 덮어써 순간이동이 사라지던 문제)
            atMiddle = phase == Phase.Covering ? atMiddle + middle : middle;
            onDone = phase != Phase.Idle ? onDone + done : done;
            coverTime = Mathf.Max(0.05f, cover);
            revealTime = Mathf.Max(0.05f, reveal);
            holdTime = Mathf.Max(0f, hold);
            material.SetFloat(StyleId, (float)style);
            if (phase == Phase.Idle) material.SetFloat(SeedId, UnityEngine.Random.Range(0f, 100f));   // 걷는 중이면 그 모양 그대로 다시 덮는다
            phase = Phase.Covering;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (Debug.isDebugBuild && kb != null && phase == Phase.Idle)
            {
                if (kb.numpad6Key.wasPressedThisFrame) Play(InkStyle.Bleed, null);
                else if (kb.numpad7Key.wasPressedThisFrame) Play(InkStyle.Brush, null);
                else if (kb.numpad8Key.wasPressedThisFrame) Play(InkStyle.Drip, null, 1.4f);   // 천천히 짙어지는 모양이라 길게
            }
            if (phase == Phase.Idle) return;
            // 씬 로딩 같은 긴 프레임은 0.05초로 친다 → 덮고 걷는 모양이 끊기지 않는다
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            switch (phase)
            {
                case Phase.Covering:
                    SetProgress(progress + dt / coverTime);
                    if (progress >= 1f)
                    {
                        phase = Phase.Hold;
                        holdTimer = 0f;
                        framesSinceMiddle = 0;
                        material.SetFloat(SeedId, UnityEngine.Random.Range(0f, 100f));   // 다 덮였으니 걷을 모양을 새로
                        Action a = atMiddle;
                        atMiddle = null;
                        a?.Invoke();
                    }
                    break;

                case Phase.Hold:
                    holdTimer += dt;
                    framesSinceMiddle++;
                    // 씬을 불러왔다면 그 다음 프레임들이 지나야 새 화면이 그려진다
                    if (holdTimer >= holdTime && framesSinceMiddle >= 3) phase = Phase.Revealing;
                    break;

                case Phase.Revealing:
                    SetProgress(progress - dt / revealTime);
                    if (progress <= 0f)
                    {
                        phase = Phase.Idle;
                        Action d = onDone;
                        onDone = null;
                        d?.Invoke();
                    }
                    break;
            }
        }

        private void SetProgress(float p)
        {
            progress = Mathf.Clamp01(p);
            material.SetFloat(ProgressId, progress);
            material.SetFloat(AspectId, Screen.height > 0 ? (float)Screen.width / Screen.height : 1.7778f);
            image.enabled = progress > 0f;
            image.raycastTarget = progress > 0.5f;   // 많이 덮였을 땐 UI 클릭을 막는다
        }

        // ───────── 화면 만들기 ─────────

        private void Build(Material source)
        {
            material = new Material(source) { hideFlags = HideFlags.DontSave };
            Canvas canvas = NmjMenuUi.NewCanvas("Canvas", transform, 1000, out CanvasGroup group);   // 모든 UI 위
            group.blocksRaycasts = true;
            image = NmjMenuUi.NewRawImage("Ink", canvas.transform, Texture2D.whiteTexture);
            image.material = material;
            SetProgress(0f);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (instance == this) instance = null;
        }
    }
}
