// Prekrytie postavy svetlom pri zásahu (bliknutie), krvácaní a horení. Kreslí sa cez CommandBuffer
// na už vykreslenú postavu, takže nemení jej materiály. Farba je globálna (_FxFlashColor), nastavuje ju CombatFx.
Shader "Hidden/Progeny/FxFlash"
{
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        ZTest LEqual
        Offset -1, -1
        Cull Back

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            half4 _FxFlashColor;   // a = podiel obrysu (0 = celá plocha, 1 = len okraje)

            struct v2f
            {
                float4 pos : SV_POSITION;
                half rim : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                float3 n = UnityObjectToWorldNormal(v.normal);
                o.rim = 1.0 - saturate(dot(n, normalize(_WorldSpaceCameraPos - world)));
                return o;
            }

            half4 frag(v2f i) : SV_Target
            {
                half amount = lerp(1.0, i.rim * i.rim * 1.6, _FxFlashColor.a);
                return half4(_FxFlashColor.rgb * amount, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
