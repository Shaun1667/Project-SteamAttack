using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 원경 산 다시 만들기 — 층층이 쌓은 다면체 덩어리(WorldMeshFactory.Mountain)를 수묵 산수화 같은 산으로.
    /// 메뉴: SteamAttack ▸ NMJ ▸ 원경 산 다시 만들기
    ///
    /// · 같은 메시 파일(Arts/Models/NMJ/HyeonmuWorld/Mountain_*.asset)의 내용만 바꾼다 → 씬의 위치·크기·재질·외곽선은 그대로
    /// · 모양: 발밑은 넓고 완만, 위로 갈수록 가파른 바위 능선(ridged noise). 주봉 하나 + 곁봉 2~3, 골짜기가 패인다
    /// · 면은 평평하게(낮은 폴리곤 수묵 느낌), 크기는 예전과 같이 반지름 1 · 높이 1
    /// </summary>
    public static class NmjMountainRemesh
    {
        private const string Dir = "Assets/Arts/Models/NMJ/HyeonmuWorld";
        private const int Rings = 22, Segments = 44;

        [MenuItem("SteamAttack/NMJ/원경 산 다시 만들기", false, 21)]
        public static void Run()
        {
            int n = 0;
            foreach (string guid in AssetDatabase.FindAssets("Mountain_ t:Mesh", new[] { Dir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string file = System.IO.Path.GetFileNameWithoutExtension(path);
                if (!file.StartsWith("Mountain_") || !int.TryParse(file.Substring(9), out int seed)) continue;
                var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (mesh == null) continue;
                Build(mesh, seed);
                EditorUtility.SetDirty(mesh);
                n++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[NMJ] 원경 산 {n}개를 다시 만들었다 (씬 배치는 그대로)");
        }

        private static void Build(Mesh mesh, int seed)
        {
            var rng = new System.Random(seed * 7919 + 13);
            float R() => (float)rng.NextDouble();
            float ox = R() * 100f, oz = R() * 100f;

            // 봉우리들: 주봉 + 곁봉 (위치·높이·폭)
            var peaks = new List<Vector4>();
            peaks.Add(new Vector4((R() - 0.5f) * 0.3f, (R() - 0.5f) * 0.3f, 1f, 0.42f + R() * 0.12f));
            int extra = 2 + rng.Next(2);
            for (int i = 0; i < extra; i++)
            {
                float a = R() * Mathf.PI * 2f, d = 0.3f + R() * 0.35f;
                peaks.Add(new Vector4(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.45f + R() * 0.35f, 0.22f + R() * 0.12f));
            }

            float Height(float x, float z)
            {
                float r = Mathf.Sqrt(x * x + z * z);
                if (r >= 1f) return 0f;
                float h = 0f;
                foreach (var p in peaks)
                {
                    float dx = x - p.x, dz = z - p.y;
                    float d = Mathf.Sqrt(dx * dx + dz * dz) / p.w;
                    h = Mathf.Max(h, p.z * Mathf.Exp(-d * d * 0.9f) * (1f - 0.15f * d));   // 봉우리 몸통
                }
                h = Mathf.Max(h, 0.26f * (1f - r));   // 산자락이 납작하게 퍼지지 않고 비탈로 올라가게
                // 바위 능선: 날카로운 결 (1 - |noise|), 높을수록 강하게
                float ridge = 0f, amp = 0.5f, freq = 2.2f;
                for (int o = 0; o < 4; o++)
                {
                    float nz = Mathf.PerlinNoise(ox + x * freq, oz + z * freq) * 2f - 1f;
                    ridge += (1f - Mathf.Abs(nz)) * amp;
                    amp *= 0.5f; freq *= 2.1f;
                }
                h *= 0.62f + 0.55f * ridge;
                // 산자락: 가장자리로 갈수록 0으로 (발밑이 넓게 퍼짐)
                float edge = Mathf.Clamp01((1f - r) / 0.12f);
                h *= edge * edge * (3f - 2f * edge);
                return h;
            }

            // 극좌표 격자 → 평평한 면의 삼각형
            var grid = new Vector3[Rings + 1, Segments];
            for (int i = 0; i <= Rings; i++)
            {
                float rr = i / (float)Rings;
                rr = 1f - (1f - rr) * (1f - rr);   // 가운데를 촘촘하게
                for (int j = 0; j < Segments; j++)
                {
                    float a = j / (float)Segments * Mathf.PI * 2f + (i % 2) * (Mathf.PI / Segments);
                    float jit = i > 0 && i < Rings ? (R() - 0.5f) * 0.03f : 0f;
                    float x = Mathf.Cos(a) * (rr + jit), z = Mathf.Sin(a) * (rr + jit);
                    grid[i, j] = new Vector3(x, Height(x, z), z);
                }
            }
            float top = 0.01f;
            foreach (var v in grid) top = Mathf.Max(top, v.y);
            Vector3 apex = new Vector3(0f, Height(0f, 0f), 0f);

            var verts = new List<Vector3>();
            var tris = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int k = verts.Count; verts.Add(a); verts.Add(b); verts.Add(c); tris.Add(k); tris.Add(k + 1); tris.Add(k + 2); }

            for (int j = 0; j < Segments; j++) Tri(apex, grid[1, (j + 1) % Segments], grid[1, j]);
            for (int i = 1; i < Rings; i++)
                for (int j = 0; j < Segments; j++)
                {
                    int jn = (j + 1) % Segments;
                    Tri(grid[i, j], grid[i, jn], grid[i + 1, j]);
                    Tri(grid[i, jn], grid[i + 1, jn], grid[i + 1, j]);
                }
            // 바닥 치마: 땅과 만나는 곳에 틈이 안 보이게 살짝 아래로
            for (int j = 0; j < Segments; j++)
            {
                int jn = (j + 1) % Segments;
                Vector3 a = grid[Rings, j], b = grid[Rings, jn];
                Tri(a, b, a * 1.02f + Vector3.down * 0.03f);
                Tri(b, b * 1.02f + Vector3.down * 0.03f, a * 1.02f + Vector3.down * 0.03f);
            }

            // 높이를 1로 맞춘다 (예전 메시와 같은 크기 → 씬 배율 그대로)
            float s = 1f / top;
            for (int k = 0; k < verts.Count; k++) verts[k] = new Vector3(verts[k].x, verts[k].y * s, verts[k].z);

            var uvs = new List<Vector2>(verts.Count);
            foreach (var v in verts) uvs.Add(new Vector2(v.x * 0.5f + 0.5f, v.z * 0.5f + 0.5f));

            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
        }
    }
}
