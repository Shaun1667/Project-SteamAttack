using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 시간 역행 먹 잔상 연출.
    ///  1) Q 를 누르면 플레이어 본체가 먹이 스며들며 디졸브로 사라진다 (NGH/InkDissolve, 텍스처 모드).
    ///  2) 되감기 이동(TimeRewind Direct)이 기록 경로를 거꾸로 따라가는 동안, 기록 시간 1초 간격(ghostInterval)마다
    ///     그 시점에 플레이어가 있던 자리에 검은 먹 형상의 잔상이 나타난다. 나타나는 순간 이외에는 보이지 않는다.
    ///  3) 잔상은 그 자리에서 해당 구간(그 시점 → 1초 전)의 기록된 동작을 거꾸로 재생하고, 끝 자세로 멈춘 채 남는다.
    ///  4) 이동이 끝나면 잔상들이 차례로 디졸브되어 사라지고, 본체는 도착 자리에서 먹이 걷히듯 다시 나타난다.
    /// 잔상은 플레이어 모델(Animator 가 있는 Hero_Bound)을 복제한 뒤 뼈대·지정한 SkinnedMeshRenderer 만 남겨 만들며
    /// (스크립트·클로스·파티클·콜라이더는 모두 제거), 자세는 TimeRewind 의 기록을 같은 컨트롤러로 재생해 맞춘다.
    /// 플레이어(TimeRewind 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    [DefaultExecutionOrder(1250)]
    public class RewindInkGhosts : MonoBehaviour
    {
        [Header("참조 (비우면 자동)")]
        public TimeRewind rewind;
        [Tooltip("잔상의 본이 될 모델 (Animator 가 붙은 오브젝트, 비우면 플레이어 애니메이터)")]
        public Animator sourceModel;
        [Tooltip("디졸브 셰이더 (비우면 NGH/InkDissolve)")]
        public Shader dissolveShader;

        [Header("본체 디졸브")]
        [Tooltip("발동 때 본체가 사라지는 시간(초)")]
        [Min(0.01f)] public float playerDissolveOut = 1.0f;
        [Tooltip("도착 뒤 본체가 다시 나타나는 시간(초)")]
        [Min(0.01f)] public float playerDissolveIn = 1.0f;

        [Header("잔상")]
        [Tooltip("잔상을 남길 기록 시간 간격(초)")]
        [Min(0.2f)] public float ghostInterval = 1f;
        [Tooltip("잔상 하나가 자기 구간의 동작을 (거꾸로) 재생하는 데 걸리는 실제 시간(초)")]
        [Min(0.05f)] public float segmentPlayTime = 0.6f;
        [Tooltip("잔상이 먹으로 번져 나타나는 시간(초)")]
        [Min(0.01f)] public float ghostAppear = 0.35f;
        [Tooltip("이동이 끝난 뒤 잔상이 사라지는 시간(초)")]
        [Min(0.01f)] public float ghostDissolveOut = 0.9f;
        [Tooltip("잔상들이 사라질 때 하나씩 어긋나는 간격(초). 먼저 생긴 것부터")]
        [Min(0f)] public float ghostDissolveStagger = 0.1f;
        [Tooltip("되돌아갈 자리에서 이 시간(초) 안쪽의 잔상은 만들지 않음 (본체가 그 자리에 나타나므로)")]
        [Min(0f)] public float skipNearTarget = 0.3f;
        [Tooltip("최대 잔상 수 (풀 크기)")]
        [Range(1, 12)] public int maxGhosts = 8;
        [Tooltip("잔상에 남길 렌더러 이름 (비우면 모델의 모든 SkinnedMeshRenderer)")]
        public List<string> ghostRenderers = new List<string>
        {
            "Body", "Head", "OutterJeogori_SingleLayer", "OutterChima", "GatHatBase", "HarnessVest", "Sword", "SwordHouse",
        };

        [Header("먹 색")]
        public Color inkColor = new Color(0.02f, 0.02f, 0.025f, 1f);
        public Color inkRim = new Color(0.16f, 0.17f, 0.2f, 1f);
        [Range(0.5f, 40f)] public float noiseScale = 9f;
        [Range(0f, 0.3f)] public float edgeWidth = 0.05f;

        [Header("먹 흘러내림")]
        [Tooltip("캐릭터 키(m). 머리 → 발 방향으로 먹이 흘러내리는 기준")]
        [Min(0.1f)] public float bodyHeight = 1.3f;
        [Tooltip("키를 발동 때마다 실제 모습(갓 꼭대기)으로 잼 — 머리 꼭대기부터 바로 흐르기 시작하게. 끄면 위의 값을 씀")]
        public bool measureHeight = true;
        [Tooltip("되감기 이동이 이만큼 진행되면 (끝 무렵은 거의 움직이지 않으므로) 도착을 기다리지 않고 바로 나타나기 시작")]
        [Range(0.9f, 1f)] public float appearAtProgress = 0.99f;
        [Tooltip("먹 줄기 길이 — 클수록 줄기마다 흘러내리는 속도 차이가 커져 길게 늘어짐 (0 = 수평으로 깔끔하게 내려감)")]
        [Range(0f, 1f)] public float dripAmount = 0.35f;
        [Tooltip("먹 줄기 촘촘함 (클수록 가는 줄기가 많아짐)")]
        [Range(2f, 60f)] public float dripScale = 14f;
        [Tooltip("깎이기 전에 먹이 젖어 검게 물드는 띠 폭")]
        [Range(0.01f, 0.6f)] public float soakWidth = 0.22f;

        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");
        static readonly int InkModeId = Shader.PropertyToID("_InkMode");
        static readonly int InkColorId = Shader.PropertyToID("_InkColor");
        static readonly int InkRimId = Shader.PropertyToID("_InkRim");
        static readonly int NoiseScaleId = Shader.PropertyToID("_NoiseScale");
        static readonly int EdgeWidthId = Shader.PropertyToID("_EdgeWidth");
        static readonly int SeedId = Shader.PropertyToID("_Seed");
        static readonly int FlowDirId = Shader.PropertyToID("_FlowDir");
        static readonly int BaseYId = Shader.PropertyToID("_BaseY");
        static readonly int HeightId = Shader.PropertyToID("_Height");
        static readonly int CenterId = Shader.PropertyToID("_Center");
        static readonly int DripAmountId = Shader.PropertyToID("_DripAmount");
        static readonly int DripScaleId = Shader.PropertyToID("_DripScale");
        static readonly int SoakWidthId = Shader.PropertyToID("_SoakWidth");
        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        class Ghost
        {
            public GameObject root;      // 기록된 플레이어 루트 자세를 받는 껍데기
            public Animator anim;
            public Renderer[] renderers;
            public Renderer[] sourceRenderers;   // 켜짐/꺼짐을 따라갈 본체 쪽 렌더러
            public Material mat;
            public float dissolve = 1f;
            public bool active;
            public float playStart, fromTime, toTime;
            public bool playing;
            public Vector3 position;
        }

        readonly List<Ghost> _pool = new List<Ghost>();
        readonly List<Ghost> _live = new List<Ghost>();

        // 본체 디졸브용
        Renderer[] _playerRenderers = new Renderer[0];
        Material[][] _originalMats = new Material[0][];
        readonly Dictionary<Renderer, Material[]> _dissolveCache = new Dictionary<Renderer, Material[]>();
        bool[] _swapped = new bool[0];
        readonly List<Renderer> _hiddenExtras = new List<Renderer>();
        readonly List<Renderer> _lateHide = new List<Renderer>();
        bool _playerSwapped;
        Coroutine _playerFx, _ghostFx;

        // 이번 역행의 잔상 예약 (기록 시간, 경로 진행도)
        struct Planned { public float time; public float progress; public bool spawned; }
        readonly List<Planned> _plan = new List<Planned>();
        bool _rewinding;
        int _startFrame;
        float _height;      // 이번 발동에서 잰 키 (발 → 갓 꼭대기)

        // 지금 자세에서 발(루트) → 가장 높은 곳(갓 꼭대기)까지의 높이. 갓·머리 렌더러의 월드 경계 상자 윗면으로 잰다
        // (이 모델은 갓·머리 메시에 축소 스케일이 걸려 있어 메시를 구워 재면 값이 틀어짐)
        float MeasureHeight()
        {
            float maxY = float.NegativeInfinity;
            foreach (var smr in sourceModel.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (!smr.enabled || !smr.sharedMesh) continue;
                if (smr.name == "GatHatBase" || smr.name == "Head") maxY = Mathf.Max(maxY, smr.bounds.max.y);
            }
            float h = maxY - transform.position.y;
            return h > 0.5f && h < 4f ? h : bodyHeight;
        }

        void Awake()
        {
            if (!rewind) rewind = GetComponent<TimeRewind>();
            if (!sourceModel)
            {
                var pc = GetComponent<PlayerController>();
                sourceModel = pc && pc.animator ? pc.animator : GetComponentInChildren<Animator>();
            }
            if (!dissolveShader) dissolveShader = Shader.Find("NGH/InkDissolve");
        }

        void OnEnable()
        {
            if (!rewind) return;
            rewind.RewindStarted += OnRewindStarted;
            rewind.RewindFinished += OnRewindFinished;
        }

        void OnDisable()
        {
            if (rewind)
            {
                rewind.RewindStarted -= OnRewindStarted;
                rewind.RewindFinished -= OnRewindFinished;
            }
            if (_playerFx != null) StopCoroutine(_playerFx);
            if (_ghostFx != null) StopCoroutine(_ghostFx);
            _playerFx = _ghostFx = null;
            RestorePlayerMaterials();
            foreach (var g in _live) Recycle(g);
            _live.Clear();
            _rewinding = false;
        }

        void OnDestroy()
        {
            foreach (var g in _pool) { if (g.mat) Destroy(g.mat); if (g.root) Destroy(g.root); }
            _pool.Clear();
            foreach (var arr in _dissolveCache.Values) if (arr != null) foreach (var m in arr) if (m) Destroy(m);
            _dissolveCache.Clear();
        }

        void Start()
        {
            if (!dissolveShader || !sourceModel) { enabled = false; return; }
            // 첫 발동 때 끊기지 않게 풀을 미리 만든다
            for (int i = 0; i < maxGhosts; i++) _pool.Add(CreateGhost());
        }

        void LateUpdate()
        {
            if (!_rewinding) return;
            // RewindStarted 가 불린 프레임에는 진행도가 아직 지난 역행 값일 수 있으니 한 프레임 건너뜀
            if (Time.frameCount <= _startFrame) return;

            float u = rewind.RewindTravelProgress;
            // 도착 자리에 거의 다 왔으면 (끝 무렵은 거의 움직이지 않음) 기다리지 않고 바로 나타나기 시작
            if (u >= appearAtProgress) { BeginArrival(); return; }
            for (int i = 0; i < _plan.Count; i++)
            {
                if (_plan[i].spawned || u < _plan[i].progress) continue;
                var p = _plan[i]; p.spawned = true; _plan[i] = p;
                Spawn(p.time);
            }
            foreach (var g in _live) UpdateGhost(g);
        }

        #region 역행 시작·끝

        void OnRewindStarted()
        {
            if (!enabled || rewind.RecordCount < 2) return;
            _rewinding = true;
            _startFrame = Time.frameCount;

            // 지금 → 되돌아갈 시점까지 기록 시간 ghostInterval 마다 잔상. 경로 길이 기준 진행도로 바꿔 둔다
            _plan.Clear();
            int n = rewind.RecordCount;
            TimeRewind.Snapshot newest = rewind.GetSnapshot(n - 1), oldest = rewind.GetSnapshot(0);
            float span = newest.time - oldest.time;
            // 누적 거리 (최근 → 과거)
            var cum = new float[n];
            Vector3 prev = transform.position;
            float total = 0f;
            for (int i = n - 1; i >= 0; i--)
            {
                Vector3 p = rewind.GetSnapshot(i).position;
                total += Vector3.Distance(prev, p);
                cum[i] = total;
                prev = p;
            }
            bool byDistance = total > 0.05f;
            for (float back = ghostInterval; back < span - skipNearTarget; back += ghostInterval)
            {
                float t = newest.time - back;
                float progress;
                if (byDistance)
                {
                    // t 를 감싸는 기록의 누적 거리 보간
                    int ia = 0;
                    for (int i = n - 1; i >= 0; i--) if (rewind.GetSnapshot(i).time <= t) { ia = i; break; }
                    int ib = Mathf.Min(ia + 1, n - 1);
                    float ta = rewind.GetSnapshot(ia).time, tb = rewind.GetSnapshot(ib).time;
                    float f = tb > ta ? Mathf.Clamp01((t - ta) / (tb - ta)) : 0f;
                    progress = Mathf.Lerp(cum[ia], cum[ib], f) / total;
                }
                else progress = back / span;
                // 경로상 되돌아갈 자리와 거의 같은 곳(서 있던 구간)은 본체가 나타나는 자리와 겹치므로 만들지 않음
                if (progress > 0.97f) continue;
                // 되돌아갈 자리 바로 옆(제자리에 서 있었던 경우 등)이면 본체가 나타나는 모습을 가리므로 만들지 않음
                if (rewind.SampleTransform(t, out Vector3 gp, out _) && (gp - rewind.RewindTargetPosition).sqrMagnitude < 0.6f * 0.6f) continue;
                _plan.Add(new Planned { time = t, progress = Mathf.Clamp01(progress) });
            }

            if (_ghostFx != null) { StopCoroutine(_ghostFx); _ghostFx = null; }
            foreach (var g in _live) Recycle(g);
            _live.Clear();

            _height = measureHeight ? MeasureHeight() : bodyHeight;
            if (_playerFx != null) StopCoroutine(_playerFx);
            _playerFx = StartCoroutine(PlayerDissolve(true));
        }

        void OnRewindFinished() => BeginArrival();

        // 도착: 잔상은 흘러내리며 사라지고 본체는 나타남. 진행도로 먼저 불렸으면 끝났을 때는 다시 하지 않음
        void BeginArrival()
        {
            if (!_rewinding) return;
            _rewinding = false;
            foreach (var g in _live) g.playing = false;
            if (_ghostFx != null) StopCoroutine(_ghostFx);
            _ghostFx = StartCoroutine(GhostsDissolveOut());
            if (_playerFx != null) StopCoroutine(_playerFx);
            _playerFx = StartCoroutine(PlayerDissolve(false));
        }

        #endregion

        #region 본체 디졸브

        // 발동할 때마다 플레이어 전체에서 다시 찾는다. 클로스 스크립트가 실행 중에 만드는 메시
        // (치마 뒤 접촉면 RearSkirtContactSurface 등)도 빠짐없이 같이 사라지게 하기 위함
        void PreparePlayerMaterials()
        {
            var list = new List<Renderer>();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (r is SkinnedMeshRenderer || r is MeshRenderer) list.Add(r);
            _playerRenderers = list.ToArray();
            _originalMats = new Material[list.Count][];
            _swapped = new bool[list.Count];
        }

        // 지금 쓰는 머티리얼을 기억하고, 같은 텍스처·색의 디졸브 머티리얼로 바꾼다 (_BaseMap 이 있는 것만. 나머지 렌더러는 그냥 숨김)
        void SwapPlayerMaterials()
        {
            if (_playerSwapped) return;
            _playerSwapped = true;
            _hiddenExtras.Clear();
            PreparePlayerMaterials();
            float seed = Random.Range(0f, 100f);
            for (int i = 0; i < _playerRenderers.Length; i++)
            {
                var r = _playerRenderers[i];
                if (!r) continue;
                var mats = r.sharedMaterials;
                _originalMats[i] = mats;
                bool ok = mats.Length > 0;
                foreach (var m in mats) if (!m || !m.HasProperty(BaseMapId)) { ok = false; break; }
                // 디졸브 셰이더로 못 바꾸는 것(증기 게이지 등)은 디졸브가 절반 지난 뒤 숨기고, 돌아올 때 절반에서 다시 보임
                if (!ok) { _swapped[i] = false; if (!r.forceRenderingOff) _lateHide.Add(r); continue; }

                _dissolveCache.TryGetValue(r, out var d);
                if (d == null || d.Length != mats.Length)
                {
                    if (d != null) foreach (var m in d) if (m) Destroy(m);
                    d = new Material[mats.Length];
                    for (int k = 0; k < d.Length; k++) d[k] = new Material(dissolveShader) { name = mats[k].name + "_InkDissolve" };
                    _dissolveCache[r] = d;
                }
                for (int k = 0; k < d.Length; k++)
                {
                    d[k].SetTexture(BaseMapId, mats[k].GetTexture(BaseMapId));
                    d[k].SetTextureScale(BaseMapId, mats[k].GetTextureScale(BaseMapId));
                    d[k].SetTextureOffset(BaseMapId, mats[k].GetTextureOffset(BaseMapId));
                    d[k].SetColor(BaseColorId, mats[k].HasProperty(BaseColorId) ? mats[k].GetColor(BaseColorId) : Color.white);
                    ApplyInkLook(d[k], 0f, seed);
                }
                r.sharedMaterials = d;
                _swapped[i] = true;
            }
            // 본체 파티클(증기 등)은 디졸브 동안 숨김
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                if (!(r is SkinnedMeshRenderer) && !(r is MeshRenderer) && !r.forceRenderingOff) { r.forceRenderingOff = true; _hiddenExtras.Add(r); }
        }

        void RestorePlayerMaterials()
        {
            if (!_playerSwapped) return;
            _playerSwapped = false;
            for (int i = 0; i < _playerRenderers.Length; i++)
                if (_swapped[i] && _playerRenderers[i] && _originalMats[i] != null) _playerRenderers[i].sharedMaterials = _originalMats[i];
            foreach (var r in _hiddenExtras) if (r) r.forceRenderingOff = false;
            _hiddenExtras.Clear();
            foreach (var r in _lateHide) if (r) r.forceRenderingOff = false;
            _lateHide.Clear();
        }

        // flowDir 0 = 사라짐(머리부터 먹이 흘러내리며 깎임), 1 = 나타남(머리부터 먹이 걷힘)
        void SetPlayerDissolve(float v, float flowDir)
        {
            Vector3 p = transform.position;   // 되감기 중 움직이고 떠오르는 것까지 따라감
            var center = new Vector4(p.x, p.z, 0f, 0f);
            for (int i = 0; i < _playerRenderers.Length; i++)
                if (_swapped[i] && _playerRenderers[i] && _dissolveCache.TryGetValue(_playerRenderers[i], out var d))
                    foreach (var m in d) { m.SetFloat(DissolveId, v); m.SetFloat(FlowDirId, flowDir); m.SetFloat(BaseYId, p.y); m.SetVector(CenterId, center); }
            bool hide = v >= 0.5f;
            foreach (var r in _lateHide) if (r && r.forceRenderingOff != hide) r.forceRenderingOff = hide;
        }

        IEnumerator PlayerDissolve(bool vanish)
        {
            SwapPlayerMaterials();
            float dur = vanish ? playerDissolveOut : playerDissolveIn;
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                // 먹이 흐르듯 일정한 속도로 내려감 (끝만 살짝 부드럽게)
                float x = t / dur;
                float e = Mathf.Lerp(x, Mathf.SmoothStep(0f, 1f, x), 0.4f);
                float v = vanish ? e : 1f - e;
                SetPlayerDissolve(v, vanish ? 0f : 1f);
                yield return null;
            }
            SetPlayerDissolve(vanish ? 1f : 0f, vanish ? 0f : 1f);
            if (!vanish) RestorePlayerMaterials();
            _playerFx = null;
        }

        void ApplyInkLook(Material m, float inkMode, float seed)
        {
            m.SetFloat(InkModeId, inkMode);
            m.SetColor(InkColorId, inkColor);
            m.SetColor(InkRimId, inkRim);
            m.SetFloat(NoiseScaleId, noiseScale);
            m.SetFloat(EdgeWidthId, edgeWidth);
            m.SetFloat(SeedId, seed);
            m.SetFloat(HeightId, _height > 0.1f ? _height : bodyHeight);
            m.SetFloat(DripAmountId, dripAmount);
            m.SetFloat(DripScaleId, dripScale);
            m.SetFloat(SoakWidthId, soakWidth);
        }

        #endregion

        #region 잔상

        Ghost CreateGhost()
        {
            // 비활성 부모 아래에 복제해 스크립트의 Awake/OnEnable 이 돌지 않게 한 뒤 필요한 것만 남긴다
            var root = new GameObject("NGH_RewindGhost");
            root.SetActive(false);
            var clone = Instantiate(sourceModel.gameObject, root.transform);
            clone.name = "Model";
            clone.transform.localPosition = sourceModel.transform.localPosition;
            clone.transform.localRotation = sourceModel.transform.localRotation;
            clone.transform.localScale = sourceModel.transform.localScale;

            // 남길 렌더러와 그 뼈대
            var srcRenderers = sourceModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var cloneRenderers = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var keep = new HashSet<Transform> { clone.transform };
            var kept = new List<Renderer>();
            var keptSrc = new List<Renderer>();
            for (int i = 0; i < cloneRenderers.Length; i++)
            {
                var r = cloneRenderers[i];
                if (ghostRenderers.Count > 0 && !ghostRenderers.Contains(r.name)) continue;
                kept.Add(r); keptSrc.Add(i < srcRenderers.Length ? srcRenderers[i] : null);
                for (var t = r.transform; t && t != clone.transform; t = t.parent) keep.Add(t);
                foreach (var b in r.bones) for (var t = b; t && t != clone.transform; t = t.parent) keep.Add(t);
                if (r.rootBone) for (var t = r.rootBone; t && t != clone.transform; t = t.parent) keep.Add(t);
            }
            // 뼈대·남길 렌더러가 아닌 오브젝트는 통째로 제거
            var all = clone.GetComponentsInChildren<Transform>(true);
            foreach (var t in all) if (t && !keep.Contains(t) && t.gameObject) DestroyImmediate(t.gameObject);
            // 남은 오브젝트에서 Transform·Animator(모델 루트)·남길 SkinnedMeshRenderer 이외의 컴포넌트 제거 (의존 관계 때문에 스크립트부터)
            foreach (var c in clone.GetComponentsInChildren<MonoBehaviour>(true)) if (c) DestroyImmediate(c);
            foreach (var c in clone.GetComponentsInChildren<Component>(true))
            {
                if (!c || c is Transform) continue;
                if (c is Animator && c.gameObject == clone) continue;
                if (c is SkinnedMeshRenderer && kept.Contains(c as Renderer)) continue;
                DestroyImmediate(c);
            }

            var anim = clone.GetComponent<Animator>();
            anim.applyRootMotion = false;
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.speed = 0f;

            var mat = new Material(dissolveShader) { name = "NGH_RewindGhost_Ink" };
            ApplyInkLook(mat, 1f, Random.Range(0f, 100f));
            mat.SetFloat(DissolveId, 1f);
            foreach (var r in kept)
            {
                var smr = (SkinnedMeshRenderer)r;
                var mats = new Material[smr.sharedMaterials.Length];
                for (int k = 0; k < mats.Length; k++) mats[k] = mat;
                smr.sharedMaterials = mats;
                smr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                smr.updateWhenOffscreen = true;
                smr.skinnedMotionVectors = false;
            }
            return new Ghost { root = root, anim = anim, renderers = kept.ToArray(), sourceRenderers = keptSrc.ToArray(), mat = mat };
        }

        void Spawn(float time)
        {
            Ghost g = null;
            foreach (var p in _pool) if (!p.active) { g = p; break; }
            if (g == null) return;
            if (!rewind.SampleTransform(time, out Vector3 pos, out Quaternion rot)) return;

            g.active = true;
            g.position = pos;
            g.root.transform.SetPositionAndRotation(pos, rot);
            // 본체 쪽 렌더러의 켜짐/꺼짐(검을 뽑았는지 등)을 따라감
            for (int i = 0; i < g.renderers.Length; i++)
                g.renderers[i].enabled = g.sourceRenderers[i] ? g.sourceRenderers[i].enabled && g.sourceRenderers[i].gameObject.activeInHierarchy : true;
            g.root.SetActive(true);
            g.anim.speed = 0f;
            ApplyInkLook(g.mat, 1f, Random.Range(0f, 100f));
            g.mat.SetFloat(BaseYId, pos.y);
            g.mat.SetVector(CenterId, new Vector4(pos.x, pos.z, 0f, 0f));
            g.mat.SetFloat(FlowDirId, 1f);   // 머리부터 먹이 번져 형상이 드러남
            g.dissolve = 1f;
            g.mat.SetFloat(DissolveId, 1f);
            // 그 시점 → ghostInterval 전까지의 동작을 거꾸로
            g.fromTime = time;
            g.toTime = Mathf.Max(rewind.GetSnapshot(0).time, time - ghostInterval);
            g.playStart = Time.time;
            g.playing = true;
            rewind.SampleAnimatorPose(g.anim, time);
            _live.Add(g);
        }

        void UpdateGhost(Ghost g)
        {
            // 먹이 번져 나타남
            if (g.dissolve > 0f)
            {
                g.dissolve = Mathf.MoveTowards(g.dissolve, 0f, Time.deltaTime / ghostAppear);
                g.mat.SetFloat(DissolveId, g.dissolve);
            }
            if (!g.playing) return;
            float x = Mathf.Clamp01((Time.time - g.playStart) / segmentPlayTime);
            float t = Mathf.Lerp(g.fromTime, g.toTime, x);
            rewind.SampleAnimatorPose(g.anim, t);
            if (rewind.SampleTransform(t, out _, out Quaternion rot)) g.root.transform.rotation = rot;   // 자리는 고정, 방향만 따라 돎
            if (x >= 1f) g.playing = false;   // 끝 자세로 남음
        }

        IEnumerator GhostsDissolveOut()
        {
            var list = new List<Ghost>(_live);
            foreach (var g in list) g.mat.SetFloat(FlowDirId, 0f);   // 머리부터 먹이 흘러내리며 사라짐
            float dur = ghostDissolveOut + ghostDissolveStagger * Mathf.Max(0, list.Count - 1);
            for (float t = 0f; t < dur; t += Time.deltaTime)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    float local = Mathf.Clamp01((t - ghostDissolveStagger * i) / ghostDissolveOut);
                    float v = Mathf.Max(list[i].dissolve, Mathf.SmoothStep(0f, 1f, local));
                    list[i].mat.SetFloat(DissolveId, v);
                }
                yield return null;
            }
            foreach (var g in list) Recycle(g);
            _live.Clear();
            _ghostFx = null;
        }

        void Recycle(Ghost g)
        {
            if (!g.active) return;
            g.active = false;
            g.playing = false;
            g.dissolve = 1f;
            if (g.mat) g.mat.SetFloat(DissolveId, 1f);
            if (g.root) g.root.SetActive(false);
        }

        #endregion
    }
}
