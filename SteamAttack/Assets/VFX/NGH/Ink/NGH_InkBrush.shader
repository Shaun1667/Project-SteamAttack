// 먹 붓 베기 (NGH) — 칼 궤적 메시(SlashTrail)와 찌르기 메시에 쓰는 붓 획.
// UV: x = 획 길이 (0 꼬리 → 1 붓 머리 = 지금 칼 위치), y = 획 폭 (0 = 먹이 진한 쪽(칼끝 가장자리 / 찌르기 중심선) → 1 = 반대쪽)
// 정점 색: rgb = 색 곱, a = 남은 수명 (1 → 0). 수명이 줄면 투명해지는 대신 붓 결을 따라 말라 갈라지며 사라진다.
Shader "NGH/FX/InkBrush"
{
    Properties
    {
        _BristleTex ("Bristle (붓 결, 가로 줄)", 2D) = "gray" {}
        _NoiseTex ("Noise (번짐)", 2D) = "gray" {}
        _InkColor ("Ink (먹)", Color) = (0.035, 0.035, 0.045, 1)
        _WetColor ("Wet (묽은 먹, 획 가운데)", Color) = (0.20, 0.20, 0.22, 1)
        _AccentColor ("Accent (주홍 칼금, a = 세기)", Color) = (0.85, 0.16, 0.08, 0)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Width ("Width (획 폭, UV y 기준)", Range(0.05, 1.5)) = 0.75
        _TailWidth ("Tail Width (꼬리 쪽 폭 비율)", Range(0, 1)) = 0.08
        _TaperLen ("Taper Length (꼬리에서 굵어지는 길이)", Range(0.01, 1)) = 0.55
        _HeadRound ("Head Round (붓 머리 둥근 길이)", Range(0, 0.3)) = 0.05
        _Inset ("Inset (진한 쪽 가장자리 들어옴)", Range(0, 0.3)) = 0.02
        _Soft ("Edge Softness", Range(0.001, 0.3)) = 0.05
        _EdgeNoise ("Edge Noise (가장자리 울퉁불퉁)", Range(0, 0.5)) = 0.18
        _StreakRepeat ("Streak Repeat (결 반복, 길이 방향)", Float) = 1.5
        _StreakDensity ("Streak Density (결 개수, 폭 방향)", Float) = 1
        _Dry ("Dry (기본 마름)", Range(-0.5, 1)) = 0.05
        _DryTail ("Dry Tail (꼬리 쪽 마름)", Range(0, 1.5)) = 0.75
        _DryAge ("Dry Age (시간에 따른 마름)", Range(0, 2)) = 1.1
        _Wet ("Wet (가운데 묽기)", Range(0, 1)) = 0.35
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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

            TEXTURE2D(_BristleTex); SAMPLER(sampler_BristleTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _BristleTex_ST, _NoiseTex_ST;
                half4 _InkColor, _WetColor, _AccentColor;
                float _Opacity, _Width, _TailWidth, _TaperLen, _HeadRound, _Inset, _Soft, _EdgeNoise;
                float _StreakRepeat, _StreakDensity, _Dry, _DryTail, _DryAge, _Wet, _Seed;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            V vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float u = saturate(i.uv.x), v = saturate(i.uv.y);
                float life = saturate(i.color.a);
                float age = 1.0 - life;

                // 번짐 노이즈 (길이 방향으로 길게 늘인 얼룩)
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 1.7 + _Seed, v * 0.35 + _Seed * 0.37)).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 5.3 - _Seed * 0.61, 0.5 + _Seed)).r;

                // 획 폭: 꼬리에서 가늘게 시작해 굵어지고, 붓 머리는 둥글게 닫힘
                float thick = _Width * lerp(_TailWidth, 1.0, smoothstep(0.0, _TaperLen, u));
                float head = saturate((1.0 - u) / max(_HeadRound, 1e-4));
                thick *= sqrt(saturate(1.0 - (1.0 - head) * (1.0 - head)));
                thick *= 1.0 + (n2 - 0.5) * 2.0 * _EdgeNoise;          // 붓 자국 가장자리 흔들림
                float inset = _Inset * (0.5 + n1);

                float d = (v - inset) / max(thick, 1e-4);              // 0 = 진한 가장자리 → 1 = 획 반대 가장자리
                float body = smoothstep(1.0, 1.0 - _Soft / max(thick, 0.05), d) * smoothstep(0.0, _Soft * 0.5, v - inset);

                // 붓 결: 획 폭을 따라 줄이 나란히 (꼬리로 갈수록 결이 모여듦)
                float2 bu = float2(u * _StreakRepeat + _Seed * 0.13, saturate(d) * _StreakDensity + _Seed * 0.29);
                float b = SAMPLE_TEXTURE2D(_BristleTex, sampler_BristleTex, bu * _BristleTex_ST.xy + _BristleTex_ST.zw).r;

                // 마름: 꼬리·바깥 가장자리·시간이 지날수록 결 사이가 비며 갈라짐
                float dry = _Dry + (1.0 - u) * _DryTail + age * _DryAge + saturate(d) * 0.25 + (n1 - 0.5) * 0.3;
                float val = b * 0.75 + body * 0.55 - dry;
                float a = smoothstep(0.0, 0.07, val) * body;
                a *= saturate(life * 6.0);                              // 마지막 순간에만 살짝 흐려짐

                // 색: 가장자리에 먹이 고이고(진함) 가운데는 묽음, 결 사이로 옅은 먹
                float centre = 1.0 - abs(saturate(d) * 2.0 - 1.0);
                half3 col = lerp(_InkColor.rgb, _WetColor.rgb, saturate(centre * _Wet * (0.6 + n1 * 0.8) * (1.2 - b)));
                // 주홍 칼금: 진한 가장자리를 따라 가늘게, 붓 머리 쪽에서만
                float accent = smoothstep(0.10, 0.02, abs(d - 0.08)) * smoothstep(0.25, 0.75, u) * smoothstep(0.35, 0.6, b + 0.2) * _AccentColor.a;
                col = lerp(col, _AccentColor.rgb, saturate(accent));
                col *= i.color.rgb;

                return half4(col, saturate(a * _Opacity));
            }
            ENDHLSL
        }
    }
}
