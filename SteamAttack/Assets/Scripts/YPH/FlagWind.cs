using UnityEngine;

namespace YPH
{
    /// <summary>깃발의 왼쪽 끝을 고정하고 바람에 따라 천 메시를 변형합니다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FlagWind : MonoBehaviour
    {
        [SerializeField, Tooltip("방향과 세기를 읽을 Directional WindZone입니다. 비우면 아래 기본 바람을 사용합니다. 바람 오브젝트를 끄면 흔들림도 멈춥니다.")]
        private WindZone _windZone;
        [SerializeField, Tooltip("WindZone이 없을 때의 월드 바람 방향입니다. (1,0,0)은 +X 방향이며, (0,0,0)이면 멈춥니다.")]
        private Vector3 _windDirection = new Vector3(1f, 0f, .35f);
        [SerializeField, Range(0f, 3f), Tooltip("WindZone이 없을 때의 바람 세기입니다. 0은 정지, 1은 보통이며 높일수록 크게 흔들립니다.")]
        private float _windStrength = .8f;
        [SerializeField, Range(0f, .2f), Tooltip("깃발 너비에 대한 물결 크기 비율입니다. 0.06~0.1 권장. 높이면 끝부분이 크게 출렁입니다.")]
        private float _amplitude = .075f;
        [SerializeField, Range(.1f, 5f), Tooltip("초당 물결 횟수입니다. 1~2 권장. 높이면 빠르게 펄럭입니다.")]
        private float _frequency = 1.6f;
        [SerializeField, Range(0f, .3f), Tooltip("왼쪽에서 고정할 너비 비율입니다. 기본 0.02. 고정 영역은 원래 위치를 유지합니다.")]
        private float _pinnedWidth = .02f;

        private MeshFilter _filter;
        private Mesh _source, _animated;
        private Vector3[] _rest, _vertices;
        private float _minX, _width, _minY, _height, _phase;

        private void OnEnable()
        {
            if (_animated == null)
            {
                _filter = GetComponent<MeshFilter>();
                _source = _filter.sharedMesh;
                if (_source == null || !_source.isReadable || _source.bounds.size.x < .000001f)
                {
                    Debug.LogError("FlagWind에는 읽기 가능한 XY 평면의 깃발 천 메시가 필요합니다.", this);
                    enabled = false;
                    return;
                }
                _rest = _source.vertices;
                _vertices = new Vector3[_rest.Length];
                _minX = _source.bounds.min.x;
                _width = _source.bounds.size.x;
                _minY = _source.bounds.min.y;
                _height = Mathf.Max(_source.bounds.size.y, .000001f);
                _animated = Instantiate(_source);
                _animated.name = _source.name + " (바람)";
                _animated.MarkDynamic();
            }
            _phase = Mathf.Repeat(Vector3.Dot(transform.position, new Vector3(.73f, .37f, 1.13f)), Mathf.PI * 2f);
            _filter.sharedMesh = _animated;
        }

        private void Update() => Deform(Time.time);

        private void Deform(float time)
        {
            if (_animated == null) return;
            Vector3 direction = _windDirection;
            float strength = Mathf.Max(0f, _windStrength);
            float turbulence = .3f;
            if (_windZone != null)
            {
                direction = _windZone.transform.forward;
                strength = _windZone.gameObject.activeInHierarchy && _windZone.mode == WindZoneMode.Directional
                    ? Mathf.Max(0f, _windZone.windMain) : 0f;
                float pulse = .5f + .5f * Mathf.Sin(time * Mathf.Max(0f, _windZone.windPulseFrequency) * Mathf.PI * 2f);
                strength *= 1f + Mathf.Max(0f, _windZone.windPulseMagnitude) * pulse;
                turbulence = Mathf.Clamp01(_windZone.windTurbulence);
            }
            strength = Mathf.Clamp(strength, 0f, 3f);
            Vector3 up = transform.up;
            Vector3 flow = Vector3.ProjectOnPlane(direction, up);
            if (flow.sqrMagnitude < .000001f) strength = 0f;
            flow.Normalize();
            Vector3 right = transform.right;
            Vector3 side = Vector3.Cross(up, right);
            float width = transform.TransformVector(Vector3.right * _width).magnitude;
            float angle = Mathf.Clamp(Vector3.SignedAngle(right, flow, up), -120f, 120f) * Mathf.Deg2Rad;
            float bend = Mathf.Min(strength, 1f) * .8f;
            float speed = time * _frequency * Mathf.PI * 2f * (.6f + strength * .4f);
            float pinned = Mathf.Clamp(_pinnedWidth, 0f, .3f);
            for (int i = 0; i < _rest.Length; i++)
            {
                Vector3 p = _rest[i];
                float u = (p.x - _minX) / _width;
                float free = Mathf.Clamp01((u - pinned) / (1f - pinned));
                if (strength == 0f || free == 0f)
                {
                    _vertices[i] = p;
                    continue;
                }
                float v = (p.y - _minY) / _height;
                float length = width * (u - pinned);
                float theta = angle * bend * free;
                float along = Mathf.Abs(theta) < .0001f ? 1f : Mathf.Sin(theta) / theta;
                float across = Mathf.Abs(theta) < .0001f ? 0f : (1f - Mathf.Cos(theta)) / theta;
                Vector3 anchor = transform.TransformPoint(new Vector3(_minX + _width * pinned, p.y, p.z));
                Vector3 normal = Quaternion.AngleAxis(theta * Mathf.Rad2Deg, up) * side;
                float phase = speed - free * Mathf.PI * 3f + v * 1.4f + _phase;
                float wave = Mathf.Sin(phase) + turbulence * .5f * Mathf.Sin(phase * 2.17f - v * 2f);
                float amplitude = width * Mathf.Max(0f, _amplitude) * Mathf.Min(strength, 2f) * free * free;
                Vector3 world = anchor + length * (right * along + side * across)
                    + normal * wave * amplitude + up * Mathf.Sin(phase * .73f) * amplitude * .15f;
                _vertices[i] = transform.InverseTransformPoint(world);
            }
            _animated.vertices = _vertices;
            _animated.RecalculateNormals();
            _animated.RecalculateBounds();
        }

        private void OnDisable()
        {
            if (_filter != null && _filter.sharedMesh == _animated) _filter.sharedMesh = _source;
        }

        private void OnDestroy()
        {
            if (_animated == null) return;
            if (Application.isPlaying) Destroy(_animated);
            else DestroyImmediate(_animated);
        }
    }
}
