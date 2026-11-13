// Voda jazier (built-in RP): priehľadná, farba podľa hĺbky (vertexová farba R), mäkký okraj pri brehu (vertexová alfa),
// odrazy okolia a oblohy cez Standard osvetlenie (vysoký lesk, fresnel), jemné vlnenie od vetra a kruhy od dopadajúcich kvapiek dažďa.
// Dážď riadi globálna premenná _PG_Rain (0–1), ktorú nastavuje WeatherSystem.
Shader "Progeny/Water"
{
    Properties
    {
        _ShallowColor ("Plytká voda", Color) = (0.25, 0.45, 0.45, 1)
        _DeepColor ("Hlboká voda", Color) = (0.03, 0.13, 0.18, 1)
        _Smoothness ("Lesk", Range(0, 1)) = 0.96
        _RippleScale ("Mierka vĺn", Float) = 0.9
        _RippleSpeed ("Rýchlosť vĺn", Float) = 0.6
        _RippleStrength ("Sila vĺn", Float) = 0.35
        _FresnelPower ("Fresnel", Range(1, 8)) = 3
        _MinAlpha ("Najmenšia nepriehľadnosť", Range(0, 1)) = 0.5
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        LOD 300

        CGPROGRAM
        #pragma surface surf Standard alpha:fade nolightmap
        #pragma target 3.0

        fixed4 _ShallowColor;
        fixed4 _DeepColor;
        half _Smoothness;
        float _RippleScale;
        float _RippleSpeed;
        float _RippleStrength;
        half _FresnelPower;
        half _MinAlpha;
        float _PG_Rain;   // globálne: 0 = bez dažďa, 1 = silný dážď

        struct Input
        {
            float3 worldPos;
            float3 viewDir;
            float4 color : COLOR;
        };

        float Hash21(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float Noise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = Hash21(i);
            float b = Hash21(i + float2(1, 0));
            float c = Hash21(i + float2(0, 1));
            float d = Hash21(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }

        // Výška hladiny: vietor (tri posúvané šumy) + kruhové vlny od kvapiek
        float Height(float2 p)
        {
            float t = _Time.y * _RippleSpeed;
            float h = Noise(p * _RippleScale + float2(t * 0.7, t * 0.4)) * 0.5
                    + Noise(p * _RippleScale * 2.3 - float2(t * 0.9, -t * 0.6)) * 0.3
                    + Noise(p * _RippleScale * 5.1 + float2(-t * 1.3, t * 1.1)) * 0.2;

            if (_PG_Rain > 0.01)
            {
                float2 g = p * 1.6;
                float2 cell = floor(g);
                float2 local = frac(g) - 0.5;
                float rnd = Hash21(cell);
                float2 center = (float2(Hash21(cell + 3.1), Hash21(cell + 7.7)) - 0.5) * 0.6;
                float phase = frac(_Time.y * (0.7 + rnd * 0.6) + rnd * 7.0);
                float d = length(local - center);
                float ring = sin((d - phase * 0.55) * 38.0) * saturate(1.0 - phase) * saturate(1.0 - d * 2.2);
                h += ring * 0.25 * step(rnd, _PG_Rain);   // pri slabom daždi dopadá len časť kvapiek
            }
            return h;
        }

        void surf(Input IN, inout SurfaceOutputStandard o)
        {
            float2 p = IN.worldPos.xz;
            const float e = 0.08;
            float h0 = Height(p);
            float2 grad = float2(Height(p + float2(e, 0)) - h0, Height(p + float2(0, e)) - h0) / e;

            // Tangentový priestor siete vody: tangent = +X, binormála = -Z
            o.Normal = normalize(float3(-grad.x * _RippleStrength, grad.y * _RippleStrength, 1.0));

            float depth01 = IN.color.r;
            float3 baseColor = lerp(_ShallowColor.rgb, _DeepColor.rgb, depth01);
            float fresnel = pow(1.0 - saturate(dot(normalize(IN.viewDir), float3(0, 0, 1))), _FresnelPower);

            o.Albedo = baseColor;
            o.Metallic = 0.0;
            o.Smoothness = _Smoothness;
            o.Emission = baseColor * 0.04;
            o.Alpha = saturate(lerp(_MinAlpha, 0.95, depth01) + fresnel * 0.3) * IN.color.a;
        }
        ENDCG
    }

    FallBack "Transparent/Diffuse"
}
