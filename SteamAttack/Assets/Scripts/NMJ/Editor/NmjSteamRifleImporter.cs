using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace NMJ.EditorTools
{
    /// <summary>
    /// Blender에서 정리해 온 증기 소총(Assets/Arts/Models/NMJ/SteamRifle)을 임포트할 때 설정을 자동으로 맞춘다.
    ///   - _Normal 텍스처 → Normal map 타입
    ///   - _MetallicSmoothness 텍스처 → sRGB 끔 (RGB=금속, A=매끄러움, URP Lit 규격)
    ///   - URP Lit 재질 NMJ_SteamRifle.mat 을 만들어 FBX 재질에 연결
    /// FBX 안 메쉬 이름이 _LOD0/_LOD1/_LOD2 로 끝나서 유니티가 LOD Group 을 자동으로 붙인다.
    /// 총구 위치는 자식 "Muzzle"(앞쪽 +Z), 피벗은 손잡이(방아쇠 뒤).
    /// </summary>
    public class NmjSteamRifleImporter : AssetPostprocessor
    {
        const string Root = "Assets/Arts/Models/NMJ/SteamRifle";
        const string Name = "NMJ_SteamRifle";
        const string MatPath = Root + "/" + Name + ".mat";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var ti = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("_Normal.png"))
                ti.textureType = TextureImporterType.NormalMap;
            else if (assetPath.EndsWith("_MetallicSmoothness.png"))
            {
                ti.sRGBTexture = false;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
            }
        }

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.importNormals = ModelImporterNormals.Import;
            mi.importTangents = ModelImporterTangents.CalculateMikk;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat != null)
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), Name), mat);
        }

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.StartsWith(Root) && (p.EndsWith(".fbx") || p.EndsWith(".png")))
                {
                    EditorApplication.delayCall -= EnsureMaterial;
                    EditorApplication.delayCall += EnsureMaterial;
                    return;
                }
        }

        [MenuItem("Tools/NMJ/Steam Rifle - 재질 다시 만들기")]
        static void EnsureMaterial()
        {
            var tex = Root + "/Textures/" + Name;
            var baseCol = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_BaseColor.png");
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_Normal.png");
            var ms = AssetDatabase.LoadAssetAtPath<Texture2D>(tex + "_MetallicSmoothness.png");
            if (baseCol == null || normal == null || ms == null) return;

            bool created = false;
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (mat == null)
            {
                var shader = GraphicsSettings.currentRenderPipeline != null
                    ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
                mat = new Material(shader) { name = Name };
                AssetDatabase.CreateAsset(mat, MatPath);
                created = true;
            }
            mat.SetTexture("_BaseMap", baseCol); mat.SetTexture("_MainTex", baseCol);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetTexture("_BumpMap", normal); mat.SetFloat("_BumpScale", 1f); mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_MetallicGlossMap", ms); mat.SetFloat("_Smoothness", 1f); mat.SetFloat("_GlossMapScale", 1f);
            mat.SetFloat("_SmoothnessTextureChannel", 0f); // metallic map alpha
            mat.EnableKeyword("_METALLICSPECGLOSSMAP"); mat.EnableKeyword("_METALLICGLOSSMAP");
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            var fbx = Root + "/" + Name + ".fbx";
            if (created && File.Exists(fbx))
            {
                var mi = (ModelImporter)AssetImporter.GetAtPath(fbx);
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), Name), mat);
                mi.SaveAndReimport();
            }
            Debug.Log("[NMJ SteamRifle] 재질 연결 완료: " + MatPath);
        }
    }
}
