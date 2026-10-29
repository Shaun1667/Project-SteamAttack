// 지붕·담장 위에 얹힌 눈 (NGH). 기존 재질 위에 한 겹 덧그리는 재질이다.
// 렌더러의 재질 목록 끝에 추가하면 Unity 가 메시를 한 번 더 그리므로, 원래 모델·텍스처는 그대로 두고 위를 향한 면만 눈으로 덮는다.
// - 월드 법선이 위를 향할수록 눈 (_Coverage 로 덮이는 각도 조절), 경계는 월드 좌표 노이즈로 군데군데 끊김
// - 눈이 아닌 곳은 완전히 투명 (알파 블렌드), 깊이는 살짝 당겨서 원래 면과 겹쳐 깜빡이지 않게
// - 빛: 밝은 면은 흰색, 그늘·그림자는 옅은 푸른 회색
Shader "NGH/SnowCap"
{
    Properties
    {
        _SnowColor ("Snow Color", Color) = (0.95, 0.96, 0.98, 1)
        _ShadowColor ("Snow Shadow", Color) = (0.62, 0.66, 0.74, 1)
        _Coverage ("Coverage (0 none, 1 all up faces)", Range(0, 1)) = 0.55
        _Softness ("Edge Softness", Range(0.01, 0.5)) = 0.12
        _NoiseScale ("Noise Scale (per m)", Range(0.1, 8)) = 1.6
        _NoiseAmount ("Noise Amount", Range(0, 1)) = 0.45
        _Opacity ("Opacity", Range(0, 1)) = 0.95
        _Push ("Push Along Normal (m)", Range(0, 0.05)) = 0.006
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Geometry+460" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "SnowCap"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _SnowColor;
                half4 _ShadowColor;
                half _Coverage;
                half _Softness;
                half _NoiseScale;
                half _NoiseAmount;
                half _Opacity;
                half _Push;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float3 p = TransformObjectToWorld(v.positionOS.xyz) + n * _Push;
                o.positionWS = p;
                o.normalWS = n;
                o.positionCS = TransformWorldToHClip(p);
                o.fogFactor = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float Hash31(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            // 부드러운 3D 값 노이즈
            float Noise3(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash31(i), n100 = Hash31(i + float3(1, 0, 0)), n010 = Hash31(i + float3(0, 1, 0)), n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1)), n101 = Hash31(i + float3(1, 0, 1)), n011 = Hash31(i + float3(0, 1, 1)), n111 = Hash31(i + float3(1, 1, 1));
                return lerp(lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y), lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y), f.z);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 q = i.positionWS * _NoiseScale;
                float nz = Noise3(q) * 0.65 + Noise3(q * 3.1) * 0.35;

                // 위를 향한 정도 + 노이즈 → 눈 덮임 (Coverage 가 클수록 더 기운 면까지 덮음)
                float up = n.y + (nz - 0.5) * _NoiseAmount;
                float edge = 1.0 - _Coverage;
                float snow = smoothstep(edge - _Softness, edge + _Softness, up);
                clip(snow - 0.01);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float ndl = saturate(dot(n, mainLight.direction) * 0.5 + 0.5);
                float lit = smoothstep(0.35, 0.75, ndl) * lerp(0.35, 1.0, mainLight.shadowAttenuation);
                half3 col = lerp(_ShadowColor.rgb, _SnowColor.rgb, lit);
                col *= 0.97 + 0.06 * Hash31(floor(i.positionWS * 40.0));   // 눈 결

                col = MixFog(col, i.fogFactor);
                return half4(col, snow * _Opacity);
            }
            ENDHLSL
        }
    }
}
