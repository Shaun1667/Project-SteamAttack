#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Environment/Replace Ice Crystals With Shards (Open Scene)
    /// 열린 씬의 바르코 얼음 결정(VARCO_PR_IceCrystal_Small/Spike/Large)을 깨진 얼음 조각 모델(Arts/Models/NGH/IceShards)로 바꿉니다.
    ///  - 기존 얼음은 지우지 않고 끔(SetActive false). 새 얼음은 같은 부모 아래 VARCO_IceShard_* 이름으로 놓음
    ///  - 기존 얼음의 바닥 가운데·방향에 맞추고, 기존 테두리(높이·가로 폭) 안에 들어가게 크기를 맞춤 (바닥은 살짝 묻음)
    ///  - Small → Small_A, Spike → Spike_A/B 번갈아, Large → Large_A/B 번갈아
    ///  - FBX에 박힌 텍스처를 꺼내 반투명 URP Lit 머티리얼(M_IceShard_*, 불투명도 70%)을 만듦
    ///  - Restore 메뉴: 새 얼음을 지우고 기존 얼음을 다시 켬
    /// </summary>
    public static class IceShardReplacer
    {
        const string Dir = "Assets/Arts/Models/NGH/IceShards";
        const string Prefix = "VARCO_IceShard_";
        // 기존 결정은 테두리 안이 비어 있고 새 얼음은 꽉 찬 덩어리라, 테두리보다 작게 넣어야 체감 크기가 비슷함
        const float Fill = 0.75f;

        static readonly Dictionary<string, string[]> Map = new Dictionary<string, string[]>
        {
            { "VARCO_PR_IceCrystal_Small", new[] { "IceShard_Small_A" } },
            { "VARCO_PR_IceCrystal_Spike", new[] { "IceShard_Spike_A", "IceShard_Spike_B" } },
            { "VARCO_PR_IceCrystal_Large", new[] { "IceShard_Large_A", "IceShard_Large_B" } },
        };

        [MenuItem("Tools/NGH/Environment/Replace Ice Crystals With Shards (Open Scene)")]
        public static void Replace()
        {
            var models = new Dictionary<string, GameObject>();
            var mats = new Dictionary<string, Material>();
            foreach (var list in Map.Values)
                foreach (var n in list)
                {
                    if (models.ContainsKey(n)) continue;
                    string path = $"{Dir}/{n}.fbx";
                    models[n] = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (models[n] == null) { Debug.LogError($"[NGH] 얼음 모델이 없다: {path}"); return; }
                    mats[n] = PrepareMaterial(path, n);
                }

            var counters = new Dictionary<string, int>();
            int replaced = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                var go = t.gameObject;
                if (!go.activeSelf || !go.scene.IsValid()) continue;
                string key = null;
                foreach (var k in Map.Keys) if (go.name == k || go.name.StartsWith(k + " ")) key = k;
                if (key == null) continue;

                var rends = go.GetComponentsInChildren<Renderer>();
                if (rends.Length == 0) continue;
                var ob = rends[0].bounds; foreach (var r in rends) ob.Encapsulate(r.bounds);

                counters.TryGetValue(key, out int c); counters[key] = c + 1;
                string modelName = Map[key][c % Map[key].Length];

                var inst = (GameObject)PrefabUtility.InstantiatePrefab(models[modelName], go.transform.parent);
                inst.name = Prefix + modelName.Substring("IceShard_".Length);
                Undo.RegisterCreatedObjectUndo(inst, "Replace Ice Crystals");
                inst.transform.rotation = Quaternion.Euler(0f, go.transform.eulerAngles.y, 0f) * models[modelName].transform.localRotation;
                inst.transform.localScale = Vector3.one;
                foreach (var r in inst.GetComponentsInChildren<MeshRenderer>())
                {
                    var slots = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < slots.Length; i++) slots[i] = mats[modelName];
                    r.sharedMaterials = slots;
                }

                // 기존 얼음의 테두리 안에 들어가게: 높이와 가로 폭 중 빡빡한 쪽에 맞춤
                var nb = Bounds(inst);
                float sh = ob.size.y / Mathf.Max(nb.size.y, 0.0001f);
                float sw = Mathf.Max(ob.size.x, ob.size.z) / Mathf.Max(Mathf.Max(nb.size.x, nb.size.z), 0.0001f);
                float s = Mathf.Min(sh, sw) * Fill;
                inst.transform.localScale = Vector3.one * s;
                nb = Bounds(inst);
                // 바닥 가운데를 맞추고 높이의 3%만큼 묻음
                Vector3 target = new Vector3(ob.center.x, ob.min.y - ob.size.y * 0.03f, ob.center.z);
                Vector3 current = new Vector3(nb.center.x, nb.min.y, nb.center.z);
                inst.transform.position += target - current;
                GameObjectUtility.SetStaticEditorFlags(inst, GameObjectUtility.GetStaticEditorFlags(go));

                Undo.RecordObject(go, "Replace Ice Crystals");
                go.SetActive(false);
                replaced++;
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[NGH] 얼음 결정 교체: {replaced}개 ({SceneManager.GetActiveScene().name})");
        }

        [MenuItem("Tools/NGH/Environment/Restore Ice Crystals (Open Scene)")]
        public static void Restore()
        {
            int removed = 0, restored = 0;
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include))
            {
                if (t == null) continue;
                var go = t.gameObject;
                if (go.name.StartsWith(Prefix)) { Undo.DestroyObjectImmediate(go); removed++; continue; }
                foreach (var k in Map.Keys)
                    if ((go.name == k || go.name.StartsWith(k + " ")) && !go.activeSelf)
                    { Undo.RecordObject(go, "Restore Ice Crystals"); go.SetActive(true); restored++; }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[NGH] 얼음 결정 되돌림: 새 얼음 {removed}개 지움, 기존 {restored}개 켬");
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Material PrepareMaterial(string modelPath, string name)
        {
            string matPath = $"{Dir}/Materials/M_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null && mat.GetTexture("_BaseMap") != null) return mat;

            string texFolder = $"{Dir}/Textures/{name}";
            Directory.CreateDirectory(texFolder);
            Directory.CreateDirectory($"{Dir}/Materials");
            ((ModelImporter)AssetImporter.GetAtPath(modelPath)).ExtractTextures(texFolder);
            AssetDatabase.Refresh();

            Texture2D baseTex = null, normal = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { texFolder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                var lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (lower.Contains("normal"))
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                    if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                    normal = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
                else if (!lower.Contains("orm") && !lower.Contains("rough") && !lower.Contains("metal") && baseTex == null)
                    baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }

            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", baseTex);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.75f);
            mat.SetFloat("_Metallic", 0f);
            // 반투명 (불투명도 70%). ZWrite를 켜 얼음 자기 안쪽 면은 가리고 뒤 배경만 비치게 → 겹친 조각 정렬 꼬임 방지
            mat.SetFloat("_Surface", 1);
            mat.SetFloat("_Blend", 0);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
            mat.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 1);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            mat.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.7f));
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
#endif
