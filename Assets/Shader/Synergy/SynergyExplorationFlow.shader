// 探寻 (Exploration) — flowing energy coating for claimed blocks.
//
// A transparent ADDITIVE overlay meant for a slightly-inflated shell of the
// block mesh (see MaterialOverlayVisualizer / ExplorationRig): bright stripes
// scroll across each face (the "linear flow" / material current) with a fresnel
// edge glow, so the block itself reads as an energized, moving surface — not a
// separate particle effect. Self-animating via _Time; colour comes from
// _BaseColor (theme-tinted per instance through a MaterialPropertyBlock).
Shader "GeoWorld/Synergy/ExplorationFlow"
{
    Properties
    {
        [HDR] _BaseColor ("Color", Color) = (1.0, 0.4, 0.3, 1.0)
        _Speed     ("Flow Speed", Float) = 0.6
        _Density   ("Stripe Density", Float) = 5.0
        _Sharp     ("Stripe Sharpness", Range(1, 40)) = 8.0
        _Glow      ("Glow", Float) = 1.6
        _FresnelPow("Fresnel Power", Range(0.5, 8)) = 3.0
        _BaseFill  ("Base Fill (constant sheen under the stripes)", Range(0, 1)) = 0.10
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ExplorationFlow"
            Tags { "LightMode"="UniversalForward" }

            Blend SrcAlpha One      // additive glow
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 viewDirWS  : TEXCOORD2;
            };

            // GPU-instanced per-renderer color (MaterialPropertyBlock).
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            float _Speed, _Density, _Sharp, _Glow, _FresnelPow, _BaseFill;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 wp = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(wp);
                OUT.uv         = IN.uv;
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS  = GetWorldSpaceViewDir(wp);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float4 baseCol = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);

                // Scrolling stripe band along the face's V axis.
                float flow   = frac(IN.uv.y * _Density - _Time.y * _Speed);
                float band   = sin(flow * 6.2831853) * 0.5 + 0.5;
                float stripe = pow(saturate(band), _Sharp);

                // Fresnel rim so edges glow (reads as an energized coating).
                float fres = pow(1.0 - saturate(dot(normalize(IN.normalWS), normalize(IN.viewDirWS))), _FresnelPow);

                float intensity = stripe * _Glow + fres * 0.6 + _BaseFill;
                half3 col = baseCol.rgb * intensity;
                return half4(col, saturate(intensity) * baseCol.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
