// Flat, always-on-top arrow sprite for the buff / debuff status particles
// (StatusArrowFx). Companion to GeoWorld/BuffGlow, but WITHOUT the radial
// falloff — the shape is carried by the arrow mesh itself, so the fragment
// stage only needs to tint it and fade the tail out.
//
// Alpha-blended rather than additive: a debuff has to read as a solid dark red
// warning, and additive red over the pale sky just washes out to nothing.
// ZTest Always so arrows on a turret stay legible behind blocks / the beacon,
// matching how the existing buff auras behave.
Shader "GeoWorld/StatusArrow"
{
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 1)
        _TailFade  ("Tail Fade", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _TailFade;
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
                // uv.y runs 0 at the tail → 1 at the tip. Fade the tail so each
                // arrow looks like it's streaking rather than sitting still.
                float a = lerp(1.0 - _TailFade, 1.0, saturate(IN.uv.y));
                return half4(_BaseColor.rgb, a * _BaseColor.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
