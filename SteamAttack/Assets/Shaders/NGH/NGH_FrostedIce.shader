// 서리 낀 얼음 덩어리 (NGH) — 크리스탈처럼 맑은 결정 대신, 하얗게 흐리고 반쯤 비치는 얼음
//  - 얼음 너머(_CameraOpaqueTexture)를 법선 방향으로 살짝 굴절시키고 여러 번 샘플해 뿌옇게 흐림
//  - 안쪽에 세로로 결이 진 구름 무늬(월드 좌표 노이즈, 보는 방향으로 한 번 더 샘플해 깊이감)
//  - 정면은 많이 비치고 가장자리로 갈수록 하얗고 불투명, 하이라이트·밝은 테두리
// URP Opaque Texture 가 켜져 있어야 뒤가 비침 (꺼져 있으면 회색 바탕 위에 얼음색만 보임)
Shader "NGH/FrostedIce"
{
    Properties
    {
        _IceColor ("Ice Color (얇은 곳)", Color) = (0.78, 0.88, 0.96, 1)
        _CloudColor ("Cloud Color (뿌연 결)", Color) = (0.95, 0.97, 1.0, 1)
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _RimPower ("Rim Power", Range(0.5, 8)) = 2.2
        _OpacityFacing ("Opacity (정면)", Range(0, 1)) = 0.45
        _OpacityEdge ("Opacity (가장자리)", Range(0, 1)) = 0.92
        _CloudScale ("Cloud Scale (m 당)", Float) = 0.9
        _CloudStrength ("Cloud Strength", Range(0, 1)) = 0.75
        _StreakStretch ("Streak Stretch (세로 결)", Range(1, 8)) = 3.5
        _Depth ("Depth Parallax", Range(0, 2)) = 0.6
        _Refract ("Refract (굴절)", Range(0, 0.1)) = 0.025
        _Frost ("Frost Blur (뒤 흐림)", Range(0, 0.03)) = 0.008
        _BackTint ("Back Tint (비친 뒤 색)", Color) = (0.82, 0.9, 0.98, 1)
        _Ambient ("Ambient", Range(0, 1)) = 0.6
        _SpecPower ("Spec Power", Range(4, 256)) = 48
        _SpecStrength ("Spec Strength", Range(0, 2)) = 0.5
        _DetailTex ("Detail (원래 텍스처, 명암만 씀)", 2D) = "gray" {}
        _DetailStrength ("Detail Strength", Range(0, 1)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _IceColor, _CloudColor, _RimColor, _BackTint;
                half _RimPower, _OpacityFacing, _OpacityEdge, _CloudStrength, _StreakStretch, _Depth;
                half _Refract, _Frost, _Ambient, _SpecPower, _SpecStrength, _DetailStrength;
                float _CloudScale;
                float4 _DetailTex_ST;
            CBUFFER_END
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 screenPos  : TEXCOORD2;
                half   fog        : TEXCOORD3;
                float2 uv         : TEXCOORD4;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fog = ComputeFogFactor(p.positionCS.z);
                o.uv = TRANSFORM_TEX(v.uv, _DetailTex);
                return o;
            }

            float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float Noise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i + float3(0,0,0)), Hash(i + float3(1,0,0)), f.x),
                                 lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x),
                                 lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float Fbm(float3 p)
            {
                float a = 0.5, s = 0;
                for (int k = 0; k < 4; k++) { s += a * Noise(p); p = p * 2.03 + 11.7; a *= 0.5; }
                return s;
            }
            // 세로로 결이 진 구름: y를 늘려 줄무늬처럼
            float Cloud(float3 pws)
            {
                float3 q = pws * _CloudScale;
                q.y /= _StreakStretch;
                float n = Fbm(q);
                float streak = Noise(float3(q.x * 6.0, q.y * 0.6, q.z * 6.0));   // 가는 세로 금
                return saturate(n * 1.25 - 0.2 + (streak - 0.5) * 0.35);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half ndv = saturate(dot(N, V));
                half fres = pow(1.0h - ndv, _RimPower);

                // 안쪽 깊이감: 표면과 보는 방향으로 들어간 곳 두 번 샘플
                float c1 = Cloud(i.positionWS);
                float c2 = Cloud(i.positionWS - V * _Depth);
                float cloud = saturate(c1 * 0.55 + c2 * 0.45);
                // 원래 텍스처의 명암: 금·받침 같은 디테일을 결에 섞고, 어두운 곳은 조금 어둡게
                half3 det = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, i.uv).rgb;
                half detL = dot(det, half3(0.3, 0.59, 0.11));
                cloud = saturate(cloud + (detL - 0.5) * _DetailStrength);
                half detShade = lerp(1.0h, saturate(0.55h + detL * 0.6h), _DetailStrength);

                // 얼음 너머: 굴절 + 뿌옇게 여러 번 샘플
                float2 uv = i.screenPos.xy / i.screenPos.w;
                float3 nVS = mul((float3x3)UNITY_MATRIX_V, N);
                uv += nVS.xy * _Refract * (0.6 + cloud);
                half3 back = 0;
                const float2 taps[6] = { float2(0,0), float2(1,0.3), float2(-0.8,0.6), float2(0.2,-1), float2(-0.5,-0.7), float2(0.9,-0.4) };
                [unroll] for (int k = 0; k < 6; k++) back += SampleSceneColor(uv + taps[k] * _Frost);
                back /= 6.0;
                back *= _BackTint.rgb;

                Light L = GetMainLight();
                half wrap = saturate(dot(N, L.direction) * 0.5 + 0.5);
                half3 lightCol = min(L.color, 1.5);
                half3 ice = lerp(_IceColor.rgb, _CloudColor.rgb, cloud * _CloudStrength);
                ice *= (_Ambient + (1 - _Ambient) * wrap) * lightCol * detShade;
                half spec = pow(saturate(dot(N, normalize(L.direction + V))), _SpecPower) * _SpecStrength;

                half opacity = saturate(lerp(_OpacityFacing, _OpacityEdge, fres) + cloud * _CloudStrength * 0.3);
                half3 col = lerp(back, ice, opacity);
                col += spec * lightCol + _RimColor.rgb * fres * 0.5;
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
        // 바닥에 그림자를 드리워 떠 보이지 않게
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _IceColor, _CloudColor, _RimColor, _BackTint;
                half _RimPower, _OpacityFacing, _OpacityEdge, _CloudStrength, _StreakStretch, _Depth;
                half _Refract, _Frost, _Ambient, _SpecPower, _SpecStrength, _DetailStrength;
                float _CloudScale;
                float4 _DetailTex_ST;
            CBUFFER_END

            float3 _LightDirection;
            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 vertShadow (A v) : SV_POSITION
            {
                float3 pws = TransformObjectToWorld(v.positionOS.xyz);
                float3 nws = TransformObjectToWorldNormal(v.normalOS);
                float4 p = TransformWorldToHClip(ApplyShadowBias(pws, nws, _LightDirection));
            #if UNITY_REVERSED_Z
                p.z = min(p.z, UNITY_NEAR_CLIP_VALUE);
            #else
                p.z = max(p.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                return p;
            }
            half4 fragShadow () : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
