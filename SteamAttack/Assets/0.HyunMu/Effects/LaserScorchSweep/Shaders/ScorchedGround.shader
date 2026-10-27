Shader "LaserScorchEffects/ScorchedGround"
{
 Properties { _Reveal("Reveal",Range(0,1))=0 _Heat("Heat",Range(0,1))=1 _Fade("Fade",Range(0,1))=1 _BlastProgress("Blast progress",Float)=-1 }
 SubShader
 {
  Tags { "Queue"="Transparent-20" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   Offset -1,-1
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
   struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float2 ground:TEXCOORD1; };
   CBUFFER_START(UnityPerMaterial)
    float _Reveal, _Heat, _Fade, _BlastProgress;
   CBUFFER_END
   V vert(A v){V o;float3 world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(world);o.uv=v.uv;o.ground=world.xz;return o;}
   float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
   float noise(float2 p){float2 i=floor(p),f=frac(p);f=f*f*(3.-2.*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);}
   half4 frag(V i):SV_Target
   {
    clip(_Reveal+.002-i.uv.x);
    float2 p=i.ground;
    float broad=noise(p*2.7);
    float grain=noise(p*34.);
    float fine=noise(p*77.+broad);
    float warp=noise(float2(i.uv.x*32.,2.7));
    float center=.5+(warp-.5)*.19;
    float across=abs(i.uv.y-center)*2.;
    float ragged=across+(noise(p*12.)-.5)*.29+(broad-.5)*.18;
    float feather=1.-smoothstep(.32,.98,ragged);
    float endFade=smoothstep(0.,.025,i.uv.x)*(1.-smoothstep(.975,1.,i.uv.x));
    float front=1.-smoothstep(_Reveal-.009,_Reveal+.002,i.uv.x);
    float charred=(1.-smoothstep(.08,.6,across))*(.65+.35*broad);
    float ash=pow(saturate(grain*.7+fine*.3),2.)*(.4+.6*smoothstep(.12,.78,across));
    half3 soot=lerp(half3(.018,.017,.015),half3(.085,.079,.067),broad);
    half3 color=lerp(soot,half3(.22,.205,.18),ash*.65);
    color*=1.-charred*.5;
    // Broken, subdued ember seams instead of a repeating polygon/cell outline.
    float seam=exp(-abs(i.uv.y-center-(noise(p*9.)-.5)*.055)*105.);
    float embers=seam*smoothstep(.42,.72,grain)+smoothstep(.79,.94,fine)*(1.-smoothstep(.1,.45,across))*.35;
    float hot=_Heat*(1.-saturate(_BlastProgress));
    float fresh=exp(-abs(i.uv.x-_Reveal)*200.)*(1.-step(.999,_Reveal));
    color+=half3(.65,.12,.018)*embers*hot*.6+half3(.55,.23,.08)*fresh*hot*seam;
    float alpha=feather*endFade*front*(.48+charred*.35+ash*.1)*_Fade;
    return half4(color,alpha);
   }
   ENDHLSL
  }
 }
}
