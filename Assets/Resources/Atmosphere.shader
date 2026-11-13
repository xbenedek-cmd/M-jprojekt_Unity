// Obrazovkové efekty atmosféry (built-in RP): bloom, slnečné lúče, výšková hmla, farebná korekcia,
// vinetácia, chromatická aberácia, zrno, démonické vnímanie a krvavé videnie.
// Riadi ho AtmosphereDirector. Je v Resources, aby bol vždy súčasťou zostavenia.
Shader "Hidden/Progeny/Atmosphere"
{
    Properties
    {
        _MainTex ("Obraz", 2D) = "white" {}
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    float4 _MainTex_TexelSize;
    sampler2D _HighTex;
    sampler2D _BloomTex;
    sampler2D _ShaftTex;
    sampler2D _TargetTex;
    sampler2D _TargetBlurTex;
    sampler2D_float _CameraDepthTexture;

    float4 _BloomParams;      // x prah, y mäkké koleno, z intenzita
    float4 _SunScreen;        // xy poloha slnka na obrazovke, z polomer masky, w krok rozmazania
    float4x4 _FrustumRays;    // riadky: ĽD, PD, ĽH, PH (svetové vektory po vzdialenú rovinu)
    float3 _CamPos;

    float4 _FogColor;         // a = najvyššie krytie
    float4 _FogSunColor;      // a = sila prifarbenia smerom k slnku
    float4 _FogParams;        // x hustota do diaľky, y hustota pri zemi, z výška zeme, w pokles s výškou
    float4 _FogParams2;       // x sila šumu, y mierka šumu, z opar na oblohe, w začiatok hmly (m)
    float3 _SunDir;           // smer K slnku
    float _AtmoTime;

    float4 _ColorFilter;
    float4 _Grade;            // x expozícia, y sýtosť, z kontrast, w tonemapping 0..1
    float4 _Vignette;         // x sila, y mäkkosť
    float4 _VignetteColor;
    float4 _Fx;               // x chromatická aberácia, y zrno, z -, w sila lúčov
    float4 _ShaftColor;
    float4 _Demon;            // x červený posun, y pulz 0..1, z krvavé videnie, w žiara cieľov
    float4 _DemonColor;
    float4 _BloodColor;
    float4 _TargetColor;
    float4 _EdgeGlow;         // rgb farba žiary okrajov, a = sila (kŕmenie)

    struct v2f
    {
        float4 pos : SV_POSITION;
        float2 uv : TEXCOORD0;
        float2 uvDepth : TEXCOORD1;
    };

    v2f vert(appdata_img v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = v.texcoord;
        o.uvDepth = v.texcoord;
        #if UNITY_UV_STARTS_AT_TOP
        if (_MainTex_TexelSize.y < 0.0) o.uvDepth.y = 1.0 - o.uvDepth.y;
        #endif
        return o;
    }

    half3 Box4(sampler2D tex, float2 uv, float2 texel, float spread)
    {
        float4 d = texel.xyxy * float4(-spread, -spread, spread, spread);
        half3 s = tex2D(tex, uv + d.xy).rgb;
        s += tex2D(tex, uv + d.zy).rgb;
        s += tex2D(tex, uv + d.xw).rgb;
        s += tex2D(tex, uv + d.zw).rgb;
        return s * 0.25;
    }

    float Hash(float2 p)
    {
        // Bez sínusu: pri veľkých súradniciach (pixely obrazovky) nerobí pruhy
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    float ValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);
        float a = Hash(i);
        float b = Hash(i + float2(1.0, 0.0));
        float c = Hash(i + float2(0.0, 1.0));
        float d = Hash(i + float2(1.0, 1.0));
        return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    }

    float3 ViewRay(float2 uv)
    {
        float3 bottom = lerp(_FrustumRays[0].xyz, _FrustumRays[1].xyz, uv.x);
        float3 top = lerp(_FrustumRays[2].xyz, _FrustumRays[3].xyz, uv.x);
        return lerp(bottom, top, uv.y);
    }

    half3 Aces(half3 x)
    {
        return saturate((x * (2.51 * x + 0.03)) / (x * (2.43 * x + 0.59) + 0.14));
    }
    ENDCG

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        // 0: bloom - výber jasných miest
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                half3 c = Box4(_MainTex, i.uv, _MainTex_TexelSize.xy, 1.0);
                c = min(c, 8.0);    // veľké presvetlené plochy by inak zaliali pol obrazu
                half br = max(c.r, max(c.g, c.b));
                half soft = clamp(br - _BloomParams.x + _BloomParams.y, 0.0, 2.0 * _BloomParams.y);
                soft = soft * soft / (4.0 * _BloomParams.y + 1e-4);
                half w = max(soft, br - _BloomParams.x) / max(br, 1e-4);
                return half4(c * w, 1.0);
            }
            ENDCG
        }

        // 1: zmenšenie
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                return half4(Box4(_MainTex, i.uv, _MainTex_TexelSize.xy, 1.0), 1.0);
            }
            ENDCG
        }

        // 2: zväčšenie + pripočítanie vyššej úrovne
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                half3 low = Box4(_MainTex, i.uv, _MainTex_TexelSize.xy, 0.5);
                half3 high = tex2D(_HighTex, i.uv).rgb;
                return half4(low + high, 1.0);
            }
            ENDCG
        }

        // 3: maska slnečných lúčov (len obloha okolo slnka)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                float depth = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uvDepth));
                half sky = step(0.9995, depth);
                float2 d = i.uv - _SunScreen.xy;
                d.x *= _MainTex_TexelSize.z / _MainTex_TexelSize.w;
                half falloff = saturate(1.0 - length(d) / _SunScreen.z);
                half3 c = min(tex2D(_MainTex, i.uv).rgb, 4.0);
                return half4(c * sky * falloff * falloff, 1.0);
            }
            ENDCG
        }

        // 4: radiálne rozmazanie k slnku
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                float2 dir = (_SunScreen.xy - i.uv) * _SunScreen.w;
                half3 c = 0;
                float2 uv = i.uv;
                for (int k = 0; k < 6; k++)
                {
                    c += tex2D(_MainTex, uv).rgb;
                    uv += dir;
                }
                return half4(c / 6.0, 1.0);
            }
            ENDCG
        }

        // 5: výsledné zloženie
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag(v2f i) : SV_Target
            {
                float2 centered = i.uv - 0.5;
                float r2 = dot(centered, centered);

                // Chromatická aberácia: kanály sa rozchádzajú smerom k okrajom
                float2 shift = centered * r2 * _Fx.x * 0.03;
                half3 c;
                c.g = tex2D(_MainTex, i.uv).g;
                c.r = tex2D(_MainTex, i.uv - shift).r;
                c.b = tex2D(_MainTex, i.uv + shift).b;

                // Hmla: rovnomerná do diaľky + exponenciálna vrstva pri zemi (analytický integrál pozdĺž lúča)
                float depth = Linear01Depth(SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture, i.uvDepth));
                float3 ray = ViewRay(i.uvDepth);
                float rayLen = length(ray);
                float3 dir = ray / rayLen;
                float isSky = step(0.9995, depth);
                float dist = rayLen * depth;

                float uniformFog = _FogParams.x * max(0.0, dist - _FogParams2.w);
                float fall = _FogParams.w;
                float camH = clamp((_CamPos.y - _FogParams.z) * fall, -4.0, 30.0);
                float dy = dir.y * fall;
                float travel = lerp(dist, 600.0, isSky);
                float t = dy * travel;
                float integral = abs(t) > 0.001 ? (1.0 - exp(-clamp(t, -30.0, 30.0))) / dy : travel;
                float groundFog = _FogParams.y * exp(-camH) * integral;

                float3 wp = _CamPos + dir * min(travel, 90.0);
                float n = ValueNoise(wp.xz * _FogParams2.y + float2(_AtmoTime * 0.05, _AtmoTime * 0.03));
                n = n * 0.65 + 0.35 * ValueNoise(wp.xz * _FogParams2.y * 2.7 - _AtmoTime * 0.04);
                groundFog *= 1.0 + (n - 0.5) * 2.0 * _FogParams2.x;

                float haze = _FogParams2.z * exp(-max(dir.y, 0.0) * 7.0);
                float fog = 1.0 - exp(-(lerp(uniformFog, 0.0, isSky) + max(groundFog, 0.0)));
                fog = max(fog, haze * isSky) * _FogColor.a;

                float sunAmount = pow(saturate(dot(dir, _SunDir)), 10.0) * _FogSunColor.a;
                half3 fogColor = lerp(_FogColor.rgb, _FogSunColor.rgb, sunAmount);
                c = lerp(c, fogColor, saturate(fog));

                // Lúče a bloom
                c += tex2D(_ShaftTex, i.uv).rgb * _ShaftColor.rgb * _Fx.w;
                c += tex2D(_BloomTex, i.uv).rgb * _BloomParams.z;

                c *= _Grade.x;

                // Démonické vnímanie: posun do červenej, pulz podľa krvi
                half lum = dot(c, half3(0.2126, 0.7152, 0.0722));
                half demon = saturate(_Demon.x * (1.0 + _Demon.y * 0.35));
                half3 demonLook = lerp(c * _DemonColor.rgb, lum * _DemonColor.rgb * 1.7, 0.6);
                c = lerp(c, demonLook, demon);

                // Krvavé videnie: takmer jednofarebná červená s tvrdším kontrastom
                half bl = saturate(lum * 2.0);
                bl = bl * bl * (3.0 - 2.0 * bl);
                half3 bloodLook = bl * _BloodColor.rgb * 1.7 + _BloodColor.rgb * 0.02;
                c = lerp(c, bloodLook, _Demon.z);

                c = lerp(c, Aces(c), _Grade.w);

                // Farebná korekcia
                c *= _ColorFilter.rgb;
                half l2 = dot(c, half3(0.2126, 0.7152, 0.0722));
                c = lerp(l2.xxx, c, _Grade.y);
                c = (c - 0.18) * _Grade.z + 0.18;
                c = max(c, 0.0);

                // Vinetácia (pulzuje s tepom)
                half vig = smoothstep(0.0, 1.0, saturate(r2 * 4.0 * (0.4 + _Vignette.y)));
                half vigAmount = saturate(_Vignette.x * (1.0 + _Demon.y * 0.5));
                c = lerp(c, c * _VignetteColor.rgb, vig * vigAmount);

                // Žiara okrajov (kŕmenie): pripočíta sa, aby ju bolo vidno aj na tmavej alebo červenej scéne
                c += _EdgeGlow.rgb * (vig * vig * _EdgeGlow.a);

                // Zvýraznené ciele (krvavé videnie): obrys žiari, silueta jemne presvitá
                half sharp = tex2D(_TargetTex, i.uvDepth).r;
                half glow = tex2D(_TargetBlurTex, i.uv).r;
                c += _TargetColor.rgb * (glow * 1.3 + sharp * 0.18) * _Demon.w * (0.8 + 0.2 * _Demon.y);

                // Zrno
                half g = Hash(floor(i.uv * _ScreenParams.xy) + floor(frac(_AtmoTime) * 60.0) * 17.0) - 0.5;
                c += g * _Fx.y * (0.04 + 0.5 * sqrt(saturate(l2)));   // v tme slabšie, inak by šum prebil obraz

                return half4(max(c, 0.0), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
