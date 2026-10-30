Shader "YPH/EtherealSoftMist"
{
 Properties
 {
  _NoiseVolume ("Density noise", 3D) = "white" {}
  [HDR] _BaseColor ("Mist light", Color) = (.7,1.2,1.5,1)
  _Opacity ("Opacity", Range(0,2)) = .8
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+3" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   TEXTURE3D(_NoiseVolume);SAMPLER(sampler_NoiseVolume);
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor;float _Opacity;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float2 uv:TEXCOORD1;float4 color:COLOR;};
   V Vert(A v){V o;o.positionWS=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.uv=v.uv;o.color=v.color;return o;}
   float N(float3 p){return SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,p*.125,0).r;}
   half4 Frag(V i):SV_Target
   {
    float2 p=i.uv*2-1;
    float falloff=pow(saturate(1-dot(p,p)),2.2);
    float3 q=i.positionWS*1.3+float3(_Time.y*.045,-_Time.y*.065,0);
    float density=N(q)*.6+N(q*2.17)*.27+N(q*4.13)*.13;
    float alpha=falloff*smoothstep(.2,.7,density)*i.color.a*_Opacity;
    float sceneDepth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
    float particleDepth=-TransformWorldToView(i.positionWS).z;
    alpha*=saturate((sceneDepth-particleDepth)/.55);
    return half4(_BaseColor.rgb*i.color.rgb*(.65+density*.6),alpha);
   }
   ENDHLSL
  }
 }
}
