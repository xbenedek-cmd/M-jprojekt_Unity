// Obloha arény bossa: nesvietená guľa videná zvnútra s prechodom farieb.
// Má aj hĺbkový prechod (ShadowCaster), aby sa dostala do hĺbkovej textúry kamery: bez neho by hmla
// z AtmosphereDirector kreslila na oblohu siluety susedných arén, ktoré guľa zakrýva.
Shader "Hidden/Progeny/ArenaSky"
{
    Properties
    {
        _MainTex ("Prechod", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Geometry+100" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Cull Off

        Pass
        {
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_shadowcaster
            #include "UnityCG.cginc"

            struct v2f
            {
                V2F_SHADOW_CASTER;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                TRANSFER_SHADOW_CASTER_NORMALOFFSET(o)
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                SHADOW_CASTER_FRAGMENT(i)
            }
            ENDCG
        }
    }
    Fallback Off
}
