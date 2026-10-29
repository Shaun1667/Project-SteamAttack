Shader "HyunMuBite/GroundWarning"
{
 Properties { _Tint("Warning tint",Color)=(.16,.65,.95,1) _Progress("Charge",Range(0,1))=0 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha Offset -1,-1
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A { float4 p:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
   struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
   CBUFFER_START(UnityPerMaterial)
   half4 _Tint;
   float _Progress;
   CBUFFER_END
   V vert(A a) {V o;o.p=TransformObjectToHClip(a.p.xyz);o.uv=a.uv;o.color=a.color;return o;}
   half4 frag(V i):SV_Target
   {
    float x=abs(i.uv.x-.5)*2,y=i.uv.y;
    float width=lerp(1,.12,smoothstep(.84,1,y));
    float inside=1-smoothstep(width-.035,width,x);
    float edge=smoothstep(width-.11,width-.04,x);
    float fill=1-smoothstep(_Progress-.015,_Progress+.015,y);
    float endEdge=1-smoothstep(.01,.04,y);
    float head=exp(-abs(y-_Progress)*80);
    half3 c=lerp(_Tint.rgb,half3(.68,.94,1),saturate(edge*.65+head*.45+fill*.15));
    float alpha=inside*(.07+fill*.22+edge*.52+endEdge*.3+head*.12)*i.color.a*_Tint.a;
    return half4(c,alpha);
   }
   ENDHLSL
  }
 }
}
