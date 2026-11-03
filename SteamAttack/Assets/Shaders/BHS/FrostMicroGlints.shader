Shader "HyunMuFrost/MicroGlints"
{
    Properties { [HDR] _Tint("Cold radiance", Color) = (0.65,1.4,2,1) }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            CBUFFER_END
            V vert(A i) { V o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color*_Tint; return o; }
            half4 frag(V i):SV_Target
            {
                float2 p=i.uv*2-1;
                float radial=dot(p,p);
                float core=exp(-radial*12);
                float glint=exp(-abs(p.x)*32)*exp(-abs(p.y)*5);
                float mask=saturate(core+glint*.22)*(1-smoothstep(.5,1,radial));
                return half4(i.color.rgb,mask*i.color.a);
            }
            ENDHLSL
        }
    }
}
