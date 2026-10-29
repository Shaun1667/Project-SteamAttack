// 근정전 입체감 셰이더 (URP Lit 기반) — NMJ
//  · 꼭짓점 색에 구운 틈새 그늘: R = 큰 그늘(2.5m, 처마 밑·공포 사이), G = 좁은 틈(0.35m, 기와 골·서까래 사이) — bake_ao.py
//  · 반구 환경광: 위를 보는 면은 하늘빛, 아래를 보는 면(처마 밑)은 눈에 반사돼 올라오는 따뜻한 빛
//  · 하늘 테두리 빛: 윤곽이 하늘과 갈라지게 아주 약하게
// 그림자·깊이·노멀(SSAO) 패스는 URP Lit 것을 그대로 씀. 추가 값은 URP Lit 에서 안 쓰는 칸을 빌려 씀 → SRP Batcher 그대로
Shader "NMJ/GJJ_Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("색 텍스처", 2D) = "white" {}
        [MainColor] _BaseColor("색", Color) = (1,1,1,1)
        _Smoothness("매끈함(맵 있으면 배율)", Range(0, 1)) = 0.2
        _Metallic("금속(맵 없을 때)", Range(0, 1)) = 0
        _MetallicGlossMap("금속·매끈 맵 (R 금속, A 매끈)", 2D) = "white" {}
        _BumpScale("요철 세기", Float) = 1
        [Normal] _BumpMap("요철(노멀) 맵", 2D) = "bump" {}

        [Header(Depth)]
        _OcclusionStrength("큰 틈새 그늘 세기 (꼭짓점 R)", Range(0, 1)) = 0.85
        _DetailAlbedoMapScale("좁은 틈 그늘 세기 (꼭짓점 G)", Range(0, 1)) = 0.7
        _ClearCoatMask("햇빛 받는 면에도 그늘 섞기", Range(0, 1)) = 0.35
        _ClearCoatSmoothness("위아래 밝기 차 (하늘 쪽 밝게)", Range(0, 1)) = 0.25
        _EmissionColor("눈 반사광 (아래를 보는 면)", Color) = (0.66, 0.62, 0.56, 1)
        _SpecColor("하늘 테두리 빛 색", Color) = (0.62, 0.72, 0.88, 1)
        _Parallax("하늘 테두리 빛 세기", Range(0, 1)) = 0.12

        // URP Lit 이 쓰는 나머지 (숨김)
        [HideInInspector] _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.5
        [HideInInspector] _OcclusionMap("Occlusion", 2D) = "white" {}
        [HideInInspector] _EmissionMap("Emission", 2D) = "white" {}
        [HideInInspector] _SpecGlossMap("Specular", 2D) = "white" {}
        [HideInInspector] _ParallaxMap("Height Map", 2D) = "black" {}
        [HideInInspector] _DetailMask("Detail Mask", 2D) = "white" {}
        [HideInInspector] _DetailAlbedoMap("Detail Albedo", 2D) = "linearGrey" {}
        [HideInInspector] _DetailNormalMapScale("Scale", Range(0, 2)) = 1
        [HideInInspector] _DetailNormalMap("Normal Map", 2D) = "bump" {}
        [HideInInspector] _Surface("__surface", Float) = 0
        [HideInInspector] _Cull("__cull", Float) = 2
        [HideInInspector] _AlphaClip("__clip", Float) = 0
        [HideInInspector] _SmoothnessTextureChannel("Smoothness texture channel", Float) = 0
        [HideInInspector] _QueueOffset("Queue offset", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "UniversalMaterialType" = "Lit" "Queue" = "Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #if defined(_DBUFFER_MRT1) || defined(_DBUFFER_MRT2) || defined(_DBUFFER_MRT3)
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DBuffer.hlsl"
            #endif

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 texcoord   : TEXCOORD0;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                half3  normalWS   : TEXCOORD2;
                half4  tangentWS  : TEXCOORD3;
                half4  color      : TEXCOORD4;
                half   fogFactor  : TEXCOORD5;
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                real sgn = input.tangentOS.w * GetOddNegativeScale();
                o.tangentWS = half4(n.tangentWS, sgn);
                o.color = input.color;
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.positionCS = p.positionCS;
                return o;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                SurfaceData s;
                InitializeStandardLitSurfaceData(input.uv, s);
                s.clearCoatMask = 0; s.clearCoatSmoothness = 0; s.emission = 0;

                // 구운 틈새 그늘
                half aoL = lerp(1.0h, input.color.r, _OcclusionStrength);
                half aoS = lerp(1.0h, input.color.g, _DetailAlbedoMapScale);
                half ao = aoL * aoS;

                float3 bitangent = input.tangentWS.w * cross(input.normalWS.xyz, input.tangentWS.xyz);
                #if defined(_NORMALMAP)
                half3 nWS = TransformTangentToWorld(s.normalTS, half3x3(input.tangentWS.xyz, bitangent, input.normalWS.xyz));
                #else
                half3 nWS = input.normalWS;
                #endif
                nWS = NormalizeNormalPerPixel(nWS);

                InputData d = (InputData)0;
                d.positionWS = input.positionWS;
                d.positionCS = input.positionCS;
                d.normalWS = nWS;
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                d.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                d.fogCoord = input.fogFactor;
                d.vertexLighting = 0;
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                // 반구 환경광: 하늘빛(위) + 눈에 반사돼 올라오는 빛(아래·옆)
                //  큰 그늘은 주로 하늘빛을 가림 (처마가 하늘을 덮어도 마당 눈빛은 처마 밑·벽으로 들어옴)
                half up = nWS.y;
                half3 sky = SampleSH(nWS) * (1.0h + _ClearCoatSmoothness * up);
                half wG = saturate(0.45h - 0.55h * up);                  // 위 0 · 옆 0.45 · 아래 1
                half3 amb = sky * (1.0h - wG) * aoL + _EmissionColor.rgb * wG * lerp(aoL, 1.0h, 0.6h);
                d.bakedGI = amb * aoS;

                // 햇빛 받는 색에는 좁은 틈 그늘만 살짝 (큰 그늘은 그림자 맵이 이미 맡음)
                s.albedo *= lerp(1.0h, aoS, _ClearCoatMask);

                #if defined(_DBUFFER_MRT1) || defined(_DBUFFER_MRT2) || defined(_DBUFFER_MRT3)
                ApplyDecalToSurfaceData(input.positionCS, s, d);
                #endif

                half4 c = UniversalFragmentPBR(d, s);

                // 하늘 테두리 빛 (그늘진 곳엔 덜)
                half fres = pow(1.0h - saturate(dot(nWS, d.viewDirectionWS)), 4.0h);
                c.rgb += _SpecColor.rgb * fres * _Parallax * aoL * saturate(up * 0.5h + 0.6h);

                c.rgb = MixFog(c.rgb, d.fogCoord);
                c.a = 1;
                return c;
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Universal Render Pipeline/Lit"
}
