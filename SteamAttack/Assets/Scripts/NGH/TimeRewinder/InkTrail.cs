using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 시간 역행 먹선 궤적 (리그 오브 레전드 '에코'의 잔상처럼): 플레이어 몸통(척추 뼈)에서 검은 붓 자국이 뻗어 나와
    /// 최근 5초(시간 역행 기록 길이) 동안 지나온 경로를 따라 남음.
    ///  - 평상시: 몸통 높이에 굵고 진한 붓 자국이 남았다가 마른 붓 끝처럼 갈라지며 사라짐 (항상 카메라를 향하는 띠)
    ///  - 시간 역행 발동: 5초 궤도 전체가 짙은 먹색으로 드러나며 현재 위치 → 과거 방향으로 번지고,
    ///    플레이어가 먹선을 거꾸로 따라 이동하는 만큼(TimeRewind.RewindTravelProgress) 먹선이 5초 전 위치로 빨려 들어감
    ///  - 경로는 TimeRewind 의 기록을 그대로 사용 (끝점 = 실제로 돌아가는 위치). 일정 간격으로 다시 나눠 매끈하게 그림
    /// 플레이어(TimeRewind 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    [DefaultExecutionOrder(1300)]
    public class InkTrail : MonoBehaviour
    {
        [Header("참조 (비우면 자동)")]
        public TimeRewind rewind;
        [Tooltip("먹선 머티리얼 (비우면 NGH/Ink 셰이더로 만듦)")]
        public Material inkMaterial;
        [Tooltip("먹선이 바라볼 카메라 (비우면 플레이어 카메라)")]
        public Camera viewCamera;
        [Tooltip("먹선이 나오는 몸통 뼈 (비우면 아래 이름으로 찾음)")]
        public Transform bodyBone;
        public string bodyBoneName = "Spine1";
        [Tooltip("몸통 뼈를 못 찾았을 때의 높이 (발 기준, m)")]
        public float bodyHeight = 0.62f;
        public Color inkColor = new Color(0.02f, 0.02f, 0.025f, 1f);

        [Header("평상시 — 남는 먹선")]
        [Tooltip("먹선 폭(m)")]
        public float normalWidth = 0.3f;
        [Tooltip("먹선 길이를 시간 역행 기록 길이(5초)에 맞춤 → 먹선 꼬리 끝 = 시간 역행으로 돌아가는 위치")]
        public bool matchRewindLength = true;
        [Tooltip("(위를 끈 경우) 지나간 뒤 이 시간(초)이 지나면 완전히 사라짐")]
        public float normalLifetime = 3f;
        [Tooltip("지나간 뒤 이 시간(초)부터 붓 끝처럼 갈라지며 옅어지기 시작")]
        public float normalFadeStart = 2f;
        [Tooltip("꼬리 끝(가장 오래된 지점)에 남는 먹 진하기 — 돌아갈 위치가 보이도록 (0 = 다 사라짐)")]
        [Range(0f, 1f)] public float tailInk = 0.45f;
        [Tooltip("몸통에서 이 거리(m)까지 가늘게 시작해 굵어짐 (몸에서 뻗어 나오는 느낌)")]
        public float bodyFade = 0.35f;

        [Header("시간 역행 — 짙은 먹선")]
        public float rewindWidth = 0.42f;
        [Tooltip("연출 시간 중 '역방향 번짐'에 쓰는 비율 (이 동안 5초 궤도 전체가 드러남)")]
        [Range(0.05f, 0.6f)] public float spreadPortion = 0.25f;
        [Tooltip("번지는 앞쪽 / 빨려가는 끝이 굵어지는 정도 (폭 대비)")]
        public float frontBulge = 0.45f;
        [Tooltip("굵어지는 부분의 길이(m)")]
        public float frontLength = 0.4f;
        [Tooltip("연출 동안 플레이어를 숨김 (끄면 먹선을 따라 날아가는 모습이 보임)")]
        public bool hidePlayerDuringRewind = false;
        [Tooltip("먹물 덩어리 크기(m) — 출발 자리 / 도착 자리")]
        public float blotSize = 1.1f;
        [Tooltip("도착한 뒤 먹물 덩어리가 사라지는 시간(초)")]
        public float blotFadeTime = 0.5f;

        [Header("모양")]
        [Tooltip("경로를 다시 나누는 간격(m) — 작을수록 매끈")]
        public float sampleSpacing = 0.06f;
        [Tooltip("붓 무늬가 반복되는 길이(m)")]
        public float textureLength = 1.4f;
        [Tooltip("폭이 들쭉날쭉한 정도")]
        [Range(0f, 0.8f)] public float widthNoise = 0.3f;
        [Tooltip("먹선이 위아래로 일렁이는 폭(m)")]
        public float wobble = 0.03f;
        [Tooltip("카메라에서 이 거리(m) 안의 먹은 보이지 않음")]
        public float cameraFadeNear = 0.5f;
        [Tooltip("카메라에서 이 거리(m)부터 원래 진하기")]
        public float cameraFadeFar = 1.6f;

        struct Info { public Vector3 pos; public float cum; }

        readonly List<Vector3> _positions = new List<Vector3>();
        readonly List<float> _times = new List<float>();
        readonly Dictionary<float, Info> _cache = new Dictionary<float, Info>();
        float _lastCum; float _lastTime = float.NegativeInfinity; Vector3 _lastPos;
        float _bodyOffset = -1f;

        // 원본 점 (오래된 것 → 최근) → 일정 간격으로 다시 나눈 점
        readonly List<Vector3> _srcPos = new List<Vector3>();
        readonly List<float> _srcCum = new List<float>();
        readonly List<float> _srcTime = new List<float>();
        readonly List<Vector3> _sPos = new List<Vector3>();
        readonly List<float> _sCum = new List<float>();
        readonly List<float> _sTime = new List<float>();
        readonly List<Vector3> _sTan = new List<Vector3>();

        // 역행 연출용 (시작 순간 복사)
        readonly List<Vector3> _rwPos = new List<Vector3>();
        readonly List<float> _rwCum = new List<float>();
        readonly List<Vector3> _rwTan = new List<Vector3>();
        float _rwLen;
        bool _rewinding; float _rwX;

        Mesh _mesh; GameObject _meshGo;
        Material _mat, _blotMat;
        readonly List<Vector3> _v = new List<Vector3>();
        readonly List<Vector2> _uv = new List<Vector2>();
        readonly List<Color> _c = new List<Color>();
        readonly List<int> _tri = new List<int>();
        Vector3 _lastSide = Vector3.right;
        Vector3 _camPos;

        class Blot { public Transform t; public Mesh mesh; public Color[] colors = new Color[4]; public float spin; }
        Blot _blotFrom, _blotTo;
        readonly List<Renderer> _hidden = new List<Renderer>();
        Coroutine _fx;

        static Texture2D s_trailTex, s_blotTex;

        void Awake()
        {
            if (!rewind) rewind = GetComponent<TimeRewind>();
            if (!bodyBone && !string.IsNullOrEmpty(bodyBoneName))
                foreach (var t in GetComponentsInChildren<Transform>(true))
                    if (t.name == bodyBoneName) { bodyBone = t; break; }
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
            if (_fx != null) { StopCoroutine(_fx); _fx = null; }
            _rewinding = false;
            ShowPlayer();
            SetBlot(_blotFrom, Vector3.zero, 0f, 0f);
            SetBlot(_blotTo, Vector3.zero, 0f, 0f);
            if (_mesh) _mesh.Clear();
        }

        void OnDestroy()
        {
            if (_meshGo) Destroy(_meshGo);
            if (_blotFrom != null && _blotFrom.t) Destroy(_blotFrom.t.gameObject);
            if (_blotTo != null && _blotTo.t) Destroy(_blotTo.t.gameObject);
            if (_mesh) Destroy(_mesh);
            if (_mat) Destroy(_mat);
            if (_blotMat) Destroy(_blotMat);
        }

        void LateUpdate()
        {
            if (!rewind || !Prepare()) return;
            if (!viewCamera)
            {
                var tpc = FindAnyObjectByType<ThirdPersonCamera>();
                viewCamera = tpc ? tpc.GetComponent<Camera>() : Camera.main;
            }
            _camPos = viewCamera ? viewCamera.transform.position : transform.position + Vector3.back * 5f;
            // 몸통 높이 (평상시 동작 기준으로 천천히 따라감 — 공격·구르기로 출렁이지 않게)
            if (!_rewinding)
            {
                float h = bodyBone ? Mathf.Clamp(bodyBone.position.y - transform.position.y, 0.1f, 3f) : bodyHeight;
                _bodyOffset = _bodyOffset < 0f ? h : Mathf.Lerp(_bodyOffset, h, 1f - Mathf.Exp(-3f * Time.deltaTime));
            }
            _mat.color = inkColor;
            _blotMat.color = inkColor;
            if (_rewinding) BuildRewind(_rwX);
            else BuildNormal();
            FaceCamera(_blotFrom);
            FaceCamera(_blotTo);
        }

    #region 평상시

        void BuildNormal()
        {
            rewind.GetRecordedPath(_positions, _times);
            int n = _positions.Count;
            BeginStrip();
            if (n < 1) { EndStrip(); return; }
            UpdateCache();

            float now = Time.time;
            // 맞춤: 기록 전체(가장 오래된 기록 = 돌아갈 위치)까지 그림
            float lifetime = matchRewindLength ? Mathf.Max(0.1f, now - _times[0]) : normalLifetime;
            float minInk = matchRewindLength ? tailInk : 0f;
            int start = 0;
            if (!matchRewindLength) while (start < n - 1 && now - _times[start] > lifetime) start++;

            _srcPos.Clear(); _srcCum.Clear(); _srcTime.Clear();
            for (int i = start; i < n; i++)
            {
                var info = _cache[_times[i]];
                _srcPos.Add(info.pos); _srcCum.Add(info.cum); _srcTime.Add(_times[i]);
            }
            // 지금 몸통 위치까지 이어서 몸에서 바로 뻗어 나오게
            Vector3 body = Body(transform.position);
            var newest = _cache[_times[n - 1]];
            _srcPos.Add(body); _srcCum.Add(newest.cum + Vector3.Distance(newest.pos, body)); _srcTime.Add(now);

            Resample(_srcPos, _srcCum, _srcTime, _sPos, _sCum, _sTime);
            int m = _sPos.Count;
            if (m < 2) { EndStrip(); return; }
            Tangents(_sPos, _sTan);
            float bodyCum = _sCum[m - 1];
            float fadeStart = Mathf.Min(normalFadeStart, lifetime * 0.8f);
            float fadeLen = Mathf.Max(0.01f, lifetime - fadeStart);
            float tailCum = _sCum[0];

            for (int k = 0; k < m; k++)
            {
                float cum = _sCum[k];
                float age = now - _sTime[k];
                float life = 1f - Smooth01((age - fadeStart) / fadeLen);
                float grow = Smooth01((bodyCum - cum) / Mathf.Max(0.01f, bodyFade));
                float tail = Smooth01((cum - tailCum) / 0.3f);
                // 진하기(정점 알파) = 붓결이 남는 정도 → 오래될수록 마른 붓 끝처럼 갈라지며 옅어짐 (맞춤이면 꼬리 끝까지 남음)
                float ink = Mathf.Lerp(Mathf.Max(0.15f, minInk), 1f, life) * Mathf.Lerp(0.55f, 1f, grow) * Mathf.Lerp(0.6f, 1f, tail);
                if (life <= 0.001f && minInk <= 0f) ink = 0f;
                float w = normalWidth * Noise(cum) * Mathf.Lerp(0.6f, 1f, life) * Mathf.Lerp(0.25f, 1f, grow);
                AddPoint(_sPos[k] + Wobble(cum, now), _sTan[k], cum, w, ink);
            }
            EndStrip();
        }

        // 기록마다 몸통 위치와 누적 거리(무늬가 미끄러지지 않게)를 한 번만 계산해 둠
        void UpdateCache()
        {
            for (int i = 0; i < _positions.Count; i++)
            {
                float t = _times[i];
                if (_cache.ContainsKey(t)) continue;
                Vector3 p = Body(_positions[i]);
                float cum = float.IsNegativeInfinity(_lastTime) ? 0f : _lastCum + Vector3.Distance(_lastPos, p);
                _cache[t] = new Info { pos = p, cum = cum };
                if (t > _lastTime) { _lastTime = t; _lastCum = cum; _lastPos = p; }
            }
            if (_cache.Count > _positions.Count + 64)
            {
                var keep = new HashSet<float>(_times);
                var remove = new List<float>();
                foreach (var key in _cache.Keys) if (!keep.Contains(key)) remove.Add(key);
                foreach (var key in remove) _cache.Remove(key);
            }
        }

        Vector3 Body(Vector3 feet) => feet + Vector3.up * (_bodyOffset > 0f ? _bodyOffset : bodyHeight);

        // 누적 거리(cum) 기준으로 일정 간격 다시 나누기 (제자리에 몰린 점·꺾임 때문에 띠가 깨지지 않게)
        void Resample(List<Vector3> pos, List<float> cum, List<float> extra, List<Vector3> oPos, List<float> oCum, List<float> oExtra)
        {
            oPos.Clear(); oCum.Clear(); oExtra?.Clear();
            int n = pos.Count;
            if (n == 0) return;
            float step = Mathf.Max(0.01f, sampleSpacing);
            float c0 = cum[0], c1 = cum[n - 1];
            if (c1 - c0 < step) return;
            int seg = 0;
            for (float c = c0; ; c += step)
            {
                bool last = c >= c1;
                if (last) c = c1;
                while (seg < n - 2 && cum[seg + 1] < c) seg++;
                float f = cum[seg + 1] > cum[seg] ? Mathf.Clamp01((c - cum[seg]) / (cum[seg + 1] - cum[seg])) : 1f;
                oPos.Add(Vector3.Lerp(pos[seg], pos[seg + 1], f));
                oCum.Add(c);
                oExtra?.Add(Mathf.Lerp(extra[seg], extra[seg + 1], f));
                if (last) break;
            }
        }

        // 앞뒤 몇 점을 보고 정한 진행 방향 (꺾이는 곳에서도 부드럽게)
        static void Tangents(List<Vector3> pos, List<Vector3> tan)
        {
            tan.Clear();
            int n = pos.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 d = pos[Mathf.Min(i + 2, n - 1)] - pos[Mathf.Max(i - 2, 0)];
                tan.Add(d.sqrMagnitude > 1e-8f ? d.normalized : (i > 0 ? tan[i - 1] : Vector3.forward));
            }
        }

    #endregion

    #region 시간 역행

        void OnRewindStarted()
        {
            if (!Prepare()) return;
            rewind.GetRecordedPath(_positions, _times);
            UpdateCache();
            _srcPos.Clear(); _srcCum.Clear(); _srcTime.Clear();
            for (int i = 0; i < _positions.Count; i++)
            {
                var info = _cache[_times[i]];
                _srcPos.Add(info.pos); _srcCum.Add(info.cum); _srcTime.Add(0f);
            }
            Vector3 now = Body(transform.position);
            float lastCum = _srcCum.Count > 0 ? _srcCum[_srcCum.Count - 1] : 0f;
            _srcPos.Add(now); _srcCum.Add(lastCum + (_srcPos.Count > 1 ? Vector3.Distance(_srcPos[_srcPos.Count - 2], now) : 0f)); _srcTime.Add(0f);
            Resample(_srcPos, _srcCum, null, _rwPos, _rwCum, null);
            Tangents(_rwPos, _rwTan);
            _rwLen = _rwCum.Count > 1 ? _rwCum[_rwCum.Count - 1] - _rwCum[0] : 0f;

            if (_fx != null) StopCoroutine(_fx);
            _fx = StartCoroutine(RewindFx(Mathf.Max(0.05f, rewind.RewindTravelTime), now, Body(rewind.RewindTargetPosition)));
        }

        void OnRewindFinished()
        {
            // 기록이 지워졌으므로 평상시 먹선도 새로 시작
            _cache.Clear();
            _lastTime = float.NegativeInfinity; _lastCum = 0f;
            _rewinding = false;
            ShowPlayer();
        }

        IEnumerator RewindFx(float duration, Vector3 fromPos, Vector3 toPos)
        {
            _rewinding = true;
            _rwX = 0f;
            if (hidePlayerDuringRewind) HidePlayer();
            _blotFrom.spin = Random.Range(0f, 360f); _blotTo.spin = Random.Range(0f, 360f);

            for (float t = 0f; t < duration; t += Time.deltaTime)
            {
                float x = t / duration;
                _rwX = x;
                // 출발 자리: 몸에서 먹물이 확 터졌다가 갈라지며 사라짐
                float sA = blotSize * EaseOutCubic(Mathf.Clamp01(x / (spreadPortion * 0.5f)));
                float aA = 1f - Smooth01((x - spreadPortion * 0.3f) / spreadPortion);
                SetBlot(_blotFrom, fromPos, sA, aA);
                // 도착 자리: 빨려 온 먹물이 모여 덩어리가 커짐
                float k = rewind.RewindTravelProgress;
                SetBlot(_blotTo, toPos, blotSize * Mathf.Lerp(0.1f, 0.8f, k * k), Smooth01(x / spreadPortion) * Mathf.Lerp(0.5f, 1f, k));
                yield return null;
            }
            _rwX = 1f;
            _rewinding = false;
            SetBlot(_blotFrom, fromPos, 0f, 0f);
            ShowPlayer();

            for (float t = 0f; t < blotFadeTime; t += Time.deltaTime)
            {
                float f = t / blotFadeTime;
                SetBlot(_blotTo, toPos, blotSize * Mathf.Lerp(0.8f, 1.1f, EaseOutCubic(f)), 1f - Smooth01(f));
                yield return null;
            }
            SetBlot(_blotTo, toPos, 0f, 0f);
            _fx = null;
        }

        void BuildRewind(float x)
        {
            BeginStrip();
            int n = _rwPos.Count;
            float L = _rwLen;
            if (n < 2 || L < 0.02f) { EndStrip(); return; }
            float c0 = _rwCum[0];

            // 번짐: 현재 위치(L) → 과거(0) 쪽으로 드러남 / 빨려 감: 플레이어가 경로를 따라간 만큼 현재 쪽 끝이 줄어듦
            float a = L * (1f - Smooth01(x / spreadPortion));
            float b = L * (1f - Mathf.Clamp01(rewind.RewindTravelProgress));
            if (b - a < 0.02f) { EndStrip(); return; }
            float appear = Smooth01(x / 0.05f);
            float fl = Mathf.Max(0.01f, frontLength);

            for (int i = 0; i < n; i++)
            {
                float s = _rwCum[i] - c0;
                if (s < a || s > b) continue;
                float ends = Smooth01((s - a) / 0.3f) * Smooth01((b - s) / 0.3f);   // 양 끝: 붓 끝처럼 갈라짐
                float ga = (s - (a + fl * 0.7f)) / fl, gb = (s - (b - fl * 0.7f)) / fl;
                float bulge = frontBulge * (Mathf.Exp(-gb * gb) + (a > 0.01f ? Mathf.Exp(-ga * ga) : 0f));
                float w = rewindWidth * (Noise(_rwCum[i]) + bulge) * Mathf.Lerp(0.55f, 1f, ends);
                AddPoint(_rwPos[i] + Wobble(_rwCum[i], Time.time * 2f), _rwTan[i], _rwCum[i], w, appear * Mathf.Lerp(0.3f, 1f, ends));
            }
            EndStrip();
        }

        void HidePlayer()
        {
            _hidden.Clear();
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r.forceRenderingOff) continue;
                r.forceRenderingOff = true;
                _hidden.Add(r);
            }
        }

        void ShowPlayer()
        {
            foreach (var r in _hidden) if (r) r.forceRenderingOff = false;
            _hidden.Clear();
        }

    #endregion

    #region 메시

        bool Prepare()
        {
            if (_mesh) return true;
            var shader = Shader.Find("NGH/Ink");
            if (!inkMaterial && !shader) return false;
            _mat = inkMaterial ? new Material(inkMaterial) : new Material(shader);
            _mat.mainTexture = TrailTexture();
            _blotMat = new Material(_mat) { mainTexture = BlotTexture() };

            _meshGo = new GameObject("NGH_InkTrail");
            _mesh = new Mesh { name = "NGH_InkTrail" };
            _mesh.MarkDynamic();
            _meshGo.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = _meshGo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            _blotFrom = MakeBlot("NGH_InkBlot_From");
            _blotTo = MakeBlot("NGH_InkBlot_To");
            return true;
        }

        // 카메라를 향하는 사각형 (먹물 덩어리)
        Blot MakeBlot(string name)
        {
            var go = new GameObject(name);
            var m = new Mesh { name = name };
            m.SetVertices(new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) });
            m.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            m.SetTriangles(new[] { 0, 2, 1, 1, 2, 3 }, 0);
            m.colors = new Color[4];
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _blotMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.SetActive(false);
            return new Blot { t = go.transform, mesh = m };
        }

        void SetBlot(Blot b, Vector3 pos, float size, float ink)
        {
            if (b == null || !b.t) return;
            bool on = size > 0.001f && ink > 0.001f;
            b.t.gameObject.SetActive(on);
            if (!on) return;
            b.t.position = pos;
            b.t.localScale = new Vector3(size, size, 1f);
            if (viewCamera) ink *= CameraFade(Vector3.Distance(viewCamera.transform.position, pos) - size * 0.25f);
            var c = new Color(1f, 1f, 1f, Mathf.Clamp01(ink));
            for (int i = 0; i < 4; i++) b.colors[i] = c;
            b.mesh.colors = b.colors;
            FaceCamera(b);
        }

        void FaceCamera(Blot b)
        {
            if (b == null || !b.t || !b.t.gameObject.activeSelf || !viewCamera) return;
            var cam = viewCamera.transform;
            b.t.rotation = Quaternion.LookRotation(cam.forward, cam.up) * Quaternion.Euler(0f, 0f, b.spin);
        }

        void BeginStrip() { _v.Clear(); _uv.Clear(); _c.Clear(); _tri.Clear(); _lastSide = Vector3.zero; }

        // 진행 방향과 카메라 방향 모두에 수직인 쪽으로 폭을 펼침 → 항상 카메라를 향하는 띠
        void AddPoint(Vector3 p, Vector3 tan, float cum, float width, float ink)
        {
            Vector3 toCam = (_camPos - p).normalized;
            Vector3 side = Vector3.Cross(tan, toCam);
            // 먹선이 카메라 쪽을 곧장 향하면 방향이 불안정해지므로 수평 방향 폭으로 섞음
            float facing = Mathf.Clamp01(side.magnitude / 0.5f);
            Vector3 flat = Vector3.Cross(Vector3.up, tan);
            if (flat.sqrMagnitude < 1e-8f) flat = _lastSide.sqrMagnitude > 0f ? _lastSide : Vector3.right;
            if (side.sqrMagnitude > 1e-8f && Vector3.Dot(flat, side) < 0f) flat = -flat;
            side = Vector3.Lerp(flat.normalized, side.sqrMagnitude > 1e-8f ? side.normalized : flat.normalized, facing);
            if (side.sqrMagnitude < 1e-8f) side = _lastSide.sqrMagnitude > 0f ? _lastSide : Vector3.right;
            side.Normalize();
            // 이웃 점과 같은 쪽을 보게 (띠가 꼬이지 않게) + 급하게 돌지 않게 살짝 섞음
            if (_lastSide.sqrMagnitude > 0f)
            {
                if (Vector3.Dot(side, _lastSide) < 0f) side = -side;
                side = Vector3.Slerp(_lastSide, side, 0.5f).normalized;
            }
            _lastSide = side;

            ink *= CameraFade((_camPos - p).magnitude);
            Vector3 half = side * (width * 0.5f);
            int i = _v.Count;
            _v.Add(p - half); _v.Add(p + half);
            float u = cum / Mathf.Max(0.01f, textureLength);
            _uv.Add(new Vector2(u, 0f)); _uv.Add(new Vector2(u, 1f));
            var col = new Color(1f, 1f, 1f, Mathf.Clamp01(ink));
            _c.Add(col); _c.Add(col);
            if (i >= 2)
            {
                _tri.Add(i - 2); _tri.Add(i); _tri.Add(i - 1);
                _tri.Add(i - 1); _tri.Add(i); _tri.Add(i + 1);
            }
        }

        void EndStrip()
        {
            _mesh.Clear();
            if (_tri.Count == 0) return;
            _mesh.SetVertices(_v);
            _mesh.SetUVs(0, _uv);
            _mesh.SetColors(_c);
            _mesh.SetTriangles(_tri, 0);
            _mesh.RecalculateBounds();
        }

    #endregion

    #region 붓 무늬 (코드로 생성) — 알파 = 먹 농도 (셰이더가 농도가 낮은 붓결부터 깎아 냄)

        // 가로(u) 방향으로 반복되는 붓 자국: 꽉 찬 먹 + 가장자리로 갈수록 끊어지는 붓결 + 안쪽의 흰 붓결 틈
        static Texture2D TrailTexture()
        {
            if (s_trailTex) return s_trailTex;
            const int W = 512, H = 128;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "NGH_InkBrush", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            var px = new Color32[W * H];
            var rng = new System.Random(11);
            // 줄(붓털)마다 세기·끊김 정도·씨앗
            var bristle = new float[H]; var gapFreq = new float[H]; var gapCut = new float[H]; var seed = new float[H];
            for (int y = 0; y < H; y++)
            {
                bristle[y] = (float)rng.NextDouble();
                gapFreq[y] = 1f + (float)rng.NextDouble() * 5f;
                gapCut[y] = 0.25f + (float)rng.NextDouble() * 0.45f;
                seed[y] = (float)rng.NextDouble() * 100f;
            }
            for (int y = 0; y < H; y++)
            {
                float v = y / (H - 1f);
                float d = Mathf.Abs(v * 2f - 1f);   // 0 = 가운데, 1 = 가장자리
                for (int x = 0; x < W; x++)
                {
                    float u = x / (float)W;
                    float core = 0.5f + 0.18f * Loop(u, 3f, 1.7f) + 0.1f * Loop(u, 11f, 4.2f);
                    float dens;
                    if (d < core)
                    {
                        dens = 0.85f + 0.15f * Loop(u, 7f, seed[y] * 0.1f);
                        // 안쪽에 드문드문 흰 붓결 틈
                        if (bristle[y] < 0.07f && Loop(u, gapFreq[y], seed[y]) > 0.45f) dens = 0.08f;
                    }
                    else
                    {
                        // 가장자리: 붓털 한 올씩 끊어졌다 이어지는 붓결
                        float edge = 1f - (d - core) / Mathf.Max(0.01f, 1f - core);
                        float on = Loop(u, gapFreq[y], seed[y]) > gapCut[y] ? 1f : 0f;
                        dens = bristle[y] > 0.3f ? on * (0.25f + 0.65f * bristle[y]) * Mathf.Lerp(0.35f, 1f, edge) : 0f;
                        if (d > 0.97f) dens = 0f;
                    }
                    px[y * W + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(dens) * 255f));
                }
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return s_trailTex = tex;
        }

        // 먹물 덩어리: 가운데가 진하고 바깥으로 옅어지는 울퉁불퉁한 얼룩 + 주변 먹 방울 (사라질 때 바깥부터 깎임)
        static Texture2D BlotTexture()
        {
            if (s_blotTex) return s_blotTex;
            const int S = 128;
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, true) { name = "NGH_InkBlot", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[S * S];
            var drops = new Vector3[12];
            var rng = new System.Random(7);
            for (int i = 0; i < drops.Length; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f, r = 0.55f + (float)rng.NextDouble() * 0.35f;
                drops[i] = new Vector3(Mathf.Cos(ang) * r, Mathf.Sin(ang) * r, 0.03f + (float)rng.NextDouble() * 0.07f);
            }
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float px2 = (x + 0.5f) / S * 2f - 1f, py = (y + 0.5f) / S * 2f - 1f;
                    float d = Mathf.Sqrt(px2 * px2 + py * py);
                    float ang01 = (Mathf.Atan2(py, px2) / (Mathf.PI * 2f)) + 0.5f;
                    float rb = 0.45f + 0.16f * Loop(ang01, 5f, 2.3f) + 0.09f * Loop(ang01, 13f, 6.1f);
                    float dens = Mathf.Clamp01((rb - d) / rb * 1.6f) * (0.85f + 0.15f * Mathf.PerlinNoise(px2 * 5f + 11f, py * 5f + 5f));
                    if (d < rb && dens < 0.2f) dens = 0.2f;
                    foreach (var dr in drops)
                    {
                        float dd = Vector2.Distance(new Vector2(px2, py), new Vector2(dr.x, dr.y));
                        if (dd < dr.z) dens = Mathf.Max(dens, 0.35f + 0.4f * (1f - dd / dr.z));
                    }
                    if (d > 0.98f) dens = 0f;
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(dens) * 255f));
                }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return s_blotTex = tex;
        }

        // u(0~1)가 한 바퀴 돌면 이어지는 노이즈 (텍스처 반복 이음매 없음)
        static float Loop(float u, float freq, float seed)
        {
            float ang = u * Mathf.PI * 2f, r = freq / (Mathf.PI * 2f);
            return Mathf.PerlinNoise(seed + Mathf.Cos(ang) * r, seed * 1.37f + Mathf.Sin(ang) * r);
        }

    #endregion

        // 카메라 바로 앞의 먹은 지움 (카메라가 먹선을 따라 이동할 때 화면을 가리지 않게)
        float CameraFade(float dist) => Smooth01((dist - cameraFadeNear) / Mathf.Max(0.01f, cameraFadeFar - cameraFadeNear));
        float Noise(float cum) => 1f + widthNoise * (Mathf.PerlinNoise(cum * 2.5f, 3.3f) * 2f - 1f);
        Vector3 Wobble(float cum, float time) => Vector3.up * (wobble * (Mathf.PerlinNoise(cum * 1.3f, time * 0.6f) * 2f - 1f));
        static float Smooth01(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }
        static float EaseOutCubic(float x) { x = 1f - Mathf.Clamp01(x); return 1f - x * x * x; }
    }
}
