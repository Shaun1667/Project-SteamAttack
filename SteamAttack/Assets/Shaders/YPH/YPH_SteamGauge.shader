// 증기 메시의 로컬 높이와 잔량 비율로 보이는 영역을 정한 뒤, 움직이는 노이즈로 증기 질감을 만듭니다.
// FillView가 _Fill·색·높이 범위를 전달합니다. 이 셰이더는 압력을 계산하거나 실제 메시를 변형하지 않습니다.
// 아래 흐름은 정점의 위치 전달 → 높이별 투명도 계산 → 노이즈 계산 → 최종 색과 투명도 출력입니다.
Shader "YPH/SteamGauge"
{
    Properties
    {
        // 재질 기본값입니다. 실행 중 FillView가 이 렌더러의 색·잔량·메시 높이 범위를 덮어씁니다.
        _BaseColor ("Steam Color", Color) = (0.95, 0.95, 0.9, 0.7)
        _Fill ("Fill", Range(0, 1)) = 1
        _MinY ("Bottom Y", Float) = -1
        _MaxY ("Top Y", Float) = 1
        // 윗 경계의 흐림 두께는 로컬 좌표 단위입니다. 노이즈 스케일이 커지면 무늬가 더 촘촘해집니다.
        _EdgeSoftness ("Edge Softness", Range(0.001, 0.5)) = 0.08
        _NoiseScale ("Noise Scale", Float) = 3
        _ScrollSpeed ("Rise Speed", Float) = 0.3
        _NoiseStrength ("Noise Strength", Range(0, 1)) = 0.6
    }
    SubShader
    {
        // URP 투명 패스로 그립니다. 증기는 기본 큐 3000, 바깥 유리는 머티리얼 큐 3001로 나중에 그립니다.
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            // 알파 비율로 배경과 섞고 깊이는 기록하지 않아, 투명한 표면이 뒤쪽 물체를 완전히 가리지 않게 합니다.
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            // 원통 뒷면도 그려 앞·뒷면이 겹치는 두께감을 만듭니다. 실제 입체 유체를 계산하는 방식은 아닙니다.
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 머티리얼 속성을 하나의 상수 버퍼에 모아 URP의 머티리얼 데이터 배치 규칙에 맞춥니다.
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float _Fill;
                float _MinY;
                float _MaxY;
                float _EdgeSoftness;
                float _NoiseScale;
                float _ScrollSpeed;
                float _NoiseStrength;
            CBUFFER_END

            // positionOS는 모델 자체의 좌표, positionCS는 화면에 투영할 좌표입니다.
            // 높이와 노이즈는 로컬 좌표를 사용하므로 백팩을 이동·회전해도 무늬가 메시를 따라갑니다.
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            float Hash(float3 p)
            {
                // 같은 3차원 위치에는 항상 같은 0~1 값을 돌려주는 의사 난수입니다.
                // 별도 노이즈 텍스처 없이 공간의 격자마다 서로 다른 밝기를 만들 때 사용합니다.
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                // 위치가 속한 격자 칸과 칸 안의 비율을 구합니다. 비율을 부드럽게 변환한 뒤,
                // 여덟 꼭짓점의 해시값을 X·Y·Z 방향으로 차례로 섞어 칸 경계가 이어지는 무늬를 만듭니다.
                float3 cell = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(lerp(Hash(cell), Hash(cell + float3(1,0,0)), f.x),
                         lerp(Hash(cell + float3(0,1,0)), Hash(cell + float3(1,1,0)), f.x), f.y),
                    lerp(lerp(Hash(cell + float3(0,0,1)), Hash(cell + float3(1,0,1)), f.x),
                         lerp(Hash(cell + float3(0,1,1)), Hash(cell + float3(1,1,1)), f.x), f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                // 메시 형태는 그대로 두고 화면 위치를 계산합니다. 원래 로컬 위치도 픽셀 계산 단계로 넘깁니다.
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 1. 잔량 비율을 실제 높이로 바꿉니다. 흐림 두께가 0이 되지 않게 해 나눗셈을 보호합니다.
                // 가득 찬 높이를 천장보다 흐림 두께만큼 높여, 100%일 때 윗면까지 충분히 보이게 합니다.
                float softness = max(_EdgeSoftness, 0.001);
                float fillY = lerp(_MinY, _MaxY + softness, saturate(_Fill));
                // 2. 표면의 각 위치가 채움 높이보다 얼마나 아래인지로 가시성을 계산합니다.
                // 높이 위는 0(투명), 충분히 아래는 1이며 경계에서만 부드럽게 흐려집니다. 빈 상태는 전부 0입니다.
                float mask = saturate((fillY - input.positionOS.y) / softness);
                // 3. 노이즈를 읽는 Y좌표를 시간에 따라 낮추면 무늬 자체는 위로 이동하는 것처럼 보입니다.
                float3 p = input.positionOS * _NoiseScale + float3(0, -_Time.y * _ScrollSpeed, 0);
                // 큰 무늬와 두 배로 촘촘한 작은 무늬를 섞습니다. 가중치 합 1.5로 나누어 0~1 범위를 유지합니다.
                float noise = (Noise(p) + 0.5 * Noise(p * 2.0)) / 1.5;
                // 4. 기본 알파 × 높이 마스크 × 노이즈로 최종 투명도를 정합니다.
                // 노이즈 강도가 0이면 질감에 따른 투명도 차이는 사라지지만, 아래 RGB의 밝기 변화는 남습니다.
                float alpha = _BaseColor.a * mask * lerp(1.0, noise, _NoiseStrength);
                // 조명 계산 없이 지정색에 약한 밝기 변화를 더해 출력합니다. 실제 빛을 내는 조명은 생성하지 않습니다.
                return half4(_BaseColor.rgb * (0.85 + 0.3 * noise), alpha);
            }
            ENDHLSL
        }
    }
}
