Shader "HyunMuEffects/TurbulentPressureSteam"
{
 Properties { _Density("Density",Range(0,2))=1 }
 SubShader {
 Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
 Pass {
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A{float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float3 world:TEXCOORD1;};
 CBUFFER_START(UnityPerMaterial)
 float _Density;
 CBUFFER_END
 V vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.uv=i.uv;o.color=i.color;return o;}
 float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
 float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
 float fbm(float2 p){return noise(p)*.55+noise(p*2.07+17)*.28+noise(p*4.13+4)*.17;}
 half4 frag(V i):SV_Target{
 float2 p=i.uv*2-1;float t=_Time.y;
 float2 q=p*3.5+float2(t*.8,-t*2.3);
 float2 warp=float2(fbm(q*.7+3),fbm(q*.7+11))-.5;
 float density=fbm(q+warp*3.5);
 float fine=noise(q*4+warp*5);
 float radius=length(p+warp*.23);
 float edge=1-smoothstep(.48,.98,radius+(density-.5)*.32);
 float alpha=edge*smoothstep(.17,.66,density)*_Density*i.color.a;
 float light=saturate(.5+p.y*.16-p.x*.12+(density-.5)*.8);
 half3 color=i.color.rgb*(.45+.6*light)+half3(.025,.035,.038)*fine;
 return half4(color,alpha);
 }
 ENDHLSL
 } }
}
