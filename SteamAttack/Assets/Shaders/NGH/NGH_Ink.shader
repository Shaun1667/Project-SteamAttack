// 먹선 궤적용 (붓 자국): 텍스처 알파 = 먹 농도. 정점 알파가 줄어들면 반투명해지는 대신
// 옅은 붓결부터 깎여 나가 마른 붓 끝처럼 갈라지며 사라짐. 양면, 깊이 쓰기 없음
Shader "NGH/Ink"
{
    Properties
    {
        _MainTex ("Ink (A = 먹 농도)", 2D) = "white" {}
        _Color ("Color", Color) = (0.02, 0.02, 0.025, 1)
        _Sharp ("붓 가장자리 선명도", Range(1, 20)) = 7
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float _Sharp;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                float erode = 1.0 - i.color.a;                      // 0 = 그대로, 1 = 모두 깎임
                float a = saturate((t.a - erode) * _Sharp);
                return fixed4(_Color.rgb * i.color.rgb, a * _Color.a);
            }
            ENDCG
        }
    }
}
