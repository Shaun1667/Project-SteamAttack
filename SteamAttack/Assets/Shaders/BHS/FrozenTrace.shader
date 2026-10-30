Shader "HyunMuFrost/FrozenGround"
{
 Properties { _Reveal("Reveal",Range(0,1))=1 _Heat("Cold energy",Range(0,1))=1 _Fade("Fade",Range(0,1))=1 _Charge("Pressure",Range(0,1))=0 }
 SubShader {
 Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
 Pass {
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Off Offset -1,-1
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
 struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;};
 CBUFFER_START(UnityPerMaterial)
 float _Reveal,_Heat,_Fade,_Charge;
 CBUFFER_END
 V vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.uv=i.uv;return o;}
 float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
 float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
 float cracks(float2 p){float2 b=floor(p),f=frac(p);float d1=10,d2=10;for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){float2 o=float2(x,y);float2 q=o+float2(hash(b+o),hash(b+o+17))*.8+.1-f;float d=dot(q,q);if(d<d1){d2=d1;d1=d;}else d2=min(d2,d);}return 1-smoothstep(.015,.095,d2-d1);}
 half4 frag(V i):SV_Target {
 float2 p=i.world.xz;float n=noise(p*3.5),fine=noise(p*48);
 float edge=1-smoothstep(.25,.5,abs(i.uv.y-.5)+(n-.5)*.16);
 float front=1-smoothstep(_Reveal-.008,_Reveal+.008,i.uv.x);
 float ends=smoothstep(0,.025,i.uv.x)*(1-smoothstep(.975,1,i.uv.x));
 float fracture=cracks(p*2.7)*(.45+.55*n);
 float frost=pow(saturate(fine),3)*.7+noise(p*22)*.15;
 float pulse=.85+.15*sin(_Time.y*(4+_Charge*7)+n*9);
 half3 base=lerp(half3(.035,.13,.24),half3(.38,.66,.78),n*.65+frost);
 half3 glow=half3(.15,1.15,1.9)*fracture*_Heat*(.45+.8*_Charge)*pulse;
 return half4(base+glow,edge*front*ends*_Fade*(.72+.20*n));
 }
 ENDHLSL
 } }
}
