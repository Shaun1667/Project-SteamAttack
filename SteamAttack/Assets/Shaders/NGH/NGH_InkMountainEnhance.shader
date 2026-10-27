// 수묵 산 보정: 원본 텍스처(URP Unlit과 같은 출력)를 그대로 쓰고 수묵 느낌만 얹는다.
// 모든 강도를 0으로 두면 URP/Unlit과 같은 모습. 속성 이름(_BaseMap, _BaseColor)이 같아서 기존 머티리얼에 셰이더만 바꿔 끼우면 됨
// - 먹색 톤: 색을 빼고 명암 대비를 올려 먹 계열 회색으로
// - 외곽 먹선: 시선과 거의 나란한 면(외곽)에 마른 붓처럼 끊기는 먹선
// - 안개·원경: 산 아래와 먼 산을 옅게
// - 붓결·한지 결: 은은한 세로 붓결과 종이 결
Shader "NGH/InkMountainEnhance"
{
    Properties
    {
        _BaseMap ("텍스처", 2D) = "white" {}
        _BaseColor ("색", Color) = (1, 1, 1, 1)

        [Header(Ink Tone)]
        _Desaturate ("색 빼기 (먹색으로)", Range(0, 1)) = 0.6
        _Contrast ("명암 대비", Range(0.5, 2)) = 1.2
        _InkTint ("먹 색조", Color) = (0.93, 0.93, 0.95, 1)

        [Header(Outline)]
        _RimInk ("외곽 먹선 강도", Range(0, 1)) = 0.55
        _RimWidth ("외곽 먹선 굵기", Range(0, 1)) = 0.3
        _RimDry ("마른 붓 갈라짐", Range(0, 1)) = 0.5

        [Header(Brush)]
        _StrokeInk ("세로 붓결 강도", Range(0, 1)) = 0.18
        _StrokeWidth ("붓결 폭 (m)", Float) = 6
        _StrokeLength ("붓결 길이 (m)", Float) = 45
        _PaperGrain ("한지 결", Range(0, 0.3)) = 0.05

        [Header(Mist)]
        _MistColor ("안개·원경색", Color) = (0.82, 0.83, 0.83, 1)
        _MistAmount ("산 아래 안개 강도", Range(0, 1)) = 0.6
        _MistBottom ("안개 시작 높이", Float) = -5
        _MistTop ("안개 끝 높이", Float) = 35
        _FarAmount ("원경 흐려짐 강도", Range(0, 1)) = 0.35
        _FarStart ("원경 흐려짐 시작 거리", Float) = 300
        _FarEnd ("원경 흐려짐 끝 거리", Float) = 1000
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor, _InkTint, _MistColor;
            half _Desaturate, _Contrast;
            half _RimInk, _RimWidth, _RimDry;
            half _StrokeInk, _StrokeWidth, _StrokeLength, _PaperGrain;
            half _MistAmount, _MistBottom, _MistTop, _FarAmount, _FarStart, _FarEnd;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "InkMountain"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float a = Hash21(i), b = Hash21(i + float2(1, 0)), c = Hash21(i + float2(0, 1)), d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 3; k++) { v += a * ValueNoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return v;
            }

            // 세로 붓결: 높이 방향으로 길게 늘인 노이즈
            float Strokes(float2 hv)
            {
                float2 p = float2(hv.x / _StrokeWidth, hv.y / _StrokeLength);
                p.x += (Fbm(hv * 0.015) - 0.5) * 4.0;
                float n = ValueNoise(p) * 0.7 + ValueNoise(p * float2(2.1, 1.3) + 5.3) * 0.3;
                return smoothstep(0.5, 0.8, n);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 pos = i.positionWS;
                float3 n = normalize(i.normalWS);
                float3 viewDir = normalize(GetWorldSpaceViewDir(pos));
                float dist = length(GetCameraPositionWS() - pos);

                // 원본: URP Unlit과 같은 값
                half3 col = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;

                // 보정은 눈에 보이는 밝기(감마)에서
                half3 g = LinearToSRGB(col);
                float lum = dot(g, float3(0.299, 0.587, 0.114));
                g = lerp(g, lum.xxx * _InkTint.rgb, _Desaturate);                       // 먹색으로
                g = saturate((g - 0.5) * _Contrast + 0.5);                                // 명암 대비

                float footprint = length(fwidth(pos));
                float3 w = pow(abs(n), 4.0);
                w /= max(w.x + w.y + w.z, 1e-4);

                // 세로 붓결 (멀어서 붓결보다 픽셀이 크면 지움)
                float detail = saturate(1.5 - footprint / (_StrokeWidth * 0.6));
                float stroke = (Strokes(float2(pos.z, pos.y)) * w.x + Strokes(float2(pos.x, pos.y)) * w.z) * detail;
                g *= 1.0 - stroke * _StrokeInk;

                // 외곽 먹선
                float rim = 1.0 - saturate(dot(n, viewDir));
                float outline = smoothstep(1.0 - _RimWidth, 1.0 - _RimWidth * 0.35, rim);
                float dry = lerp(1.0, smoothstep(0.35, 0.6, Fbm(float2(pos.x + pos.z, pos.y) * 0.08)), _RimDry);
                g *= 1.0 - outline * dry * _RimInk;

                // 산 아래 안개, 먼 산 옅게
                half3 mistC = LinearToSRGB(_MistColor.rgb);
                float mist = (1.0 - smoothstep(_MistBottom, _MistTop, pos.y)) * _MistAmount;
                float far = smoothstep(_FarStart, _FarEnd, dist) * _FarAmount;
                g = lerp(g, mistC, saturate(mist + far));

                // 한지 결
                float grain = (ValueNoise(pos.xy * 2.7) + ValueNoise(pos.zy * 3.1)) * 0.5 - 0.5;
                g *= 1.0 + grain * _PaperGrain;

                return half4(SRGBToLinear(saturate(g)), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert (Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); return o; }
            half frag (Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert (Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag (Varyings i) : SV_Target { return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
