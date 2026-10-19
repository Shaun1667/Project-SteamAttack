// 먹물 화면 전환 (NMJ.InkTransition 전용 UI 셰이더). 화면 전체를 덮는 RawImage 하나에 쓴다.
// _Progress 0 = 맑은 화면, 1 = 먹으로 다 덮임. 걷어 낼 때는 1 → 0 으로 되돌린다.
// _Style
//   0 먹 번짐   : 먹방울이 떨어져 한지에 번지듯 퍼진다. 번진 끝에는 먹이 고인 짙은 테가 생기고, 그 밖으로 옅은 물기가 번진다.
//   1 붓질      : 굵은 붓 세 획이 번갈아 화면을 가로지른다. 획 가장자리는 붓털이 갈라지고, 획 끝으로 갈수록 붓이 말라 비백이 생긴다.
//   2 먹 흘러내림: 위에서 먹이 배어 내려오고, 굵기가 다른 먹줄기가 굽이치며 먼저 흘러내린다.
// 모양은 모두 셰이더 안의 잡음으로 만든다 (텍스처 없음). _Seed 를 바꾸면 매번 모양이 달라진다.
// 각 모양은 (덮임 정도 field, 먹 농도 density) 를 내고, 색은 공통으로 옅은 먹(_InkWash) ~ 짙은 먹(_InkDark) 사이에서 정한다.
Shader "NMJ/UI/InkTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Progress ("Progress", Range(0, 1)) = 0
        _Style ("Style (0 bleed, 1 brush, 2 drip)", Float) = 0
        _Seed ("Seed", Float) = 0
        _Aspect ("Aspect (w/h)", Float) = 1.7778

        _InkDark ("Ink Dark", Color) = (0.018, 0.018, 0.022, 1)
        _InkWash ("Ink Wash (옅은 먹)", Color) = (0.20, 0.195, 0.19, 1)
        _Halo ("Wet Halo Alpha", Range(0, 1)) = 0.35

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float4 color  : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                fixed4 color  : COLOR;
                float2 uv     : TEXCOORD0;
            };

            fixed4 _Color;
            float _Progress, _Style, _Seed, _Aspect, _Halo;
            fixed4 _InkDark, _InkWash;

            // 먹이 얼마나 짙게 앉았나 (0 = 막 배어 든 옅은 물먹, 1 = 짙은 먹). 지금은 먹 흘러내림만 쓴다
            static float gOpacity = 1.0;

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord;
                o.color = v.color * _Color;
                return o;
            }

            // ───────── 잡음 ─────────

            float hash11(float n) { return frac(sin(n * 127.1 + _Seed * 3.17) * 43758.5453); }
            float hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7)) + _Seed * 1.31) * 43758.5453); }

            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i), b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1)), d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                for (int k = 0; k < 4; k++) { s += a * vnoise(p); p = p * 2.03 + 17.1; a *= 0.5; }
                return s;   // 대략 0 ~ 0.94, 평균 0.47
            }

            // ───────── 0. 먹 번짐 ─────────
            // field: 먹 안쪽 + / 바깥 - (화면 높이 = 1 기준 거리). density: 0 옅음 ~ 1 짙음
            float BleedField(float2 p, float t, out float density)
            {
                // 둘레가 고르지 않게 좌표를 잡음으로 비튼다 (큰 굽이 + 잔 굽이)
                float2 warp = float2(fbm(p * 2.2 + 3.0), fbm(p * 2.2 + 11.0)) - 0.47;
                float2 warp2 = float2(vnoise(p * 9.0 + 5.0), vnoise(p * 9.0 + 23.0)) - 0.5;
                float2 q = p + warp * 0.20 + warp2 * 0.025;

                float f = -10.0;
                float age = 0.0;     // 그 자리에 먹이 닿은 지 얼마나 됐나 (가운데일수록 오래됨 = 짙음)
                const int DROPS = 6;
                for (int i = 0; i < DROPS; i++)
                {
                    float fi = (float)i;
                    float2 c = float2(hash11(fi * 3.1 + 0.3) * _Aspect, hash11(fi * 5.7 + 1.9));
                    c = lerp(float2(_Aspect * 0.5, 0.5), c, 0.9);
                    float delay = (i == 0) ? 0.0 : 0.04 + hash11(fi * 9.3) * 0.36;
                    float s = saturate((t - delay) / (0.92 - delay));
                    // 떨어진 순간 작은 방울 → 처음엔 천천히, 먹이 종이에 먹히며 점점 넓게
                    float rMax = 0.85 + hash11(fi * 2.2) * 0.45;
                    float r = (s > 0.0) ? (0.025 + rMax * pow(s, 1.55)) : -1.0;
                    float d = length(q - c);
                    float fi_f = r - d;
                    if (fi_f > f) { f = fi_f; age = saturate(fi_f / max(r, 0.001)); }

                    // 떨어질 때 튄 작은 먹점 (방울 둘레)
                    for (int k = 0; k < 6; k++)
                    {
                        float fk = (float)k + fi * 7.0;
                        float ang = hash11(fk * 1.7) * 6.2832;
                        float dist = 0.035 + hash11(fk * 4.1) * 0.11;
                        float2 sc = c + float2(cos(ang), sin(ang)) * dist;
                        float sr = (0.003 + hash11(fk * 2.9) * 0.009) * saturate(s * 10.0);
                        float sf = sr - length(p - sc);
                        if (sf > f) { f = sf; age = 0.6; }
                    }
                }
                // 번진 끝의 잔 털 (한지 섬유를 따라 실오라기처럼 번짐)
                f += (fbm(p * 42.0) - 0.47) * 0.018 + (vnoise(float2(p.x * 160.0, p.y * 40.0)) - 0.5) * 0.006;

                // 농도: 오래된 가운데는 짙고, 막 번진 끝 안쪽은 옅다. 맨 끝에는 먹이 고여 짙은 테
                float rim = smoothstep(0.016, 0.0, f) * step(0.0, f);
                density = saturate(0.4 + age * 0.9 + (fbm(p * 4.0 + 7.0) - 0.47) * 0.5);
                density = max(density, rim * 0.95);
                return f;
            }

            // ───────── 1. 붓질 ─────────
            float BrushField(float2 p, float t, out float density)
            {
                const float ANG = -0.17;   // 살짝 기울여 긋는다
                float2 d = p - float2(_Aspect * 0.5, 0.5);
                float u = d.x * cos(ANG) - d.y * sin(ANG);   // 획 방향
                float v = d.x * sin(ANG) + d.y * cos(ANG);   // 획에 수직
                float halfLen = _Aspect * 0.5 + 0.45;

                float f = -10.0;
                density = 0.0;
                const int STROKES = 3;
                for (int k = 0; k < STROKES; k++)
                {
                    float fk = (float)k;
                    float dir = (k == 1) ? -1.0 : 1.0;          // 가운데 획은 반대로 긋는다
                    float along = u * dir;
                    // 획은 곧지 않고 살짝 휜다
                    float centre = (fk - 1.0) * 0.40 + (hash11(fk * 3.3) - 0.5) * 0.06 + sin(along * 1.3 + fk * 2.0) * 0.035;
                    float halfW = 0.28 + hash11(fk * 7.7) * 0.04;

                    float start = fk * 0.22;
                    float s = saturate((t - start) / 0.46);
                    s = 1.0 - pow(1.0 - s, 2.0);                // 붓은 빠르게 들어가 끝에서 느려진다
                    float head = lerp(-halfLen - 0.25, halfLen + 0.4, s);

                    float across = (v - centre) / halfW;         // -1 ~ 1
                    float traveled = saturate((along + halfLen + 0.25) / (2.0 * halfLen + 0.65));

                    // 붓털 한 가닥 한 가닥: 획 방향으로 아주 길쭉한 줄무늬 (가닥마다 먹의 양이 다름)
                    float hair = vnoise(float2(along * 0.6 + fk * 5.0, across * 11.0 + fk * 3.0));
                    float hairFine = vnoise(float2(along * 1.8, across * 34.0 + fk));
                    float strand = hair * 0.65 + hairFine * 0.35;

                    // 가장자리는 몇 가닥만 닿아 갈라지고, 지나온 길이가 길수록 붓이 말라 가운데도 비백이 생긴다
                    float edge = smoothstep(0.6, 1.0, abs(across));
                    float dry = saturate((traveled - 0.6) * 2.0) * 0.55;
                    float need = saturate(edge * 0.75 + dry);    // 이 값보다 먹이 많은 가닥만 남는다
                    float carried = strand - need;

                    float body = 1.0 - abs(across);
                    // 획 머리: 가닥마다 길이가 달라 끝이 들쭉날쭉
                    float tip = head - along - across * across * 0.16 + (strand - 0.5) * 0.08;

                    float sf = min(min(body * halfW, tip), carried * 0.05);
                    if (sf > f) f = sf;
                    // 먹 농도: 많이 머금은 가닥은 짙고, 마른 쪽은 옅다. 겹쳐 그은 곳은 더 짙다
                    float inside = saturate(sf * 60.0);
                    density += inside * saturate(0.45 + strand * 0.7 - dry * 0.4);
                }
                density = saturate(density);
                return f;
            }

            // ───────── 2. 먹 흘러내림 ─────────
            float DripField(float2 p, float t, out float density)
            {
                float depth = 1.0 - p.y;                     // 위에서 아래로 0 → 1
                float T = t * 1.28;
                // 배어 내려오는 앞선: 크게 굽이치고 잘게 떨린다
                float frontAt = 0.70 + 0.55 * vnoise(float2(p.x * 1.8, 1.0));
                float front = T * frontAt - 0.10 + (vnoise(float2(p.x * 7.0, 3.0)) - 0.5) * 0.06 + (vnoise(float2(p.x * 30.0, 5.0)) - 0.5) * 0.012;
                float f = front - depth;
                // 앞선이 지나간 지 오래된 곳일수록 먹이 더 배어 짙다 (처음엔 옅은 물먹으로 내려온다)
                float age = saturate((front - depth) / 0.55);

                // 먹줄기: 칸마다 하나 (없는 칸도 있음). 굵기·길이·흔들림이 칸마다 다르다
                const float CELL = 0.055;
                float cellX = floor(p.x / CELL);
                for (int k = -1; k <= 1; k++)
                {
                    float ci = cellX + (float)k;
                    if (hash11(ci * 1.37 + 5.0) < 0.38) continue;
                    float r = 0.003 + pow(hash11(ci * 4.13), 2.0) * 0.016;          // 가는 줄기가 많고 굵은 건 드물다
                    float cx0 = (ci + 0.3 + 0.4 * hash11(ci * 2.71)) * CELL;
                    float fa = 0.70 + 0.55 * vnoise(float2(cx0 * 1.8, 1.0));
                    float baseFront = T * fa - 0.10;
                    float len = (0.05 + hash11(ci * 6.7) * 0.38) * saturate(T * 1.5 - hash11(ci * 8.1) * 0.3);
                    float tipD = baseFront + len;
                    // 흘러내리며 좌우로 살짝 굽이친다
                    float cx = cx0 + sin(depth * (9.0 + hash11(ci) * 6.0) + ci) * 0.004;
                    float dx = abs(p.x - cx);
                    // 위(앞선 가까이)는 넓게 이어지고 끝으로 갈수록 가늘다
                    float taper = saturate((tipD - depth) / max(len, 0.01));
                    float wide = r * (0.75 + taper * 1.4);
                    float bodyF = (depth < tipD && depth > baseFront - 0.05) ? (wide - dx) : -10.0;
                    // 끝에 맺힌 둥근 먹방울 (줄기보다 약간 굵다)
                    float dropF = r * 1.35 - length(float2(p.x - cx, (depth - tipD) * 0.85));
                    float g = max(bodyF, dropF);
                    if (g > f) { f = g; age = 0.12 + 0.4 * taper * saturate((front - depth) / 0.2 + 1.0); }   // 막 흘러내린 줄기는 옅다
                }
                f += (fbm(p * 48.0) - 0.47) * 0.010;
                // 흘러내린 결: 세로로 길쭉한 농담 + 방울 끝은 먹이 고여 짙다
                float streak = vnoise(float2(p.x * 120.0, depth * 2.0));
                // 시간이 지날수록 화면 전체의 먹도 천천히 짙어진다
                float soak = smoothstep(0.15, 0.95, t);
                float wet = saturate(age * 0.75 + soak * 0.45);
                gOpacity = lerp(0.3, 1.0, pow(wet, 0.85));
                density = saturate(wet * 0.9 + (streak - 0.5) * 0.35);
                return f;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = saturate(_Progress);
                if (t <= 0.0001) return fixed4(0, 0, 0, 0);
                float2 p = float2(i.uv.x * _Aspect, i.uv.y);

                float density;
                gOpacity = 1.0;
                int style = (int)round(_Style);
                float field;
                if (style == 1) field = BrushField(p, t, density);
                else if (style == 2) field = DripField(p, t, density);
                else field = BleedField(p, t, density);

                // 끝무렵에는 남은 틈이 메워지며 전체가 고르게 짙어진다 (완전히 덮인 화면 = 짙은 먹)
                float finish = smoothstep(0.86, 1.0, t);
                field += finish * 2.0;
                density = lerp(density, 1.0, smoothstep(0.75, 1.0, t));

                // 먹 색: 옅은 먹 ~ 짙은 먹. 한지 섬유 결이 살짝 비친다
                float fiber = vnoise(float2(p.x * 300.0, p.y * 110.0)) * 0.5 + vnoise(p * 600.0) * 0.5;
                float d = saturate(density + (fiber - 0.5) * 0.12);
                fixed3 col = lerp(_InkWash.rgb, _InkDark.rgb, d * d * (3.0 - 2.0 * d));

                float soft = 0.002;
                float ink = smoothstep(-soft, soft, field);
                // 먹 밖으로 옅게 번진 물기 (붓질은 물기가 적다)
                float haloW = (style == 1) ? 0.012 : 0.05;
                float halo = smoothstep(-haloW, 0.0, field + (fbm(p * 14.0) - 0.47) * 0.03) * _Halo * (style == 1 ? 0.5 : 1.0);
                // 옅게 배어 든 곳은 아래가 비친다. 끝무렵에는 모두 짙어져 화면을 다 가린다
                float op = lerp(gOpacity, 1.0, smoothstep(0.82, 1.0, t));
                float a = max(ink * op, halo * (1.0 - ink) * (style == 2 ? 0.6 : 1.0));
                col = lerp(_InkWash.rgb, col, ink);

                return fixed4(col, a) * i.color;
            }
            ENDCG
        }
    }
}
