Shader "YPH/SoftParticle"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        _Softness ("Softness", Float) = 2
        _NoiseScale ("Noise Scale", Float) = 3
        _NoiseStrength ("Noise Strength", Range(0,1)) = 0.5
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend [_SrcBlend] [_DstBlend]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Softness, _NoiseScale, _NoiseStrength, _SrcBlend, _DstBlend;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            // 네 모서리 난수를 부드럽게 잇습니다. 외부 텍스처 없이 연기의 얼룩을 만듭니다.
            float Noise(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash(cell), Hash(cell + float2(1,0)), f.x),
                    lerp(Hash(cell + float2(0,1)), Hash(cell + 1), f.x), f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float r = length(input.uv - 0.5) * 2;
                float shape = pow(saturate(1 - r), max(_Softness, 0.01));
                float alpha = shape * lerp(1, Noise(input.uv * _NoiseScale), _NoiseStrength);
                return half4(_BaseColor.rgb * input.color.rgb, alpha * _BaseColor.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
