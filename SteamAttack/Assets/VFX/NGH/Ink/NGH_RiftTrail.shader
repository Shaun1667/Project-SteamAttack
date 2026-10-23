// 강공격 칼 궤적 '찢김' 층 (NGH) — SlashTrail 궤적 메시에 한 층 더 그림.
//  - 어두운 띠 위로 칼 방향을 따라 흐르는 빛 줄무늬, 칼끝·안쪽 가장자리가 빛남
//  - 띠 안은 뒤 화면(_CameraOpaqueTexture)을 칼 방향으로 끌어 번지고 일렁이게 비틀어 공간이 찢긴 느낌
// UV: x = 궤적 길이 (0 꼬리 → 1 지금 칼 위치), y = 폭 (0 칼끝 → 1 안쪽). 정점 색: rgb = 빛 색 곱, a = 남은 수명
// 큐 Transparent-10: 먹 획·기존 빛 궤적이 이 층 위에 그려짐. URP Opaque Texture 가 켜져 있어야 화면 비틀기가 보임 (PC_RPAsset 켜짐)
Shader "NGH/FX/RiftTrail"
{
    Properties
    {
        _NoiseTex ("Noise", 2D) = "gray" {}
        _GlowColor ("Glow (HDR)", Color) = (2.4, 0.10, 0.04, 1)
        _DarkColor ("Dark Band", Color) = (0.06, 0.0, 0.005, 1)
        _SceneTint ("Scene Tint", Color) = (0.75, 0.30, 0.30, 1)
        _StreakRepeat ("Streak Repeat (길이 방향)", Float) = 2.5
        _StreakDensity ("Streak Density (폭 방향)", Float) = 1.6
        _Scroll ("Scroll Speed", Float) = 1.8
        _Distort ("Distort (화면 일렁임)", Range(0, 0.1)) = 0.025
        _Smear ("Smear (칼 방향 번짐, 궤적 UV 길이)", Range(0, 0.3)) = 0.08
        _Dark ("Dark Amount", Range(0, 1)) = 0.55
        _GlowIntensity ("Glow Intensity", Float) = 0.8
        _TailFade ("Tail Fade", Range(0.01, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _NoiseTex_ST;
                half4 _GlowColor, _DarkColor, _SceneTint;
                float _StreakRepeat, _StreakDensity, _Scroll, _Distort, _Smear, _Dark, _GlowIntensity, _TailFade;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; float4 screen : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float u = saturate(i.uv.x), v = saturate(i.uv.y);
                float life = saturate(i.color.a);
                float t = _Time.y * _Scroll;

                // 궤적 UV 변화 → 화면 UV 변화 (칼 방향으로 화면을 끌기 위해)
                float2 suv = i.screen.xy / i.screen.w;
                float2 duvx = ddx(i.uv), duvy = ddy(i.uv);
                float2 dsx = ddx(suv), dsy = ddy(suv);
                float det = duvx.x * duvy.y - duvx.y * duvy.x;
                float2x2 invUv = float2x2(duvy.y, -duvy.x, -duvx.y, duvx.x) / (abs(det) > 1e-12 ? det : 1e-12);
                float2x2 toScreen = mul(float2x2(dsx.x, dsy.x, dsx.y, dsy.y), invUv);
                float2 along = mul(toScreen, float2(1, 0));
                float al = length(along);
                along = al > 1e-6 ? along / al * min(al, 0.5) : 0;   // 너무 길게 늘어나지 않게

                // 줄무늬: 칼이 지나간 방향(꼬리 → 머리)으로 흐름
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * _StreakRepeat - t * 0.5, v * _StreakDensity)).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * _StreakRepeat * 2.3 - t * 0.9 + 0.31, v * _StreakDensity * 3.0 + 0.17)).r;
                float streak = saturate((n1 * 1.2 + n2 * 0.8) - 0.85) * 2.4;

                // 뒤 화면: 일렁이게 비틀고 칼 반대 방향으로 끌어 번짐
                float2 wob = (float2(n1, n2) - 0.5) * _Distort;
                half3 scene = 0;
                [unroll] for (int k = 0; k < 5; k++)
                    scene += SampleSceneColor(saturate(suv + wob - along * _Smear * (k / 4.0)));
                scene /= 5.0;

                // 모양: 꼬리는 사라지고, 칼끝·안쪽 가장자리는 부드럽게
                float env = smoothstep(0.0, _TailFade, u) * smoothstep(0.0, 0.08, v) * smoothstep(1.0, 0.85, v);

                half3 col = scene * _SceneTint.rgb;
                col = lerp(col, _DarkColor.rgb, _Dark * saturate(0.6 + streak * 0.4));
                float rimTip = exp(-pow((v - 0.06) / 0.05, 2.0));
                float rimIn = exp(-pow((v - 0.82) / 0.06, 2.0)) * 0.6;
                float glow = (rimTip * (0.7 + 0.6 * n2) + rimIn * n1 + pow(streak, 2.0)) * smoothstep(0.1, 0.6, u);
                col += _GlowColor.rgb * i.color.rgb * glow * _GlowIntensity * (0.35 + 0.65 * life);

                return half4(col, env * saturate(life * 2.5));
            }
            ENDHLSL
        }
    }
}
