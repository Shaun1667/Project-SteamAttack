Shader "YPH/EtherealThresholdMist"
{
 Properties
 {
  _NoiseVolume ("Density noise", 3D) = "white" {}
  [HDR] _LightColor ("Lit mist", Color) = (.85,1.7,2,1)
  _ShadowColor ("Mist shadow", Color) = (.035,.13,.2,1)
  _Density ("Mist thickness", Range(0,20)) = 9
  _Speed ("Mist flow", Range(0,1)) = .35
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-5" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend One OneMinusSrcAlpha
   Cull Front ZWrite Off ZTest Always
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   TEXTURE3D(_NoiseVolume); SAMPLER(sampler_NoiseVolume);
   CBUFFER_START(UnityPerMaterial)
   float4 _LightColor, _ShadowColor;
   float _Density, _Speed;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; };
   struct V { float4 positionCS:SV_POSITION; float3 positionOS:TEXCOORD0; };
   V Vert(A v) {V o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.positionOS=v.positionOS.xyz;return o;}
   float N(float3 p) {return SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,p*.125,0).r;}
   half4 Frag(V i):SV_Target
   {
    float3 origin=TransformWorldToObject(GetCameraPositionWS());
    float3 ray=normalize(i.positionOS-origin);
    float3 safeRay=sign(ray+1e-7)*max(abs(ray),1e-5);
    float3 first=(-.5-origin)/safeRay,last=(.5-origin)/safeRay;
    float3 nearHit=min(first,last),farHit=max(first,last);
    float nearT=max(max(nearHit.x,nearHit.y),nearHit.z);
    float farT=min(min(farHit.x,farHit.y),farHit.z);
    nearT=max(nearT,0);
    float sceneDepth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
    float eyePerUnit=-TransformWorldToView(TransformObjectToWorld(origin+ray)).z;
    farT=min(farT,sceneDepth/max(eyePerUnit,1e-5));
    float stepSize=max(0,farT-nearT)/40;
    float time=_Time.y*_Speed;
    float3 color=0;float alpha=0;
    [loop] for(int k=0;k<40;k++)
    {
     float3 p=origin+ray*(nearT+(k+.5)*stepSize);
     float2 centered=p.xy-float2(.015,.12);
     float radius=length(centered*float2(1,1.12));
     float angle=atan2(centered.y,centered.x)+radius*2.4+p.z*.55+time*.12;
     float3 q=float3(cos(angle)*radius,sin(angle)*radius,p.z*.45);
     q=q*8+float3(0,-time*.17,time*.07);
     q+=(float3(N(q+3.1),N(q+7.3),N(q+11.7))-.5)*.85;
     float field=N(q)*.46+N(q*2.09+4.3)*.30+N(q*4.31)*.16+N(q*8.2)*.08;
     float edgeDistance=min(.5-abs(p.x),.5-abs(p.y));
     float rim=exp(-edgeDistance*14);
     float low=exp(-(p.y+.5)*5);
     float plume=exp(-pow((p.x+.23+.09*sin(p.y*9+time*.3))*6,2))*(1-smoothstep(.02,.4,p.y));
     float envelope=(rim*.85+low*.75+plume*.7)*smoothstep(.015,.075,edgeDistance);
     envelope*=smoothstep(.12,.42,radius);
     // A thin veil crosses the central void without filling its depth with white.
     float haze=.03*smoothstep(.02,.12,edgeDistance)*(1-smoothstep(.02,.32,radius)*.45);
     float density=(smoothstep(.42,.66,field)*envelope+smoothstep(.25,.65,field)*haze)*_Density;
     density*=1-smoothstep(.25,.5,abs(p.z));
     float opacity=1-exp(-density*stepSize);
     float illumination=smoothstep(.40,.68,field)*(.6+.4*rim);
     float3 mist=lerp(_ShadowColor.rgb,_LightColor.rgb,illumination);
     color+=(1-alpha)*opacity*mist;
     alpha+=(1-alpha)*opacity;
    }
    return half4(color,alpha);
   }
   ENDHLSL
  }
 }
}
