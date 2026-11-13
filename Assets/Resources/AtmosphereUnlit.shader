// Nesvietený HDR materiál pre AtmosphereDirector: neónové trubice (nepriehľadné), dym (priehľadné častice)
// a maska cieľov krvavého videnia (kreslí sa cez steny). Režim miešania nastavuje skript.
Shader "Hidden/Progeny/AtmosphereUnlit"
{
    Properties
    {
        _MainTex ("Textúra", 2D) = "white" {}
        [HDR] _Color ("Farba", Color) = (1, 1, 1, 1)
        _SrcBlend ("Src", Float) = 1
        _DstBlend ("Dst", Float) = 0
        _ZWrite ("ZWrite", Float) = 1
        _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "Queue" = "Geometry" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        ZTest [_ZTest]

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            half4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * i.color;
            }
            ENDCG
        }
    }
    Fallback Off
}
