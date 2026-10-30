using UnityEngine;
namespace LaserScorchEffects
{
    // Fixed buffers for up to 81 samples; reused for every sweep.
    internal sealed class ScorchTrailGeometry
    {
        private readonly Vector3[] vertices = new Vector3[81 * 3];
        private readonly Vector3[] normals = new Vector3[81 * 3];
        private readonly Vector2[] uv = new Vector2[81 * 3];
        private readonly int[] triangles = new int[80 * 12];
        public void UpdateMesh(Mesh mesh, Vector3[] points, Vector3[] surfaceNormals, bool[] valid,
            int count, float width, System.Func<Vector3, Vector3> snap)
        {
            int indices = 0;
            for (int i = 0; i < count; i++)
            {
                Vector3 tangent = points[Mathf.Min(i + 1, count - 1)] - points[Mathf.Max(0, i - 1)];
                Vector3 side = Vector3.Cross(surfaceNormals[i], tangent).normalized;
                if (side.sqrMagnitude < .1f) side = Vector3.right;
                float irregular = .91f + Mathf.PerlinNoise(i * .37f, 5.1f) * .18f;
                for (int row = 0; row < 3; row++)
                {
                    int v = i * 3 + row;
                    vertices[v] = snap(points[i] + side * (row - 1) * width * .5f * irregular) + surfaceNormals[i] * .012f;
                    normals[v] = surfaceNormals[i];
                    uv[v] = new Vector2((float)i / (count - 1), row * .5f);
                }
                if (i == 0 || !valid[i - 1] || !valid[i]) continue;
                for (int row = 0; row < 2; row++)
                {
                    int a = (i - 1) * 3 + row, b = i * 3 + row;
                    triangles[indices++] = a; triangles[indices++] = a + 1; triangles[indices++] = b;
                    triangles[indices++] = b; triangles[indices++] = a + 1; triangles[indices++] = b + 1;
                }
            }
            mesh.Clear(false);
            mesh.SetVertices(vertices, 0, count * 3);
            mesh.SetNormals(normals, 0, count * 3);
            mesh.SetUVs(0, uv, 0, count * 3);
            mesh.SetTriangles(triangles, 0, indices, 0, true);
        }
    }
}
