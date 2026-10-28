Shader "HyunMuFrost/Crystal"
{
 Properties {
 _Tint("Ice depth tint",Color)=(.36,.65,.78,1)
 _Opacity("Ice density",Range(0,1))=.76
 _Frost("Surface frost",Range(0,1))=.34
 }
 SubShader {
 Tags {"Queue"="Transparent-5" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
 Pass {
 Tags {"LightMode"="UniversalForward"}
 Blend SrcAlpha OneMinusSrcAlpha
 ZWrite Off Cull Back
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;half4 color:COLOR;};
 struct V {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;half4 color:COLOR;};
 CBUFFER_START(UnityPerMaterial)
 half4 _Tint;
 float _Opacity,_Frost;
 CBUFFER_END
 V vert(A i) {V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normalOS);o.uv=i.uv;o.color=i.color;return o;}
 float hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
 float noise(float3 p){float3 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(lerp(hash(a),hash(a+float3(1,0,0)),f.x),lerp(hash(a+float3(0,1,0)),hash(a+float3(1,1,0)),f.x),f.y),lerp(lerp(hash(a+float3(0,0,1)),hash(a+float3(1,0,1)),f.x),lerp(hash(a+float3(0,1,1)),hash(a+1),f.x),f.y),f.z);}
 half4 frag(V i):SV_Target {
 float3 n=normalize(i.normal),v=SafeNormalize(_WorldSpaceCameraPos-i.world);
 // Fine striation and branching frost replace the old triangle UV outlines.
 float3 q=float3(i.uv.x*9,i.uv.y*3,i.uv.x*2+i.uv.y);
 float coarse=noise(q),fine=noise(q*4.1);
 float warp=noise(q*1.7)*.35;
 float veinSignal=noise(float3(i.uv.x*28+warp,i.uv.y*5,i.uv.y*1.8))-.49;
 float aa=max(.009,fwidth(veinSignal)*.8);
 float veins=1-smoothstep(.008,.008+aa,abs(veinSignal));
 veins*=smoothstep(.25,.65,noise(q*.7));
 float frost=smoothstep(.55,.88,coarse*.65+fine*.35)*_Frost;
 float baseFrost=pow(saturate(1-i.uv.y),3)*.14;
 float facing=saturate(dot(n,v)),rim=pow(1-facing,3);
 Light mainLight=GetMainLight();
 float3 lightDir=mainLight.direction;
 float lit=saturate(dot(n,lightDir));
 float transmitted=pow(saturate(dot(v,-lightDir+n*.35)),3)*.24;
 float spec=pow(saturate(dot(n,SafeNormalize(lightDir+v))),110)*.9;
 float broad=pow(saturate(dot(n,SafeNormalize(float3(-.7,.7,.45)+v))),24)*.14;
 half3 deep=_Tint.rgb*half3(.65,.82,.9);
 half3 ice=lerp(deep,half3(.68,.82,.88),.22+lit*.27+coarse*.13);
 ice+=half3(.18,.27,.31)*transmitted;
 ice=lerp(ice,half3(.82,.9,.93),saturate(frost+baseFrost+veins*.19));
 ice+=half3(.68,.82,.9)*(rim*.38+broad)+mainLight.color*spec;
 float alpha=saturate(_Opacity*.77+rim*.25+frost*.3+veins*.12+baseFrost);
 float baseFade=smoothstep(0,.025,i.uv.y);
 return half4(ice*i.color.rgb,alpha*i.color.a*baseFade);
 }
 ENDHLSL
 }
 }
}

