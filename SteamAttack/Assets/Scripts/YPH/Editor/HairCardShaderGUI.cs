using UnityEditor;
using UnityEngine;

namespace YPH
{
    /// <summary>
    /// Hair Cards의 두 데이터 맵과 절단 기준을 표시하는 머티리얼 편집기다.
    /// 본체용 UV 좌표 맵은 카드에서 쓰지 않으므로 노출하지 않는다.
    /// </summary>
    public sealed class HairCardShaderGUI : ShaderGUI
    {
        /// <summary>두께와 광택을 조절하는 항목 및 한국어 도움말을 표시한다.</summary>
        public override void OnGUI(MaterialEditor editor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            editor.TexturePropertySingleLine(new GUIContent("잔머리 모양", "알파가 머리카락의 빈 공간을 정합니다. Linear 데이터로 가져옵니다."), FindProperty("_BaseMap", properties));
            editor.TexturePropertySingleLine(new GUIContent("머리결 표면", "RG는 Normal, B는 밝기, A는 반사 변화입니다. Linear 데이터로 가져옵니다."), FindProperty("_HairBakeMap", properties));
            editor.ShaderProperty(FindProperty("_Cutoff", properties), new GUIContent("절단 기준", "0~1. 올리면 가는 결이 사라지고 내리면 굵어집니다. 0.3~0.5를 권장합니다."));
            editor.ShaderProperty(FindProperty("_BumpScale", properties), new GUIContent("결의 깊이", "배율. 올리면 가는 결의 반사가 거칠어집니다. 0.1~0.4를 권장합니다."));
            editor.ShaderProperty(FindProperty("_Smoothness", properties), new GUIContent("광택의 선명함", "0~1. 올리면 반사 띠가 좁아집니다. 0.5~0.8을 권장합니다."));
            editor.ShaderProperty(FindProperty("_BaseColor", properties), new GUIContent("머리색", "표면 밝기에 곱하는 색입니다. 알파를 내리면 가는 결이 잘립니다. 알파 1을 권장합니다."));
            editor.ShaderProperty(FindProperty("_SpecColor", properties), new GUIContent("반사 색과 강도", "색상 0.2가 기본 강도입니다. 밝게 하면 광택이 강해집니다."));
            editor.ShaderProperty(FindProperty("_SpecularHighlights", properties), new GUIContent("방향성 광택", "켜면 결을 따른 반사 띠를 사용합니다."));
            editor.ShaderProperty(FindProperty("_EnvironmentReflections", properties), new GUIContent("주변광 반사", "켜면 주변 환경과 하늘빛의 반사를 더합니다."));
            editor.EnableInstancingField();
            if (EditorGUI.EndChangeCheck())
            {
                foreach (Object target in editor.targets)
                {
                    ValidateMaterial((Material)target);
                }
            }
        }

        /// <summary>모든 렌더 패스가 같은 알파 절단과 TBN을 쓰도록 맞춘다.</summary>
        public override void ValidateMaterial(Material material)
        {
            material.EnableKeyword("_NORMALMAP");
            material.EnableKeyword("_ALPHATEST_ON");
            material.DisableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            SetKeyword(material, "_SPECULARHIGHLIGHTS_OFF", material.GetFloat("_SpecularHighlights") < .5f);
            SetKeyword(material, "_ENVIRONMENTREFLECTIONS_OFF", material.GetFloat("_EnvironmentReflections") < .5f);
        }

        /// <summary>토글 값과 Shader Keyword의 활성 상태를 맞춘다.</summary>
        private static void SetKeyword(Material material, string keyword, bool enabled)
        {
            if (enabled)
            {
                material.EnableKeyword(keyword);
            }
            else
            {
                material.DisableKeyword(keyword);
            }
        }
    }
}
