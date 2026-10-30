Shader "CyanSteamEffects/CompressedSteam"
{
 Properties { _Mode("Core 0 / Surface 1",Float)=0 }
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
 float _Mode;
 CBUFFER_END
 V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;o.color=i.color;return o;}
 float hash(float2 p){p=frac(p*float2(123.34,456.21));p+=dot(p,p+45.32);return frac(p.x*p.y);}
 float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
 float fbm(float2 p){return noise(p)*.56+noise(p*2.13+13)*.28+noise(p*4.71+7)*.16;}
 half4 frag(V i):SV_Target{
 float2 p=i.uv*2-1;float r=length(p);float t=_Time.y;
 float twist=t*1.4+r*1.2;float cs=cos(twist),sn=sin(twist);
 float2 q=float2(cs*p.x-sn*p.y,sn*p.x+cs*p.y);
 float n=fbm(q*4.2+float2(t*.6,-t*.3));
 float fine=fbm(q*float2(13,5)+n*3);
 float edge=1-smoothstep(.77,.98,r+(n-.5)*.18);
 float sphere=sqrt(saturate(1-r*r));
 float shade=saturate(.22+p.y*.27-p.x*.18+sphere*.35);
 float density=edge*saturate(1.35-r*.25);
 half3 color=i.color.rgb*(.35+shade*.8)*( .6+smoothstep(.25,.75,n)*.7);
 if(_Mode>.5){
 float strands=pow(saturate(1-abs(fine-.53)*13),2);
 density=edge*strands*smoothstep(.42,.7,n)*.38;
 color=i.color.rgb*(.5+.35*fine);
 }else{
 float cracks=fbm(q*7+float2(n*2,t*.25));
 float fissure=pow(saturate(1-abs(cracks-.51)*45),2)*smoothstep(.38,.68,fine);
 float heat=fissure*(.65+.35*noise(q*12+t));
 color+=half3(.22,.9,1.15)*heat;
 color+=half3(.015,.055,.065)*exp(-r*r*5)*n;
 }
 return half4(color,density*i.color.a);
 }
 ENDHLSL
 } }
}
