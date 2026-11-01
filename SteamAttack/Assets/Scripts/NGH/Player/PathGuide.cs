using UnityEngine;
using UnityEngine.AI;

namespace NGH
{
    /// <summary>
    /// 길 안내: NavMesh로 플레이어에서 목적지(보스방 포털)까지 길을 찾아, 바닥에 연한 노란 세모를 줄지어 그려 갈 방향을 보여 줌.
    ///  - 세모는 앞쪽 몇 m 만 보이고, 멀어질수록 옅어지며, 길을 따라 천천히 앞으로 흘러감
    ///  - 목적지를 못 찾거나(보스방 등) 길이 없으면 아무것도 그리지 않음. 목적지에 가까워지면 사라짐
    ///  - 세모 오브젝트는 시작할 때 정해진 개수만 만들고 계속 다시 씀 (플레이 중 새로 만들지 않음)
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class PathGuide : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public PlayerController player;
        [Tooltip("갈 곳. 비우면 이름이 targetName 인 오브젝트를 찾음")]
        public Transform target;
        [Tooltip("target 을 비웠을 때 찾을 목적지 오브젝트 이름")]
        public string targetName = "NGH_Portal_ToBossArena_Trigger";
        [Tooltip("세모에 쓸 재질 (NGH/PathGuide 셰이더)")]
        public Material markerMaterial;

        [Header("표시")]
        [Tooltip("길 안내를 켬")]
        public bool showGuide = true;
        [Tooltip("세모 개수 (앞쪽으로 보이는 길이 = 개수 × 간격)")]
        [Min(1)] public int markerCount = 10;
        [Tooltip("세모 사이 간격 (m)")]
        [Min(0.3f)] public float spacing = 1.6f;
        [Tooltip("플레이어 발밑에서 첫 세모까지 거리 (m)")]
        [Min(0f)] public float startOffset = 1.2f;
        [Tooltip("세모 크기 (m)")]
        [Min(0.1f)] public float markerSize = 0.9f;
        [Tooltip("켜면 속이 찬 세모, 끄면 테두리만 있는 세모")]
        public bool filled = true;
        [Tooltip("세모 테두리 두께 (세모 크기에 대한 비율). filled 를 껐을 때만 씀")]
        [Range(0.05f, 0.5f)] public float lineWidth = 0.13f;
        [Tooltip("바닥에서 띄우는 높이 (m)")]
        public float heightOffset = 0.08f;
        [Tooltip("세모 색")]
        public Color color = new Color(1f, 0.93f, 0.6f, 0.95f);
        [Tooltip("맨 앞 세모가 맨 가까운 세모보다 옅어지는 정도 (0 = 모두 같게)")]
        [Range(0f, 1f)] public float farFade = 0.5f;
        [Tooltip("세모가 길을 따라 흐르는 빠르기 (m/초). 0 = 멈춤")]
        public float flowSpeed = 1.2f;
        [Tooltip("목적지에 이만큼 가까워지면 안내를 끔 (m)")]
        public float hideWithin = 4f;

        [Header("계산")]
        [Tooltip("길을 다시 찾는 간격 (초)")]
        [Min(0.05f)] public float repathInterval = 0.25f;
        [Tooltip("세모를 바닥에 붙일 때 아래로 쏘는 레이에 맞을 층")]
        public LayerMask groundMask = ~0;

        static readonly int ColorId = Shader.PropertyToID("_Color");

        Transform[] _markers;
        Renderer[] _renderers;
        MaterialPropertyBlock _mpb;
        NavMeshPath _path;
        Vector3[] _corners = new Vector3[64];
        int _cornerCount;
        float _nextRepath;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            _path = new NavMeshPath();
            _mpb = new MaterialPropertyBlock();
        }

        void Start()
        {
            if (!target && !string.IsNullOrEmpty(targetName))
            {
                var go = GameObject.Find(targetName);
                if (go) target = go.transform;
            }
            BuildMarkers();
        }

        void OnDisable() => SetVisible(0);

        void OnDestroy()
        {
            if (_markers == null) return;
            foreach (var m in _markers) if (m) Destroy(m.gameObject);
        }

        void BuildMarkers()
        {
            var mesh = filled ? BuildTriangleFilled() : BuildTriangleOutline(lineWidth);
            var root = new GameObject("NGH_PathGuideMarkers").transform;   // 플레이어가 돌아도 세모는 바닥에 그대로 있게 따로 둠
            _markers = new Transform[markerCount];
            _renderers = new Renderer[markerCount];
            for (int i = 0; i < markerCount; i++)
            {
                var g = new GameObject("Marker_" + i);
                g.transform.SetParent(root, false);
                g.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = g.AddComponent<MeshRenderer>();
                mr.sharedMaterial = markerMaterial;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                g.SetActive(false);
                _markers[i] = g.transform;
                _renderers[i] = mr;
            }
        }

        void LateUpdate()
        {
            if (_markers == null) return;
            if (!showGuide || !target || !markerMaterial || (player && player.IsDead)) { SetVisible(0); return; }

            Vector3 from = transform.position;
            if ((target.position - from).sqrMagnitude < hideWithin * hideWithin) { SetVisible(0); return; }

            if (Time.time >= _nextRepath)
            {
                _nextRepath = Time.time + repathInterval;
                _cornerCount = 0;
                if (NavMesh.SamplePosition(from, out var a, 3f, NavMesh.AllAreas) &&
                    NavMesh.SamplePosition(target.position, out var b, 6f, NavMesh.AllAreas) &&
                    NavMesh.CalculatePath(a.position, b.position, NavMesh.AllAreas, _path) &&
                    _path.status != NavMeshPathStatus.PathInvalid)
                {
                    _cornerCount = _path.GetCornersNonAlloc(_corners);
                }
            }
            if (_cornerCount < 2) { SetVisible(0); return; }

            float flow = flowSpeed > 0f ? Mathf.Repeat(Time.time * flowSpeed, spacing) : 0f;
            float total = PathLength();
            int shown = 0;
            for (int i = 0; i < _markers.Length; i++)
            {
                float d = startOffset + flow + i * spacing;
                if (d > total) break;
                PointAt(d, out Vector3 pos, out Vector3 dir);
                pos = SnapToGround(pos) + Vector3.up * heightOffset;

                var t = _markers[i];
                t.SetPositionAndRotation(pos, Quaternion.LookRotation(Vector3.up, dir));   // 바닥에 눕히고 꼭짓점이 갈 방향을 봄
                t.localScale = Vector3.one * markerSize;

                // 앞쪽으로 갈수록 옅어지고, 맨 앞·맨 끝은 흐르면서 부드럽게 나타나고 사라짐
                float k = (d - startOffset) / (markerCount * spacing);
                float alpha = color.a * (1f - k * farFade) * Mathf.Clamp01((d - startOffset) / spacing) * Mathf.Clamp01((startOffset + markerCount * spacing - d) / spacing);
                _mpb.SetColor(ColorId, new Color(color.r, color.g, color.b, alpha));
                _renderers[i].SetPropertyBlock(_mpb);
                shown++;
            }
            SetVisible(shown);
        }

        float PathLength()
        {
            float len = 0f;
            for (int i = 1; i < _cornerCount; i++) len += Vector3.Distance(_corners[i - 1], _corners[i]);
            return len;
        }

        // 길 위에서 출발점부터 dist 만큼 간 위치와 그 구간의 방향(수평)
        void PointAt(float dist, out Vector3 pos, out Vector3 dir)
        {
            for (int i = 1; i < _cornerCount; i++)
            {
                Vector3 a = _corners[i - 1], b = _corners[i];
                float seg = Vector3.Distance(a, b);
                if (dist <= seg || i == _cornerCount - 1)
                {
                    pos = Vector3.Lerp(a, b, seg > 0.0001f ? Mathf.Clamp01(dist / seg) : 0f);
                    dir = b - a; dir.y = 0f;
                    if (dir.sqrMagnitude < 1e-6f) dir = transform.forward;
                    dir.Normalize();
                    return;
                }
                dist -= seg;
            }
            pos = _corners[_cornerCount - 1];
            dir = transform.forward;
        }

        // 바닥 충돌체가 보이는 돌판보다 낮은 곳이 있어, 레이로 찾은 바닥과 NavMesh 높이 중 높은 쪽에 붙임 (세모가 돌판 밑에 묻히지 않게)
        Vector3 SnapToGround(Vector3 p)
        {
            if (Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 3f, groundMask, QueryTriggerInteraction.Ignore))
                p.y = Mathf.Max(p.y, hit.point.y);
            return p;
        }

        void SetVisible(int count)
        {
            if (_markers == null) return;
            for (int i = 0; i < _markers.Length; i++)
            {
                bool on = i < count;
                if (_markers[i] && _markers[i].gameObject.activeSelf != on) _markers[i].gameObject.SetActive(on);
            }
        }

        // 꼭짓점이 +Y(로컬)를 향하는 속이 찬 세모
        static Mesh BuildTriangleFilled()
        {
            var m = new Mesh
            {
                name = "NGH_PathGuideTriangleFilled",
                vertices = new[] { new Vector3(0f, 0.6f, 0f), new Vector3(-0.5f, -0.4f, 0f), new Vector3(0.5f, -0.4f, 0f) },
                triangles = new[] { 0, 1, 2 }
            };
            m.RecalculateBounds();
            return m;
        }

        // 꼭짓점이 +Y(로컬)를 향하는 세모 테두리. 바깥 세모에서 안쪽 세모를 뺀 띠 3개
        static Mesh BuildTriangleOutline(float width)
        {
            Vector3[] outer = { new Vector3(0f, 0.6f, 0f), new Vector3(-0.5f, -0.4f, 0f), new Vector3(0.5f, -0.4f, 0f) };
            Vector3 c = (outer[0] + outer[1] + outer[2]) / 3f;
            float s = 1f - width * 2.6f;   // 테두리 두께만큼 줄인 안쪽 세모
            var inner = new Vector3[3];
            for (int i = 0; i < 3; i++) inner[i] = c + (outer[i] - c) * Mathf.Max(0.05f, s);

            var verts = new Vector3[6];
            for (int i = 0; i < 3; i++) { verts[i] = outer[i]; verts[i + 3] = inner[i]; }
            var tris = new int[18];
            int n = 0;
            for (int i = 0; i < 3; i++)
            {
                int j = (i + 1) % 3;
                tris[n++] = i; tris[n++] = j; tris[n++] = i + 3;
                tris[n++] = j; tris[n++] = j + 3; tris[n++] = i + 3;
            }
            var m = new Mesh { name = "NGH_PathGuideTriangle", vertices = verts, triangles = tris };
            m.RecalculateBounds();
            return m;
        }
    }
}
