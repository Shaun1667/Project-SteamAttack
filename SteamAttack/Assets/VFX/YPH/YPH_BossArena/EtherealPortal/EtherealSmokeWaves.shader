Shader "YPH/EtherealSmokeWaves"
{
 Properties
 {
  _NoiseVolume ("Density noise", 3D) = "white" {}
  [HDR] _LightColor ("Smoke light", Color) = (.8,1.5,1.7,1)
  _ShadowColor ("Smoke shadow", Color) = (.08,.17,.22,1)
  _Density ("Smoke thickness", Range(0,16)) = 7
  _Speed ("Rising speed", Range(0,1)) = .4
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+2" "RenderType"="Transparent" }
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
   V Vert(A v){V o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.positionOS=v.positionOS.xyz;return o;}
   float N(float3 p){return SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,p*.125,0).r;}
   float Plume(float3 p,float time,float side,float phase,float height)
   {
    float h=(p.y+.5)/height;
    float wave=h*10-time+phase;
    float spread=smoothstep(0,.8,h);
    float centerX=side*(.30-.06*spread)+sin(wave)*(.008+.055*spread)+sin(wave*1.73+phase)*.027*spread;
    float centerZ=.04+sin(wave*.78+phase)*.12*spread;
    float2 d=float2(p.x-centerX,(p.z-centerZ)*.64);
    float angle=atan2(d.y,d.x);
    float radius=.015+spread*.067;
    float fold=(sin(angle*3+h*15-time*1.2+phase)*.018+sin(angle*5-h*20+time*.9)*.006)*spread;
    float n=N(p*13+float3(phase,-time*.4,0));
    float shell=abs(length(d)-(radius+fold+(n-.5)*.035));
    float thickness=.008+.009*spread;
    float density=exp(-shell*shell/(thickness*thickness));
    float breakup=smoothstep(.36,.68,N(p*23+float3(phase,-time*.7,3)));
    return density*(.16+breakup*.84)*smoothstep(0,.065,h)*(1-smoothstep(.66,1,h));
   }
   half4 Frag(V i):SV_Target
   {
    float3 origin=TransformWorldToObject(GetCameraPositionWS());
    float3 ray=normalize(i.positionOS-origin);
    float3 safeRay=sign(ray+1e-7)*max(abs(ray),1e-5);
    float3 first=(-.5-origin)/safeRay,last=(.5-origin)/safeRay;
    float3 nearHit=min(first,last),farHit=max(first,last);
    float nearT=max(0,max(max(nearHit.x,nearHit.y),nearHit.z));
    float farT=min(min(farHit.x,farHit.y),farHit.z);
    // Clamp the volume to scene depth so wisps disappear behind the door frame.
    float sceneDepth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
    float eyePerUnit=-TransformWorldToView(TransformObjectToWorld(origin+ray)).z;
    farT=min(farT,sceneDepth/max(eyePerUnit,1e-5));
    float stepSize=max(0,farT-nearT)/64;
    float time=_Time.y*_Speed;
    float3 color=0;float alpha=0;
    [loop] for(int k=0;k<64;k++)
    {
     float3 p=origin+ray*(nearT+(k+.5)*stepSize);
     float smoke=Plume(p,time,-1,0,1)+Plume(p,time*.87,1,2.7,.89);
     float3 low=p+float3(.12,-.015,.09);
     smoke+=Plume(low,time*1.13,-.12,4.4,.63)*.65;
     float boundsFade=smoothstep(0,.06,.5-max(abs(p.x),max(abs(p.y),abs(p.z))));
     float density=smoke*_Density*boundsFade;
     float opacity=1-exp(-density*stepSize);
     float illumination=saturate(.35+smoke*.7);
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
