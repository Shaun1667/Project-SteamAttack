// 시간 역행용 먹 디졸브 (URP, SRP Batcher)
//  - 먹이 흘러내리듯: 위(머리)부터 먹이 스며 검게 젖어 내려가고, 세로로 길게 늘어진 먹 줄기(눈물 자국)를 따라
//    아래로 흘러내리며 깎여 사라진다. 나타날 때(_FlowDir 1)는 반대로 위부터 먹이 걷히며 원래 모습이 드러난다
//  - _InkMode 0: 원래 텍스처 + 젖어 드는 먹 (플레이어 본체)
//  - _InkMode 1: 텍스처 없이 검은 먹 형상 (잔상)
//  높이·줄기는 월드 공간(_BaseY = 발 높이, _Height = 키) 기준이라 모델 스케일과 상관없이 같은 모양
Shader "NGH/InkDissolve"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        _InkColor ("먹 색", Color) = (0.02, 0.02, 0.025, 1)
        _InkRim ("먹 가장자리 번짐 색 (잔상)", Color) = (0.16, 0.17, 0.2, 1)
        _InkMode ("0 = 텍스처 디졸브, 1 = 먹 형상", Range(0, 1)) = 0
        _Dissolve ("디졸브 (0 = 온전, 1 = 사라짐)", Range(0, 1)) = 0
        _FlowDir ("0 = 사라짐(위부터 흘러내림), 1 = 나타남(위부터 걷힘)", Range(0, 1)) = 0
        _BaseY ("발 높이 (월드 Y)", Float) = 0
        _Height ("키 (m)", Float) = 1.8
        _Center ("몸 중심 (월드 XZ)", Vector) = (0, 0, 0, 0)
        _DripScale ("먹 줄기 촘촘함", Range(2, 60)) = 14
        _DripAmount ("먹 줄기 길이 (흘러내림 정도)", Range(0, 1)) = 0.45
        _SoakWidth ("먹이 젖어 드는 띠 폭", Range(0.01, 0.6)) = 0.22
        _EdgeWidth ("깎이는 가장자리 폭", Range(0, 0.3)) = 0.05
        _NoiseScale ("잔 번짐 크기", Range(0.5, 40)) = 9
        _Seed ("노이즈 시드", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _InkColor;
            half4 _InkRim;
            half _InkMode;
            half _Dissolve;
            half _FlowDir;
            float _BaseY;
            float _Height;
            float4 _Center;
            half _DripScale;
            half _DripAmount;
            half _SoakWidth;
            half _EdgeWidth;
            half _NoiseScale;
            float _Seed;
        CBUFFER_END

        float Hash31(float3 p)
        {
            p = frac(p * 0.3183099 + float3(0.11, 0.37, 0.71));
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 p)
        {
            float3 i = floor(p);
            float3 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = Hash31(i), n100 = Hash31(i + float3(1,0,0));
            float n010 = Hash31(i + float3(0,1,0)), n110 = Hash31(i + float3(1,1,0));
            float n001 = Hash31(i + float3(0,0,1)), n101 = Hash31(i + float3(1,0,1));
            float n011 = Hash31(i + float3(0,1,1)), n111 = Hash31(i + float3(1,1,1));
            float nx00 = lerp(n000, n100, f.x), nx10 = lerp(n010, n110, f.x);
            float nx01 = lerp(n001, n101, f.x), nx11 = lerp(n011, n111, f.x);
            return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
        }

        // 0~1. 작을수록 먼저 깎임.
        // 높이: 사라질 때는 위가 작고(머리부터), 나타날 때는 위가 커서(머리부터 드러남) 둘 다 먹이 위에서 아래로 흐름.
        // 줄기: 몸 중심축을 도는 각도(둘레)로는 촘촘하고 세로로는 길게 늘인 노이즈 → 몸을 감싸며 세로 줄기가 생기고,
        //       줄기마다 흘러내리는 속도가 달라 눈물 자국처럼 늘어짐. 각도는 원 위 좌표(cos, sin)로 써서 이음매가 없음
        float DissolveField(float3 wp)
        {
            float h = saturate((wp.y - _BaseY) / max(_Height, 0.1));
            float hd = lerp(1.0 - h, h, _FlowDir);
            float2 dxz = wp.xz - _Center.xy;
            float a = atan2(dxz.y, dxz.x);
            float k = _DripScale * 0.3;
            float3 sp = float3(cos(a) * k, sin(a) * k, wp.y * _DripScale * 0.08) + _Seed;
            float streak = 0.7 * ValueNoise(sp) + 0.3 * ValueNoise(sp * float3(2.1, 2.1, 1.0) + 5.3);
            streak = streak * streak * (3.0 - 2.0 * streak);                      // 줄기 경계를 또렷하게
            float fine = ValueNoise(wp * _NoiseScale * 2.0 + _Seed + 11.7);      // 가장자리 잔 번짐
            return saturate(hd * (1.0 - _DripAmount) + streak * _DripAmount) * 0.96 + fine * 0.04;
        }

        // 깎는 기준값. 값이 움직이자마자 바로 변화가 보이도록 시작점을 실제 필드 값의 끝(가장 먼저 깎이는/드러나는 값)에 맞춤
        //  - 사라짐(_FlowDir 0, _Dissolve 0→1): 머리 꼭대기의 가장 낮은 값에서 시작 → 누르자마자 깎이기 시작, 끝은 1 넘겨 남김없이 사라짐
        //  - 나타남(_FlowDir 1, _Dissolve 1→0): 머리 꼭대기의 가장 높은 값에서 시작 → 시작하자마자 드러남, 끝은 0 아래로 내려 남김없이 보임
        float Threshold()
        {
            float lo = _DripAmount * 0.08 * 0.96;                                   // 사라짐 첫 깎임
            float hi = ((1.0 - _DripAmount) + _DripAmount * 0.6) * 0.96;            // 나타남 첫 드러남 (머리 꼭대기의 줄기 평균치 → 시작하자마자 드러남)
            float start = lerp(lo, -0.01, _FlowDir);   // _Dissolve 0 쪽 끝
            float end = lerp(1.01, hi, _FlowDir);      // _Dissolve 1 쪽 끝
            return lerp(start, end, _Dissolve);
        }

        // 깎인 곳은 clip. 반환값 x = 깎이는 가장자리(1 = 경계 바로 옆), y = 먹이 젖어 든 정도(1 = 흠뻑)
        half2 DissolveClip(float3 wp)
        {
            half d = DissolveField(wp) - Threshold();
            clip(d - 0.0005);
            half edge = 1.0h - saturate(d / max(_EdgeWidth, 1e-4h));
            half soak = 1.0h - saturate(d / max(_SoakWidth, 1e-3h));
            return half2(edge, soak);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half2 es = DissolveClip(i.positionWS);
                half edge = es.x, soak = es.y;
                half3 n = normalize(i.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half grain = ValueNoise(i.positionWS * _NoiseScale * 1.7 + 3.3);   // 먹 농담
                half3 ink = _InkColor.rgb * (0.75h + 0.5h * grain);

                half3 color;
                if (_InkMode > 0.5h)
                {
                    // 먹 형상: 검은 먹에 농담, 실루엣 가장자리는 물기 많은 옅은 먹. 깎이는 경계는 짙게 고임
                    half rim = pow(1.0h - saturate(dot(n, v)), 3.0h);
                    color = lerp(ink, _InkRim.rgb, rim * 0.7h * (1.0h - soak));
                    color = lerp(color, _InkColor.rgb * 0.6h, edge);
                }
                else
                {
                    half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                    half4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                    Light light = GetMainLight(shadowCoord);
                    half ndl = saturate(dot(n, light.direction));
                    half3 lit = tex.rgb * (light.color * light.shadowAttenuation * ndl + SampleSH(n));
                    // 깎이는 경계 위쪽 띠가 먹에 젖어 검게 → 경계에서 먹이 고여 짙어지며 깎임
                    color = lerp(lit, ink, saturate(soak * soak * 1.2h));
                    // 젖은 먹의 윤기 (살짝 번들거림)
                    half3 hlf = normalize(light.direction + v);
                    color += light.color * pow(saturate(dot(n, hlf)), 48.0h) * 0.25h * soak;
                    color = lerp(color, _InkColor.rgb * 0.6h, edge);
                }
                return half4(MixFog(color, i.fog), 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
            #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
            #endif
                o.positionCS = positionCS;
                o.positionWS = positionWS;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(i.positionWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(i.positionWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                DissolveClip(i.positionWS);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0.0h);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
