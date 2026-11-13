// Postava hráča: PBR (albedo + ORM + normála z Tripo modelu) s oddeleným prefarbením pokožky a oblečenia.
// Textúra je atlas z malých fragmentov bez masky, preto sa pokožka / oblečenie / žiarivé detaily rozlíšia podľa farby pixelu:
//  - pokožka: svetlé, málo sýte, teplé (ružovkasté) pixely,
//  - žiarivé detaily: zelená dominantná farba (ostáva pôvodná),
//  - oblečenie: všetko ostatné (tmavá koža, látka, oranžové kovanie).
// Prefarbenie zachová tieňovanie (jas) pôvodnej textúry.
Shader "Progeny/PlayerBody"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _OrmMap ("ORM (occlusion / roughness / metallic)", 2D) = "white" {}
        [Normal] _BumpMap ("Normal", 2D) = "bump" {}
        _SkinColor ("Skin color", Color) = (0.86, 0.69, 0.57, 1)
        _ClothesColor ("Clothes color", Color) = (0.22, 0.26, 0.40, 1)
        _Recolor ("Recolor amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _OrmMap;
        sampler2D _BumpMap;
        fixed4 _SkinColor;
        fixed4 _ClothesColor;
        half _Recolor;

        struct Input
        {
            float2 uv_MainTex;
        };

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            half3 c = tex2D(_MainTex, IN.uv_MainTex).rgb;

            // Rozhodovanie podľa farby v gamma priestore (tak ako je textúra viditeľná)
            #ifdef UNITY_COLORSPACE_GAMMA
                half3 g = c;
            #else
                half3 g = LinearToGammaSpace(c);
            #endif

            half mx = max(g.r, max(g.g, g.b));
            half mn = min(g.r, min(g.g, g.b));
            half sat = (mx - mn) / max(mx, 0.0001h);
            half lum = dot(g, half3(0.299h, 0.587h, 0.114h));

            half skin = smoothstep(0.50h, 0.62h, mx)
                      * (1.0h - smoothstep(0.34h, 0.46h, sat))
                      * smoothstep(-0.02h, 0.04h, g.r - g.b);
            half accent = smoothstep(0.02h, 0.10h, g.g - max(g.r, g.b)) * smoothstep(0.25h, 0.40h, mx);
            half clothes = (1.0h - skin) * (1.0h - accent);

            // Nová farba s pôvodným tieňovaním
            half skinShade = clamp(lum / 0.75h, 0.25h, 1.25h);
            half3 skinOut = _SkinColor.rgb * pow(skinShade, 2.2h);
            half clothShade = lerp(0.45h, 1.15h, saturate(lum * 2.0h));
            half3 clothOut = _ClothesColor.rgb * pow(clothShade, 2.2h);

            half3 result = lerp(c, skinOut, skin * _Recolor);
            result = lerp(result, clothOut, clothes * _Recolor);

            half4 orm = tex2D(_OrmMap, IN.uv_MainTex);
            o.Albedo = result;
            o.Occlusion = orm.r;
            o.Smoothness = 1.0h - orm.g;
            o.Metallic = orm.b;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
        }
        ENDCG
    }

    FallBack "Diffuse"
}
