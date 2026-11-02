using UnityEditor;
using UnityEngine;

namespace YPH
{
    /// <summary>
    /// Hero Hair Detail 재질의 전용 Inspector다. 머리결 맵, 깊이와 광택을 조절한다.
    /// GameObject에 붙이지 않으며 Shader의 CustomEditor에서 사용한다.
    /// </summary>
    public sealed class HeroHairShaderGUI : ShaderGUI
    {
        /// <summary>선택한 머리 재질의 조절 항목을 그리고 변경된 Keyword를 맞춘다.</summary>
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            DrawMaps(materialEditor, properties);
            DrawProperty(materialEditor, properties, "_BaseColor", "머리색",
                "검은 바탕에 곱하는 색입니다. 기본 흰색을 유지하면 베이크한 명암을 그대로 씁니다.");
            DrawProperty(materialEditor, properties, "_BumpScale", "결의 깊이",
                "노멀의 깊이 배율입니다. 올리면 홈이 깊어집니다. 0.15~0.5를 권장하며 0이면 표면이 매끈해집니다.");
            DrawProperty(materialEditor, properties, "_DetailAlbedoMapScale", "옆머리 미세결 사용량",
                "0~1 배율입니다. 1이면 옆머리 곡선을 따라 가는 결을 더하고, 0이면 사진의 명암을 사용합니다. 1을 권장합니다.");
            DrawProperty(materialEditor, properties, "_Smoothness", "광택의 선명도",
                "0~1 값입니다. 올리면 반사 띠가 가늘고 선명해집니다. 0.5~0.75를 권장합니다.");
            DrawProperty(materialEditor, properties, "_SpecColor", "광택색·강도",
                "광택의 색과 밝기 배율입니다. RGB 0.2가 기본 강도이며 낮추면 차분해집니다.");
            DrawProperty(materialEditor, properties, "_SpecularHighlights", "광택 사용",
                "켜면 결 방향의 반사를 계산합니다. 끄면 직접광·환경광의 광택을 함께 끕니다.");
            DrawProperty(materialEditor, properties, "_EnvironmentReflections", "넓은 환경 반사",
                "켜면 환경 반사와 갓 아래의 넓은 광택 띠를 더합니다. 띠는 주변광과 태양 방향을 이용한 근사입니다.");
            DrawProperty(materialEditor, properties, "_ReceiveShadows", "그림자 받기",
                "켜면 직접광에 갓과 주변 물체의 그림자를 받습니다. 환경 반사는 별도로 계산합니다.");

            // 맵이 없어도 뼈를 따라 움직이는 결 방향 계산에는 탄젠트가 필요하다.
            if (EditorGUI.EndChangeCheck())
            {
                foreach (Object target in materialEditor.targets)
                {
                    ValidateMaterial((Material)target);
                }
            }
            materialEditor.EnableInstancingField();
        }

        /// <summary>깊이 맵 유무와 관계없이 TBN을 유지하고 광택·그림자 옵션을 동기화한다.</summary>
        public override void ValidateMaterial(Material material)
        {
            material.EnableKeyword("_NORMALMAP");
            SetDisabledKeyword(material, "_SPECULARHIGHLIGHTS_OFF", "_SpecularHighlights");
            SetDisabledKeyword(material, "_ENVIRONMENTREFLECTIONS_OFF", "_EnvironmentReflections");
            SetDisabledKeyword(material, "_RECEIVE_SHADOWS_OFF", "_ReceiveShadows");
        }

        /// <summary>머리 표현에 필요한 네 데이터 맵과 필수 가져오기 설정을 안내한다.</summary>
        private static void DrawMaps(MaterialEditor editor, MaterialProperty[] properties)
        {
            editor.TexturePropertySingleLine(new GUIContent("결·표면 맵",
                "기존 UV에 맞춘 데이터 맵입니다. RG는 노멀, B는 명암, A는 가는 결입니다. sRGB를 끕니다."),
                FindProperty("_HairBakeMap", properties));
            editor.TexturePropertySingleLine(new GUIContent("결 방향 맵",
                "2배 각도의 결 방향과 옆머리 영역을 저장한 전용 맵입니다. sRGB를 끕니다."),
                FindProperty("_HairFlowMap", properties));
            editor.TexturePropertySingleLine(new GUIContent("옆머리 곡선 좌표",
                "UV 조각 사이의 결을 이어 주는 16비트 좌표 맵입니다. sRGB·압축·Mipmap을 모두 끕니다."),
                FindProperty("_HairCoordinateMap", properties));
            editor.TexturePropertySingleLine(new GUIContent("옆머리 미세결 원본",
                "곡선 좌표와 함께 사용하는 섬유 데이터 맵입니다. sRGB를 끄고 Mipmap을 켭니다."),
                FindProperty("_HairDetailMap", properties));
        }

        /// <summary>값의 역할과 권장 범위를 툴팁으로 표시한다.</summary>
        private static void DrawProperty(MaterialEditor editor, MaterialProperty[] properties,
            string name, string label, string tooltip)
        {
            editor.ShaderProperty(FindProperty(name, properties), new GUIContent(label, tooltip));
        }

        /// <summary>켜기 옵션이 0일 때 대응하는 비활성화 Keyword를 켠다.</summary>
        private static void SetDisabledKeyword(Material material, string keyword, string property)
        {
            if (material.GetFloat(property) < 0.5f)
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
