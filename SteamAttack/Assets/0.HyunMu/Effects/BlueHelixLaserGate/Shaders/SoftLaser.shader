Shader "BlueHelixEffects/SoftLaser"
{
 Properties
 {
  [HDR] _CoreColor ("Core", Color) = (.55,1.25,2.4,1)
  [HDR] _EdgeColor ("Halo", Color) = (.015,.16,1.5,1)
  _Intensity ("Intensity", Range(0,2)) = 1
 }
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
    half4 _CoreColor, _EdgeColor;
    float _Intensity;
   CBUFFER_END
   Varyings vert(Attributes v) { Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz); o.uv=v.uv; return o; }
   half4 frag(Varyings i):SV_Target
   {
    float radius=abs(i.uv.y*2.-1.);
    float halo=pow(saturate(1.-radius),2.);
    float core=exp(-radius*radius*95.);
    float shimmer=.94+.06*sin(i.uv.x*38.-_Time.y*9.);
    half3 color=lerp(_EdgeColor.rgb,_CoreColor.rgb,core);
    return half4(color,(halo*.72+core*.7)*shimmer*_Intensity);
   }
   ENDHLSL
  }
 }
}
