Shader "PressureSteamEffects/SoftSteam"
{
 Properties { _Tint("Tint",Color)=(1,1,1,1) }
 SubShader {
 Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
 Pass {
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A {float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR;};
 struct V {float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR;};
 CBUFFER_START(UnityPerMaterial)
 half4 _Tint;
 CBUFFER_END
 V vert(A i) {V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color*_Tint;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
 half4 frag(V i):SV_Target {
 float2 p=i.uv*2-1;
 float n=noise(p*3.1+_Time.y*.4)*.6+noise(p*8-_Time.y*.25)*.3+noise(p*19)*.1;
 float edge=1-smoothstep(.35,.95,length(p)+(n-.5)*.25);
 float alpha=edge*(.4+.6*n)*i.color.a;
 return half4(i.color.rgb*(.66+.4*n),alpha);
 }
 ENDHLSL
 } }
}
