using System.Collections.Generic;
using UnityEngine;

namespace SteamAttack.EditorTools
{
    /// <summary>
    /// 기본 도형으로 안 되는 메시를 코드로 만든다.
    /// 한옥 지붕(처마 끝이 들린 우진각), 정자 지붕(n각 처마), n각기둥, 먼 산(각뿔), 원판/고리.
    /// </summary>
    public static class WorldMeshFactory
    {
        /// <summary>
        /// 처마 끝이 위로 들린 우진각 지붕. 원점 = 처마 높이의 중심.
        /// width(X) x depth(Z) 는 처마 끝 기준, height 는 용마루까지 높이.
        /// </summary>
        public static Mesh HanokRoof(float width, float depth, float height, float eaveLift, int segments = 16)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            float hw = width * 0.5f;
            float hd = depth * 0.5f;
            float ridgeHalf = Mathf.Max(0.1f, hw - hd * 0.9f);

            float Lift(float t) => eaveLift * Mathf.Pow(Mathf.Abs(t * 2f - 1f), 3f);

            // 앞/뒤 경사면 (살짝 오목하게 휘어 기와 곡선을 흉내 낸다)
            for (int side = -1; side <= 1; side += 2)
            {
                const int Rows = 4;
                int start = verts.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    for (int r = 0; r <= Rows; r++)
                    {
                        float v = r / (float)Rows;
                        float x = Mathf.Lerp(Mathf.Lerp(-hw, hw, t), Mathf.Lerp(-ridgeHalf, ridgeHalf, t), v);
                        float z = Mathf.Lerp(hd * side, 0f, v);
                        float yLinear = Mathf.Lerp(Lift(t), height, v);
                        float sag = Mathf.Sin(v * Mathf.PI) * height * 0.12f;
                        verts.Add(new Vector3(x, yLinear - sag, z));
                    }
                }

                for (int i = 0; i < segments; i++)
                {
                    for (int r = 0; r < Rows; r++)
                    {
                        int a = start + i * (Rows + 1) + r;
                        int b = a + Rows + 1;
                        if (side > 0)
                        {
                            tris.Add(a); tris.Add(b + 1); tris.Add(a + 1);
                            tris.Add(a); tris.Add(b); tris.Add(b + 1);
                        }
                        else
                        {
                            tris.Add(a); tris.Add(a + 1); tris.Add(b + 1);
                            tris.Add(a); tris.Add(b + 1); tris.Add(b);
                        }
                    }
                }
            }

            // 양 옆 합각면
            for (int side = -1; side <= 1; side += 2)
            {
                int start = verts.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float ez = Mathf.Lerp(-hd, hd, t);
                    verts.Add(new Vector3(hw * side, Lift(t) * 0.9f + eaveLift * 0.1f, ez));
                }

                int apex = verts.Count;
                verts.Add(new Vector3(ridgeHalf * side, height, 0f));

                for (int i = 0; i < segments; i++)
                {
                    int a = start + i;
                    if (side > 0) { tris.Add(a); tris.Add(apex); tris.Add(a + 1); }
                    else { tris.Add(a); tris.Add(a + 1); tris.Add(apex); }
                }
            }

            return Finish("HanokRoof", verts, tris, true);
        }

        /// <summary>정자 지붕: n각 처마 모서리가 들린 뿔 지붕(향원정 육모지붕).</summary>
        public static Mesh PavilionRoof(int sides, float radius, float height, float cornerLift, int perSide = 8)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            var ring = new List<Vector3>();
            for (int s = 0; s < sides; s++)
            {
                float a0 = s / (float)sides * Mathf.PI * 2f;
                float a1 = (s + 1) / (float)sides * Mathf.PI * 2f;
                var c0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                var c1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                for (int i = 0; i < perSide; i++)
                {
                    float t = i / (float)perSide;
                    var p = Vector3.Lerp(c0, c1, t);
                    float edge = Mathf.Abs(t * 2f - 1f);
                    p *= Mathf.Lerp(0.93f, 1f, edge);
                    p.y = cornerLift * Mathf.Pow(edge, 3f);
                    ring.Add(p);
                }
            }

            var apex = new Vector3(0f, height, 0f);
            for (int i = 0; i < ring.Count; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Count];
                var mid = (a + b) * 0.5f;
                // 오목한 곡면: 가운데 줄을 한 번 꺾는다
                var kneeA = Vector3.Lerp(a, apex, 0.45f) + Vector3.down * height * 0.1f;
                var kneeB = Vector3.Lerp(b, apex, 0.45f) + Vector3.down * height * 0.1f;
                AddQuad(verts, tris, a, b, kneeB, kneeA);
                AddTri(verts, tris, kneeA, kneeB, apex);
            }

            return Finish("PavilionRoof", verts, tris, true);
        }

        /// <summary>n각기둥(정자 기단, 가갑 판, 굴뚝).</summary>
        public static Mesh Prism(int sides, float radius, float height)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            var bottom = new Vector3[sides];
            var top = new Vector3[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2f + Mathf.PI / sides;
                var p = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                bottom[i] = p;
                top[i] = p + Vector3.up * height;
            }

            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                AddQuad(verts, tris, bottom[j], bottom[i], top[i], top[j]);
                AddTri(verts, tris, top[j], top[i], Vector3.up * height);
                AddTri(verts, tris, bottom[i], bottom[j], Vector3.zero);
            }

            return Finish("Prism", verts, tris, false);
        }

        /// <summary>먼 산: 층층이 좁아지는 울퉁불퉁한 봉우리. seed 로 모양이 바뀐다.</summary>
        public static Mesh Mountain(int sides, float radius, float height, float jitter, int seed)
        {
            var rng = new System.Random(seed);
            float R() => (float)rng.NextDouble();

            var verts = new List<Vector3>();
            var tris = new List<int>();

            float[] levels = { 0f, 0.28f, 0.58f, 0.84f };
            float[] radii = { 1f, 0.72f, 0.40f, 0.17f };
            var lean = new Vector3((R() - 0.5f) * 0.5f, 0f, (R() - 0.5f) * 0.5f) * radius;

            var rings = new Vector3[levels.Length][];
            for (int l = 0; l < levels.Length; l++)
            {
                rings[l] = new Vector3[sides];
                var center = lean * levels[l];
                for (int i = 0; i < sides; i++)
                {
                    float a = i / (float)sides * Mathf.PI * 2f;
                    float r = radius * radii[l] * (1f + (R() - 0.5f) * jitter);
                    float y = height * levels[l] * (1f + (R() - 0.5f) * 0.18f);
                    rings[l][i] = center + new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
                }
            }

            var apex = lean + new Vector3(0f, height, 0f);

            for (int l = 0; l < levels.Length - 1; l++)
            {
                for (int i = 0; i < sides; i++)
                {
                    int j = (i + 1) % sides;
                    AddQuad(verts, tris, rings[l][j], rings[l][i], rings[l + 1][i], rings[l + 1][j]);
                }
            }

            var top = rings[levels.Length - 1];
            for (int i = 0; i < sides; i++)
                AddTri(verts, tris, top[(i + 1) % sides], top[i], apex);

            return Finish("Mountain", verts, tris, false);
        }

        /// <summary>XZ 평면의 고리(inner 0 이면 원판). 혼천의 고리, 얼음 연못 테두리 등.</summary>
        public static Mesh Ring(float inner, float outer, int segments = 64)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float a0 = i / (float)segments * Mathf.PI * 2f;
                float a1 = (i + 1) / (float)segments * Mathf.PI * 2f;
                var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                AddQuad(verts, tris, d0 * inner, d0 * outer, d1 * outer, d1 * inner);
            }

            return Finish("Ring", verts, tris, true, 0.5f / Mathf.Max(0.0001f, outer), 0.5f);
        }

        // ── 내부 ───────────────────────────────────────────────

        private static void AddTri(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c)
        {
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(c);
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
        }

        private static void AddQuad(List<Vector3> v, List<int> t, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            AddTri(v, t, a, b, c);
            AddTri(v, t, a, c, d);
        }

        /// <summary>삼각형마다 버텍스를 따로 둬서 면이 또렷한(붓 터치 같은) 음영이 나오게 한다.</summary>
        private static Mesh Finish(string name, List<Vector3> verts, List<int> tris, bool doubleSided, float uvScale = 0.1f, float uvOffset = 0f)
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int s = v.Count;
                v.Add(verts[tris[i]]); v.Add(verts[tris[i + 1]]); v.Add(verts[tris[i + 2]]);
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                if (doubleSided)
                {
                    int s2 = v.Count;
                    v.Add(verts[tris[i]]); v.Add(verts[tris[i + 2]]); v.Add(verts[tris[i + 1]]);
                    t.Add(s2); t.Add(s2 + 1); t.Add(s2 + 2);
                }
            }

            var mesh = new Mesh { name = name };
            if (v.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(v);
            var colors = new Color[v.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            mesh.colors = colors;
            var uv = new Vector2[v.Count];
            for (int i = 0; i < uv.Length; i++) uv[i] = new Vector2(v[i].x * uvScale + uvOffset, v[i].z * uvScale + uvOffset);
            mesh.uv = uv;
            mesh.SetTriangles(t, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
