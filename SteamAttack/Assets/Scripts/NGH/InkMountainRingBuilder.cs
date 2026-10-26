#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Environment/Build Ink Mountain Ring
    /// 바르코로 뽑은 수묵 산 파츠 8종(Arts/Models/NGH/InkMountains)을 도성 둘레에 타원형으로 둘러 산맥을 조립합니다.
    ///  - FBX에 박힌 텍스처를 Textures/파츠이름 폴더로 꺼내고, URP Unlit 머티리얼(M_InkPartXX)을 만듭니다 (그림에 명암이 이미 그려져 있어 조명을 받지 않게)
    ///  - 파츠는 높이 1 · 중심 기준이라 높이 배율로 키우고 바닥을 y=0에 맞춥니다
    ///  - 한 칸마다 큰 봉우리 1 + 곁봉 + 받침 바위, 몇 칸마다 연결 능선·바위·뒷줄 봉우리를 시드 랜덤으로 배치 (같은 시드면 같은 결과)
    ///  - 결과는 NGH_InkMountainRing 프리팹으로 저장하고 열린 씬에 놓습니다. 기존 NMJ_BackgroundMountains는 끄기만 합니다 (지우지 않음). 씬 저장은 사람이 합니다
    /// </summary>
    public static class InkMountainRingBuilder
    {
        const string ModelDir = "Assets/Arts/Models/NGH/InkMountains";
        const string TexDir = ModelDir + "/Textures";
        const string MatDir = ModelDir + "/Materials";
        const string PrefabPath = "Assets/Prefabs/NGH/NGH_InkMountainRing.prefab";
        const string RootName = "NGH_InkMountainRing";
        const string NmjRingName = "NMJ_BackgroundMountains";

        // 타원 둘레 (NMJ 원경 산 배치를 따라 잡음: 북 북악·북한산, 서 인왕산, 동 낙산, 남 남산 너머)
        static readonly Vector2 Center = new Vector2(-10f, 20f);
        const float RadiusX = 420f;
        const float RadiusZ = 450f;
        const float Step = 80f;        // 큰 봉우리 간격 (둘레를 따라)
        const int Seed = 1887;

        enum Part { MainPeak, Cluster, TwinPeaks, Needle, ShortPeak, BaseMound, Rock, LowRidge }

        static readonly string[] Files =
        {
            "InkPart01_MainPeak", "InkPart02_Cluster", "InkPart03_TwinPeaks", "InkPart04_Needle",
            "InkPart05_ShortPeak", "InkPart06_BaseMound", "InkPart07_Rock", "InkPart08_LowRidge",
        };

        [MenuItem("Tools/NGH/Environment/Build Ink Mountain Ring")]
        public static void Build()
        {
            var models = new GameObject[Files.Length];
            var mats = new Material[Files.Length];
            for (int i = 0; i < Files.Length; i++)
            {
                string path = $"{ModelDir}/{Files[i]}.fbx";
                models[i] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (models[i] == null) { Debug.LogError($"[NGH] 산 파츠가 없다: {path}"); return; }
                mats[i] = PrepareMaterial(path, Files[i]);
            }

            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Build Ink Mountain Ring");
            var rng = new System.Random(Seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // 둘레를 호 길이 기준으로 고르게 자름
            const int Samples = 2048;
            var pts = new Vector2[Samples + 1];
            var arc = new float[Samples + 1];
            for (int i = 0; i <= Samples; i++)
            {
                float t = i / (float)Samples * Mathf.PI * 2f;
                pts[i] = Center + new Vector2(Mathf.Cos(t) * RadiusX, Mathf.Sin(t) * RadiusZ);
                if (i > 0) arc[i] = arc[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            }
            float perimeter = arc[Samples];
            int slots = Mathf.RoundToInt(perimeter / Step);

            int count = 0;
            for (int s = 0; s < slots; s++)
            {
                Vector2 p = PointAt(pts, arc, s * perimeter / slots, out Vector2 tangent);
                Vector2 outward = (p - Center).normalized;

                // 큰 봉우리: 주봉 / 3봉 / 쌍봉 / 낮은 봉우리
                float roll = R(0f, 1f);
                Part tall = roll < 0.2f ? Part.MainPeak : roll < 0.5f ? Part.Cluster : roll < 0.75f ? Part.TwinPeaks : Part.ShortPeak;
                float tallH = tall switch
                {
                    Part.MainPeak => R(190f, 240f),
                    Part.TwinPeaks => R(150f, 190f),
                    Part.Cluster => R(130f, 170f),
                    _ => R(85f, 115f),
                };
                Place(root, models, mats, tall, p + tangent * R(-10f, 10f), tallH, R(0f, 360f), R(0.9f, 1.15f)); count++;

                // 곁봉 1~2개 (바늘봉·낮은 봉우리)
                int sides = rng.Next(1, 3);
                for (int k = 0; k < sides; k++)
                {
                    Part side = R(0f, 1f) < 0.5f ? Part.Needle : Part.ShortPeak;
                    float h = side == Part.Needle ? R(100f, 150f) : R(60f, 90f);
                    Vector2 q = p + tangent * R(-Step * 0.5f, Step * 0.5f) + outward * R(-15f, 35f);
                    Place(root, models, mats, side, q, h, R(0f, 360f), R(0.9f, 1.1f)); count++;
                }

                // 받침 바위 더미: 봉우리 밑동 안쪽을 덮어 이음새를 가림 (가로 폭 기준)
                Place(root, models, mats, Part.BaseMound, p - outward * R(25f, 40f) + tangent * R(-15f, 15f), R(55f, 75f), Yaw(tangent) + R(-20f, 20f), 1f); count++;

                // 연결 능선: 두 칸에 하나, 봉우리 사이 아래쪽
                if (s % 2 == 0)
                {
                    Vector2 mid = PointAt(pts, arc, (s + 0.5f) * perimeter / slots, out Vector2 midTan);
                    Place(root, models, mats, Part.LowRidge, mid - (mid - Center).normalized * R(10f, 25f), R(45f, 60f), Yaw(midTan), 1f); count++;
                }

                // 바위: 세 칸에 하나, 가장 안쪽
                if (s % 3 == 1)
                {
                    Place(root, models, mats, Part.Rock, p - outward * R(60f, 75f) + tangent * R(-25f, 25f), R(30f, 45f), R(0f, 360f), R(0.9f, 1.1f)); count++;
                }

                // 뒷줄 높은 봉우리: 두 칸에 하나, 바깥쪽으로 물려 깊이감
                if (s % 2 == 1)
                {
                    Part back = R(0f, 1f) < 0.5f ? Part.MainPeak : Part.TwinPeaks;
                    Place(root, models, mats, back, p + outward * R(70f, 100f), R(220f, 280f), R(0f, 360f), R(0.95f, 1.2f)); count++;
                }
            }

            var nmj = GameObject.Find(NmjRingName);
            if (nmj != null)
            {
                Undo.RecordObject(nmj, "Hide NMJ mountains");
                nmj.SetActive(false);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Selection.activeGameObject = root;
            Debug.Log($"[NGH] 수묵 산맥 조립: 둘레 {perimeter:0}m, 칸 {slots}개, 파츠 {count}개 → {PrefabPath}" + (nmj != null ? $" ({NmjRingName}는 껐음)" : ""));
        }

        static Vector2 PointAt(Vector2[] pts, float[] arc, float d, out Vector2 tangent)
        {
            float total = arc[arc.Length - 1];
            d = Mathf.Repeat(d, total);
            int i = System.Array.BinarySearch(arc, d);
            if (i < 0) i = ~i;
            i = Mathf.Clamp(i, 1, arc.Length - 1);
            float seg = arc[i] - arc[i - 1];
            float t = seg > 0f ? (d - arc[i - 1]) / seg : 0f;
            tangent = (pts[i] - pts[i - 1]).normalized;
            return Vector2.Lerp(pts[i - 1], pts[i], t);
        }

        static float Yaw(Vector2 dir) => Mathf.Atan2(dir.x, dir.y) * Mathf.Rad2Deg + 90f;

        // 바닥 능선·받침 더미는 가로 폭(height 인자를 폭으로 해석), 나머지는 높이로 크기를 맞춤
        static void Place(GameObject root, GameObject[] models, Material[] mats, Part part, Vector2 xz, float size, float yaw, float widthJitter)
        {
            var src = models[(int)part];
            var go = (GameObject)PrefabUtility.InstantiatePrefab(src, root.transform);
            go.name = $"{part}_{root.transform.childCount:000}";

            var mesh = go.GetComponentInChildren<MeshFilter>().sharedMesh;
            var b = mesh.bounds;                         // 원본: x/y 바닥면, z 높이 (루트 -90°X로 세워짐)
            bool byWidth = part == Part.BaseMound || part == Part.LowRidge;
            float unit = byWidth ? Mathf.Max(b.size.x, b.size.y) : b.size.z;
            float scale = size / Mathf.Max(unit, 0.001f);
            float height = b.size.z * scale;

            go.transform.localScale = new Vector3(scale * widthJitter, scale * widthJitter, scale) * 1f;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f) * src.transform.localRotation;
            // 중심 기준 → 바닥을 y=0에, 밑동이 뜨지 않게 높이의 4%를 묻음
            float bottom = (b.center.z - b.extents.z) * scale;
            go.transform.localPosition = new Vector3(xz.x, -bottom - height * 0.04f, xz.y);

            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                // Remesh한 모델은 서브메시가 여러 개라 모든 슬롯에 같은 머티리얼을 넣음
                var slots = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < slots.Length; i++) slots[i] = mats[(int)part];
                r.sharedMaterials = slots;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic);
        }

        static Material PrepareMaterial(string modelPath, string name)
        {
            string matPath = $"{MatDir}/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null && mat.GetTexture("_BaseMap") != null) return mat;

            string texFolder = $"{TexDir}/{name}";
            Directory.CreateDirectory(texFolder);
            Directory.CreateDirectory(MatDir);
            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.ExtractTextures(texFolder);
            AssetDatabase.Refresh();

            Texture2D baseTex = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texFolder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (lower.Contains("normal") || lower.Contains("orm") || lower.Contains("rough") || lower.Contains("metal")) continue;
                baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                break;
            }
            if (baseTex == null) Debug.LogWarning($"[NGH] {name} 텍스처를 찾지 못함 (FBX에 박힌 텍스처 없음?)");

            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", baseTex);
            mat.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
#endif
