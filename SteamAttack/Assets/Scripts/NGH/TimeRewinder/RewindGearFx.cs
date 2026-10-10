using System.Collections;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 시간 역행 화면 연출: 화면 사방(가장자리 바깥)에서 기어가 튀어나와 돌다가 다시 밖으로 빠져나감.
    ///  - TimeRewind.RewindStarted 에 맞춰 재생, 길이 = 되감기 이동 시간(Rewind Travel Time)
    ///  - 기어는 카메라 앞(Distance)에 붙여 두므로 화면에 고정됨 (되감기 중 카메라가 움직여도 같은 자리)
    ///  - 화면 가운데는 비워 두고 가장자리를 둘러싸도록 배치. 이웃한 기어끼리 반대 방향으로 돎
    /// 플레이어(TimeRewind 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class RewindGearFx : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public TimeRewind rewind;
        [Tooltip("기어를 붙일 카메라 (비우면 플레이어 카메라)")]
        public Camera targetCamera;

        [Header("기어")]
        [Tooltip("사용할 기어 모델들 (돌아가며 사용)")]
        public GameObject[] gearModels;
        [Tooltip("화면에 나오는 기어 개수")]
        [Range(1, 40)] public int count = 14;
        [Tooltip("기어 지름 범위 (화면 높이 대비 비율, 0.3 = 화면 높이의 30%)")]
        public Vector2 sizeRange = new Vector2(0.22f, 0.42f);
        [Tooltip("카메라에서 기어까지 거리(m). 카메라 Near Clip(0.3)보다 커야 보임")]
        public float distance = 0.8f;
        [Tooltip("기어가 화면 안쪽으로 들어오는 정도 (지름 대비, 0.5 = 절반만 보임)")]
        [Range(0.1f, 1f)] public float reveal = 0.55f;

        [Header("움직임 (연출 시간 0~1 기준)")]
        [Tooltip("튀어나오는 데 걸리는 비율")]
        [Range(0.05f, 0.5f)] public float enterPortion = 0.22f;
        [Tooltip("빠져나가는 데 걸리는 비율")]
        [Range(0.05f, 0.5f)] public float exitPortion = 0.22f;
        [Tooltip("기어마다 나오는 순간을 엇갈리게 하는 최대 지연 비율")]
        [Range(0f, 0.3f)] public float stagger = 0.12f;
        [Tooltip("튀어나올 때 넘쳤다 돌아오는 정도 (0 = 없음)")]
        [Range(0f, 3f)] public float overshoot = 1.6f;
        [Tooltip("회전 속도 범위(도/초). 이웃한 기어는 반대 방향")]
        public Vector2 spinSpeedRange = new Vector2(240f, 520f);
        [Tooltip("연출 길이를 직접 정함 (0 이하면 시간 역행의 되감기 이동 시간을 따름)")]
        public float durationOverride = 0f;

        class Gear
        {
            public Transform t;
            public Quaternion baseRot;      // 기어 회전축 → 카메라 앞 방향
            public Vector3 from, to;        // 카메라 기준 위치 (화면 밖 → 가장자리)
            public float scale, spin, delay, angle;
        }

        Gear[] _gears = new Gear[0];
        Transform _holder;
        Coroutine _play;

        void Awake()
        {
            if (!rewind) rewind = GetComponent<TimeRewind>();
        }

        void OnEnable() { if (rewind) rewind.RewindStarted += OnRewindStarted; }

        void OnDisable()
        {
            if (rewind) rewind.RewindStarted -= OnRewindStarted;
            StopEffect();
        }

        void OnRewindStarted()
        {
            float d = durationOverride > 0f ? durationOverride : (rewind ? rewind.RewindTravelTime : 1f);
            if (d <= 0f) return;
            Play(d);
        }

        /// <summary>연출 재생 (seconds 동안)</summary>
        public void Play(float seconds)
        {
            if (!Prepare()) return;
            if (_play != null) StopCoroutine(_play);
            _play = StartCoroutine(PlayRoutine(seconds));
        }

        IEnumerator PlayRoutine(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                Evaluate(t / seconds, t);
                yield return null;
            }
            StopEffect();
        }

        void StopEffect()
        {
            if (_play != null) { StopCoroutine(_play); _play = null; }
            if (_holder) _holder.gameObject.SetActive(false);
        }

        /// <summary>기어를 만들고(처음 한 번) 이번 재생의 위치·크기·회전을 새로 정함</summary>
        public bool Prepare()
        {
            if (!targetCamera)
            {
                var tpc = FindAnyObjectByType<ThirdPersonCamera>();
                targetCamera = tpc ? tpc.GetComponent<Camera>() : Camera.main;
            }
            if (!targetCamera || gearModels == null || gearModels.Length == 0) return false;

            if (!_holder)
            {
                _holder = new GameObject("NGH_RewindGears").transform;
                _holder.SetParent(targetCamera.transform, false);
            }
            if (_gears.Length != count) BuildGears();
            _holder.gameObject.SetActive(true);

            // 카메라 앞 distance 거리 평면에서 보이는 반 너비·반 높이
            float halfH = distance * Mathf.Tan(targetCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float halfW = halfH * targetCamera.aspect;
            float perimeter = 4f * (halfW + halfH);
            float offset = Random.value;   // 매번 배치가 조금씩 달라지게

            for (int i = 0; i < _gears.Length; i++)
            {
                var g = _gears[i];
                // 화면 테두리를 따라 고르게 (조금씩 흔듦)
                float s = Mathf.Repeat((i + offset + Random.Range(-0.25f, 0.25f)) / _gears.Length, 1f) * perimeter;
                Vector2 edge, normal;
                PerimeterPoint(s, halfW, halfH, out edge, out normal);
                float diameter = Random.Range(sizeRange.x, sizeRange.y) * 2f * halfH;
                float r = diameter * 0.5f;
                g.scale = diameter;
                g.to = new Vector3(edge.x - normal.x * r * (reveal * 2f - 1f), edge.y - normal.y * r * (reveal * 2f - 1f), distance);
                g.from = new Vector3(edge.x + normal.x * r * 1.4f, edge.y + normal.y * r * 1.4f, distance);
                g.spin = Random.Range(spinSpeedRange.x, spinSpeedRange.y) * (i % 2 == 0 ? 1f : -1f);
                g.delay = Random.Range(0f, stagger);
                g.angle = Random.Range(0f, 360f);
                g.t.localScale = Vector3.one * g.scale;
            }
            Evaluate(0f, 0f);
            return true;
        }

        /// <summary>u = 연출 진행도(0~1), seconds = 지난 시간(초, 회전용)</summary>
        public void Evaluate(float u, float seconds)
        {
            foreach (var g in _gears)
            {
                float enterEnd = g.delay + enterPortion;
                float exitStart = 1f - exitPortion - (stagger - g.delay) * 0.5f;   // 늦게 나온 기어는 조금 늦게 나감
                float k;   // 0 = 화면 밖, 1 = 자리
                if (u < g.delay) k = 0f;
                else if (u < enterEnd) k = EaseOutBack((u - g.delay) / enterPortion);
                else if (u < exitStart) k = 1f;
                else k = 1f - EaseInBack(Mathf.Clamp01((u - exitStart) / Mathf.Max(0.01f, 1f - exitStart)));
                g.t.localPosition = Vector3.LerpUnclamped(g.from, g.to, k);
                g.t.localRotation = Quaternion.AngleAxis(g.angle + g.spin * seconds, Vector3.forward) * g.baseRot;
            }
        }

        void BuildGears()
        {
            foreach (var g in _gears) if (g != null && g.t) Destroy(g.t.gameObject);
            _gears = new Gear[count];
            for (int i = 0; i < count; i++)
            {
                var model = gearModels[i % gearModels.Length];
                var go = Instantiate(model, _holder);
                go.name = "Gear" + i;
                foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r.receiveShadows = false;
                }
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                _gears[i] = new Gear { t = go.transform, baseRot = AxisToForward(go) };
            }
        }

        // 기어의 가장 얇은 축(회전축)을 카메라 앞 방향(로컬 +Z)으로
        static Quaternion AxisToForward(GameObject go)
        {
            var mf = go.GetComponentInChildren<MeshFilter>();
            if (!mf || !mf.sharedMesh) return Quaternion.identity;
            Vector3 size = mf.sharedMesh.bounds.size;
            Vector3 axis = size.x <= size.y && size.x <= size.z ? Vector3.right : size.y <= size.z ? Vector3.up : Vector3.forward;
            return Quaternion.FromToRotation(axis, Vector3.forward);
        }

        // 화면 테두리(카메라 기준 직사각형) 위의 점과 바깥쪽 방향. s = 둘레를 따라간 거리
        static void PerimeterPoint(float s, float hw, float hh, out Vector2 p, out Vector2 n)
        {
            float top = 2f * hw, right = 2f * hh;
            if (s < top) { p = new Vector2(-hw + s, hh); n = Vector2.up; return; }
            s -= top;
            if (s < right) { p = new Vector2(hw, hh - s); n = Vector2.right; return; }
            s -= right;
            if (s < top) { p = new Vector2(hw - s, -hh); n = Vector2.down; return; }
            s -= top;
            p = new Vector2(-hw, -hh + s); n = Vector2.left;
        }

        float EaseOutBack(float x)
        {
            float c1 = overshoot, c3 = c1 + 1f;
            x = Mathf.Clamp01(x) - 1f;
            return 1f + c3 * x * x * x + c1 * x * x;
        }

        float EaseInBack(float x)
        {
            float c1 = overshoot, c3 = c1 + 1f;
            x = Mathf.Clamp01(x);
            return c3 * x * x * x - c1 * x * x;
        }
    }
}
