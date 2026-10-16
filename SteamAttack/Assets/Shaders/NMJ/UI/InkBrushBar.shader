// 플레이어 체력바(NMJ.InkHealthBar) 전용 UI 셰이더.
// _MainTex 는 그림이 아니라 "붓 획 데이터"다. InkHealthBar 가 붓털을 한 가닥씩 시뮬레이션해서 만든다.
//   A = 검은 먹이 묻은 정도(획 모양, 비백 포함)   R = 먹의 농도(붓털 결·가장자리 고임)
//   G = 그 자리를 그은 붓털 번호(0~1)            B = 붉은 먹이 올라갈 수 있는 자리(바깥 붓털 몇 가닥은 검게 남김)
// 검은 먹 획 위에 붉은 먹을 _Fill 만큼, 옅은 잔상을 _Trail 만큼 얹는다.
// 붉은 먹의 끝은 붓털(G)마다 끝나는 자리가 달라서 붓 끝처럼 갈라지고, 끝에 가까울수록 먹이 듬성듬성해진다.
Shader "NMJ/UI/InkBrushBar"
{
    Properties
    {
        [PerRendererData] _MainTex ("Stroke Data", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _Fill ("Fill", Range(0, 1)) = 1
        _Trail ("Trail", Range(0, 1)) = 1
        _TearLen ("Tear Length (uv)", Float) = 0.03
        _FringeLen ("Dry Fringe Length (uv)", Float) = 0.03
        _Soft ("Edge Softness (uv)", Float) = 0.0025

        _InkLight ("Ink Light", Color) = (0.20, 0.19, 0.19, 1)
        _InkDark ("Ink Dark", Color) = (0.03, 0.03, 0.04, 1)
        _RedLight ("Red Light", Color) = (0.90, 0.13, 0.11, 1)
        _RedDark ("Red Dark", Color) = (0.50, 0.03, 0.04, 1)
        _TrailColor ("Trail", Color) = (0.97, 0.70, 0.64, 0.9)
        _Pulse ("Pulse", Range(0, 1.5)) = 1

        // UGUI 마스크·클리핑과 함께 쓰기 위한 기본 항목 (UI/Default 와 같음)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _ClipRect;

            float _Fill, _Trail, _TearLen, _FringeLen, _Soft, _Pulse;
            fixed4 _InkLight, _InkDark, _RedLight, _RedDark, _TrailColor;

            v2f vert (appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // 같은 입력에는 항상 같은 0~1 값
            float Hash11 (float p)
            {
                p = frac(p * 0.1031);
                p *= p + 33.33;
                p *= p + p;
                return frac(p);
            }

            // 가로로 부드럽게 이어지는 1차원 값 노이즈 (붓털 한 가닥 안에서 먹이 끊겼다 이어지는 모양)
            float ValueNoise (float x)
            {
                float i = floor(x);
                float f = frac(x);
                float u = f * f * (3.0 - 2.0 * f);
                return lerp(Hash11(i), Hash11(i + 1.0), u);
            }

            // level 만큼 먹이 차 있을 때 이 붓털(hash)의 먹이 끝나는 자리.
            // 붓털마다 끝나는 자리가 다르다 → 끝이 붓 끝처럼 갈라진다
            float EdgeOf (float level, float hash)
            {
                return level * (1.0 + _TearLen) - _TearLen * 0.5 + (hash - 0.5) * _TearLen;
            }

            // level 만큼 먹이 차 있을 때, 이 붓털(hash) 자리 x 에 먹이 남아 있는 정도
            float InkReach (float x, float level, float hash, float lane)
            {
                float edge = EdgeOf(level, hash);
                float a = saturate((edge - x) / _Soft);

                // 끝에 가까울수록 먹이 듬성듬성 (마른 붓)
                float inside = saturate((edge - x) / _FringeLen);   // 0 = 붓 끝, 1 = 충분히 안쪽
                float n = ValueNoise(x * 180.0 + lane * 37.0);
                a *= step(n, lerp(0.2, 1.05, inside));
                return a;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                float4 s = tex2D(_MainTex, IN.texcoord);
                float cover  = s.a;   // 검은 먹이 묻은 정도
                float dens   = s.r;   // 먹 농도
                float hash   = s.g;   // 붓털 번호
                float redCov = s.b;   // 붉은 먹이 올라갈 수 있는 자리

                float x = IN.texcoord.x;
                float lane = floor(hash * 97.0);

                fixed3 ink = lerp(_InkLight.rgb, _InkDark.rgb, dens);

                float red = redCov * InkReach(x, _Fill, hash, lane);
                // 잔상은 붉은 먹이 끝난 "뒤"에만 남는다. 붉은 먹 끝의 마른 틈은 잔상이 아니라 검은 먹이 비쳐야 한다.
                float redSolid = saturate((EdgeOf(_Fill, hash) - x) / _Soft);
                float trail = redCov * InkReach(x, _Trail, hash, lane + 13.0) * (1.0 - redSolid);

                // 붉은 먹: 농도가 짙은 곳(가장자리·붓을 누른 머리)은 진하고, 붓털마다 조금씩 달라 결이 보인다
                fixed3 redCol = lerp(_RedLight.rgb, _RedDark.rgb, saturate(dens * 1.25)) * (0.90 + 0.20 * hash);
                redCol *= _Pulse;

                // 잔상: 물에 풀린 붉은 먹. 같은 붓털 결이 옅게 남는다
                fixed3 trailCol = _TrailColor.rgb * (0.93 + 0.14 * hash) * lerp(1.0, 0.86, dens);

                fixed3 col = ink;
                col = lerp(col, trailCol, trail * _TrailColor.a);
                col = lerp(col, redCol, red);

                fixed4 c = fixed4(col, cover) * IN.color;

                #ifdef UNITY_UI_CLIP_RECT
                c.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(c.a - 0.001);
                #endif

                return c;
            }
            ENDCG
        }
    }
}
