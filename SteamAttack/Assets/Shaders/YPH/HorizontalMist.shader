// 수평 안개 아래에서 하늘을 볼 때 안개판이 천장처럼 덮이지 않도록 한다.
Shader "YPH/HorizontalMist"
{
    Properties
    {
        _MainTex ("안개 모양", 2D) = "white" {}
        _BaseColor ("안개 색과 투명도", Color) = (0.88,0.88,0.86,0.24)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float worldY : TEXCOORD1; float fog : TEXCOORD2; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.worldY = TransformObjectToWorld(input.positionOS).y;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // 원본 메시가 앞뒤 삼각형을 모두 포함하므로 카메라 높이로도 아래쪽 표시를 막는다.
                clip(_WorldSpaceCameraPos.y - input.worldY);
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _BaseColor;
                color.rgb = MixFog(color.rgb, input.fog);
                return color;
            }
            ENDHLSL
        }
    }
}
