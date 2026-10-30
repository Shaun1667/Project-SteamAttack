Shader "CyanChimneyEffects/JetFlame"
{
 Properties { _Intensity ("Intensity", Range(0,2)) = 1 }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha One
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
   CBUFFER_START(UnityPerMaterial)
    float _Intensity;
   CBUFFER_END
   Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
   half4 frag(Varyings i):SV_Target
   {
    float z=saturate(i.uv.y);
    float time=_Time.y;
    float ripple=(sin(z*28.-time*24.)*.035+sin(z*53.-time*37.)*.016)*z;
    float width=.78*pow(saturate(1.-z),.62)+.025;
    float r=abs((i.uv.x*2.-1.)+ripple)/width;
    float envelope=(1.-smoothstep(.45,1.,z))*smoothstep(0.,.018,z);
    float sheath=pow(saturate(1.-r),1.4)*envelope;
    float core=exp(-r*r*18.)*pow(saturate(1.-z),1.5);
    float flow=.87+.13*sin(z*48.-time*32.);
    float diamonds=.85+.15*pow(saturate(sin(z*30.-.4)),4.);
    half3 blue=lerp(half3(.035,.85,1.65),half3(.18,.15,.8),z*z);
    half3 color=lerp(blue,half3(1.65,2.1,2.4),saturate(core*1.8));
    float alpha=(sheath*.58+core*.4)*flow*diamonds*_Intensity;
    return half4(color,alpha);
   }
   ENDHLSL
  }
 }
}
