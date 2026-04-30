Shader "Custom/SacredMarker_SolidCore"
{
    Properties
    {
        _CoreColor     ("Core Color",     Color)  = (0.3, 0.7, 1, 1)
        _EdgeColor     ("Edge Color",     Color)  = (0.6, 0.9, 1, 1)

        // Lower = wider solid core.  Try 1.0–2.5 for a nice orb feel.
        _FresnelPower  ("Core Falloff",   Float)  = 1.5

        // How much additive rim-aura bleeds at the very edge (0 = none).
        _EdgeSoftness  ("Rim Glow",       Float)  = 0.4

        _PulseSpeed    ("Pulse Speed",    Float)  = 2.0
        _PulseStrength ("Pulse Strength", Float)  = 0.25
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off       // render inside too — sphere reads as solid from any angle

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS    : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
            };

            float4 _CoreColor;
            float4 _EdgeColor;
            float  _FresnelPower;
            float  _EdgeSoftness;
            float  _PulseSpeed;
            float  _PulseStrength;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS  = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                // Always use the geometric normal (not flipped for back-face),
                // so both sides of a sphere read correctly.
                o.normalWS    = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                // abs() so back-faces read as if front-facing → hollow sphere
                // becomes a solid-looking volume.
                float ndv = abs(dot(normalize(i.normalWS), viewDir));

                // ── Core ─────────────────────────────────────────────────────
                // pow(ndv, power): 1.0 where face meets camera (center of orb),
                // falls toward 0 at the silhouette rim.
                // Low power → wide solid area.  High power → tight hot-spot.
                float core = pow(ndv, _FresnelPower);

                // ── Rim aura ──────────────────────────────────────────────────
                // A small additive rim so the edge isn't perfectly invisible —
                // gives the orb a slight halo outline.
                float rim = pow(1.0 - ndv, 3.0) * _EdgeSoftness;

                // ── Pulse ─────────────────────────────────────────────────────
                float pulse = (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5) * _PulseStrength;

                // ── Final alpha ───────────────────────────────────────────────
                // Each term only adds — no subtraction fighting itself.
                float alpha = saturate(core + rim + pulse * 0.12);

                // ── Color ─────────────────────────────────────────────────────
                // CoreColor at center, EdgeColor at rim, pulse brightens emission.
                float3 col = lerp(_EdgeColor.rgb, _CoreColor.rgb, core);
                col *= 1.0 + pulse * 0.5;

                return half4(col, alpha);
            }

            ENDHLSL
        }
    }
}
