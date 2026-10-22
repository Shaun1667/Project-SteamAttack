Shader "YPH/UI/AimFocusLines"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _InkTex ("Ink Border", 2D) = "black" {}
        _InkColor ("Ink Color", Color) = (0.015,0.018,0.022,0.95)
        _ReticleColor ("Reticle Color", Color) = (1,1,1,0.9)
        _AimBlend ("Aim Blend", Range(0,1)) = 0
        _Reveal ("Reveal Progress", Range(0,1)) = 0
        [Enum(ThreePoints,0,Fade,1)] _RevealMode ("Reveal Mode", Float) = 0
        _Focus ("Focus", Vector) = (0.5,0.5,0,0)
        _ReticleRadius ("Reticle Radius at 1080p", Float) = 18
        _ReticleThickness ("Reticle Thickness at 1080p", Float) = 1.4
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            TEXTURE2D(_InkTex);
            SAMPLER(sampler_InkTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _InkColor, _ReticleColor;
                float4 _Focus;
                float _AimBlend, _Reveal, _RevealMode, _ReticleRadius, _ReticleThickness;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 ellipse = (input.uv - 0.5) * 2;
                float radius = length(ellipse);
                float angle = frac(atan2(ellipse.y, ellipse.x) / TWO_PI + 1);
                // 먹의 모양은 원본 질감을 그대로 쓰고, 세 붓끝의 진행 위치만 가립니다.
                float start = angle > 0.375 && angle <= 0.875 ? 0.875 : (angle > 0.125 && angle <= 0.375 ? 0.375 : 0.125);
                float arc = start == 0.875 ? 0.5 : 0.25;
                float along = frac(start - angle) / arc;
                float tip = along + sin(radius * 137 + sin(radius * 79) * 2) * 0.008;
                float reveal = _Reveal >= 1 ? 1 : (1 - smoothstep(_Reveal - 0.012, _Reveal + 0.012, tip)) * step(0.0001, _Reveal);
                // 페이드 모드에서는 위치별 가림 없이 원본 먹선 전체의 불투명도를 함께 올립니다.
                reveal = _RevealMode > 0.5 ? saturate(_Reveal) : reveal;
                float ink = SAMPLE_TEXTURE2D(_InkTex, sampler_InkTex, input.uv).a * reveal * _InkColor.a;

                // 1080p 기준으로 원과 짧은 십자를 그려 화면 비율이 달라져도 원형을 유지합니다.
                float2 pixel = (input.uv - _Focus.xy) * float2(_ScreenParams.x / _ScreenParams.y, 1) * 1080;
                float aa = max(fwidth(length(pixel)), 0.5);
                float ring = 1 - smoothstep(_ReticleThickness * 0.5, _ReticleThickness * 0.5 + aa, abs(length(pixel) - _ReticleRadius));
                float2 d = abs(pixel);
                float ticks = (1 - smoothstep(_ReticleThickness * 0.5, _ReticleThickness * 0.5 + aa, min(d.x,d.y)))
                    * smoothstep(_ReticleRadius + 2, _ReticleRadius + 3, max(d.x,d.y))
                    * (1 - smoothstep(_ReticleRadius + 9, _ReticleRadius + 10, max(d.x,d.y)));
                float dot = 1 - smoothstep(1, 1 + aa, length(pixel));
                float reticle = max(ring, max(ticks, dot)) * _ReticleColor.a;
                float alpha = max(ink, reticle) * _AimBlend * input.color.a;
                return half4(lerp(_InkColor.rgb, _ReticleColor.rgb, saturate(reticle / max(ink + reticle, 0.0001))), alpha);
            }
            ENDHLSL
        }
    }
}
