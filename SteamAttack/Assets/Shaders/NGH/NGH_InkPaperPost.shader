// 화면 전체 후처리: 먹 외곽선 + 한지 질감 (NGH)
// - PC_Renderer 의 Full Screen Pass Renderer Feature(NGH_InkPaperPost)가 이 재질로 그린다
// - 외곽선: 깊이·노멀 차이로 물체 테두리를 찾아 먹색으로 칠한다. 멀수록 옅어지고, 붓처럼 굵기가 조금씩 흔들린다
// - 한지: 화면에 종이 섬유 무늬를 살짝 곱한다 (텍스처 없이 노이즈로 만든다)
// - 반투명(눈·이펙트)보다 먼저 그려서 이펙트 위에는 선이 생기지 않는다
Shader "NGH/InkPaperPost"
{
    Properties
    {
        [Header(Ink Outline)]
        _InkColor ("먹색", Color) = (0.06, 0.055, 0.07, 1)
        _OutlineStrength ("외곽선 진하기", Range(0, 1)) = 0.65
        _OutlineWidth ("외곽선 굵기 (1080p 기준 픽셀)", Range(0.5, 4)) = 1.4
        _DepthSensitivity ("깊이 민감도", Float) = 6
        _NormalSensitivity ("노멀 민감도", Float) = 1.6
        _NormalThreshold ("노멀 문턱", Range(0, 1)) = 0.25
        _FadeStart ("옅어지기 시작 거리 (m)", Float) = 18
        _FadeEnd ("사라지는 거리 (m)", Float) = 60
        _BrushJitter ("붓 굵기 흔들림", Range(0, 1)) = 0.45
        _BrushScale ("붓 흔들림 크기", Float) = 9

        [Header(Hanji Paper)]
        _PaperStrength ("한지 무늬 세기", Range(0, 0.5)) = 0.09
        _PaperScale ("한지 무늬 크기", Float) = 3
        _PaperTint ("한지 색 (밝은 곳에 섞임)", Color) = (1, 0.965, 0.9, 1)
        _PaperTintStrength ("한지 색 세기", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "InkPaper"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _InkColor;
            float _OutlineStrength, _OutlineWidth, _DepthSensitivity, _NormalSensitivity, _NormalThreshold;
            float _FadeStart, _FadeEnd, _BrushJitter, _BrushScale;
            float _PaperStrength, _PaperScale, _PaperTintStrength;
            float4 _PaperTint;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i), b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * ValueNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float2 res = _ScreenParams.xy;
                float aspect = res.x / res.y;
                float2 screenP = float2(uv.x * aspect, uv.y);

                // ---------- 먹 외곽선 ----------
                float d0 = EyeDepth(uv);
                float brush = lerp(1.0 - _BrushJitter, 1.0 + _BrushJitter * 0.5, ValueNoise(screenP * _BrushScale + d0 * 0.15));
                float2 px = (_OutlineWidth * brush * (res.y / 1080.0)) / res;

                float2 o1 = float2(px.x, px.y), o2 = float2(-px.x, px.y);
                float dA = EyeDepth(uv + o1), dB = EyeDepth(uv - o1);
                float dC = EyeDepth(uv + o2), dD = EyeDepth(uv - o2);
                float depthDiff = abs(dA - dB) + abs(dC - dD);
                float depthEdge = saturate((depthDiff / max(d0, 0.001)) * _DepthSensitivity - 0.15);

                float3 nA = SampleSceneNormals(uv + o1), nB = SampleSceneNormals(uv - o1);
                float3 nC = SampleSceneNormals(uv + o2), nD = SampleSceneNormals(uv - o2);
                float normalDiff = length(nA - nB) + length(nC - nD);
                float normalEdge = saturate((normalDiff - _NormalThreshold) * _NormalSensitivity);

                float edge = saturate(max(depthEdge, normalEdge));
                // 하늘(먼 평면)과 먼 곳은 선 없음
                float fade = 1.0 - saturate((d0 - _FadeStart) / max(_FadeEnd - _FadeStart, 0.01));
                #if UNITY_REVERSED_Z
                    float raw = SampleSceneDepth(uv);
                    fade *= raw > 0.00001 ? 1.0 : 0.0;
                #else
                    float raw = SampleSceneDepth(uv);
                    fade *= raw < 0.99999 ? 1.0 : 0.0;
                #endif
                float ink = edge * fade * _OutlineStrength;
                // 먹이 고르게 묻지 않도록 끊김 조금
                ink *= lerp(0.75, 1.0, ValueNoise(screenP * _BrushScale * 3.1 + 5.3));
                col.rgb = lerp(col.rgb, _InkColor.rgb, ink);

                // ---------- 한지 질감 ----------
                float2 pp = screenP * _PaperScale * 60.0;
                // (얼룩처럼 보여서 종이 얼룩 노이즈는 뺌 — 섬유 무늬만 남김)
                float fiber = ValueNoise(float2(pp.x * 0.15, pp.y * 2.4));    // 가로로 길게 뻗은 섬유
                fiber = smoothstep(0.62, 0.95, fiber);
                float paper = -fiber * 0.35;
                col.rgb *= 1.0 + paper * _PaperStrength;
                float lum = dot(col.rgb, float3(0.299, 0.587, 0.114));
                col.rgb = lerp(col.rgb, col.rgb * _PaperTint.rgb, _PaperTintStrength * saturate(lum * 1.5));

                return col;
            }
            ENDHLSL
        }
    }
}
