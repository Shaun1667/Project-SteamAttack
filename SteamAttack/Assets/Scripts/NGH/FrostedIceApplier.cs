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
    /// Tools/NGH/Environment/Apply Frosted Ice (Open Scene)
    /// 열린 씬의 크리스탈 같은 얼음(바르코 얼음 결정 3종, 가시 얼음 M_IceCrystal)을 서리 낀 얼음(NGH/FrostedIce)으로 바꿉니다.
    ///  - 원본 머티리얼(NMJ·바르코 폴더)은 건드리지 않고, 씬에 놓인 렌더러의 머티리얼 칸만 바꿈 (먹 외곽선 같은 다른 칸은 그대로)
    ///  - 광화문 얼음문·얼음벽은 부서지는 연출과 엮여 있을 수 있어 대상에서 뺌
    ///  - Revert 메뉴로 되돌림 (메시가 든 바르코 폴더 이름으로 원본 머티리얼을 찾고, 아니면 M_IceCrystal)
    /// </summary>
    public static class FrostedIceApplier
    {
        const string MatPath = "Assets/VFX/NGH/Ice/M_NGH_FrostedIce.mat";

        // 바꿀 원본 머티리얼 이름 → 원본 경로 (되돌릴 때 씀)
        static readonly Dictionary<string, string> Targets = new Dictionary<string, string>
        {
            { "M_IceCrystal", "Assets/Materials/NMJ/HyeonmuWorld/M_IceCrystal.mat" },
            { "PR_IceCrystal_Small", "Assets/Arts/Models/Varco3D/PR_IceCrystal_Small/PR_IceCrystal_Small.mat" },
            { "PR_IceCrystal_Spike", "Assets/Arts/Models/Varco3D/PR_IceCrystal_Spike/PR_IceCrystal_Spike.mat" },
            { "PR_IceCrystal_Large", "Assets/Arts/Models/Varco3D/PR_IceCrystal_Large/PR_IceCrystal_Large.mat" },
        };

        [MenuItem("Tools/NGH/Environment/Apply Frosted Ice (Open Scene)")]
        public static void Apply()
        {
            var ice = GetOrCreateMaterial();
            // 종류마다 원래 텍스처를 디테일로 쓰는 변형 머티리얼 (기본 값은 M_NGH_FrostedIce를 따름)
            var variants = new Dictionary<string, Material>();
            foreach (var kv in Targets)
            {
                var src = AssetDatabase.LoadAssetAtPath<Material>(kv.Value);
                variants[kv.Key] = GetOrCreateVariant(ice, kv.Key, src);
            }
            int changed = Swap((name) => variants.TryGetValue(name, out var m) ? m : null, "Apply Frosted Ice");
            Debug.Log($"[NGH] 서리 얼음 적용: 렌더러 {changed}개 ({SceneManager.GetActiveScene().name})");
        }

        [MenuItem("Tools/NGH/Environment/Revert Frosted Ice (Open Scene)")]
        public static void Revert()
        {
            // 렌더러 이름·메시로 원본을 고름: 바르코 결정은 메시가 든 FBX 폴더 이름, 가시 얼음은 M_IceCrystal
            int changed = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials; bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || mats[i].shader.name != "NGH/FrostedIce") continue;
                    string original = Targets["M_IceCrystal"];
                    var mf = r.GetComponent<MeshFilter>();
                    string meshPath = mf && mf.sharedMesh ? AssetDatabase.GetAssetPath(mf.sharedMesh) : "";
                    foreach (var kv in Targets) if (meshPath.Contains("/" + kv.Key + "/")) original = kv.Value;
                    var m = AssetDatabase.LoadAssetAtPath<Material>(original);
                    if (m == null) continue;
                    if (!dirty) { Undo.RecordObject(r, "Revert Frosted Ice"); dirty = true; }
                    mats[i] = m;
                }
                if (dirty) { r.sharedMaterials = mats; changed++; }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[NGH] 서리 얼음 되돌림: 렌더러 {changed}개");
        }

        static int Swap(System.Func<string, Material> pick, string undo)
        {
            int changed = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var mats = r.sharedMaterials; bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null) continue;
                    var m = pick(mats[i].name);
                    if (m == null || m == mats[i]) continue;
                    if (!dirty) { Undo.RecordObject(r, undo); dirty = true; }
                    mats[i] = m;
                }
                if (dirty) { r.sharedMaterials = mats; changed++; }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            return changed;
        }

        static Material GetOrCreateVariant(Material baseMat, string name, Material src)
        {
            string path = $"{Path.GetDirectoryName(MatPath)}/M_NGH_FrostedIce_{name}.mat".Replace('\\', '/');
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(baseMat);
                AssetDatabase.CreateAsset(mat, path);
            }
            Texture tex = null;
            if (src != null)
            {
                if (src.HasProperty("_BaseMap")) tex = src.GetTexture("_BaseMap");
                if (tex == null && src.HasProperty("_MainTex")) tex = src.GetTexture("_MainTex");
            }
            mat.SetTexture("_DetailTex", tex);
            mat.SetFloat("_DetailStrength", tex != null ? 0.5f : 0f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }

        static Material GetOrCreateMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat != null) return mat;
            Directory.CreateDirectory(Path.GetDirectoryName(MatPath));
            mat = new Material(Shader.Find("NGH/FrostedIce"));
            AssetDatabase.CreateAsset(mat, MatPath);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
#endif
