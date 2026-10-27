Shader "SteamGateEffects/PressureSteam"
{
 Properties
 {
  _Glow ("Glow mode", Float) = 0
  [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
 }
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
   struct Attributes { float4 positionOS:POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; half4 color:COLOR; float2 uv:TEXCOORD0; };
   CBUFFER_START(UnityPerMaterial)
    float _Glow, _DstBlend;
   CBUFFER_END
   Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.color=v.color; o.uv=v.uv; return o; }
   float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
   float noise(float2 p) { float2 i=floor(p),f=frac(p);f=f*f*(3.-2.*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y); }
   float fbm(float2 p) { return noise(p)*.53+noise(p*2.07+7.)*.27+noise(p*4.13+19.)*.13+noise(p*8.1)*.07; }
   half4 frag(Varyings i):SV_Target
   {
    float2 p=i.uv*2.-1.;float r=length(p);
    if(_Glow>.5) return half4(i.color.rgb,exp(-r*r*5.5)*smoothstep(1.,.55,r)*i.color.a);
    float angle=(1.-r)*2.4+_Time.y*.55;
    float c=cos(angle),s=sin(angle);
    float2 q=float2(p.x*c-p.y*s,p.x*s+p.y*c);
    float n=fbm(q*3.8+float2(_Time.y*.32,-_Time.y*.46));
    float wisps=pow(saturate(1.-abs(fbm(q*7.+n*2.)-.5)*4.),3.);
    float edge=1.-smoothstep(.42,.98,r+(n-.5)*.35);
    float alpha=edge*smoothstep(.18,.7,n)*(.65+wisps*.55)*i.color.a;
    half3 lighting=lerp(half3(.42,.43,.43),half3(1.13,1.10,1.04),n)+wisps*.1;
    return half4(i.color.rgb*lighting,alpha);
   }
   ENDHLSL
  }
 }
}
