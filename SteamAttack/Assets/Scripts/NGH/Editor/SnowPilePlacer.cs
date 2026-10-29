using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NGH.EditorTools
{
    /// <summary>
    /// 경복궁 마당에 '눈을 치워 쌓아 둔 눈더미'(Varco3D PR_SnowSoft_* 8종)를 군데군데 놓는다.
    ///  - 치운 눈은 길(삼도)에서 밀어내 마당 가장자리·담장 아래·건물 앞 구석에 쌓이므로,
    ///    담장 안쪽 둘레와 삼도 양옆 바깥 띠를 따라 일정 간격으로 놓고 위치·크기·방향을 조금씩 흔든다
    ///  - 삼도 위, 근정전 월대 위, 기존 오브젝트(건물·석등 등)와 겹치는 자리는 피한다 (아래로 쏜 레이가 마당 바닥이 아닌 것에 맞으면 건너뜀)
    ///  - Varco 모델은 길이 1m 로 정규화돼 있어 원하는 크기(m)로 키워 놓는다
    /// 메뉴: NGH/궁궐 눈더미 다시 놓기 (몇 번 눌러도 이전 것을 지우고 같은 배치로 다시 놓음)
    /// </summary>
    public static class SnowPilePlacer
    {
        const string GroundPath = "현무_세계관 (1887 정월, 멈춘 도성)/00_지면";
        const string RootName = "NGH_궁궐_눈더미";
        const string ModelDir = "Assets/Arts/Models/Varco3D/";

        // 궁성 담장 안쪽 (PalaceFloorTileBuilder.Fill 과 같음)
        const float WallW = -52.4f, WallE = 52.4f, WallS = -39.3f, WallN = 88.2f;
        const float SamdoHalf = 4f;          // 어도+신도 반폭
        static readonly Rect Woldae = Rect.MinMaxRect(-24f, 11.4f, 24f, 49.4f);   // 근정전 월대 (여유 1m)

        struct Kind { public string name; public Vector2 size; }   // size: 놓을 때 길이(m), 높이 배율

        [MenuItem("NGH/궁궐 눈더미 다시 놓기")]
        public static void Place()
        {
            var ground = GameObject.Find(GroundPath);
            if (!ground) { Debug.LogError("[SnowPile] 지면 그룹을 찾지 못했습니다"); return; }
            var old = ground.transform.Find(RootName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "궁궐 눈더미");
            root.transform.SetParent(ground.transform, false);

            var kinds = new List<(GameObject prefab, float length, float heightMul)>();
            void Add(string n, float len, float hMul)
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}{n}/{n}.fbx");
                if (p) { EnsureMaterial(n, p); kinds.Add((p, len, hMul)); }
            }
            // 부드러운 눈더미 8종 (Varco, 모델은 가장 긴 변 1m 로 정규화 → 길이(m)로 키우고 높이는 모델 비율 그대로)
            Add("PR_SnowSoft_Bank", 4.0f, 1.0f);     // 길게 쌓인 눈둑
            Add("PR_SnowSoft_Dome", 1.6f, 1.0f);     // 둥근 돔
            Add("PR_SnowSoft_Drift", 2.6f, 1.0f);    // 한쪽이 높은 쐐기 (벽에 기댄 눈)
            Add("PR_SnowSoft_Twin", 2.3f, 1.0f);     // 두 봉우리
            Add("PR_SnowSoft_Low", 1.2f, 1.0f);      // 낮고 작은 더미
            Add("PR_SnowSoft_Crescent", 3.0f, 1.0f); // 휘어진 눈둑
            Add("PR_SnowSoft_Cone", 1.4f, 1.0f);     // 높은 더미
            Add("PR_SnowSoft_Lobed", 2.1f, 1.0f);    // 세 갈래 낮은 더미
            if (kinds.Count == 0) { Debug.LogError("[SnowPile] 눈더미 모델이 없습니다"); return; }

            // 담장 쪽만: 마당 가운데(삼도·근정전 앞)는 비우고, 담장 안쪽 면을 따라 5.5m 간격으로 놓는다.
            // 방향 = 담장과 나란하게, 담장에서 1.1~2.3m 띄움. 2.8m 간격 (벽 쪽으로 밀어 쌓은 눈)
            var spots = new List<Vector3>();   // x, z, 방향(도)
            const float Step = 2.8f;   // 담장 쪽에 소품이 많아 건너뛰는 자리를 감안 (이전 18개의 약 3배)
            for (float z = WallS + 2.5f; z < WallN - 2f; z += Step) { spots.Add(new Vector3(WallW + 1.7f, z, 90f)); spots.Add(new Vector3(WallE - 1.7f, z + Step * 0.5f, 270f)); }
            // 남쪽 담장 (광화문 앞 |x|<17 은 문)
            for (float x = WallW + 4f; x < WallE - 3f; x += Step) { if (Mathf.Abs(x) < 18f) continue; spots.Add(new Vector3(x, WallS + 1.7f, 0f)); }
            // 북쪽 담장 안쪽 (건청궁 담장 바깥 모서리 양옆)
            for (float x = WallW + 4f; x < -27f; x += Step) spots.Add(new Vector3(x, WallN - 1.7f, 180f));
            for (float x = 28f; x < WallE - 3f; x += Step) spots.Add(new Vector3(x, WallN - 1.7f, 180f));
            // 건청궁 앞 담장 (z 65, 가운데 문 |x|<3 제외)
            for (float x = -24f; x < 25f; x += Step) { if (Mathf.Abs(x) < 6f) continue; spots.Add(new Vector3(x, 63.3f, 180f)); }

            _obstacles = null;
            var rnd = new System.Random(1887);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            int placed = 0, skipped = 0;
            foreach (var s in spots)
            {
                // 담장과 나란한 방향으로는 크게, 담장에서 떨어지는 방향으로는 조금만 흔듦
                bool alongZ = Mathf.Approximately(s.z, 90f) || Mathf.Approximately(s.z, 270f);
                float x = s.x + (alongZ ? R(-0.6f, 0.6f) : R(-1.8f, 1.8f)), z = s.y + (alongZ ? R(-1.8f, 1.8f) : R(-0.6f, 0.6f));
                if (Woldae.Contains(new Vector2(x, z)) || Mathf.Abs(x) < SamdoHalf + 0.4f) { skipped++; continue; }
                if (Blocked(x, z)) { skipped++; continue; }
                var k = kinds[rnd.Next(kinds.Count)];
                float scale = k.length * R(0.8f, 1.2f);
                // Varco FBX 는 Z-up 이라 모델 자체에 X -90° 회전이 들어 있음 → 그 회전은 그대로 두고,
                // 빈 부모에 방향(Y)·크기를 줘서 세움 (부모 Y 스케일 = 높이)
                var holder = new GameObject("VARCO_" + k.prefab.name);
                holder.transform.SetParent(root.transform, false);
                holder.transform.position = new Vector3(x, 0.06f, z);
                holder.transform.rotation = Quaternion.Euler(0f, s.z + R(-20f, 20f), 0f);
                holder.transform.localScale = new Vector3(scale, scale * k.heightMul * R(0.85f, 1.15f), scale);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(k.prefab, holder.transform);
                go.transform.localPosition = Vector3.zero;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                placed++;
            }
            EditorSceneManager.MarkSceneDirty(ground.scene);
            Debug.Log($"[SnowPile] 눈더미 {placed}개 배치 (겹침·길 위 {skipped}곳 생략), 종류 {kinds.Count}");
        }

        // 그 자리에 바닥이 아닌 것(건물·소품)이 있으면 true — 위에서 아래로 반경 1m 를 쏴서 높이 0.4m 이상에 맞으면 막힘
        // 바닥에서 1m 안쪽에 실제로 놓인 오브젝트(건물 기단·석등·품계석·수레 등)만 막힘으로 봄.
        // 지붕·처마·나무 가지처럼 높이 떠 있는 것, 얇은 파티클·바닥판, 아주 큰 묶음 경계는 무시
        static List<Bounds> _obstacles;
        static bool Blocked(float x, float z)
        {
            if (_obstacles == null)
            {
                _obstacles = new List<Bounds>();
                var groundT = GameObject.Find(GroundPath).transform;
                foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude))
                {
                    var b = r.bounds;
                    if (b.min.y > 1.0f || b.max.y < 0.3f) continue;               // 떠 있거나 바닥에 깔린 것
                    if (b.size.x > 30f || b.size.z > 30f) continue;               // 담장 한 줄·큰 바닥
                    if (r.transform.IsChildOf(groundT)) continue;
                    _obstacles.Add(b);
                }
            }
            foreach (var b in _obstacles)
                if (x > b.min.x - 0.8f && x < b.max.x + 0.8f && z > b.min.z - 0.8f && z < b.max.z + 0.8f) return true;
            return false;
        }

        // 다른 Varco 소품처럼 URP Lit + base_color/normal, 눈은 살짝 푸른 흰색
        static void EnsureMaterial(string n, GameObject prefab)
        {
            string matPath = $"{ModelDir}{n}/{n}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!m)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                var bc = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}{n}/base_color.png");
                var nm = AssetDatabase.LoadAssetAtPath<Texture2D>($"{ModelDir}{n}/normal.png");
                if (bc) m.SetTexture("_BaseMap", bc);
                if (nm)
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(nm));
                    if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                    m.SetTexture("_BumpMap", nm); m.EnableKeyword("_NORMALMAP");
                }
                m.SetColor("_BaseColor", new Color(0.97f, 0.98f, 1f));
                m.SetFloat("_Smoothness", 0.25f);
                AssetDatabase.CreateAsset(m, matPath);
            }
            var path = AssetDatabase.GetAssetPath(prefab);
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            bool changed = false;
            foreach (var r in prefab.GetComponentsInChildren<Renderer>())
                foreach (var sm in r.sharedMaterials)
                {
                    if (!sm) continue;
                    var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), sm.name);
                    mi.AddRemap(id, m); changed = true;
                }
            if (changed) mi.SaveAndReimport();
        }
    }
}
