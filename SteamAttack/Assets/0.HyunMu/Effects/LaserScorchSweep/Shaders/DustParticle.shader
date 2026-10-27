Shader "LaserScorchEffects/DustParticle"
{
 Properties { _Glow("Glow mode",Float)=0 [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination blend",Float)=10 }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha [_DstBlend]
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   struct A { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; float eyeDepth:TEXCOORD1; };
   CBUFFER_START(UnityPerMaterial)
    float _Glow, _DstBlend;
   CBUFFER_END
   V vert(A v){V o;float3 world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(world);o.eyeDepth=-TransformWorldToView(world).z;o.color=v.color;o.uv=v.uv;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3.-2.*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   float fbm(float2 p){return noise(p)*.57+noise(p*2.03+7.)*.29+noise(p*4.11+19.)*.14;}
   half4 frag(V i):SV_Target
   {
    float sceneDepth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
    float depthFade=saturate((sceneDepth-i.eyeDepth)*3.5);
    float2 p=i.uv*2.-1.;float radius=length(p);
    float soft=1.-smoothstep(.3,1.,radius);
    if(_Glow>.5)return half4(i.color.rgb,exp(-radius*radius*7.)*soft*i.color.a*depthFade);
    float2 q=p*2.8+float2(_Time.y*.065,-_Time.y*.09);
    float large=fbm(q);
    float detail=noise(q*4.7+large*1.6);
    float edge=1.-smoothstep(.27,.99,radius+(large-.5)*.32);
    float density=pow(saturate(large*.7+detail*.3+.12),1.6)*edge;
    float shade=saturate(.4+large*.58+p.y*.11+detail*.13);
    return half4(i.color.rgb*lerp(.58,1.04,shade),density*i.color.a*depthFade);
   }
   ENDHLSL
  }
 }
}
