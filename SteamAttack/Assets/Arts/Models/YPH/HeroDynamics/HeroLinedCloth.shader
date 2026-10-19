Shader "YPH/Hero Lined Cloth"
{
    Properties
    {
        [MainTexture] _BaseMap("Outer fabric", 2D) = "white" {}
        _BackMap("Inner fabric", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        [Normal] _BumpMap("Outer normal", 2D) = "bump" {}
        _BumpScale("Normal strength", Float) = 1
        _Smoothness("Smoothness", Range(0,1)) = 0.25
        _Metallic("Metallic", Range(0,1)) = 0
        [HideInInspector] _Cull("Cull", Float) = 0
        [HideInInspector] _Cutoff("Cutoff", Float) = 0.5
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
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BackMap); SAMPLER(sampler_BackMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _BumpScale, _Smoothness, _Metallic, _Cull, _Cutoff;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT;
                float2 uv:TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0;
                half3 normalWS:TEXCOORD1; half4 tangentWS:TEXCOORD2;
                float2 uv:TEXCOORD3; half fog:TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes v)
            {
                Varyings o=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v,o); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(v.normalOS,v.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS; o.normalWS=n.normalWS;
                o.tangentWS=half4(n.tangentWS,v.tangentOS.w*GetOddNegativeScale());
                o.uv=TRANSFORM_TEX(v.uv,_BaseMap); o.fog=ComputeFogFactor(p.positionCS.z);
                return o;
            }
            half4 Frag(Varyings i, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i); UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half front=IS_FRONT_VFACE(face,1.0h,0.0h);
                half3 color=lerp(SAMPLE_TEXTURE2D(_BackMap,sampler_BackMap,i.uv).rgb,
                                 SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb,front)*_BaseColor.rgb;
                half3 n=normalize(i.normalWS),t=normalize(i.tangentWS.xyz);
                half3 b=cross(n,t)*i.tangentWS.w;
                half3 normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.uv),_BumpScale*front);
                InputData input=(InputData)0;
                input.positionWS=i.positionWS;
                input.normalWS=normalize(TransformTangentToWorld(normalTS,half3x3(t,b,n)))*(front*2-1);
                input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                input.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                input.bakedGI=SampleSH(input.normalWS);
                input.vertexLighting=VertexLighting(i.positionWS,input.normalWS);
                input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                input.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=color; surface.metallic=_Metallic; surface.smoothness=_Smoothness;
                surface.normalTS=half3(0,0,1); surface.occlusion=1; surface.alpha=1;
                half4 result=UniversalFragmentPBR(input,surface);
                result.rgb=MixFog(result.rgb,i.fog);
                return result;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
