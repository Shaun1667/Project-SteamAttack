// 화면 전체 후처리: 거리 안개 (NGH)
// - PC_Renderer 의 Full Screen Pass Renderer Feature(NGH_DistanceFog)가 이 재질로 그린다
// - 카메라에서 멀수록 안개색이 섞여 근경과 원경이 나뉘어 보이게 한다 (재질 셰이더가 유니티 안개를 지원하지 않아도 모두 적용됨)
// - 땅 가까이는 짙고 높이 올라갈수록 옅어진다. 하늘(스카이박스)은 건드리지 않는다
// - 궁궐 구역(_RegionMin~_RegionMax) 밖은 안개를 넣지 않는다 — 둘레 산은 원래대로 보이게
// - 반투명(눈·이펙트)보다 먼저 그려서 이펙트는 안개에 묻히지 않는다
Shader "NGH/DistanceFog"
{
    Properties
    {
        _FogColor ("안개 색", Color) = (0.6, 0.64, 0.7, 1)
        _FogStart ("안개 시작 거리 (m)", Float) = 20
        _FogEnd ("안개가 가장 짙어지는 거리 (m)", Float) = 180
        _FogMax ("최대 짙기", Range(0, 1)) = 0.5
        _FogCurve ("짙어지는 곡선 (1 = 고르게, 클수록 먼 곳에 몰림)", Range(0.3, 3)) = 1.3
        _HeightBase ("높이 안개 기준 높이 (m)", Float) = 2
        _HeightFalloff ("높이에 따라 옅어지는 정도", Range(0, 0.2)) = 0.025
        _HeightMin ("높은 곳에 남는 최소 비율", Range(0, 1)) = 0.35
        _RegionMin ("안개 구역 최소 (x, z) — 이 밖(둘레 산)은 안개 없음", Vector) = (-130, -165, 0, 0)
        _RegionMax ("안개 구역 최대 (x, z)", Vector) = (130, 240, 0, 0)
        _RegionFade ("구역 경계에서 옅어지는 폭 (m)", Float) = 15
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "DistanceFog"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            float4 _FogColor;
            float _FogStart, _FogEnd, _FogMax, _FogCurve;
            float _HeightBase, _HeightFalloff, _HeightMin;
            float4 _RegionMin, _RegionMax;
            float _RegionFade;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float raw = SampleSceneDepth(uv);
                #if UNITY_REVERSED_Z
                    if (raw <= 0.00001) return col;     // 하늘
                #else
                    if (raw >= 0.99999) return col;
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif

                float3 worldPos = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                float dist = distance(worldPos, _WorldSpaceCameraPos);

                float t = saturate((dist - _FogStart) / max(_FogEnd - _FogStart, 0.01));
                t = pow(t, _FogCurve);
                float h = exp(-max(worldPos.y - _HeightBase, 0.0) * _HeightFalloff);
                h = lerp(_HeightMin, 1.0, h);

                // 궁궐 구역 안의 건물·땅에만 안개 (둘레 산은 그대로 보이게)
                float2 inside = min(worldPos.xz - _RegionMin.xy, _RegionMax.xy - worldPos.xz);
                float region = saturate(min(inside.x, inside.y) / max(_RegionFade, 0.01));

                float fog = t * h * region * _FogMax;
                col.rgb = lerp(col.rgb, _FogColor.rgb, fog);
                return col;
            }
            ENDHLSL
        }
    }
}
