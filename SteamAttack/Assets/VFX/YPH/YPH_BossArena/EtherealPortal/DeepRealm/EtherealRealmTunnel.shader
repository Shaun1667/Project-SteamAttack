Shader "YPH/EtherealRealmTunnel"
{
 Properties
 {
  _NoiseVolume ("Density noise", 3D) = "white" {}
  _DeepColor ("Tunnel shadow", Color) = (.003,.012,.024,1)
  [HDR] _MistColor ("Tunnel wisps", Color) = (.12,.38,.52,1)
  _Speed ("Inward flow", Range(0,1)) = .3
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+10" "RenderType"="Opaque" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Cull Off ZWrite On
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE3D(_NoiseVolume); SAMPLER(sampler_NoiseVolume);
   CBUFFER_START(UnityPerMaterial)
   float4 _DeepColor,_MistColor;
   float _Speed;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;float3 positionOS:TEXCOORD1;};
   V Vert(A v){V o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;o.color=v.color;o.positionOS=v.positionOS.xyz;return o;}
   float N(float3 p){return SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,p*.125,0).r;}
   half4 Frag(V i):SV_Target
   {
    // Only show the interior through the doorway, including at oblique angles.
    float3 eye=TransformWorldToObject(GetCameraPositionWS());
    float3 sight=i.positionOS-eye;
    float entryT=(.012-eye.z)/(sign(sight.z+1e-7)*max(abs(sight.z),1e-5));
    float2 entry=eye.xy+sight.xy*entryT;
    clip(entryT);clip(1.0001-entryT);
    clip(.292-abs(entry.x));clip(.28-abs(entry.y-.255));
    float time=_Time.y*_Speed;
    if(i.color.g>.5)
    {
     float2 p=i.uv;
     float horizon=exp(-pow((p.y-.47)*5,2));
     float clouds=N(float3(p*6,time*.025))*.6+N(float3(p*15,time*.04+2))*.4;
     float3 sky=lerp(_DeepColor.rgb,float3(.12,.32,.4),horizon*(.45+clouds*.5));
     // Small, distant silhouettes give the tunnel a destination beyond its dark walls.
     float ridge=.21+N(float3(p.x*5,1,2))*.19+N(float3(p.x*13,3,6))*.06;
     float silhouette=1-smoothstep(ridge-.015,ridge+.015,p.y);
     sky=lerp(sky,float3(.01,.035,.05),silhouette*.8);
     float distantGlow=exp(-dot((p-float2(.55,.46))*float2(7,12),(p-float2(.55,.46))*float2(7,12)));
     sky+=float3(.12,.32,.4)*distantGlow;
     sky=lerp(_DeepColor.rgb,sky,1-smoothstep(.26,.5,length(p-.5)));
     return half4(sky,1);
    }
    float depth=i.uv.y;
    float angle=i.uv.x*6.283185-depth*5.2-time*.18;
    float3 q=float3(cos(angle)*2,sin(angle)*2,depth*5-time*.19);
    q+=(float3(N(q+1),N(q+4),N(q+7))-.5)*.65;
    float field=N(q*1.5)*.55+N(q*3.2+1)*.3+N(q*7.1)*.15;
    float thread=exp(-abs(N(q*4.5)-.51)*30);
    float curl=.5+.5*sin(angle*2+depth*5+field*5);
    float wisps=smoothstep(.29,.67,field)*(.35+smoothstep(.2,.8,curl)*.5+thread*.25);
    float distanceFade=exp(-depth*1.25);
    float3 color=_DeepColor.rgb*(1+field*.7)+_MistColor.rgb*wisps*distanceFade;
    float2 cells=i.uv*float2(65,110)-float2(0,time*.7);
    float seed=frac(sin(dot(floor(cells),float2(127.1,311.7)))*43758.5453);
    float2 offset=frac(cells)-float2(.2+seed*.6,.2+frac(seed*7)*.6);
    float spark=exp(-dot(offset,offset)*350)*step(.985,seed)*distanceFade;
    color+=float3(.3,.8,1.2)*spark;
    return half4(color,1);
   }
   ENDHLSL
  }
 }
}
