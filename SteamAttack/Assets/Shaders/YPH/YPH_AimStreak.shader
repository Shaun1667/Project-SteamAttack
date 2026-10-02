Shader "YPH/AimStreak"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,1)) = 0
        _Focus ("Focus", Vector) = (0.5,0.5,0,0)
        _LineCount ("Line Count", Float) = 90
        _LineWidth ("Line Width", Range(0.05,0.9)) = 0.35
        _ClearRadius ("Clear Radius", Float) = 0.28
        _FadeRadius ("Fade Radius", Float) = 0.65
        _FlickerSpeed ("Flicker Speed", Float) = 6
        _Randomness ("Randomness", Range(0,1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+100" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Focus;
                float _Intensity, _LineCount, _LineWidth, _ClearRadius, _FadeRadius, _FlickerSpeed, _Randomness;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            // Quad의 실제 크기와 FOV 대신 클립 공간을 써서 항상 화면 전체를 덮습니다.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy * 2, 0, 1);
                output.uv = input.positionOS.xy + 0.5;
                // 렌더 텍스처를 거치는 플랫폼에서도 WorldToViewportPoint와 위아래를 맞춥니다.
                output.positionCS.y *= _ProjectionParams.x;
                return output;
            }

            // 각 줄에 고정된 난수를 주어, 매 프레임 선의 위치가 통째로 바뀌지 않게 합니다.
            float Hash(float n) { return frac(sin(n * 127.1) * 43758.5453); }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 delta = input.uv - _Focus.xy;
                delta.x *= _ScreenParams.x / _ScreenParams.y;
                float radius = length(delta);
                float angle = (atan2(delta.y, delta.x) / TWO_PI + 0.5) * _LineCount;
                float cell = floor(angle);
                float width = _LineWidth * lerp(1, lerp(0.45, 1.25, Hash(cell)), _Randomness);
                float distanceToLine = abs(frac(angle) - 0.5);
                float edge = max(fwidth(angle), 0.005);
                float lineMask = 1 - smoothstep(width * 0.5 - edge, width * 0.5 + edge, distanceToLine);
                float flicker = lerp(1, 0.65 + 0.35 * sin(_Time.y * _FlickerSpeed + Hash(cell + 41) * TWO_PI), _Randomness);
                float alpha = lineMask * smoothstep(_ClearRadius, _FadeRadius, radius) * flicker * _Intensity * _Color.a;
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
