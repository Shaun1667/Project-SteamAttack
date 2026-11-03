Shader "YPH/SteamExhaust"
{
    Properties
    {
        [MainColor] _BaseColor ("연기 색", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            ZWrite Off
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 a=floor(p), f=frac(p);
                f=f*f*(3-2*f);
                return lerp(lerp(Hash(a),Hash(a+float2(1,0)),f.x),lerp(Hash(a+float2(0,1)),Hash(a+1),f.x),f.y);
            }
            float Billow(float2 p)
            {
                return Noise(p)*.57+Noise(p*2.03+17.1)*.28+Noise(p*4.07+8.4)*.15;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float2 p=i.uv*2-1;
                float2 flow=float2(_Time.y*.09,-_Time.y*.14);
                float2 warp=float2(Noise(p*2.4+flow),Noise(p*2.4-flow+23))-.5;
                float2 q=p+warp*.36;
                float cloud=Billow(q*2.2+flow);
                float silhouette=1-dot(q,q);
                // 가장자리까지 완만하게 사라지는 농도로 입자 사이의 둥근 경계를 감춥니다.
                float density=smoothstep(0,.78,silhouette)*lerp(.3,1,smoothstep(.16,.8,cloud));
                float edge=1-smoothstep(.32,1,dot(p,p));
                float lighting=saturate(.6+(cloud-Billow(q*2.2+flow+float2(-.25,.32)))*1.25);
                float3 tint=_BaseColor.rgb*i.color.rgb;
                float3 rgb=tint*lerp(.68,1.08,lighting)+lighting*.035;
                return half4(rgb,density*edge*i.color.a*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
