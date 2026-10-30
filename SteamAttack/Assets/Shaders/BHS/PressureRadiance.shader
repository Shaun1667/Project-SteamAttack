Shader "HyunMuEffects/PressureRadiance"
{
 Properties { _Intensity("Intensity",Range(0,5))=1 }
 SubShader {
 Tags {"Queue"="Transparent+5" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
 Pass {
 Blend SrcAlpha One
 ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 CBUFFER_START(UnityPerMaterial)
 float _Intensity;
 CBUFFER_END
 V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
 float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
 float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
 half4 frag(V i):SV_Target{
 float2 p=i.uv*2-1;float n=noise(p*3.4+float2(_Time.y*1.8,-_Time.y*3));
 float fine=noise(p*9+n*2);float edge=1-smoothstep(.18,.95,length(p)+(n-.5)*.20);
 float density=edge*(.25+.65*n+.1*fine);
 return half4(i.color.rgb*_Intensity,density*i.color.a);
 }
 ENDHLSL
 } }
}
