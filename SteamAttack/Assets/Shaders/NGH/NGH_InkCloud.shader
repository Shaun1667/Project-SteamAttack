// 수묵 구름 판 (NGH) — 먼 산 중턱에 걸친 구름
// - 판(Quad)이 항상 카메라를 보도록 정점에서 돌린다 (세로축 기준이라 기울지 않음)
// - 텍스처 없이 먹 번짐 무늬를 코드로 만들고, 가장자리는 부드럽게 사라진다. 무늬가 천천히 흐른다
// - 산과 만나는 곳은 깊이 차이로 부드럽게 흐려져 판 모서리가 칼처럼 잘리지 않는다 (Depth Texture 필요)
// - 판마다 무늬가 다르게 보이도록 오브젝트 위치로 무늬를 어긋나게 한다
Shader "NGH/InkCloud"
{
    Properties
    {
        _Color ("구름 색", Color) = (0.72, 0.75, 0.8, 1)
        _InkColor ("구름 아랫부분 먹 색 (그늘)", Color) = (0.36, 0.39, 0.45, 1)
        _Alpha ("최대 불투명도", Range(0, 1)) = 0.75
        _NoiseScale ("먹 번짐 무늬 크기 (판 안에서 반복 수)", Float) = 2.2
        _Cover ("덮는 정도 (작을수록 빽빽함)", Range(0, 1)) = 0.38
        _Softness ("무늬 경계 부드러움", Range(0.02, 0.6)) = 0.28
        _EdgeFade ("판 가장자리에서 사라지는 폭", Range(0.05, 0.5)) = 0.3
        _Speed ("흐르는 빠르기", Float) = 0.015
        _DepthFade ("산과 만나는 곳 흐림 거리 (m)", Float) = 25
    }

    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "InkCloud"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _InkColor;
                float _Alpha, _NoiseScale, _Cover, _Softness, _EdgeFade, _Speed, _DepthFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float seed : TEXCOORD2;
            };

            Varyings Vert(Attributes v)
            {
                Varyings o;
                float3 center = TransformObjectToWorld(float3(0, 0, 0));
                float3 scale = float3(length(UNITY_MATRIX_M._m00_m10_m20), length(UNITY_MATRIX_M._m01_m11_m21), 1);
                // 세로축 기준으로 카메라를 보게 돌림
                float3 toCam = _WorldSpaceCameraPos - center;
                toCam.y = 0;
                float3 fwd = normalize(toCam + float3(0, 0, 1e-4));
                float3 right = normalize(cross(float3(0, 1, 0), fwd));
                float3 posWS = center + right * v.positionOS.x * scale.x + float3(0, 1, 0) * v.positionOS.y * scale.y;
                o.positionCS = TransformWorldToHClip(posWS);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.uv = v.uv;
                o.seed = frac(dot(center.xz, float2(0.0137, 0.0071)));
                return o;
            }

            float CloudHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            float CloudNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(CloudHash(i), CloudHash(i + float2(1, 0)), u.x), lerp(CloudHash(i + float2(0, 1)), CloudHash(i + float2(1, 1)), u.x), u.y);
            }

            float CloudFbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 5; k++) { v += a * CloudNoise(p); p = p * 2.02 + 5.3; a *= 0.5; }
                return v;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;
                // 판 가장자리: 가로는 길게, 세로는 위아래로 납작하게 사라짐 (띠 모양 구름)
                float2 c = uv - 0.5;
                float edge = saturate(1.0 - length(c * float2(1.0, 1.6)) * 2.0);
                edge = smoothstep(0.0, _EdgeFade, edge);

                float2 p = uv * float2(_NoiseScale * 2.0, _NoiseScale) + i.seed * 37.0;
                p.x += _Time.y * _Speed * _NoiseScale;
                float n = CloudFbm(p);
                n += (CloudFbm(p * 2.7 + 9.1) - 0.5) * 0.25;                         // 잔 붓결
                float puff = smoothstep(_Cover, _Cover + _Softness, n);

                // 아래쪽과 짙은 곳은 먹 그늘, 위쪽은 밝은 구름
                float shade = saturate((1.0 - uv.y) * 0.8 + (n - _Cover) * 0.6);
                float3 col = lerp(_Color.rgb, _InkColor.rgb, shade * 0.7);

                // 산과 만나는 곳은 부드럽게
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneZ = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float myZ = i.screenPos.w;
                float soft = saturate((sceneZ - myZ) / max(_DepthFade, 0.01));

                return half4(col, puff * edge * soft * _Alpha);
            }
            ENDHLSL
        }
    }
}
