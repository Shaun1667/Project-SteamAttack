using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 원경 산을 바르코3D 모델로 바꿔 배치 (조선스팀펑크 플로우에서 만든 산 4종, Arts/Models/NMJ/VarcoMountains).
    /// 메뉴: SteamAttack ▸ NMJ ▸ 원경 산 바르코 모델로 배치
    ///
    /// · 기존 '10_북악산_원경'의 산 36개 위치·회전·크기를 그대로 따라 NMJ_BackgroundMountains 프리팹을 만든다
    ///   (모델은 블렌더에서 바닥 가운데 기준 · 반지름 1 · 높이 1로 맞춰 둠 → 기존 배율 그대로 씀)
    /// · 이름으로 모델을 고른다: 먼 산 = 푸른 원경 / 남산·낙산 = 소나무 눈 언덕 / 서릉·동릉·북릉 = 세 봉우리 능선 / 나머지 = 뾰족한 바위산
    /// · 열린 씬에 프리팹을 놓고 기존 산 묶음은 끈다 (지우지 않음 — 되돌리려면 켜고 프리팹을 지우면 됨). 씬 저장은 사람이 한다
    /// </summary>
    public static class NmjVarcoMountainPlacer
    {
        private const string Dir = "Assets/Arts/Models/NMJ/VarcoMountains";
        private const string PrefabPath = "Assets/Prefabs/NMJ/NMJ_BackgroundMountains.prefab";
        private const string OldGroup = "현무_세계관 (1887 정월, 멈춘 도성)/10_북악산_원경";
        private static readonly string[] Models = { "SnowyMountainPeak", "SnowyRockyMountains", "SnowyMountainPines", "BlueLowPolyMountains" };
        // 족자(산수화) 화풍: 사용자 족자 사진을 참고 이미지로 바르코에서 새로 뽑은 4종 (같은 순서: 주봉 / 세 봉우리 능선 / 소나무 언덕 / 먼 산)
        private static readonly string[] ScrollModels = { "ScrollPeak", "ScrollRidge", "ScrollPineHill", "ScrollFarRange" };

        [MenuItem("SteamAttack/NMJ/원경 산 바르코 모델로 배치", false, 22)]
        public static void Run() => Place(Models);

        [MenuItem("SteamAttack/NMJ/원경 산 족자화풍 모델로 배치", false, 23)]
        public static void RunScroll() => Place(ScrollModels);

        private static void Place(string[] models)
        {
            var old = FindIncludingInactive(OldGroup);   // 한 번 배치하면 꺼져 있으므로 GameObject.Find로는 못 찾음
            if (old == null) { Debug.LogWarning("[NMJ] 씬에 '10_북악산_원경'이 없다. 공용 Main을 열고 다시 실행해라."); return; }

            var meshes = new Mesh[models.Length];
            var mats = new Material[models.Length];
            for (int i = 0; i < models.Length; i++) { meshes[i] = PrepareMesh(models[i]); mats[i] = PrepareMaterial(models[i]); }

            var root = new GameObject("NMJ_BackgroundMountains");
            int n = 0, side = 0;
            foreach (Transform t in old.transform)
            {
                if (t.GetComponent<MeshRenderer>() == null) continue;
                int k = Pick(t.name, ref side);
                var go = new GameObject(t.name + " (바르코)");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(t.position, t.rotation);
                go.transform.localScale = t.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = meshes[k];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { mats[k] };   // 먹 외곽선은 뺀다: 세밀한 모델 표면 전체에 겹쳐 산이 검게 덮임
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 먼 배경: 그림자 생략
                go.isStatic = true;
                n++;
            }

            System.IO.Directory.CreateDirectory("Assets/Prefabs/NMJ");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            // 씬: 이미 놓였으면 바꿔 놓고, 기존 산 묶음은 끈다
            var existing = GameObject.Find("NMJ_BackgroundMountains");
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.scene);
            Undo.RegisterCreatedObjectUndo(inst, "바르코 산 배치");
            Undo.RecordObject(old, "기존 산 끄기");
            old.SetActive(false);
            EditorSceneManager.MarkSceneDirty(old.scene);
            Debug.Log($"[NMJ] 바르코 산 {n}개를 배치했다 ({PrefabPath}). 기존 '10_북악산_원경'은 꺼 둠. 씬을 저장하면 반영된다.");
        }

        private static GameObject FindIncludingInactive(string path)
        {
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                foreach (var r in UnityEngine.SceneManagement.SceneManager.GetSceneAt(s).GetRootGameObjects())
                {
                    if (r.name != rootName) continue;
                    if (slash < 0) return r;
                    var t = r.transform.Find(path.Substring(slash + 1));
                    if (t != null) return t.gameObject;
                }
            return null;
        }

        private static int Pick(string name, ref int side)
        {
            if (name.Contains("먼 산")) return 3;
            if (name.Contains("남산") || name.Contains("낙산")) return 2;
            if (name.Contains("릉")) return 1;
            if (name.Contains("곁봉")) return (side++ % 2 == 0) ? 0 : 1;
            return 0;
        }

        private static Mesh PrepareMesh(string model)
        {
            string path = $"{Dir}/{model}.fbx";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi != null && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation))
            {
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importAnimation = false;
                mi.isReadable = false;
                mi.SaveAndReimport();
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh m) return m;
            return null;
        }

        private static Material PrepareMaterial(string model)
        {
            string matPath = $"{Dir}/M_{model}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            string normalPath = $"{Dir}/{model}_Normal.png";
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{model}_Albedo.png"));
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
