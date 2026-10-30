// 화면 전체 연출 후처리 (NGH) — 평소에는 아무것도 하지 않고, 코드가 전역 값을 올릴 때만 보인다
// - PC_Renderer 의 Full Screen Pass Renderer Feature(NGH_ScreenFX)가 이 재질로 그린다 (후처리 다음, 화면 맨 위)
// - _NGH_Frost (0~1): 현무 냉기에 맞았을 때 화면 가장자리에 성에가 끼고 안쪽으로 번진다 (HitScreenFrost 가 올림)
// - _NGH_RedKeep (0~1): 시간이 느려질 때 화면이 흑백이 되고 붉은색만 남는다 (SlowMotionRedKeep 가 올림)
// - _NGH_Shock (0~1 진행도) + _NGH_ShockCenter(화면 uv): 강한 타격 순간 그 자리에서 물결 고리가 퍼지며 화면이 출렁인다 (ScreenShockwave 가 올림)
// - _NGH_InkWipe (0~1): 씬 이동 때 먹물이 번져 화면을 덮었다가 걷힌다 (InkTransition 이 올림)
Shader "NGH/ScreenFX"
{
    Properties
    {
        [Header(Frost)]
        _FrostColor ("성에 색", Color) = (0.82, 0.92, 1, 1)
        _FrostScale ("성에 결정 크기", Float) = 7
        _FrostDistort ("성에 굴절 세기", Range(0, 0.05)) = 0.012
        _FrostReach ("가장 많이 번졌을 때 안쪽까지 오는 정도", Range(0, 1)) = 0.6

        [Header(Red Keep)]
        _RedHue ("남길 색 (붉은색)", Color) = (0.8, 0.1, 0.08, 1)
        _RedRange ("붉은색으로 볼 범위", Range(0.02, 0.4)) = 0.12
        _RedMinSat ("붉은색으로 볼 최소 채도", Range(0, 1)) = 0.3
        _GrayContrast ("흑백 대비", Range(0.5, 2)) = 1.15

        [Header(Shockwave)]
        _ShockWidth ("충격파 고리 두께", Range(0.01, 0.3)) = 0.09
        _ShockStrength ("충격파 출렁임 세기", Range(0, 0.1)) = 0.05
        _ShockRadius ("충격파가 퍼지는 최대 반경 (화면 높이 기준)", Range(0.2, 2)) = 0.9

        [Header(Ink Wipe)]
        _InkColor ("먹물 색", Color) = (0.04, 0.04, 0.05, 1)
        _InkScale ("먹 번짐 무늬 크기", Float) = 3.5
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "ScreenFX"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _FrostColor;
            float _FrostScale, _FrostDistort, _FrostReach;
            float4 _RedHue;
            float _RedRange, _RedMinSat, _GrayContrast;
            float _ShockWidth, _ShockStrength, _ShockRadius;
            float4 _InkColor;
            float _InkScale;
            float _NGH_Frost, _NGH_RedKeep, _NGH_Shock, _NGH_InkWipe;
            float4 _NGH_ShockCenter;

            float2 Hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float NghNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash22(i).x, b = Hash22(i + float2(1, 0)).x, c = Hash22(i + float2(0, 1)).x, d = Hash22(i + float2(1, 1)).x;
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float NghFbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { v += a * NghNoise(p); p = p * 2.07 + 11.3; a *= 0.5; }
                return v;
            }

            // 보로노이: x = 가장 가까운 점까지 거리, y = 경계까지 거리 (결정 모양)
            float2 Voronoi(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = Hash22(i + g);
                    float d = length(g + o - f);
                    if (d < d1) { d2 = d1; d1 = d; }
                    else if (d < d2) d2 = d;
                }
                return float2(d1, d2 - d1);
            }

            float3 NghRgbToHsv(float3 c)
            {
                float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
                float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));
                float d = q.x - min(q.w, q.y);
                float e = 1.0e-10;
                return float3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float frost = saturate(_NGH_Frost);
                float redKeep = saturate(_NGH_RedKeep);
                float shock = _NGH_Shock;
                float inkWipe = saturate(_NGH_InkWipe);
                bool shockOn = shock > 0.0 && shock < 1.0;
                if (frost <= 0.001 && redKeep <= 0.001 && !shockOn && inkWipe <= 0.001)
                    return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 sp = float2(uv.x * aspect, uv.y);

                // ---------- 성에 ----------
                float frostMask = 0.0;
                float2 distortUv = uv;

                // ---------- 충격파 ----------
                if (shockOn)
                {
                    float2 d = (uv - _NGH_ShockCenter.xy) * float2(aspect, 1.0);
                    float r = length(d);
                    float front = shock * _ShockRadius;
                    float ring = 1.0 - saturate(abs(r - front) / _ShockWidth);
                    ring = ring * ring * (1.0 - shock);                           // 퍼질수록 약해짐
                    float2 dir = r > 0.0001 ? d / r : float2(0, 0);
                    distortUv -= dir / float2(aspect, 1.0) * ring * _ShockStrength;
                }
                float crystal = 0.0;
                if (frost > 0.001)
                {
                    float2 c = (uv - 0.5) * float2(aspect, 1.0);
                    float edge = length(c) / length(float2(aspect, 1.0) * 0.5);   // 가운데 0 ~ 모서리 1
                    float2 v = Voronoi(sp * _FrostScale);
                    float2 v2 = Voronoi(sp * _FrostScale * 2.7 + 3.1);
                    float grow = 1.0 - frost * _FrostReach;                       // 성에가 닿는 경계 (작을수록 안쪽까지)
                    float jag = (v.x - 0.5) * 0.18 + (v2.x - 0.5) * 0.08;          // 경계가 결정 모양으로 들쭉날쭉
                    frostMask = smoothstep(grow - 0.05, grow + 0.12, edge + jag) * frost;
                    crystal = saturate(1.0 - v.y * 14.0) * 0.5 + saturate(1.0 - v2.y * 18.0) * 0.5;   // 결정 경계선
                    distortUv += (Hash22(floor(sp * _FrostScale * 2.7)) - 0.5) * _FrostDistort * frostMask;
                }

                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, distortUv);

                // ---------- 흑백 + 붉은색만 ----------
                if (redKeep > 0.001)
                {
                    float3 hsv = NghRgbToHsv(saturate(col.rgb));
                    float3 target = NghRgbToHsv(_RedHue.rgb);
                    float hueDist = abs(hsv.x - target.x);
                    hueDist = min(hueDist, 1.0 - hueDist);
                    float keep = (1.0 - smoothstep(_RedRange * 0.6, _RedRange, hueDist)) * smoothstep(_RedMinSat * 0.6, _RedMinSat, hsv.y);
                    float g = dot(col.rgb, float3(0.299, 0.587, 0.114));
                    g = saturate((g - 0.5) * _GrayContrast + 0.5);
                    float3 gray = g.xxx;
                    col.rgb = lerp(col.rgb, lerp(gray, col.rgb * float3(1.1, 0.95, 0.95), keep), redKeep);
                }

                if (frost > 0.001)
                {
                    float3 iced = lerp(col.rgb, _FrostColor.rgb, 0.5) + crystal * 0.18 * _FrostColor.rgb;
                    col.rgb = lerp(col.rgb, iced, frostMask);
                }

                // ---------- 먹 번짐 전환 ----------
                if (inkWipe > 0.001)
                {
                    float2 c2 = (uv - 0.5) * float2(aspect, 1.0);
                    float edge2 = length(c2) / length(float2(aspect, 1.0) * 0.5);   // 가운데 0 ~ 모서리 1
                    float n = NghFbm(sp * _InkScale);
                    // 가장자리와 얼룩진 곳부터 먹이 번져 들어와 1이면 화면 전체가 덮임
                    float field = edge2 * 0.55 + n * 0.75;                       // 0 ~ 약 1.3
                    float th = 1.2 - inkWipe * 1.15;                               // 이 값보다 field 가 크면 먹
                    float inkMask = inkWipe >= 0.999 ? 1.0 : smoothstep(th - 0.03, th + 0.03, field);
                    float bleed = smoothstep(th - 0.12, th, field) * (1.0 - inkMask);   // 번지는 가장자리 옅은 먹
                    col.rgb = lerp(col.rgb, _InkColor.rgb, saturate(inkMask + bleed * 0.35));
                }
                return col;
            }
            ENDHLSL
        }
    }
}
