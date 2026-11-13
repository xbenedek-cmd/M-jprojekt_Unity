// Kopce a hory za okrajom divočiny: zem (prvá vrstva terénu) plynulo prechádza do skaly podľa farby vrcholu (r = podiel skaly).
// Textúry sa mapujú podľa polohy vo svete, takže zem nadväzuje na terén bez švu.
Shader "Hidden/Progeny/WildEdge"
{
    Properties
    {
        _MainTex ("Zem", 2D) = "white" {}
        _RockTex ("Skala", 2D) = "gray" {}
        _GroundTile ("Dlaždica zeme (m)", Float) = 8
        _RockTile ("Dlaždica skaly (m)", Float) = 16
        _RockTint ("Farba skaly", Color) = (0.9, 0.88, 0.86, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        LOD 200

        CGPROGRAM
        #pragma surface surf Standard
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _RockTex;
        float _GroundTile;
        float _RockTile;
        fixed4 _RockTint;

        struct Input
        {
            float3 worldPos;
            float4 color : COLOR;
        };

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 w = IN.worldPos.xz;
            fixed3 ground = tex2D(_MainTex, w / _GroundTile).rgb;
            // Dve mierky skaly skryjú opakovanie textúry na veľkej ploche
            fixed3 rock = (tex2D(_RockTex, w / _RockTile).rgb * 0.55 + tex2D(_RockTex, w / (_RockTile * 5.3)).rgb * 0.45) * _RockTint.rgb;
            o.Albedo = lerp(ground, rock, saturate(IN.color.r));
            o.Metallic = 0;
            o.Smoothness = 0.04;
        }
        ENDCG
    }
    Fallback "Diffuse"
}
