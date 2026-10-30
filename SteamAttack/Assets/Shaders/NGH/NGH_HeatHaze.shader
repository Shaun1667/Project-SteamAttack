// 열 아지랑이 (NGH) — 파티클용. 뒤에 보이는 화면을 일렁이게 비틀어 그린다 (색은 거의 없음)
// - 카메라 불투명 텍스처(_CameraOpaqueTexture)를 읽으므로 URP 에셋의 Opaque Texture 가 켜져 있어야 한다
// - 파티클 색의 알파가 일렁임 세기, 가장자리는 둥글게 사라진다
Shader "NGH/HeatHaze"
{
    Properties
    {
        _Strength ("일렁임 세기", Range(0, 0.1)) = 0.025
        _NoiseScale ("일렁임 무늬 크기", Float) = 3
        _Speed ("오르는 속도", Float) = 1.6
        _Tint ("살짝 섞을 색 (알파 = 섞는 양)", Color) = (1, 0.95, 0.85, 0.04)
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "HeatHaze"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            float _Strength, _NoiseScale, _Speed;
            float4 _Tint;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; float4 screenPos : TEXCOORD1; float3 positionWS : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), u.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), u.x), u.y);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv - 0.5;
                float mask = saturate(1.0 - length(c) * 2.0);
                mask = mask * mask * i.color.a;

                float t = _Time.y * _Speed;
                float2 q = i.uv * _NoiseScale + i.positionWS.xz * 0.3;
                float2 off = float2(Noise(q + float2(0, -t)), Noise(q + float2(5.2, -t * 1.3))) - 0.5;

                float2 suv = i.screenPos.xy / i.screenPos.w;
                float3 bg = SampleSceneColor(suv + off * _Strength * mask);
                bg = lerp(bg, bg * _Tint.rgb, _Tint.a * mask);
                return half4(bg, saturate(mask * 4.0));
            }
            ENDHLSL
        }
    }
}
