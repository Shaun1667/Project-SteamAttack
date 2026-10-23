// 검 이펙트 시안 C2·C3 (3차, 연출 가이드 반영) — 기존 NGH 흰 초승달 위에 얹는 칼금 (NMJ.NmjSwordSlash 전용)
// 모양은 텍스처 대신 셰이더에서 직접 그린다.
//   · 나이(칼이 그 자리를 지난 뒤 흐른 시간)로 굵기·색을 정한다: 칼 바로 뒤가 가장 두툼하고 뜨겁고, 오래된 꼬리는 바늘처럼 가늘고 검붉게 식는다
//     → 빠르게 벤 구간은 길게, 느린 구간은 짧게 남아 속도가 읽힌다 (UV1.x = 칼이 지난 시각, _Now = 지금)
//   · 단면(칼끝 → 손 쪽): 먹 테(곱하기) | 뜨거운 심(HDR, 빛 번짐은 여기만) | 주홍 칼금 | (그 안은 기존 흰 이펙트)
//   · 사라질 때: 진행 방향으로 늘어진 결의 노이즈로 꼬리부터 먹혀 들어가고, 먹히는 경계가 불씨처럼 빛난다(타는 테)
//   · 땅·벽과 맞닿는 곳은 깊이로 흐리게(딱딱한 줄 방지), 카메라 가까이는 투명
//   · C3는 _Steam 만큼 칼끝 바깥으로 붉은 김이 핀다
// 메시 UV: u = 휘두르기 진행(0 시작 → 1 끝), v = 칼날 안쪽(0) → 칼끝(1)
Shader "NMJ/FX/SwordSlash"
{
    Properties
    {
        _MainTex ("(unused)", 2D) = "white" {}
        _Tint ("Steam Tint", Color) = (1,1,1,1)
        _Head ("Head", Range(0, 1.2)) = 1.06
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Now ("Now (s)", Float) = 0
        _Life ("Age Life (s)", Range(0.05, 1.5)) = 0.32
        _EdgeWidth ("Edge Width (v)", Range(0.01, 0.3)) = 0.085
        _InkColor ("Ink Rim", Color) = (0.02, 0.022, 0.03, 1)
        _RimInk ("Ink Rim Amount", Range(0, 1)) = 0.55
        _LineColor ("Edge (vermilion)", Color) = (0.95, 0.22, 0.10, 1)
        _HotColor ("Edge Hot (near blade)", Color) = (1.0, 0.55, 0.25, 1)
        _CoolColor ("Edge Cool (old tail)", Color) = (0.48, 0.08, 0.05, 1)
        _EdgeIntensity ("Edge Intensity", Range(0, 3)) = 0.9
        [HDR] _CoreColor ("Core (HDR)", Color) = (3.2, 2.4, 1.9, 1)
        _CoreBoost ("Core Flash", Range(0, 1)) = 0
        [HDR] _BurnColor ("Burn Edge (HDR)", Color) = (1.6, 0.7, 0.25, 1)
        _DepthFade ("Depth Fade (m)", Range(0.01, 1)) = 0.25
        _Steam ("Red Steam (C3)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Tint;
            float _Head;
            float _Dissolve;
            float _Opacity;
            float _Now;
            float _Life;
            float _EdgeWidth;
            half4 _InkColor;
            float _RimInk;
            half4 _LineColor;
            half4 _HotColor;
            half4 _CoolColor;
            float _EdgeIntensity;
            half4 _CoreColor;
            float _CoreBoost;
            half4 _BurnColor;
            float _DepthFade;
            float _Steam;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
        struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float age : TEXCOORD1; float near : TEXCOORD2; float eye : TEXCOORD3; float3 posWS : TEXCOORD4; };

        Varyings vert(Attributes i)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
            o.uv = i.uv;
            o.age = max(0.0, _Now - i.uv1.x);
            o.posWS = TransformObjectToWorld(i.positionOS.xyz);
            float viewZ = -TransformWorldToView(o.posWS).z;
            o.eye = viewZ;
            o.near = saturate((viewZ - 0.8) / 1.5);   // 카메라 가까이는 투명
            return o;
        }

        float Hash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p);
            float a = Hash(i), b = Hash(i + float2(1, 0)), c = Hash(i + float2(0, 1)), d = Hash(i + float2(1, 1));
            float2 s = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(a, b, s.x), lerp(c, d, s.x), s.y);
        }

        float Fbm(float2 p)
        {
            float sum = 0.0, amp = 0.5;
            [unroll] for (int k = 0; k < 4; k++) { sum += amp * ValueNoise(p); p *= 2.03; amp *= 0.5; }
            return sum / 0.9375;
        }

        // 비대칭 테이퍼: x = 1 막 지나간 곳(머리), 0 오래된 꼬리. p 1.6 꼬리 바늘, q 0.25 머리 뭉툭 → 최고점 x ≈ 0.86
        float TaperX(float x)
        {
            x = saturate(x);
            return pow(x, 1.6) * pow(1.0 - x, 0.25) / 0.4806;
        }

        // 땅·벽과 맞닿는 곳은 흐리게 + 궤적 면을 옆에서(납작하게) 볼 때는 흐리게 (선 하나로 뭉개져 검은 줄처럼 보이지 않게)
        float Fade(Varyings i)
        {
            float2 suv = GetNormalizedScreenSpaceUV(i.positionCS.xy);
            float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
            float depth = saturate((scene - i.eye) / _DepthFade);
            float3 c = cross(ddy(i.posWS), ddx(i.posWS));
            float3 n = c * rsqrt(max(dot(c, c), 1e-20));
            float3 v = normalize(GetCameraPositionWS() - i.posWS);
            float facing = smoothstep(0.12, 0.4, abs(dot(n, v)));
            return depth * facing * i.near;
        }

        // 층 계산. rim = 먹 테, core = 뜨거운 심, edge = 주홍 칼금, burn = 타는 테, steam = 붉은 김, x = 젊음(1 머리 → 0 꼬리)
        void Layers(Varyings i, out float rim, out float core, out float edge, out float burn, out float steam, out float x)
        {
            float u = i.uv.x, v = i.uv.y;
            float head = min(_Head, 1.0);
            float seen = step(u, head);                     // 칼이 지나간 곳만
            float ahead = saturate((_Head - u) / 0.03);     // 머리 끝은 살짝 부드럽게

            x = saturate(1.0 - i.age / _Life);
            float W = TaperX(x) * pow(saturate(sin(PI * saturate(u))), 0.4);   // 나이 테이퍼 × 아크 양 끝

            // 침식: 칼 진행 방향으로 늘어진 결(9 × 18), 진행 반대쪽으로 흐름, 한 번 비틀기(도메인 워프)
            float warp = Fbm(i.uv * float2(3.0, 6.0)) - 0.5;
            float n = Fbm(float2(u * 9.0 + _Now * 0.8, v * 18.0) + warp * 0.8);
            float e = n * 0.6 + x * 0.8 + v * 0.3;          // 꼬리·안쪽부터 먹힘 → 칼금이 마지막까지
            float T = _Dissolve * 1.7;
            float keep = smoothstep(T, T + 0.08, e);
            burn = keep * (1.0 - smoothstep(T + 0.03, T + 0.10, e)) * saturate(_Dissolve * 25.0);

            float base = seen * ahead * keep * _Opacity;
            float common = base * saturate(W * 4.0);       // 바늘처럼 가늘어진 꼬리는 투명하게 (계단 방지)

            // 단면: 칼끝(v=1)에서 거리를 굵기 W로 나눈 값. 화면 1~2픽셀은 항상 부드럽게
            float dn = (1.0 - v) / max(W, 0.05);
            float aa = clamp(fwidth(dn) * 1.2, 1e-4, 0.006);   // 비스듬히 볼 때 끝없이 넓어지지 않게 상한
            rim  = (1.0 - smoothstep(0.012 - aa, 0.012 + aa, dn)) * common;
            core = smoothstep(0.008 - aa, 0.014 + aa, dn) * (1.0 - smoothstep(0.024 - aa, 0.034 + aa, dn)) * common;
            edge = smoothstep(0.004 - aa, 0.010 + aa, dn) * (1.0 - smoothstep(_EdgeWidth * 0.6, _EdgeWidth + aa, dn)) * common;
            burn *= (1.0 - smoothstep(_EdgeWidth * 0.8, _EdgeWidth * 1.6 + aa, dn)) * seen * ahead * _Opacity;

            // C3 붉은 김: 칼끝 바깥 가장자리에서, 오래된 쪽일수록 피어오름
            float d = 1.0 - v;
            float wisp = Fbm(float2(u * 9.0 - _Now * 0.6, v * 5.0));
            steam = _Steam * smoothstep(0.5, 0.0, d) * smoothstep(0.35, 0.75, wisp) * saturate((1.0 - x) * 2.0) * base * 0.45;
        }
        ENDHLSL

        // 1장 — 먹 테: 곱하기 (흰 눈 위에서도 형태가 읽히게)
        Pass
        {
            Name "InkMultiply"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor Zero
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragInk
            half4 fragInk(Varyings i) : SV_Target
            {
                float rim, core, edge, burn, steam, x;
                Layers(i, rim, core, edge, burn, steam, x);
                half a = saturate(rim * _RimInk * Fade(i));
                return half4(lerp(half3(1, 1, 1), _InkColor.rgb, a), 1);
            }
            ENDHLSL
        }

        // 2장 — 빛: 김 → 칼금 → 타는 테 → 심 순서로 덮어 그리기 (미리 곱한 알파)
        Pass
        {
            Name "Light"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragLight
            half4 fragLight(Varyings i) : SV_Target
            {
                float rim, core, edge, burn, steam, x;
                Layers(i, rim, core, edge, burn, steam, x);
                float f = Fade(i);

                // 칼금 색은 온도로: 칼 바로 뒤 뜨거운 주황 → 주홍 → 오래된 꼬리는 검붉게 식음
                half3 edgeCol = lerp(_CoolColor.rgb, _LineColor.rgb, smoothstep(0.0, 0.45, x));
                edgeCol = lerp(edgeCol, _HotColor.rgb, smoothstep(0.75, 1.0, x) * 0.6) * _EdgeIntensity;

                half s = saturate(steam) * f;
                half e = saturate(edge) * f * lerp(0.45, 1.0, smoothstep(0.0, 0.45, x));   // 식을수록 옅게 (검은 줄처럼 남지 않게)
                half b = saturate(burn) * f;
                half c = saturate(core * smoothstep(0.35, 0.85, x)) * f;   // 심은 막 벤 곳에만

                half3 rgb = _Tint.rgb * s;  half a = s;
                rgb = edgeCol * e + rgb * (1.0 - e);                        a = e + a * (1.0 - e);
                rgb = _BurnColor.rgb * b + rgb * (1.0 - b);                 a = b + a * (1.0 - b);
                rgb = _CoreColor.rgb * (1.0 + _CoreBoost * 1.5) * c + rgb * (1.0 - c);  a = c + a * (1.0 - c);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
