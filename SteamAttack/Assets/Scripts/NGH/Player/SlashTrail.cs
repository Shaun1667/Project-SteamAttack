using System.Collections.Generic;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 칼날이 실제로 지나간 면을 따라 만드는 베기 궤적 메시. AttackFx 가 풀에서 꺼내 씁니다 (직접 붙일 필요 없음).
    ///  - 경로(칼끝 / 칼날 안쪽)는 애니메이션에서 미리 계산한 값 (부모 = 플레이어 기준)
    ///  - Head(0~1) 까지 메시를 그리며, 굵은 끝(텍스처 u = 1)이 항상 현재 칼 위치에 옴
    ///  - 베기 파티클과 같은 머티리얼·색 변화(Color over Lifetime)를 그대로 사용
    /// </summary>
    [DefaultExecutionOrder(1200)]   // AttackFx(1100)가 Head 를 정한 뒤 메시를 다시 만듦
    [DisallowMultipleComponent]
    public class SlashTrail : MonoBehaviour
    {
        class Layer
        {
            public MeshFilter filter;
            public MeshRenderer renderer;
            public Mesh mesh;
            public Gradient gradient;
            public float lifetime = 1f;
        }

        readonly List<Layer> _layers = new List<Layer>();
        Vector3[] _outer, _inner;
        int _prefix;   // 경로 앞에 덧붙인 꼬리 점 개수 (처음부터 보임). Head 는 그 뒤 칼 경로에만 적용
        float _head, _age;
        bool _dirty;

        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<Color> _colors = new List<Color>();
        readonly List<int> _tris = new List<int>();

        /// <summary>궤적 시작. sources = 머티리얼·색 변화를 가져올 베기 파티클들 (프리팹 원본)</summary>
        public void Begin(Vector3[] outer, Vector3[] inner, IList<ParticleSystem> sources, int prefix = 0)
        {
            _outer = outer;
            _inner = inner;
            _prefix = Mathf.Clamp(prefix, 0, outer.Length - 2);
            _head = 0f;
            _age = 0f;
            _dirty = true;
            while (_layers.Count < sources.Count)
            {
                var go = new GameObject("layer" + _layers.Count);
                go.transform.SetParent(transform, false);
                var l = new Layer
                {
                    filter = go.AddComponent<MeshFilter>(),
                    renderer = go.AddComponent<MeshRenderer>(),
                    mesh = new Mesh { name = "NGH_SlashTrail" },
                };
                l.mesh.MarkDynamic();
                l.filter.sharedMesh = l.mesh;
                l.renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                l.renderer.receiveShadows = false;
                _layers.Add(l);
            }
            for (int i = 0; i < _layers.Count; i++)
            {
                var l = _layers[i];
                bool on = i < sources.Count;
                l.renderer.enabled = on;
                if (!on) continue;
                var ps = sources[i];
                l.renderer.sharedMaterial = ps.GetComponent<ParticleSystemRenderer>().sharedMaterial;
                l.renderer.sortingOrder = i;   // 뒤에 오는 층(가산 블렌드)이 위에 그려지게
                var col = ps.colorOverLifetime;
                l.gradient = col.enabled ? (col.color.mode == ParticleSystemGradientMode.Gradient ? col.color.gradient : col.color.gradientMax) : null;
                l.lifetime = Mathf.Max(0.01f, ps.main.startLifetime.constant);
                l.mesh.Clear();
            }
        }

        /// <summary>궤적을 어디까지 그릴지 (0 = 휘두르기 시작, 1 = 끝). 뒤로 가지 않음</summary>
        public void SetHead(float head)
        {
            head = Mathf.Clamp01(head);
            if (head > _head) { _head = head; _dirty = true; }
        }

        void LateUpdate()
        {
            if (_outer == null || _outer.Length < 2) return;
            _age += Time.deltaTime;
            if (_dirty) Rebuild();
            // 색·투명도 (파티클의 Color over Lifetime 과 같게)
            for (int i = 0; i < _layers.Count; i++)
            {
                var l = _layers[i];
                if (!l.renderer.enabled || l.mesh.vertexCount == 0) continue;
                var c = l.gradient != null ? l.gradient.Evaluate(Mathf.Clamp01(_age / l.lifetime)) : Color.white;
                _colors.Clear();
                for (int v = 0; v < l.mesh.vertexCount; v++) _colors.Add(c);
                l.mesh.SetColors(_colors);
            }
        }

        void Rebuild()
        {
            _dirty = false;
            int n = _outer.Length;
            float h = _prefix + _head * (n - 1 - _prefix);
            int full = Mathf.FloorToInt(h);
            _verts.Clear(); _uvs.Clear(); _tris.Clear();
            if (h > 0.001f)
            {
                int cols = full + 1 + (h - full > 0.001f ? 1 : 0);
                for (int j = 0; j < cols; j++)
                {
                    float s = Mathf.Min(j, h);
                    int a = Mathf.Min(Mathf.FloorToInt(s), n - 2);
                    float f = s - a;
                    Vector3 o = Vector3.LerpUnclamped(_outer[a], _outer[a + 1], f);
                    Vector3 inn = Vector3.LerpUnclamped(_inner[a], _inner[a + 1], f);
                    float u = s / h;   // 0 = 꼬리, 1 = 굵은 끝(현재 칼 위치)
                    _verts.Add(o); _uvs.Add(new Vector2(u, 0f));
                    _verts.Add(inn); _uvs.Add(new Vector2(u, 1f));
                }
                for (int j = 0; j < cols - 1; j++)
                {
                    int o0 = j * 2, i0 = o0 + 1, o1 = o0 + 2, i1 = o0 + 3;
                    _tris.Add(o0); _tris.Add(o1); _tris.Add(i0);
                    _tris.Add(i0); _tris.Add(o1); _tris.Add(i1);
                    _tris.Add(o0); _tris.Add(i0); _tris.Add(o1);   // 뒷면
                    _tris.Add(i0); _tris.Add(i1); _tris.Add(o1);
                }
            }
            foreach (var l in _layers)
            {
                if (!l.renderer.enabled) continue;
                l.mesh.Clear();
                l.mesh.SetVertices(_verts);
                l.mesh.SetUVs(0, _uvs);
                l.mesh.SetTriangles(_tris, 0);
                l.mesh.RecalculateBounds();
            }
        }

        void OnDestroy()
        {
            foreach (var l in _layers) if (l.mesh) Destroy(l.mesh);
        }
    }
}
