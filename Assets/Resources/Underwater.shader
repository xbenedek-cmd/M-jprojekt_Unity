// Efekt pod vodou (obrazovkový, built-in RP): jemné vlnenie obrazu, chromatická aberácia, modrozelený tón, hmla a tmavšie okraje.
// Používa ho UnderwaterEffect, keď je kamera pod hladinou jazera. Je v Resources, aby bol vždy súčasťou zostavenia.
Shader "Hidden/Progeny/Underwater"
{
    Properties
    {
        _MainTex ("Obraz", 2D) = "white" {}
        _Tint ("Tón vody", Color) = (0.35, 0.65, 0.7, 1)
        _Intensity ("Intenzita", Range(0, 1)) = 1
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;
            float _Intensity;

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;

                // Pomalé vlnenie: dve sínusovky v každej osi s rôznou frekvenciou, amplitúda do ~0,4 % obrazovky
                float2 wobble = float2(sin(uv.y * 18.0 + t * 1.6) + 0.5 * sin(uv.y * 41.0 - t * 2.3),
                                       cos(uv.x * 15.0 + t * 1.3) + 0.5 * cos(uv.x * 37.0 + t * 2.1)) * 0.0038 * _Intensity;

                fixed4 c = tex2D(_MainTex, uv + wobble);
                c.r = tex2D(_MainTex, uv + wobble * 1.2).r;   // jemná chromatická aberácia
                c.b = tex2D(_MainTex, uv + wobble * 0.8).b;

                float vignette = smoothstep(0.95, 0.3, distance(uv, float2(0.5, 0.5)));
                fixed3 water = lerp(c.rgb, c.rgb * _Tint.rgb * 1.4, 0.65);
                water = lerp(water, _Tint.rgb * 0.3, 0.25);                // hmla
                water *= lerp(1.0, 0.55 + 0.45 * vignette, 0.9);          // tmavšie okraje

                c.rgb = lerp(c.rgb, water, _Intensity);
                return c;
            }
            ENDCG
        }
    }
}
