Shader "MadLab/CoreExplosionParticle"
{
    Properties
    {
        _Style ("0 Glow, 1 Fire, 2 Smoke", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination Blend", Float) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                float _Style;
                float _DstBlend;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }
            float hash21(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3.-2.*f);
                return lerp(lerp(hash21(i),hash21(i+float2(1,0)),f.x),lerp(hash21(i+float2(0,1)),hash21(i+1),f.x),f.y);
            }
            float billow(float2 p)
            {
                return noise(p)*.54+noise(p*2.03+7.1)*.27+noise(p*4.09+19.4)*.13+noise(p*8.13)*.06;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 p=i.uv*2.-1.;
                float radius=length(p);
                if(_Style<.5) return half4(i.color.rgb,pow(saturate(1.-radius),2.)*i.color.a);
                float n=billow(p*3.2+float2(_Time.y*.9,-_Time.y*1.5));
                float edge=1.-smoothstep(.52,.98,radius+(n-.5)*.43);
                float density=smoothstep(.12,.62,n)*edge;
                if(_Style>1.5)
                {
                    float lighting=.32+.88*n+.15*saturate(p.y);
                    return half4(i.color.rgb*lighting,density*i.color.a);
                }
                float heat=saturate(n*1.75-radius*.3);
                half3 fire=lerp(half3(.012,.12,.18),half3(.025,.65,.9),smoothstep(.15,.55,heat));
                fire=lerp(fire,half3(.22,1.05,1.4),smoothstep(.5,.8,heat));
                fire=lerp(fire,half3(.72,1.45,1.7),smoothstep(.83,1.,heat));
                return half4(fire*i.color.rgb,density*i.color.a);
            }
            ENDHLSL
        }
    }
}
