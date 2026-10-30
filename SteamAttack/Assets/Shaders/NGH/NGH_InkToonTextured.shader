// 수묵 동양화풍 기본 셰이더 + 텍스처 (NGH). SteamAttack/InkToon(NMJ)과 같은 빛·먹 처리에 _BaseMap 을 곱한다.
// 궁궐 바닥 타일처럼 Varco 등으로 만든 텍스처를 붙일 때 쓰고, 텍스처가 없으면(흰색) InkToon 과 똑같이 보인다.
// - 빛을 두 단(밝은 면/먹 그림자 면)으로만 나눠 붓으로 칠한 느낌을 낸다.
// - 가장자리(림)를 먹색으로 번지게 하고, 한지 결 노이즈를 얹는다.
Shader "NGH/InkToonTextured"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (0.8, 0.8, 0.8, 1)
        _ShadowColor ("Shadow Tint", Color) = (0.6, 0.58, 0.56, 1)
        [Header(Edge Blend. vertex alpha 0 is edge)]
        _EdgeBaseColor ("Edge Base Color", Color) = (0.87, 0.87, 0.85, 1)
        _EdgeShadowColor ("Edge Shadow Tint", Color) = (0.6, 0.58, 0.57, 1)
        _EdgeGrain ("Edge Paper Grain", Range(0, 1)) = 0.08
        _EdgeRimInk ("Edge Rim Ink", Range(0, 1)) = 0.12
        _InkColor ("Ink Color", Color) = (0.07, 0.07, 0.08, 1)
        _RimInk ("Rim Ink", Range(0, 1)) = 0.55
        _Grain ("Paper Grain", Range(0, 1)) = 0.12
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ForwardInk"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _EdgeBaseColor;
                half4 _EdgeShadowColor;
                half _EdgeGrain;
                half _EdgeRimInk;
                half4 _InkColor;
                half _RimInk;
                half _Grain;
                half4 _EmissionColor;
            CBUFFER_END

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;     // a = 이 바닥이 얼마나 보이는지 (0 이면 가장자리 색으로 녹아듦). 정점색이 없으면 1
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                float2 uv : TEXCOORD3;
                half edge : TEXCOORD4;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.edge = v.color.a;
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float ndl = dot(n, mainLight.direction) * 0.5 + 0.5;
                float band = smoothstep(0.46, 0.60, ndl);
                band = min(band, smoothstep(0.35, 0.65, mainLight.shadowAttenuation));

                // 가장자리(정점 알파 0)에서는 바깥 지면 재질(_Edge*)과 같은 색·그늘·종이결이 되어 경계가 보이지 않음
                half w = saturate(i.edge);
                half3 baseCol = lerp(_EdgeBaseColor.rgb, _BaseColor.rgb * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb, w);
                half3 shadowTint = lerp(_EdgeShadowColor.rgb, _ShadowColor.rgb, w);
                half grainAmt = lerp(_EdgeGrain, _Grain, w);
                half3 col = lerp(baseCol * shadowTint, baseCol, band);
                col *= lerp(half3(1, 1, 1), mainLight.color, 0.35);

                float rim = pow(1.0 - saturate(dot(n, viewDir)), 3.0);
                col = lerp(col, _InkColor.rgb, saturate(rim * lerp(_EdgeRimInk, _RimInk, w) * 1.5));   // 비스듬히 볼 때 먹 번짐도 바깥 지면과 맞춤

                float grain = Hash31(floor(i.positionWS * 24.0));
                col *= 1.0 - grainAmt * (grain - 0.5);

                col += _EmissionColor.rgb;
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _EdgeBaseColor;
                half4 _EdgeShadowColor;
                half _EdgeGrain;
                half _EdgeRimInk;
                half4 _InkColor;
                half _RimInk;
                half _Grain;
                half4 _EmissionColor;
            CBUFFER_END

            float3 _LightDirection;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vertShadow(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, _LightDirection));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                return o;
            }

            half4 fragShadow(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vertDepth
            #pragma fragment fragDepth
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _EdgeBaseColor;
                half4 _EdgeShadowColor;
                half _EdgeGrain;
                half _EdgeRimInk;
                half4 _InkColor;
                half _RimInk;
                half _Grain;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vertDepth(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half4 fragDepth(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vertDN
            #pragma fragment fragDN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ShadowColor;
                half4 _EdgeBaseColor;
                half4 _EdgeShadowColor;
                half _EdgeGrain;
                half _EdgeRimInk;
                half4 _InkColor;
                half _RimInk;
                half _Grain;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            Varyings vertDN(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 fragDN(Varyings i) : SV_Target
            {
                return half4(normalize(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
}
