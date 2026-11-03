Shader "YPH/ColdMist" {
 Properties {
  _Density ("Density", Range(0,2)) = 0.65
  _NoiseVolume ("Shared density noise", 3D) = "white" {}
  _Speed ("Shared flow speed", Float) = 0.12
  _Turbulence ("Smoke detail (zero for snow)", Range(0,1)) = 1
 }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma target 3.5
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "ColdPortalField.hlsl"
   CBUFFER_START(UnityPerMaterial)
    float _Density,_Speed,_Turbulence;
   CBUFFER_END
   struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
   struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionOS:TEXCOORD1; half4 color:COLOR; };
   Varyings Vert(Attributes v) {
    Varyings o; o.positionCS=TransformObjectToHClip(v.positionOS.xyz);
    o.positionOS=v.positionOS.xyz; o.uv=v.uv; o.color=v.color; return o;
   }
   half4 Frag(Varyings i):SV_Target {
    float2 p=i.uv*2-1;
    float edge=pow(saturate(1-dot(p,p)),2);
    float density=lerp(1,smoothstep(0.24,0.70,PortalField(i.positionOS,_Time.y*_Speed)),_Turbulence);
    return half4(i.color.rgb,i.color.a*edge*density*_Density);
   }
   ENDHLSL
  }
 }
}
