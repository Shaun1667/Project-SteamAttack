Shader "HyunMuEffects/ShearedSteamWake"
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
 struct V{float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
 CBUFFER_START(UnityPerMaterial)
 float _Density;
 CBUFFER_END
 V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
 float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
 float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
 half4 frag(V i):SV_Target {
 float2 p=i.uv*2-1;
 float shear=noise(float2(p.y*3,_Time.y*.8))-.5;
 float2 q=float2((p.x+shear*.25)*5,p.y*2-_Time.y*2.5);
 float n=noise(q)*.65+noise(q*2.7+4)*.35;
 float endFade=pow(saturate(1-p.y*p.y),.7);
 float width=(1-smoothstep(.22,.95,abs(p.x+shear*.22)));
 float strands=.18+.82*smoothstep(.16,.78,n);
 return half4(i.color.rgb*(.85+.15*n),endFade*width*strands*i.color.a*_Density);
 }
 ENDHLSL
 } }
}
