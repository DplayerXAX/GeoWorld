// Always-on-top additive glow for synergy buff feedback (turret speed aura,
// enemy slow aura, income motes). Soft radial alpha from the quad UV, tinted by
// _BaseColor (driven per-instance via MaterialPropertyBlock). ZTest Always so the
// glow reads as a halo even when the turret beacon / blocks sit in front of it.
Shader "GeoWorld/BuffGlow"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha One     // additive

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.uv = IN.uv;
                return o;
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 d = IN.uv - 0.5;
                float  r = length(d) * 2.0;            // 0 centre … 1 edge
                float  a = saturate(1.0 - r);
                a = a * a;                             // soft round falloff
                return half4(_BaseColor.rgb, a * _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
