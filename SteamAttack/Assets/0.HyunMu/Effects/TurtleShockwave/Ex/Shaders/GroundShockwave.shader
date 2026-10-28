Shader "HyunMuEffects/GroundShockwave"
{
 Properties {
 _Color("Pressure edge",Color)=(.78,.91,1,.9)
 _Origin("World centre",Vector)=(0,0,0,0)
 _OuterRadius("Front radius",Float)=3
 _InnerRadius("Back radius",Float)=2
 _RimWidth("Rim width in metres",Float)=.1
 _Fade("Opacity",Range(0,1))=1
 _RefractionStrength("Floor stretching in metres",Range(0,1.5))=.45
 _RefractionAvailable("Camera opaque texture ready",Float)=0
 }
 SubShader {
 Tags {"Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
 Pass {
 Name "PressureSurface"
 Tags {"LightMode"="UniversalForward"}
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile_instancing
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
 struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
 CBUFFER_START(UnityPerMaterial)
 half4 _Color;
 float4 _Origin;
 float _OuterRadius,_InnerRadius,_RimWidth,_Fade,_RefractionStrength,_RefractionAvailable;
 CBUFFER_END
 V vert(A i) {
 V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
 o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
 o.positionCS=TransformWorldToHClip(o.positionWS);o.uv=i.uv;return o;
 }
 half4 frag(V i):SV_Target {
 UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
 float2 radial=i.positionWS.xz-_Origin.xz;
 float r=max(.0001,length(radial));
 float band=max(.001,_OuterRadius-_InnerRadius);
 float t=saturate((r-_InnerRadius)/band);
 float distance=_OuterRadius-r;
 float crest=pow(saturate(sin(t*PI)),1.5);
 float rim=min(_RimWidth,band*.4);
 float edge=exp(-pow((distance-rim*.55)/max(.012,rim*.6),2)*2);
 float echo=exp(-pow((t-.37)/.035,2)*2)*.12;
 float mask=smoothstep(0,.055,t)*(1-smoothstep(.985,1,t));
 float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
 float3 displaced=i.positionWS-float3(radial.x/r,0,radial.y/r)*(_RefractionStrength*crest*_Fade);
 float4 clip=TransformWorldToHClip(displaced);
 float4 screen=ComputeScreenPos(clip);
 float2 sampleUV=screen.xy/max(.0001,screen.w);
 // Do not pull silhouettes of objects in front of this surface into the pressure band.
 float rawDepth=SampleSceneDepth(sampleUV);
 float eyeDepth=LinearEyeDepth(rawDepth,_ZBufferParams);
 if(unity_OrthoParams.w>.5) {
 #if UNITY_REVERSED_Z
 rawDepth=1-rawDepth;
 #endif
 eyeDepth=lerp(_ProjectionParams.y,_ProjectionParams.z,rawDepth);
 }
 float surfaceDepth=-TransformWorldToView(i.positionWS).z;
 float safe=step(surfaceDepth-.06,eyeDepth);
 safe*=step(.002,sampleUV.x)*step(sampleUV.x,.998)*step(.002,sampleUV.y)*step(sampleUV.y,.998);
 sampleUV=lerp(uv,saturate(sampleUV),safe);
 half3 scene=SampleSceneColor(sampleUV);
 half glow=saturate(edge*.75+echo+crest*.035);
 half3 pressure=lerp(scene, _Color.rgb,glow);
 float alpha=(crest*.86+edge*.65+echo)*mask*_Fade*_Color.a;
 if(_RefractionAvailable<.5) { pressure=_Color.rgb;alpha=(edge*.6+echo+crest*.045)*mask*_Fade*_Color.a; }
 return half4(pressure,saturate(alpha));
 }
 ENDHLSL
 }
 }
}
