using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 시간 역행 중 검은 먹 연기: 본체가 먹으로 흘러내려 사라지는 동안 몸에서 검은 연기 기둥이 피어오르고,
    /// 되감기 이동 내내 그 연기 덩어리가 경로를 따라 날아가며 뒤에 먹 연기 자국과 먹 방울을 흩뿌린다.
    /// 도착해 다시 나타나기 시작하면 더 뿜지 않고 남은 연기만 가장자리부터 말라 사라진다.
    ///  - 연기: NGH/FX/InkSplat 셰이더(M_Ink_Splat 복제) + 코드로 만든 뭉게구름 텍스처 — 수명이 줄면 가장자리부터 먹이 마름
    ///  - 먹 방울: M_Ink_Drop, 중력으로 떨어짐
    /// 파티클은 월드 공간이라 지나간 자리에 연기가 남는다. 플레이어(TimeRewind 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    [DefaultExecutionOrder(1260)]
    public class RewindInkSmoke : MonoBehaviour
    {
        [Header("참조 (비우면 자동)")]
        public TimeRewind rewind;
        [Tooltip("연기 머티리얼 원본 (NGH/FX/InkSplat). 텍스처는 코드로 만든 뭉게구름으로 바꿔 씀")]
        public Material smokeMaterial;
        [Tooltip("먹 방울 머티리얼 (M_Ink_Drop)")]
        public Material dropMaterial;

        [Header("타이밍")]
        [Tooltip("발동 뒤 연기가 최대로 짙어지기까지(초) — 본체 디졸브 시간과 맞춤")]
        [Min(0.01f)] public float rampIn = 0.8f;
        [Tooltip("되감기 이동이 이만큼 진행되면 연기를 멈춤 (본체가 다시 나타나기 시작하는 시점과 맞춤)")]
        [Range(0.5f, 1f)] public float stopAtProgress = 0.99f;

        [Header("전체 크기")]
        [Tooltip("연기·먹 방울 전체 크기 배율 (연기 조각 크기, 기둥 굵기·높이, 퍼지는 속도에 모두 곱함). 1 = 몸 크기 연기 기둥")]
        [Range(0.05f, 1.5f)] public float effectScale = 0.3f;
        [Tooltip("먹선(InkTrail)이 없을 때 연기 덩어리 중심 높이(m, 발 기준). 있으면 먹선 높이를 따름")]
        public float centerHeight = 0.65f;

        [Header("연기 기둥")]
        [Tooltip("연기 기둥 높이(m) — 캐릭터 키")]
        public float columnHeight = 1.3f;
        [Tooltip("연기 기둥 굵기(m)")]
        public float columnWidth = 0.55f;
        [Tooltip("초당 뿜는 연기 수")]
        public float smokeRate = 90f;
        [Tooltip("이동 1m 당 추가로 남기는 연기 수 (지나간 자리 자국)")]
        public float smokePerMeter = 14f;
        public Vector2 smokeSize = new Vector2(0.35f, 0.85f);
        public Vector2 smokeLifetime = new Vector2(0.4f, 0.9f);
        [Tooltip("연기가 위로 피어오르는 속도(m/s)")]
        public float riseSpeed = 0.6f;
        public Color smokeColor = new Color(0.02f, 0.02f, 0.025f, 1f);
        [Tooltip("연기 가장자리 흐림 (클수록 부드러운 연기, 작을수록 먹 덩어리처럼 또렷)")]
        [Range(0.01f, 0.4f)] public float edgeSoftness = 0.22f;
        [Range(0f, 1f)] public float smokeOpacity = 0.9f;

        [Header("먹 방울")]
        public float dropRate = 30f;
        public Vector2 dropSize = new Vector2(0.06f, 0.2f);
        public Vector2 dropSpeed = new Vector2(1.5f, 4.5f);

        ParticleSystem _smoke, _drops;
        Transform _root;
        InkTrail _trail;
        Material _smokeMat;
        Texture2D _cloudTex;
        bool _active;
        float _startTime;

        void Awake()
        {
            if (!rewind) rewind = GetComponent<TimeRewind>();
        }

        void OnEnable()
        {
            if (!rewind) return;
            rewind.RewindStarted += OnStarted;
            rewind.RewindFinished += OnFinished;
        }

        void OnDisable()
        {
            if (rewind)
            {
                rewind.RewindStarted -= OnStarted;
                rewind.RewindFinished -= OnFinished;
            }
            StopEmitting();
        }

        void OnDestroy()
        {
            if (_root) Destroy(_root.gameObject);
            if (_smokeMat) Destroy(_smokeMat);
            if (_cloudTex) Destroy(_cloudTex);
        }

        void Start() => Build();

        void OnStarted()
        {
            if (!_smoke) return;
            _active = true;
            _startTime = Time.time;
            _root.position = EmitPoint();
            SetRates(0f);
            _smoke.Play(); _drops.Play();
        }

        void OnFinished() => StopEmitting();

        void StopEmitting()
        {
            _active = false;
            if (_smoke) _smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_drops) _drops.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void LateUpdate()
        {
            if (!_root) return;
            _root.SetPositionAndRotation(EmitPoint(), Quaternion.identity);   // 연기 덩어리는 먹선 끝을 따라감 (파티클은 월드에 남음)
            if (!_active) return;
            if (rewind.RewindTravelProgress >= stopAtProgress && Time.time - _startTime > 0.3f) { StopEmitting(); return; }
            // 몸이 녹아 사라지는 만큼 연기가 짙어짐
            float k = Mathf.SmoothStep(0f, 1f, (Time.time - _startTime) / rampIn);
            SetRates(k);
        }

        // 연기가 나오는 점 = 먹선이 그려지는 자리: 되감기 중에는 떠오른 몸이 아니라 경로 위 발 위치 + 먹선 높이
        Vector3 EmitPoint()
        {
            Vector3 feet = rewind && rewind.IsRewinding ? rewind.RewindPathPoint : transform.position;
            if (!_trail) _trail = GetComponent<InkTrail>();
            return feet + Vector3.up * (_trail ? _trail.LineHeight : centerHeight);
        }

        void SetRates(float k)
        {
            var e = _smoke.emission;
            e.rateOverTime = smokeRate * k;
            e.rateOverDistance = smokePerMeter * k;
            var d = _drops.emission;
            d.rateOverTime = dropRate * k;
        }

        #region 만들기

        void Build()
        {
            if (_root) return;
            _root = new GameObject("NGH_RewindInkSmoke").transform;
            _root.position = EmitPoint();

            _cloudTex = CloudTexture(128);
            if (smokeMaterial)
            {
                _smokeMat = new Material(smokeMaterial) { name = "NGH_RewindSmoke" };
                _smokeMat.SetTexture("_MainTex", _cloudTex);
                if (_smokeMat.HasProperty("_InkColor")) _smokeMat.SetColor("_InkColor", smokeColor);
                if (_smokeMat.HasProperty("_Edge")) _smokeMat.SetFloat("_Edge", edgeSoftness);       // 가장자리를 흐리게 → 연기처럼
                if (_smokeMat.HasProperty("_Opacity")) _smokeMat.SetFloat("_Opacity", smokeOpacity);
            }

            _smoke = NewSystem("Smoke", _smokeMat);
            {
                var m = _smoke.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(smokeLifetime.x, smokeLifetime.y);
                m.startSize = new ParticleSystem.MinMaxCurve(smokeSize.x * effectScale, smokeSize.y * effectScale);
                m.startSpeed = new ParticleSystem.MinMaxCurve(0.1f * effectScale, 0.6f * effectScale);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startColor = Color.white;
                m.maxParticles = 600;
                var s = _smoke.shape;
                s.shapeType = ParticleSystemShapeType.Box;
                s.scale = new Vector3(columnWidth, columnHeight, columnWidth) * effectScale;
                s.position = Vector3.zero;   // 기준점(_root)이 이미 먹선 높이
                s.randomDirectionAmount = 1f;
                var v = _smoke.velocityOverLifetime;
                v.enabled = true; v.space = ParticleSystemSimulationSpace.World;
                // 세 축 모두 같은 방식(두 값 사이 랜덤)이어야 함
                v.x = new ParticleSystem.MinMaxCurve(-0.15f * effectScale, 0.15f * effectScale); v.z = new ParticleSystem.MinMaxCurve(-0.15f * effectScale, 0.15f * effectScale);
                v.y = new ParticleSystem.MinMaxCurve(riseSpeed * 0.5f * effectScale, riseSpeed * effectScale);
                var n = _smoke.noise;
                n.enabled = true; n.strength = 0.7f * effectScale; n.frequency = 1.1f / Mathf.Max(0.05f, effectScale); n.scrollSpeed = 0.6f; n.octaveCount = 2;
                var sz = _smoke.sizeOverLifetime;
                sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.55f), new Keyframe(0.35f, 1f), new Keyframe(1f, 1.35f)));
                var rot = _smoke.rotationOverLifetime;
                rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1.2f, 1.2f);
                var col = _smoke.colorOverLifetime;   // 정점 알파 = 남은 먹 → 가장자리부터 말라 사라짐
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(0.75f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.7f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            }

            _drops = NewSystem("Drops", dropMaterial);
            {
                var m = _drops.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.8f);
                m.startSize = new ParticleSystem.MinMaxCurve(dropSize.x * effectScale, dropSize.y * effectScale);
                m.startSpeed = new ParticleSystem.MinMaxCurve(dropSpeed.x * effectScale, dropSpeed.y * effectScale);
                m.gravityModifier = 1.4f * effectScale;
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startColor = Color.white;
                m.maxParticles = 300;
                var s = _drops.shape;
                s.shapeType = ParticleSystemShapeType.Sphere;
                s.radius = columnWidth * 0.6f * effectScale;
                s.position = Vector3.zero;   // 기준점(_root)이 이미 먹선 높이
                var col = _drops.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
            }
        }

        ParticleSystem NewSystem(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = ps.main;
            m.playOnAwake = false;
            m.loop = true;
            m.simulationSpace = ParticleSystemSimulationSpace.World;   // 지나간 자리에 연기가 남음
            m.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var e = ps.emission; e.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = mat;
            r.sortMode = ParticleSystemSortMode.Distance;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        // 뭉게구름 모양 (알파 = 안쪽 정도: 가운데 1 → 가장자리 0, 가장자리를 노이즈로 울퉁불퉁하게)
        static Texture2D CloudTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "NGH_RewindSmokeCloud", wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            float seed = Random.Range(0f, 50f);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                float ang = Mathf.Atan2(v, u);
                // 둘레를 따라 몽글몽글한 반지름 + 안쪽 얼룩
                float lobes = 0.78f + 0.12f * Mathf.PerlinNoise(Mathf.Cos(ang) * 1.6f + seed, Mathf.Sin(ang) * 1.6f + seed)
                                    + 0.08f * Mathf.PerlinNoise(Mathf.Cos(ang) * 4f + seed * 2f, Mathf.Sin(ang) * 4f + 7f);
                float r = Mathf.Sqrt(u * u + v * v) / lobes;
                float inner = Mathf.Clamp01(1f - r);
                float mottle = Mathf.PerlinNoise(u * 3f + seed, v * 3f - seed) * 0.35f + 0.65f;
                float a = Mathf.Clamp01(Mathf.Pow(inner, 0.7f) * mottle * 1.25f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return tex;
        }

        #endregion
    }
}
