Shader "YPH/PainterlyDriftingSky"
{
    Properties
    {
        _MainTex ("하늘 파노라마 (2:1)", 2D) = "grey" {}
        _Exposure ("밝기 배율", Range(0, 3)) = 1
        _Saturation ("채도", Range(0, 1)) = 1
        _Rotation ("시작 방향 (도)", Range(0, 360)) = 0
        _DriftSpeed ("하늘 이동 (도/초, 0이면 정지)", Range(-1, 1)) = 0.15
        _SeamWidth ("경계 연결 폭 (둘레 비율)", Range(0.01, 0.15)) = 0.06
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Exposure, _Saturation, _Rotation, _DriftSpeed, _SeamWidth;

            struct Attributes
            {
                float4 vertex : POSITION;
            };
            struct Varyings
            {
                float4 position : SV_POSITION;
                float3 direction : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.direction = input.vertex.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = normalize(input.direction);
                float longitude = 0.5 - atan2(direction.z, direction.x) / (2.0 * UNITY_PI);
                float latitude = 1.0 - acos(clamp(direction.y, -1.0, 1.0)) / UNITY_PI;
                float2 uv = float2(longitude + (_Rotation + _Time.y * _DriftSpeed) / 360.0, latitude);
                float2 dx = ddx(uv);
                float2 dy = ddy(uv);
                // 0도와 360도의 좌표 점프 때문에 경계 한 줄만 흐릿해지는 것을 막는다.
                dx.x -= round(dx.x);
                dy.x -= round(dy.x);
                uv.x = frac(uv.x);
                float halfTexel = _MainTex_TexelSize.x * 0.5;
                half3 color = tex2Dgrad(_MainTex, float2(clamp(uv.x, halfTexel, 1.0-halfTexel), uv.y), dx, dy).rgb;
                // 생성 이미지의 양끝이 달라도 실제 하늘 경계는 동일한 색으로 만난다.
                half3 opposite = tex2Dgrad(_MainTex,
                    float2(clamp(1.0-uv.x, halfTexel, 1.0-halfTexel), uv.y),
                    float2(-dx.x, dx.y), float2(-dy.x, dy.y)).rgb;
                float edgeWeight = 1.0 - smoothstep(0.0, _SeamWidth, min(uv.x, 1.0-uv.x));
                color = lerp(color, opposite, 0.5 * edgeWeight);
                // 천정은 평면 투영한 구름으로 이어서 극점의 빈 원과 방사형 늘어짐을 막는다.
                float angle = (_Rotation + _Time.y * _DriftSpeed) * UNITY_PI / 180.0;
                float2 capDirection = float2(cos(angle) * direction.x + sin(angle) * direction.z,
                    -sin(angle) * direction.x + cos(angle) * direction.z);
                float2 capUV = float2(0.42, 0.68) + capDirection * float2(0.28, 0.4);
                half3 cap = tex2D(_MainTex, capUV).rgb;
                color = lerp(color, cap, smoothstep(0.78, 0.95, direction.y));
                half3 bottom = tex2Dlod(_MainTex, float4(0.5, 0.0, 0.0, 0.0)).rgb;
                color = lerp(color, bottom, smoothstep(0.9, 0.99, -direction.y));
                // shortcut: 별과 구름이 함께 이동한다. 별 고정이 필요해지면 텍스처를 두 층으로 분리한다.
                color = lerp(dot(color, half3(0.2126, 0.7152, 0.0722)), color, _Saturation);
                return half4(color * _Exposure, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
