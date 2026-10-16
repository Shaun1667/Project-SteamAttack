Shader "YPH/Jeogori Two Sided"
{
    Properties
    {
        [MainTexture] _BaseMap("겉면 무늬", 2D) = "white" {}
        [MainColor] _BaseColor("겉면 색상", Color) = (1,1,1,1)
        _LiningColor("안쪽 색상", Color) = (0.29,0.031,0.039,1)
        [Normal] _BumpMap("겉면 노멀", 2D) = "bump" {}
        _BumpScale("노멀 강도", Float) = 1
        _Smoothness("매끄러움", Range(0,1)) = 0.2
        _Metallic("금속성", Range(0,1)) = 0
        [HideInInspector] _Cull("Cull", Float) = 0
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
        [HideInInspector] _Surface("Surface", Float) = 0
        [HideInInspector] _EmissionColor("Emission", Color) = (0,0,0,0)
        [HideInInspector] _EmissionMap("Emission", 2D) = "white" {}
        [HideInInspector] _SpecColor("Specular", Color) = (0.2,0.2,0.2,1)
        [HideInInspector] _OcclusionStrength("Occlusion", Float) = 1
        [HideInInspector] _OcclusionMap("Occlusion", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment JeogoriFragment
            #pragma shader_feature_local _NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            // shortcut: 별도 안감 상수는 SRP Batcher 대상이 아니다. 다수 캐릭터에 쓰면 Shader Graph로 통합한다.
            half4 _LiningColor;

            half4 JeogoriFragment(Varyings input, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SurfaceData surface;
                InitializeStandardLitSurfaceData(input.uv, surface);
                bool isFront = IS_FRONT_VFACE(face, true, false);
                if (!isFront)
                {
                    surface.albedo = _LiningColor.rgb;
                    surface.normalTS = half3(0, 0, 1);
                    surface.smoothness = 0.3h;
                    input.normalWS = -input.normalWS;
                }
                InputData lighting;
                InitializeInputData(input, surface.normalTS, lighting);
                InitializeBakedGIData(input, lighting);
                // 뒷면은 반전된 법선을 기준으로 환경광을 받아야 검게 꺼지지 않는다.
                if (!isFront) lighting.bakedGI = SampleSH(lighting.normalWS);
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                color.a = 1;
                return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
