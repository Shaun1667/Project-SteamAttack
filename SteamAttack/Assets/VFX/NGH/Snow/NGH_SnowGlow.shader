// 허공에 멈춘 눈송이 + 은은한 빛무리.
// 파티클 사각형 가운데 1/_HaloScale 크기에 눈송이(텍스처)를 그리고, 바깥은 빛을 더해(가산) 은은하게 빛나게 한다.
// 카메라 후처리(블룸)가 꺼져 있어도 빛나 보이도록 셰이더 안에서 빛무리를 그림.
// 블렌드: 미리 곱한 알파(One, OneMinusSrcAlpha) → 눈송이는 반투명하게 덮고, 빛무리는 배경에 더해짐
Shader "NGH/SnowGlow"
{
    Properties
    {
        _MainTex ("눈송이 텍스처", 2D) = "white" {}
        _BaseColor ("눈송이 색", Color) = (1, 1, 1, 1)
        _UseVertexColor ("파티클 색 사용", Float) = 1
        _HaloScale ("사각형 / 눈송이 크기 비 (빛무리 공간)", Range(1, 4)) = 1.8
        _GlowColor ("빛 색", Color) = (0.72, 0.86, 1, 1)
        _GlowIntensity ("빛 세기", Range(0, 2)) = 0.55
        _GlowFalloff ("빛 퍼짐 (클수록 눈송이 가까이만)", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SnowGlow"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _GlowColor;
                float4 _MainTex_ST;
                half _UseVertexColor, _HaloScale, _GlowIntensity, _GlowFalloff;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float fogFactor : TEXCOORD1; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                o.color = v.color;
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                half4 vc = lerp(half4(1, 1, 1, 1), i.color, _UseVertexColor);
                float2 c = i.uv - 0.5;

                // 눈송이: 가운데 1/_HaloScale 크기
                float2 uvc = c * _HaloScale + 0.5;
                float inside = step(0.0, uvc.x) * step(uvc.x, 1.0) * step(0.0, uvc.y) * step(uvc.y, 1.0);
                half4 core = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, TRANSFORM_TEX(uvc, _MainTex)) * _BaseColor * vc * inside;

                // 빛무리: 사각형 가장자리까지 부드럽게 옅어짐
                float r = saturate(length(c) * 2.0);
                half halo = pow(1.0 - r, _GlowFalloff) * _GlowIntensity * vc.a;

                half3 rgb = core.rgb * core.a + _GlowColor.rgb * halo;
                rgb = MixFog(rgb, i.fogFactor);
                return half4(rgb, core.a);
            }
            ENDHLSL
        }
    }
}
