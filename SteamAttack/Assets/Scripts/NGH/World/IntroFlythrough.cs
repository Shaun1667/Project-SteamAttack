using System.Collections;
using System.Collections.Generic;
using NMJ;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Splines;

namespace NGH
{
    /// <summary>
    /// 인트로가 끝나면 씨네머신 카메라로 길을 쭉 보여 주는 장면.
    ///   플레이어 뒤 → 근정전 뒷문(열림) → 근정전 안(옥좌 옆) → 앞문(열림) → 마당 → 근정문 → 광화문 통로 → 육조거리 → 보스전 입구 문
    ///
    /// - 카메라 길: 자식 IntroPath(Spline). 씬 뷰에서 점을 옮겨 고칠 수 있다.
    /// - 바라보는 곳: IntroCam의 Spline Dolly LookAt Targets (길 위 지점마다 볼 곳).
    /// - 새 판 시작(오프닝을 튼 판)에서 오프닝이 먹물로 덮이는 순간 시작하고, 끝나면 먹물로 덮었다가 플레이어 카메라로 돌아온다.
    /// - ESC = 건너뛰기. 도는 동안 플레이어 조작·HUD는 멈춘다. 지나가는 근정전 문은 카메라가 다가가면 열린다 (NmjHingedDoor.ExtraOpener).
    /// </summary>
    public class IntroFlythrough : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("길을 따라 움직이는 씨네머신 카메라")]
        [SerializeField] private CinemachineCamera cinematicCamera;
        [SerializeField] private CinemachineSplineDolly dolly;

        [Header("켜기")]
        [Tooltip("새 판 시작에서 오프닝이 끝나면 저절로 튼다")]
        [SerializeField] private bool playAfterOpening = true;

        [Header("움직임")]
        [Tooltip("처음부터 끝까지 걸리는 시간(초)")]
        [SerializeField, Min(1f)] private float duration = 32f;
        [Tooltip("시간(0~1) → 길 위 위치(0~1). 처음·끝을 천천히")]
        [SerializeField] private AnimationCurve progress = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [Tooltip("끝에 도착한 뒤 멈춰서 보여 주는 시간(초)")]
        [SerializeField, Min(0f)] private float endHold = 1.5f;
        [Tooltip("카메라가 이 거리(m) 안으로 오면 근정전 문이 열린다")]
        [SerializeField, Min(1f)] private float doorOpenRadius = 16f;

        [Header("화면")]
        [Tooltip("위아래 먹 띠 높이 (1080 기준, 0이면 없음)")]
        [SerializeField, Min(0f)] private float letterbox = 90f;
        [SerializeField] private InkStyle endTransition = InkStyle.Drip;
        [Tooltip("시작할 때 아래쪽 자막 (비우면 없음)")]
        [SerializeField] private string caption = "";

        [System.Serializable]
        public class PlaceCaption
        {
            [Tooltip("자막이 나오는 길 위 지점 (IntroPath 점 번호, 0부터. 3.5처럼 점 사이도 됨)")]
            public float knot;
            public string text;
        }

        [Header("장소 자막 (그 지점에 도착하면 아래쪽에 뜸)")]
        [SerializeField] private PlaceCaption[] placeCaptions =
        {
            new PlaceCaption { knot = 0f, text = "건청궁" },
            new PlaceCaption { knot = 1.67f, text = "근정전" },
            new PlaceCaption { knot = 9.64f, text = "광화문" },
            new PlaceCaption { knot = 13f, text = "육조거리" },
        };
        [Tooltip("장소 자막이 떠 있는 시간(초)")]
        [SerializeField, Min(0.5f)] private float placeCaptionTime = 3f;
        [Tooltip("시작하고 이 시간(초)이 지나야 자막을 띄움 (오프닝 먹물이 걷히는 동안 가려지지 않게)")]
        [SerializeField, Min(0f)] private float captionStartDelay = 1.2f;

        /// <summary>장면이 도는 중 (튜토리얼 등이 기다린다)</summary>
        public static bool IsPlaying { get; private set; }
        /// <summary>새 판 시작에서 오프닝이 끝나면 곧 틀 예정 (튜토리얼 안내가 먼저 뜨지 않게)</summary>
        public static bool Pending { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { IsPlaying = false; Pending = false; }

        private bool openingSeen, played, ending;
        private float time;
        private CinemachineBrain brain;
        private bool addedBrain;
        private ThirdPersonCamera playerCamera;
        private readonly List<Canvas> hiddenCanvases = new List<Canvas>();
        private readonly List<Renderer> hiddenRenderers = new List<Renderer>();
        private readonly List<bool> hiddenWasOn = new List<bool>();
        private readonly List<PathGuide> hiddenGuides = new List<PathGuide>();
        private RectTransform topBar, bottomBar;
        private Canvas barCanvas;
        private float[] captionAt;   // 장소 자막 지점 (길 0~1)
        private int nextCaption;

        private void Awake()
        {
            if (cinematicCamera == null) cinematicCamera = GetComponentInChildren<CinemachineCamera>(true);
            if (dolly == null && cinematicCamera != null) dolly = cinematicCamera.GetComponent<CinemachineSplineDolly>();
            if (cinematicCamera != null) cinematicCamera.gameObject.SetActive(false);
        }

        private void Update()
        {
            // 오프닝이 돌다가 먹물로 덮이는 순간(IsPlaying이 꺼짐) 시작
            Pending = playAfterOpening && !played && NmjOpening.FreshStart && cinematicCamera != null;
            if (playAfterOpening && !played)
            {
                if (NmjOpening.IsPlaying) openingSeen = true;
                else if (openingSeen && NmjOpening.FreshStart) Play();
            }
            if (!IsPlaying) return;

            Keyboard kb = Keyboard.current;
            if (!ending && kb != null && kb.escapeKey.wasPressedThisFrame) { End(); return; }

            time += Time.deltaTime;
            float k = Mathf.Clamp01(time / duration);
            float pos = Mathf.Clamp01(progress.Evaluate(k));
            if (dolly != null) dolly.CameraPosition = pos;
            UpdateBars(Mathf.Clamp01(time / 0.8f));

            // 장소에 도착하면 자막 (여러 지점을 한 번에 지나면 마지막 것만)
            if (captionAt != null && time >= captionStartDelay)
            {
                string show = null;
                while (nextCaption < captionAt.Length && pos >= captionAt[nextCaption])
                {
                    show = placeCaptions[nextCaption].text;
                    nextCaption++;
                }
                if (!ending && !string.IsNullOrEmpty(show)) NmjSubtitle.Caption(show, placeCaptionTime);
            }
            if (!ending && time >= duration + endHold) End();
        }

        // 칼집의 칼처럼 다른 스크립트가 매 프레임 다시 켜는 것도 장면 동안은 계속 숨긴다
        private void LateUpdate()
        {
            if (!IsPlaying) return;
            foreach (var r in hiddenRenderers) if (r != null && r.enabled) r.enabled = false;
        }

        /// <summary>장면을 처음부터 튼다 (플레이 중 인스펙터 메뉴로도 미리 볼 수 있다)</summary>
        [ContextMenu("미리 보기")]
        public void Play()
        {
            if (!Application.isPlaying || IsPlaying || cinematicCamera == null) return;
            played = true;
            Pending = false;
            IsPlaying = true;
            ending = false;
            time = 0f;
            ControlsOverlay.MenuOpen = true;

            // 플레이어 카메라를 멈추고 씨네머신이 메인 카메라를 움직이게 한다
            Camera main = Camera.main;
            playerCamera = FindAnyObjectByType<ThirdPersonCamera>();
            if (playerCamera != null) playerCamera.enabled = false;
            if (main != null)
            {
                brain = main.GetComponent<CinemachineBrain>();
                addedBrain = brain == null;
                if (addedBrain) brain = main.gameObject.AddComponent<CinemachineBrain>();
                brain.enabled = true;
                brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
                NmjHingedDoor.ExtraOpener = main.transform;
                NmjHingedDoor.ExtraOpenRadius = doorOpenRadius;
            }
            if (dolly != null) dolly.CameraPosition = 0f;
            PrepareCaptions();
            cinematicCamera.gameObject.SetActive(true);
            cinematicCamera.Prioritize();

            HidePlayerHud(true);
            BuildBars();
            UpdateBars(0f);
            if (!string.IsNullOrEmpty(caption)) NmjSubtitle.Caption(caption, 3.5f);
        }

        private void End()
        {
            if (ending) return;
            ending = true;
            // 먹물로 덮고, 덮인 사이에 플레이어 카메라로 돌아온다
            NMJ.InkTransition.Play(endTransition, Restore, 0.9f, 1.1f, 0.2f);
        }

        private void Restore()
        {
            if (cinematicCamera != null) cinematicCamera.gameObject.SetActive(false);
            if (brain != null)
            {
                if (addedBrain) Destroy(brain);
                else brain.enabled = false;
            }
            brain = null;
            if (playerCamera != null) playerCamera.enabled = true;
            NmjHingedDoor.ExtraOpener = null;
            HidePlayerHud(false);
            if (barCanvas != null) barCanvas.gameObject.SetActive(false);
            ControlsOverlay.MenuOpen = false;
            IsPlaying = false;
        }

        private void OnDisable()
        {
            Pending = false;
            if (IsPlaying) Restore();
        }

        // 장소 자막 지점(점 번호)을 길 위 0~1 위치로 바꿔 둔다
        private void PrepareCaptions()
        {
            nextCaption = 0;
            captionAt = null;
            if (placeCaptions == null || placeCaptions.Length == 0 || dolly == null || dolly.Spline == null) return;
            var spline = dolly.Spline.Spline;
            captionAt = new float[placeCaptions.Length];
            for (int i = 0; i < placeCaptions.Length; i++)
                captionAt[i] = spline.ConvertIndexUnit(Mathf.Clamp(placeCaptions[i].knot, 0f, spline.Count - 1),
                    UnityEngine.Splines.PathIndexUnit.Knot, UnityEngine.Splines.PathIndexUnit.Normalized);
        }

        // 장면 동안 플레이어(몸·옷·칼·가방), 플레이어 몸 아래 HUD 캔버스, 바닥의 길 가이드를 숨긴다 (대사·먹물 전환은 그대로)
        private void HidePlayerHud(bool hide)
        {
            if (hide)
            {
                hiddenCanvases.Clear();
                hiddenRenderers.Clear();
                hiddenWasOn.Clear();
                hiddenGuides.Clear();
                foreach (var g in FindObjectsByType<PathGuide>(FindObjectsSortMode.None))
                    if (g.showGuide) { g.showGuide = false; hiddenGuides.Add(g); }
                var player = FindAnyObjectByType<PlayerController>();
                if (player == null) return;
                Transform root = player.transform.root;
                foreach (var c in root.GetComponentsInChildren<Canvas>())
                    if (c.isRootCanvas && c.enabled && c.renderMode != RenderMode.WorldSpace) { c.enabled = false; hiddenCanvases.Add(c); }
                // 지금 꺼져 있는 것(칼집의 칼 등)도 넣는다 — 무기 스크립트가 장면 도중에 다시 켤 수 있어서
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (r is ParticleSystemRenderer) continue;
                    hiddenRenderers.Add(r);
                    hiddenWasOn.Add(r.enabled);
                    r.enabled = false;
                }
            }
            else
            {
                foreach (var c in hiddenCanvases) if (c != null) c.enabled = true;
                for (int i = 0; i < hiddenRenderers.Count; i++)
                    if (hiddenRenderers[i] != null) hiddenRenderers[i].enabled = hiddenWasOn[i];   // 원래 켜져 있던 것만 다시 켬
                hiddenWasOn.Clear();
                foreach (var g in hiddenGuides) if (g != null) g.showGuide = true;
                hiddenCanvases.Clear();
                hiddenRenderers.Clear();
                hiddenGuides.Clear();
            }
        }

        // ───────── 위아래 먹 띠 ─────────

        private void BuildBars()
        {
            if (letterbox <= 0f) return;
            if (barCanvas != null) { barCanvas.gameObject.SetActive(true); return; }
            var go = new GameObject("NGH_IntroLetterbox", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            barCanvas = go.AddComponent<Canvas>();
            barCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            barCanvas.sortingOrder = 72;   // HUD 위, 자막(70)·대사(76)·먹물 전환 아래
            var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 1f;
            topBar = Bar("Top", go.transform, 1f);
            bottomBar = Bar("Bottom", go.transform, 0f);
        }

        private static RectTransform Bar(string name, Transform parent, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, y);
            rt.anchorMax = new Vector2(1f, y);
            rt.pivot = new Vector2(0.5f, y);
            rt.anchoredPosition = Vector2.zero;
            var img = go.AddComponent<UnityEngine.UI.Image>();
            img.color = new Color(0.02f, 0.02f, 0.025f, 1f);
            img.raycastTarget = false;
            return rt;
        }

        private void UpdateBars(float k)
        {
            if (topBar == null) return;
            float h = letterbox * Mathf.SmoothStep(0f, 1f, k);
            topBar.sizeDelta = new Vector2(0f, h);
            bottomBar.sizeDelta = new Vector2(0f, h);
        }
    }
}
