#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Player/Apply SemiReal Ink Shader
    /// NGH_Player 프리팹의 URP Lit 머티리얼을 NGH/SemiRealInk(세미리얼 + 3포인트 조명 + 먹 아웃라인)로 바꿉니다.
    ///  - 원본 머티리얼은 건드리지 않고 Materials/NGH/Player_SemiRealInk 에 같은 이름으로 새로 만들어 끼움
    ///  - 베이스·노멀 텍스처와 양면 설정은 원본에서 복사, 부위별 값은 원본 예제 프리셋을 따름
    ///  - 마스크 맵이 없으므로 금·은·보석은 텍스처 색(HSV)으로 자동 판별
    ///  - Revert 메뉴: 원래 머티리얼로 되돌림
    /// </summary>
    public static class PlayerSemiRealInkApplier
    {
        const string PrefabPath = "Assets/Prefabs/NGH/NGH_Player.prefab";
        const string MatDir = "Assets/Materials/NGH/Player_SemiRealInk";
        const string ShaderName = "NGH/SemiRealInk";

        enum SurfaceType { Standard, Skin, Cloth }

        [MenuItem("Tools/NGH/Player/Apply SemiReal Ink Shader")]
        public static void Apply()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[NGH] 쉐이더가 없다: {ShaderName}"); return; }
            Directory.CreateDirectory(MatDir);

            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            var made = new Dictionary<Material, Material>();
            int slots = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var src = mats[i];
                    if (!IsConvertible(src)) continue;
                    if (!made.TryGetValue(src, out var dst)) made[src] = dst = CreateOrUpdate(src, shader);
                    mats[i] = dst; changed = true; slots++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            AssetDatabase.SaveAssets();
            Debug.Log($"[NGH] 플레이어에 SemiRealInk 적용: 머티리얼 {made.Count}개, 슬롯 {slots}개");
        }

        [MenuItem("Tools/NGH/Player/Revert SemiReal Ink Shader")]
        public static void Revert()
        {
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            int slots = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || m.shader == null || m.shader.name != ShaderName) continue;
                    var orig = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(m.GetTag("NGH_SourceMaterial", false)));
                    if (orig == null) continue;
                    mats[i] = orig; changed = true; slots++;
                }
                if (changed) r.sharedMaterials = mats;
            }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            Debug.Log($"[NGH] 플레이어 머티리얼 되돌림: 슬롯 {slots}개");
        }

        // 텍스처 입힌 불투명 URP Lit만 바꾼다 (유리·증기 게이지·임시 몽둥이 같은 단색·반투명은 그대로)
        static bool IsConvertible(Material m)
        {
            if (m == null || m.shader == null) return false;
            if (m.shader.name != "Universal Render Pipeline/Lit") return false;
            if (m.GetFloat("_Surface") > 0.5f) return false;
            return m.GetTexture("_BaseMap") != null;
        }

        static Material CreateOrUpdate(Material src, Shader shader)
        {
            string path = $"{MatDir}/{src.name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); }
            m.shader = shader;
            m.SetTexture("_BaseMap", src.GetTexture("_BaseMap"));
            m.SetTextureScale("_BaseMap", src.GetTextureScale("_BaseMap"));
            m.SetTextureOffset("_BaseMap", src.GetTextureOffset("_BaseMap"));
            m.SetColor("_BaseColor", src.GetColor("_BaseColor"));
            m.SetTexture("_BumpMap", src.GetTexture("_BumpMap"));
            m.SetFloat("_BumpScale", src.HasProperty("_BumpScale") ? src.GetFloat("_BumpScale") : 1f);
            m.SetOverrideTag("NGH_SourceMaterial", AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(src)));
            ApplyPreset(m, src.name, src.GetFloat("_Cull") < 0.5f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // 원본 예제(SemiRealInkTestSetup.ApplyPreset)의 부위별 값
        static void ApplyPreset(Material m, string name, bool twoSidedSource)
        {
            SetSurfaceType(m, SurfaceType.Standard);
            m.SetFloat("_Cull", (float)CullMode.Off);   // 한 겹짜리 부품이 뚫려 보이지 않게 양면
            m.SetColor("_BackFaceColor", new Color(0.55f, 0.1f, 0.1f, 0f));
            m.SetFloat("_Smoothness", 0.4f);
            m.SetFloat("_SpecularStrength", 1f);
            m.SetFloat("_MetalDetect", 1f);
            m.SetFloat("_MetalHue", 0.115f);
            m.SetFloat("_MetalHueWidth", 0.055f);
            m.SetFloat("_MetalSmoothness", 0.78f);
            m.SetFloat("_MetalBoost", 1.2f);
            m.SetFloat("_SilverDetect", 1f);
            m.SetFloat("_GemDetect", 1f);

            if (name.Contains("Skin"))
            {
                SetSurfaceType(m, SurfaceType.Skin);
                m.SetFloat("_Cull", (float)CullMode.Back);
                m.SetFloat("_Smoothness", 0.5f);
                m.SetFloat("_SpecularStrength", 0.7f);
                m.SetFloat("_MetalDetect", 0f);
                m.SetFloat("_SilverDetect", 0f);
                m.SetFloat("_GemDetect", 0f);
                m.SetFloat("_BumpScale", 1.06f);
                m.SetFloat("_Realism", 1f);
                m.SetFloat("_AmbientStrength", 0.8f);
                m.SetFloat("_FillIntensity", 0.45f);
                m.SetFloat("_RimIntensity", 1.6f);
                m.SetFloat("_RimWidth", 0.45f);
                m.SetFloat("_RimSoftness", 0.4f);
                m.SetFloat("_OutlineWidth", 1.4f);
                m.SetFloat("_OutlineOpacity", 0.85f);
                m.SetFloat("_OutlineBaseBlend", 0f);
                m.SetFloat("_OutlineNoiseAmount", 0f);
            }
            else if (name.Contains("Jeogori") || name.Contains("Chima") || name.Contains("Skirt"))
            {
                // 저고리·치마: 천 + 금장 자수. 금실은 70%만 금속으로, 안감은 빨강
                SetSurfaceType(m, SurfaceType.Cloth);
                m.SetFloat("_Smoothness", 0.3f);
                m.SetFloat("_MetalDetect", 0.7f);
                m.SetFloat("_MetalSmoothness", 0.75f);
                m.SetFloat("_MetalBoost", 1.8f);
                m.SetFloat("_MetalSaturation", 1.5f);
                m.SetFloat("_SheenIntensity", 0.6f);
                m.SetFloat("_RimIntensity", 1.2f);
                if (name.Contains("RedInside"))
                    m.SetColor("_BackFaceColor", new Color(0.55f, 0.1f, 0.1f, 0.85f));
            }
            else if (name == "material_0.007")
            {
                // 머리카락
                SetSurfaceType(m, SurfaceType.Cloth);
                m.SetFloat("_Smoothness", 0.55f);
                m.SetFloat("_ClothSpecular", 0.8f);
                m.SetFloat("_SheenIntensity", 0.25f);
            }
            else if (name.EndsWith("material_0.004"))
            {
                // 칼: 금 장식 + 은빛 칼날
                m.SetFloat("_Smoothness", 0.45f);
            }
            else if (name == "material_0.018")
            {
                // 고글: 구리 링 + 초록 렌즈
                m.SetFloat("_MetalHue", 0.09f);
                m.SetFloat("_MetalHueWidth", 0.075f);
                m.SetFloat("_SilverDetect", 0.5f);
            }
            else if (name.Contains("Boots"))
            {
                m.SetFloat("_Smoothness", 0.45f);
            }
            if (twoSidedSource) m.SetFloat("_Cull", (float)CullMode.Off);
        }

        static void SetSurfaceType(Material m, SurfaceType type)
        {
            m.SetFloat("_MaterialType", (float)type);
            m.DisableKeyword("_MATERIALTYPE_STANDARD");
            m.DisableKeyword("_MATERIALTYPE_SKIN");
            m.DisableKeyword("_MATERIALTYPE_CLOTH");
            m.EnableKeyword("_MATERIALTYPE_" + type.ToString().ToUpperInvariant());
        }
    }
}
#endif
