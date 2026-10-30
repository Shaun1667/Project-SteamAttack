Shader "YPH/SpearEnergy"
{
    Properties
    {
        [MainColor][HDR] _EnergyColor ("발광 색", Color) = (.04,.9,2.5,1)
        _Intensity ("밝기", Range(0,3)) = 0
        _BladeStart ("창날 시작 Z", Float) = .27
        _BladeEnd ("창날 끝 Z", Float) = .49
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            Offset -1,-1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _EnergyColor;
                float _Intensity, _BladeStart, _BladeEnd;
            CBUFFER_END
            struct Attributes { float3 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionOS = v.positionOS;
                o.positionWS = TransformObjectToWorld(v.positionOS + v.normalOS * .0006);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }
            half4 Frag(Varyings i) : SV_Target
            {
                float3 p = i.positionOS;
                float rim = pow(1-saturate(abs(dot(normalize(i.normalWS), GetWorldSpaceNormalizeViewDir(i.positionWS)))), 1.5);
                float blade = smoothstep(_BladeStart-.04, _BladeStart+.025, p.z);
                float phase = p.z*180 + sin(p.x*280 + _Time.y*7)*3 + sin(p.y*250-_Time.y*5)*2;
                float veins = pow(saturate(1-abs(sin(phase))), 8);
                float flow = .7 + .3*sin(p.z*34 - _Time.y*8);
                float bands = pow(saturate(cos(p.z*65-_Time.y*4)), 18);
                float shape = blade*(.4 + 1.2*rim + veins)*flow + (1-blade)*(.06*rim + .15*bands);
                float tip = smoothstep(_BladeEnd-.06, _BladeEnd, p.z);
                return half4(lerp(_EnergyColor.rgb, _EnergyColor.rgb*.45+1.8, tip*.45)*_Intensity*2.4, saturate(shape));
            }
            ENDHLSL
        }
    }
}
