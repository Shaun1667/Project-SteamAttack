// 검 이펙트 시안 C2·C3 — 김·먹방울·주홍방울·불티 입자 (NMJ.NmjSwordSlash 전용)
// 텍스처 × 입자 색(정점 색, 감마 → 선형) × _Tint. 조명 없음. 포토샵 레이어 혼합처럼 _Mode 로 고른다.
//   0 보통(반투명)  : 김, 주홍 방울, 불티·불씨  Blend SrcAlpha OneMinusSrcAlpha (_Intensity로 밝기 1 넘김 가능)
//   1 곱하기        : 먹방울 (눈 위에 스며듦)  Blend DstColor Zero
//   2 더하기 + HDR  : 불티 (빛 번짐으로 퍼짐)  Blend One One, _Intensity 로 밝기 1 넘김
Shader "NMJ/FX/SwordSlashParticle"
{
    Properties
    {
        _MainTex ("Texture (RGBA)", 2D) = "white" {}
        _Tint ("Tint (multiply)", Color) = (1,1,1,1)
        _Mode ("Mode 0 alpha 1 multiply 2 add", Float) = 0
        _Intensity ("Intensity (HDR)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SwordSlashParticle"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                float _Mode;
                float _Intensity;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float near : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                // 카메라 가까이(0.8m 안)는 투명, 2.3m부터 다 보임 — 칼이 카메라 옆을 지나도 화면을 덮지 않게
                float viewZ = -TransformWorldToView(TransformObjectToWorld(i.positionOS.xyz)).z;
                o.near = saturate((viewZ - 0.8) / 1.5);
                #if !defined(UNITY_COLORSPACE_GAMMA)
                o.color.rgb = SRGBToLinear(o.color.rgb);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half4 c = t * i.color;
                c.a *= i.near;
                c.rgb *= _Tint.rgb;
                if (_Mode > 1.5) return half4(c.rgb * c.a * _Intensity, 0);          // 더하기
                if (_Mode > 0.5) return half4(lerp(half3(1, 1, 1), c.rgb, c.a), 1); // 곱하기
                return half4(c.rgb * _Intensity, c.a);                                  // 보통 (불티는 _Intensity로 막 튄 순간만 빛 번짐)
            }
            ENDHLSL
        }
    }
}
