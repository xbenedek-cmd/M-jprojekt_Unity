// Tráva pre detaily terénu (built-in RP): orezanie podľa alfy, obojstranná, vietor podľa výšky listu, podpora GPU instancingu
// (detaily terénu s inštancovaním berú materiál z prefabu, takže tento shader musí instancing umožniť).
Shader "Progeny/GrassDetail"
{
    Properties
    {
        _MainTex ("Farba (alfa = orezanie)", 2D) = "white" {}
        _BumpMap ("Normála", 2D) = "bump" {}
        _Color ("Odtieň", Color) = (1,1,1,1)
        _Cutoff ("Prah alfy", Range(0,1)) = 0.4
        _MeshHeight ("Výška siete (m)", Float) = 1
        _WindStrength ("Sila vetra", Float) = 0.1
        _WindSpeed ("Rýchlosť vetra", Float) = 1.5
    }

    SubShader
    {
        Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
        Cull Off

        CGPROGRAM
        #pragma surface surf Lambert vertex:vert alphatest:_Cutoff addshadow
        #pragma multi_compile_instancing
        #pragma target 3.0

        sampler2D _MainTex;
        sampler2D _BumpMap;
        fixed4 _Color;
        float _MeshHeight;
        float _WindStrength;
        float _WindSpeed;

        struct Input
        {
            float2 uv_MainTex;
        };

        void vert(inout appdata_full v)
        {
            // Vietor kýve len horné časti listov (spodok pri zemi stojí); fáza podľa svetovej polohy, aby nekmitala všetka tráva naraz
            float h = saturate(v.vertex.y / max(_MeshHeight, 0.001));
            float3 wp = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
            float phase = _Time.y * _WindSpeed + dot(wp.xz, float2(0.35, 0.27));
            float sway = sin(phase) * 0.7 + sin(phase * 2.3 + wp.x * 0.5) * 0.3;
            float amount = _WindStrength * h * h * _MeshHeight;
            v.vertex.x += sway * amount;
            v.vertex.z += cos(phase * 0.9) * amount * 0.5;
        }

        void surf(Input IN, inout SurfaceOutput o)
        {
            fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb;
            o.Alpha = c.a;
            o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_MainTex));
        }
        ENDCG
    }

    FallBack "Transparent/Cutout/VertexLit"
}
