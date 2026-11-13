// Holografický klam: priesvitný modrý prízrak so svetlým obrysom, bežiacimi skenovacími linkami,
// občasným vodorovným trhnutím a blikaním. Používa ho HologramDecoy.
Shader "Hidden/Progeny/Hologram"
{
    Properties
    {
        [HDR] _Color ("Farba", Color) = (0.2, 0.9, 1.6, 1)
        _Alpha ("Krytie", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _Color;
            half _Alpha;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
                float3 normal : TEXCOORD1;
                float3 view : TEXCOORD2;
            };

            float Hash(float n)
            {
                return frac(sin(n * 91.345) * 47453.5453);
            }

            v2f vert(appdata_base v)
            {
                v2f o;
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;

                // Trhnutie: pár pásov tela sa na zlomok sekundy posunie do strany
                float tick = floor(_Time.y * 14.0);
                float band = floor(world.y * 9.0);
                float glitch = step(0.93, Hash(tick + band * 3.7)) * (Hash(tick * 1.3 + band) - 0.5) * 0.35;
                world += UNITY_MATRIX_V[0].xyz * glitch;
                world.x += sin(_Time.y * 31.0 + world.y * 40.0) * 0.004;   // jemné chvenie

                o.pos = mul(UNITY_MATRIX_VP, float4(world, 1.0));
                o.worldPos = world;
                o.normal = UnityObjectToWorldNormal(v.normal);
                o.view = normalize(_WorldSpaceCameraPos - world);
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half rim = pow(1.0 - saturate(dot(normalize(i.normal), normalize(i.view))), 2.2);
                half scan = 0.55 + 0.45 * sin(i.worldPos.y * 120.0 - _Time.y * 9.0);
                half sweep = smoothstep(0.92, 1.0, frac(i.worldPos.y * 0.6 - _Time.y * 0.7));   // pomalý jasný pás
                half flicker = 0.82 + 0.18 * sin(_Time.y * 47.0) * sin(_Time.y * 13.0);

                half a = (0.16 + rim * 0.9) * scan + sweep * 0.6;
                return half4(_Color.rgb * (0.6 + rim + sweep), saturate(a) * flicker * _Alpha);
            }
            ENDCG
        }
    }
    Fallback Off
}
