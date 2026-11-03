using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGH
{
    /// <summary>
    /// 지금 열린 씬의 돌 바닥 메시(궁궐 바닥·박석마당·광화문 바닥 등)가 덮은 칸을 FootSurfaceMap 으로 굽는다.
    /// 메뉴: NGH/발소리 지면 지도 굽기. 바닥을 다시 깔거나 옮겼으면 다시 굽는다.
    /// </summary>
    public static class FootSurfaceMapBaker
    {
        const float CellSize = 1f;
        const float MaxFloorHeight = 0.45f;   // 이보다 높은 면은 월대·계단 (높이로 따로 판정)

        // 돌 바닥으로 볼 렌더러: 궁궐 바닥 타일(합친 메시)과 박석 재질
        static bool IsStoneFloor(Renderer r)
        {
            if (r.name.StartsWith("Baked_") && r.name.Contains("InkToonTextured")) return true;   // PalaceFloorTileBuilder 바닥을 합친 것
            foreach (var m in r.sharedMaterials)
                if (m && (m.name.Contains("PalaceFloor") || m.name.Contains("박석"))) return true;
            return false;
        }

        [MenuItem("NGH/발소리 지면 지도 굽기")]
        public static void BakeActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            var tris = new List<Vector3>();
            var rects = new List<Bounds>();   // 판판한 박석마당·문 바닥은 메시가 돌 조각으로 듬성듬성해 범위를 통째로 돌로 칠함
            foreach (var root in scene.GetRootGameObjects())
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(false))
                {
                    if (!r.enabled || !IsStoneFloor(r)) continue;
                    Bounds rb = r.bounds;
                    if ((r.name.Contains("박석마당") || r.name.Contains("_바닥")) && rb.size.y < 0.05f && rb.max.y <= MaxFloorHeight)
                    {
                        rects.Add(rb);
                        tris.Add(rb.min); tris.Add(rb.max); tris.Add(rb.min);   // 지도 범위 계산용 (넓이 0인 삼각형이라 칠하지는 않음)
                        continue;
                    }
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf ? mf.sharedMesh : null;
                    if (!mesh) continue;
                    var v = mesh.vertices; var t = mesh.triangles; var m = r.transform.localToWorldMatrix;
                    for (int i = 0; i < t.Length; i += 3)
                    {
                        Vector3 a = m.MultiplyPoint3x4(v[t[i]]), b = m.MultiplyPoint3x4(v[t[i + 1]]), c = m.MultiplyPoint3x4(v[t[i + 2]]);
                        if (Mathf.Max(a.y, b.y, c.y) > MaxFloorHeight) continue;
                        Vector3 n = Vector3.Cross(b - a, c - a);
                        if (n.sqrMagnitude < 1e-10f || Mathf.Abs(n.normalized.y) < 0.7f) continue;   // 바닥처럼 누운 면만
                        tris.Add(a); tris.Add(b); tris.Add(c);
                    }
                }
            if (tris.Count == 0) { Debug.LogWarning("[FootSurfaceMap] 돌 바닥 메시를 찾지 못했습니다: " + scene.name); return; }

            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in tris) { minX = Mathf.Min(minX, p.x); minZ = Mathf.Min(minZ, p.z); maxX = Mathf.Max(maxX, p.x); maxZ = Mathf.Max(maxZ, p.z); }
            minX = Mathf.Floor(minX) - 1f; minZ = Mathf.Floor(minZ) - 1f;
            int w = Mathf.CeilToInt((maxX - minX) / CellSize) + 2, h = Mathf.CeilToInt((maxZ - minZ) / CellSize) + 2;
            var cells = new byte[w * h];
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector2 a = new Vector2(tris[i].x, tris[i].z), b = new Vector2(tris[i + 1].x, tris[i + 1].z), c = new Vector2(tris[i + 2].x, tris[i + 2].z);
                // 칸보다 작은 삼각형은 칸 가운데를 덮지 못하므로 무게중심이 든 칸도 돌로 표시
                Vector2 g = (a + b + c) / 3f;
                int gx = Mathf.FloorToInt((g.x - minX) / CellSize), gz = Mathf.FloorToInt((g.y - minZ) / CellSize);
                if (gx >= 0 && gz >= 0 && gx < w && gz < h) cells[gz * w + gx] = 1;
                int x0 = Mathf.FloorToInt((Mathf.Min(a.x, b.x, c.x) - minX) / CellSize), x1 = Mathf.FloorToInt((Mathf.Max(a.x, b.x, c.x) - minX) / CellSize);
                int z0 = Mathf.FloorToInt((Mathf.Min(a.y, b.y, c.y) - minZ) / CellSize), z1 = Mathf.FloorToInt((Mathf.Max(a.y, b.y, c.y) - minZ) / CellSize);
                for (int z = Mathf.Max(0, z0); z <= Mathf.Min(h - 1, z1); z++)
                    for (int x = Mathf.Max(0, x0); x <= Mathf.Min(w - 1, x1); x++)
                    {
                        var p = new Vector2(minX + (x + 0.5f) * CellSize, minZ + (z + 0.5f) * CellSize);   // 칸 가운데가 삼각형 안이면 돌
                        if (InTriangle(p, a, b, c)) cells[z * w + x] = 1;
                    }
            }

            foreach (var rb in rects)
            {
                int x0 = Mathf.Max(0, Mathf.FloorToInt((rb.min.x - minX) / CellSize)), x1 = Mathf.Min(w - 1, Mathf.FloorToInt((rb.max.x - minX) / CellSize));
                int z0 = Mathf.Max(0, Mathf.FloorToInt((rb.min.z - minZ) / CellSize)), z1 = Mathf.Min(h - 1, Mathf.FloorToInt((rb.max.z - minZ) / CellSize));
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++) cells[z * w + x] = 1;
            }

            string path = "Assets/Prefabs/NGH/Resources/NGH_FootSurface_" + scene.name + ".asset";
            var map = AssetDatabase.LoadAssetAtPath<FootSurfaceMap>(path);
            bool isNew = map == null;
            if (isNew) map = ScriptableObject.CreateInstance<FootSurfaceMap>();
            map.sceneName = scene.name; map.origin = new Vector2(minX, minZ); map.cellSize = CellSize;
            map.width = w; map.height = h; map.stone = cells;
            if (isNew) AssetDatabase.CreateAsset(map, path); else EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            int count = 0; foreach (var b in cells) count += b;
            Debug.Log($"[FootSurfaceMap] {scene.name}: {w}x{h} 칸, 돌 {count}칸 → {path}");
        }

        static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float Sign(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
    }
}
