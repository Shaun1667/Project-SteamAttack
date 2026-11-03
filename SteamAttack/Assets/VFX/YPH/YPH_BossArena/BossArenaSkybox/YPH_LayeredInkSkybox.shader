Shader "YPH/BossArena/LayeredInkSkybox"
{
    Properties
    {
        _MainTex ("Aurora and Frozen Ground", 2D) = "black" {}
        _InkTex ("Moving Ink Clouds", 2D) = "black" {}
        _Exposure ("Exposure", Range(0, 3)) = 1
        _Rotation ("Environment Rotation", Range(0, 360)) = 0
        _HorizonElevation ("Horizon Elevation (Degrees)", Range(-35, 35)) = 0
        _SeamWidth ("Seam Blend", Range(0.01, 0.2)) = 0.02
        _InkSpeed ("Ink Drift (Degrees Per Second)", Range(-10, 10)) = 1.8
        _InkOpacity ("Ink Opacity", Range(0, 4)) = 0.8
        _InkWaver ("Ink Waver", Range(0, 0.02)) = 0.004
        _InkTint ("Ink Tint", Color) = (0.25, 0.34, 0.41, 1)
        _MoonDirection ("Moon Direction", Vector) = (-0.118, 0.559, 0.821, 0)
        [HDR] _MoonColor ("Moonlight", Color) = (1.4, 1.7, 2.0, 1)
        _MoonRadius ("Moon Radius (Degrees)", Range(0.2, 3)) = 1.1
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D(_InkTex); SAMPLER(sampler_InkTex);
            CBUFFER_START(UnityPerMaterial)
                float _Exposure, _Rotation, _HorizonElevation, _SeamWidth, _InkSpeed, _InkOpacity, _InkWaver;
                float4 _InkTint, _MoonDirection, _MoonColor;
                float _MoonRadius;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 ray : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.ray = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.ray);
                float longitude = atan2(dir.x, dir.z) * (0.5 / PI) + 0.5 + _Rotation / 360.0;
                float2 uv = float2(frac(longitude), saturate(asin(clamp(dir.y, -1.0, 1.0)) / PI + 0.5 - _HorizonElevation / 180.0));
                float2 dx = ddx(uv), dy = ddy(uv);
                dx.x -= round(dx.x); dy.x -= round(dy.x);
                float3 groundSky = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, uv, dx, dy).rgb;
                float seam = 0.5 * (1.0 - smoothstep(0.0, _SeamWidth, min(uv.x, 1.0 - uv.x)));
                if (seam > 0.0)
                {
                    float3 opposite = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, float2(1.0 - uv.x, uv.y), float2(-dx.x, dx.y), float2(-dy.x, dy.y)).rgb;
                    groundSky = lerp(groundSky, opposite, seam);
                }

                float drift = _Time.y * _InkSpeed / 360.0;
                float phase = _Time.y * _InkSpeed * 0.18;
                float2 inkUV = float2(frac(longitude + drift + _InkWaver * sin(phase + uv.y * 11.0)),
                                      saturate(lerp(uv.y, 1.0, 0.28) + _InkWaver * 0.55 * sin(phase * 0.8 + uv.x * (2.0 * PI))));
                float2 ix = ddx(inkUV), iy = ddy(inkUV);
                ix.x -= round(ix.x); iy.x -= round(iy.x);
                float4 ink = 0;
                if (uv.y > 0.465)
                {
                    ink = SAMPLE_TEXTURE2D_GRAD(_InkTex, sampler_InkTex, inkUV, ix, iy);
                    float inkSeam = 0.5 * (1.0 - smoothstep(0.0, _SeamWidth, min(inkUV.x, 1.0 - inkUV.x)));
                    if (inkSeam > 0.0)
                    {
                        float4 oppositeInk = SAMPLE_TEXTURE2D_GRAD(_InkTex, sampler_InkTex, float2(1.0 - inkUV.x, inkUV.y), float2(-ix.x, ix.y), float2(-iy.x, iy.y));
                        ink = lerp(ink, oppositeInk, inkSeam);
                    }
                }

                float angle = _Rotation * PI / 180.0;
                float cs = cos(angle), sn = sin(angle);
                float2 capDir = float2(cs * dir.x + sn * dir.z, -sn * dir.x + cs * dir.z);
                float poleBlend = smoothstep(0.72, 0.94, abs(dir.y));
                // Match the 2:1 panorama's pixel aspect to avoid stretching at the poles.
                float2 capUV = float2(0.5 + capDir.x * 0.16, saturate((dir.y > 0.0 ? 0.86 : 0.14) + capDir.y * 0.32 - _HorizonElevation / 180.0));
                float2 capDx = ddx(capUV), capDy = ddy(capUV);
                float2 inkCapUV = float2(frac(capUV.x + drift + _InkWaver * sin(phase + capUV.y * 11.0)), lerp(capUV.y, 1.0, 0.28));
                float2 icx = ddx(inkCapUV), icy = ddy(inkCapUV);
                icx.x -= round(icx.x); icy.x -= round(icy.x);
                if (poleBlend > 0.0)
                {
                    float3 cap = SAMPLE_TEXTURE2D_GRAD(_MainTex, sampler_MainTex, capUV, capDx, capDy).rgb;
                    groundSky = lerp(groundSky, cap, poleBlend);
                    if (dir.y > 0.0)
                    {
                        float4 inkCap = SAMPLE_TEXTURE2D_GRAD(_InkTex, sampler_InkTex, inkCapUV, icx, icy);
                        float capSeam = 0.5 * (1.0 - smoothstep(0.0, _SeamWidth, min(inkCapUV.x, 1.0 - inkCapUV.x)));
                        if (capSeam > 0.0)
                        {
                            float4 oppositeCap = SAMPLE_TEXTURE2D_GRAD(_InkTex, sampler_InkTex, float2(1.0 - inkCapUV.x, inkCapUV.y), float2(-icx.x, icx.y), float2(-icy.x, icy.y));
                            inkCap = lerp(inkCap, oppositeCap, capSeam);
                        }
                        ink = lerp(ink, inkCap, poleBlend);
                    }
                }

                float3 moonDirection = normalize(_MoonDirection.xyz);
                float moonDot = dot(dir, moonDirection);
                float moonRadius = _MoonRadius * PI / 180.0;
                float moon = smoothstep(cos(moonRadius * 1.08), cos(moonRadius * 0.92), moonDot);
                float halo = exp2((moonDot - 1.0) * 1400.0) * 0.24;
                float3 moonRight = normalize(cross(float3(0, 1, 0), moonDirection) + float3(0.00001, 0, 0));
                float3 moonUp = cross(moonDirection, moonRight);
                float2 moonUV = float2(dot(dir, moonRight), dot(dir, moonUp)) / max(sin(moonRadius), 0.001);
                float maria = (1.0 - smoothstep(0.12, 0.40, length(moonUV - float2(-0.25, 0.18)))) * 0.16;
                maria += (1.0 - smoothstep(0.08, 0.30, length(moonUV - float2(0.32, -0.32)))) * 0.12;
                float moonSurface = 0.94 - maria + 0.03 * sin(moonUV.x * 18.0) * sin(moonUV.y * 19.0);

                float inkShade = saturate(0.1 + 1.5 * sqrt(saturate(dot(ink.rgb, float3(0.299, 0.587, 0.114)))));
                // Let each brush edge feather into the sky instead of ending on a fixed latitude.
                float skyMask = smoothstep(0.48, 0.62, uv.y + (ink.a - 0.5) * 0.035);
                float3 inkColor = _InkTint.rgb * inkShade;
                float cloudOpacity = 1.0 - pow(max(1.0 - saturate(ink.a), 0.00001), _InkOpacity * 1.15);
                float cloudCover = cloudOpacity * skyMask;
                float3 sky = lerp(groundSky, inkColor, cloudCover);
                sky += _MoonColor.rgb * (moon * moonSurface + halo) * (1.0 - 0.65 * cloudCover);
                return half4(sky * _Exposure, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
