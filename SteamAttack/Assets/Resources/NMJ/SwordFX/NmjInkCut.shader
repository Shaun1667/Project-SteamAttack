// 검 이펙트 화려판 — 일섬(一閃) 순간 화면을 종이·먹 두 색으로 뒤집는다 (NMJ.NmjSwordFlair 전용)
// 카메라 바로 앞 판에 그린다. 화면(불투명 물체) 밝기로 나눠 밝은 곳은 먹, 어두운 곳은 종이 — 반전된 수묵화 한 장
// 맞은 자리(_Hit, 화면 0~1)를 지나는 사선에 주홍 칼금 한 줄
Shader "NMJ/FX/InkCut"
{
    Properties
    {
        _Amount ("Amount", Range(0, 1)) = 0
        _Paper ("Paper", Color) = (0.93, 0.90, 0.84, 1)
        _Ink ("Ink", Color) = (0.03, 0.03, 0.04, 1)
        _Cut ("Cut Line", Color) = (0.90, 0.18, 0.08, 1)
        _Threshold ("Threshold", Range(0, 1)) = 0.42
        _Hit ("Hit (viewport)", Vector) = (0.5, 0.5, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Overlay" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Amount;
                half4 _Paper, _Ink, _Cut;
                float _Threshold;
                float4 _Hit;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };

            V vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); return o; }

            half4 frag(V i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS.xy);
                half3 c = SampleSceneColor(uv);
                half lum = dot(c, half3(0.299, 0.587, 0.114));
                half ink = smoothstep(_Threshold - 0.04, _Threshold + 0.04, lum);   // 밝은 곳 → 먹
                half3 rgb = lerp(_Paper.rgb, _Ink.rgb, ink);

                // 맞은 자리를 지나는 사선 칼금 (화면 비율 보정)
                float2 p = (uv - _Hit.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1.0);
                float2 n = normalize(float2(0.35, 1.0));
                float d = abs(dot(p, n));
                half cutLine = smoothstep(0.006, 0.0, d);
                rgb = lerp(rgb, _Cut.rgb, cutLine);
                return half4(rgb, _Amount);
            }
            ENDHLSL
        }
    }
}
