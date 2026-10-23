// 먹 방울·튐 (NGH) — 파티클용. 텍스처 알파는 '가장자리에서 얼마나 안쪽인가'(0 가장자리 → 1 가운데)라서,
// 수명이 줄면 투명해지는 대신 가장자리부터 먹이 말라 줄어든다. 정점 색: rgb = 색 곱, a = 남은 수명.
Shader "NGH/FX/InkSplat"
{
    Properties
    {
        _MainTex ("Splat (a = 안쪽 정도)", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _InkColor ("Ink", Color) = (0.035, 0.035, 0.045, 1)
        _RimColor ("Rim (가장자리 고인 먹)", Color) = (0.0, 0.0, 0.0, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Erode ("Erode (수명에 따른 마름)", Range(0, 1.2)) = 0.85
        _Edge ("Edge Sharpness", Range(0.005, 0.3)) = 0.04
        _NoiseAmount ("Noise Amount", Range(0, 0.6)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _NoiseTex_ST;
                half4 _InkColor, _RimColor;
                float _Opacity, _Erode, _Edge, _NoiseAmount;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float3 posWS : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                o.posWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.uv = TRANSFORM_TEX(i.uv, _MainTex);
                o.color = i.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float inner = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                float n = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, i.uv * 0.7 + i.posWS.xz * 0.37).r;
                float age = 1.0 - saturate(i.color.a);
                float thr = 0.02 + age * _Erode + (n - 0.5) * _NoiseAmount * (0.3 + age);
                float a = smoothstep(thr, thr + _Edge, inner);
                // 가장자리 쪽이 더 진하게 (먹이 마르며 테두리에 고임)
                float rim = 1.0 - smoothstep(thr + _Edge, thr + _Edge + 0.25, inner);
                half3 col = lerp(_InkColor.rgb, _RimColor.rgb, rim * 0.6) * i.color.rgb;
                return half4(col, saturate(a * _Opacity));
            }
            ENDHLSL
        }
    }
}
