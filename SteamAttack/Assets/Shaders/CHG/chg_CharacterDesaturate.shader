// 캐릭터용 URP 셰이더: 기본 텍스처 + 노멀맵 + 메인 조명/그림자 + 주변광, 그리고 HP 연출(물빠짐).
// chg_HpTint가 HP에 따라 값을 서서히 바꿉니다.
//   _Saturation : 1 원래 색 → 0 흑백
//   _Fade       : 0 없음 → 1 전부 물빠짐
// 물빠짐 방식
//   1) 조명 계산 "전"에 텍스처 색(옷감 색) 자체를 바래게 함 → 그림자·굴곡은 그대로 남아 옷이 물빠진 것처럼 보임
//   2) 텍스처의 먹 농도를 따라 빠짐 → 옅게 칠해진 곳부터 먼저 바래고, 진한 먹선·주름은 마지막까지 남음
Shader "CHG/CharacterDesaturate"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Saturation ("Saturation", Range(0, 1)) = 1
        _Brightness ("Brightness", Range(0, 2)) = 1

        [Header(Wash Out)]
        _Fade ("Fade Amount", Range(0, 1)) = 0
        _FadeColor ("Fade Color (Paper)", Color) = (0.93, 0.91, 0.87, 1)
        _InkSoftness ("Ink Order Softness", Range(0.05, 0.6)) = 0.3
        _GlobalWash ("Overall Wash", Range(0, 1)) = 0.25
        _WashSaturation ("Washed Color Remain", Range(0, 1)) = 0.2
        _WashStrength ("Wash Strength", Range(0, 1)) = 0.85
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale;
                half _Smoothness;
                half _Saturation;
                half _Brightness;
                half _Fade;
                half4 _FadeColor;
                half _InkSoftness;
                half _GlobalWash;
                half _WashSaturation;
                half _WashStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = float4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;

                // ---- 물빠짐: 조명 전에 옷감 색 자체를 바꿈
                half albLum = dot(albedo.rgb, half3(0.299, 0.587, 0.114));
                albedo.rgb = lerp(albLum.xxx, albedo.rgb, _Saturation);      // HP에 따른 채도

                if (_Fade > 0.001)
                {
                    // 먹 농도 (눈에 보이는 밝기 기준): 0 = 진한 먹, 1 = 옅은 부분
                    half ink = pow(saturate(albLum), 0.4545);
                    // Fade가 커질수록 문턱이 내려가 → 옅은 곳부터 차례로 빠지고, 진한 먹선은 마지막에 빠짐
                    half th = lerp(1.0 + _InkSoftness, -_InkSoftness, _Fade);
                    half m = smoothstep(th - _InkSoftness, th + _InkSoftness, ink);
                    m = saturate(m + _Fade * _GlobalWash);                      // 전체도 살짝 함께 바래서 경계가 생기지 않게

                    // 바랜 색: 색을 조금만 남기고, 종이색 쪽으로 대비를 눌러 줌 (그냥 하얗게 덮지 않음)
                    half3 washed = lerp(albLum.xxx, albedo.rgb, _WashSaturation);
                    washed = lerp(washed, _FadeColor.rgb, _WashStrength);
                    albedo.rgb = lerp(albedo.rgb, washed, m);
                }

                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                float3 bitangent = cross(i.normalWS, i.tangentWS.xyz) * i.tangentWS.w;
                half3 nWS = normalize(TransformTangentToWorld(nTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half atten = light.distanceAttenuation * light.shadowAttenuation;
                half3 diffuse = light.color * saturate(dot(nWS, light.direction)) * atten;
                half3 ambient = SampleSH(nWS);

                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 h = normalize(light.direction + viewDir);
                half spec = pow(saturate(dot(nWS, h)), exp2(10.0 * _Smoothness + 1.0)) * _Smoothness * atten;

                // 바뀐 옷감 색으로 조명 계산 → 그림자·굴곡 유지
                half3 col = albedo.rgb * (diffuse + ambient) + light.color * spec * 0.5;
                col *= _Brightness;

                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // 그림자 / 깊이는 URP 기본 Lit 셰이더의 패스를 그대로 사용
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }

    FallBack "Universal Render Pipeline/Lit"
}
