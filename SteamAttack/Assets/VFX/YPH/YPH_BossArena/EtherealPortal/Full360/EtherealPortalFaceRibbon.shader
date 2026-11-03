Shader "YPH/EtherealPortalFaceRibbon"
{
 Properties
 {
  _NoiseVolume ("Density noise", 3D) = "white" {}
  [HDR] _Tint ("Light color", Color) = (.45,.85,1,1)
  _Opacity ("Opacity", Range(0,1)) = .6
  _Speed ("Flow speed", Range(0,1)) = .3
  _Motion ("Sway amplitude", Range(0,.03)) = .006
  _Filament ("Fine light line", Range(0,1)) = 0
  [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass
  {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha [_DstBlend]
   ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   TEXTURE3D(_NoiseVolume); SAMPLER(sampler_NoiseVolume);
   CBUFFER_START(UnityPerMaterial)
   float4 _Tint;
   float _Opacity,_Speed,_Motion,_Filament;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
   struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float3 flowPosition:TEXCOORD1; };
   V Vert(A v)
   {
    V o; float phase=v.color.r*6.283185;
    float t=_Time.y*_Speed*(.85+v.color.r*.35);
    float u=v.uv.x;
    float fade=sin(u*3.141593);
    // Anchored ends and shared cross-section motion keep each ribbon continuous.
    float3 bend=float3(sin(u*5.5-t+phase),cos(u*6.5-t*.83+phase*1.3)*.65,sin(u*5-t*.71+phase)*.9);
    float3 ripple=float3(sin(u*15-t*1.65+phase)*.32,sin(u*13+t*1.25+phase)*.25,cos(u*12-t*1.4+phase)*.35);
    v.positionOS.xyz+=(bend+ripple)*_Motion*fade;
    o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;o.color=v.color;o.flowPosition=v.positionOS.xyz;return o;
   }
   float N(float3 p) {return SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,p*.27,0).r;}
   half4 Frag(V i):SV_Target
   {
    clip(TransformWorldToObject(GetCameraPositionWS()).z-.012);
    if(i.flowPosition.z<.012)
    {
     float3 eye=TransformWorldToObject(GetCameraPositionWS());
     float3 sight=i.flowPosition-eye;
     float entryT=(.012-eye.z)/(sign(sight.z+1e-7)*max(abs(sight.z),1e-5));
     float2 entry=eye.xy+sight.xy*entryT;
     clip(entryT);clip(1.0001-entryT);
     clip(.292-abs(entry.x));clip(.28-abs(entry.y-.255));
    }
    float t=_Time.y*_Speed;
    // All curved veils share one moving density field across their joins.
    float3 p=i.flowPosition*float3(8,7,8)+float3(t*.08,-t*.18,t*.06);
    p+=(float3(N(p+.21),N(p+.53),N(p+.79))-.5)*.8;
    float n=N(p*.8)*.5+N(p*1.9+.23)*.32+N(p*4.2)*.18;
    float cross=abs(i.uv.y*2-1+(N(p*1.7+t*.03)-.5)*.3);
    float ends=smoothstep(0,.065,i.uv.x)*(1-smoothstep(.9,1,i.uv.x));
    float smoke=pow(saturate(1-cross*cross),1.4)*smoothstep(.27,.68,n);
    float strands=exp(-abs(N(p*3.1)-.52)*26);
    smoke*=.65+strands*.65;
    float lineDistance=abs(i.uv.y*2-1);
    float core=exp(-lineDistance*lineDistance*95)+exp(-lineDistance*lineDistance*5)*.20;
    float phase=i.color.r*6.283185;
    float moving=saturate(_Motion*1000);
    float energy=pow(.5+.5*cos(i.uv.x*12-t*(2.3+i.color.r*.5)+phase),8);
    float pulse=lerp(.7+.3*sin(i.uv.x*12-t*2+i.color.r*7),.55+energy*.8+.12*sin(i.uv.x*19+t+phase),moving);
    float alpha=lerp(smoke,core*pulse,_Filament)*ends*_Opacity*i.color.a;
    float3 tint=_Tint.rgb*lerp(.65+n*.6+strands*.35,1,_Filament);
    return half4(tint,alpha);
   }
   ENDHLSL
  }
 }
}
