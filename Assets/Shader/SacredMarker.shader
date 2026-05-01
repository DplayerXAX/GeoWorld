Shader "Custom/SacredMarker"
{
    Properties
    {
        // Bright beacon at the sphere's centre
        _CoreColor     ("Core Color",       Color) = (0.4, 0.95, 1.0, 1)
        // Pale translucent aerogel body
        _HazeColor     ("Haze Color",       Color) = (0.2, 0.55, 1.0, 1)

        // Higher = tighter bright centre.  1.0–2.5 covers orb → pinpoint.
        _CorePower     ("Core Tightness",   Float) = 1.4
        _CoreOpacity   ("Core Opacity",     Float) = 0.92

        // Inner glow — wider than the core, bleeds outward through the haze.
        // Keep _GlowPower < _CorePower so it spreads further.
        _GlowPower     ("Glow Width",       Float) = 0.45
        _GlowStrength  ("Glow Strength",    Float) = 0.45

        // Edge haze: Fresnel-style scatter at the silhouette (aerogel rim).
        _HazePower     ("Haze Rim Power",   Float) = 1.6
        _HazeStrength  ("Haze Strength",    Float) = 0.30

        // Extra additive emission on top of _CoreColor at the hot-spot.
        _EmissionBoost ("Emission Boost",   Float) = 1.8

        _PulseSpeed    ("Pulse Speed",      Float) = 1.5
        _PulseStrength ("Pulse Strength",   Float) = 0.18
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off   // double-sided so the sphere reads as a solid volume

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

            float4 _CoreColor, _HazeColor;
            float  _CorePower,  _CoreOpacity;
            float  _GlowPower,  _GlowStrength;
            float  _HazePower,  _HazeStrength;
            float  _EmissionBoost;
            float  _PulseSpeed, _PulseStrength;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS  = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS    = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                // abs() makes back-faces read as front-faces → closed, solid-looking volume
                float  ndv     = abs(dot(normalize(i.normalWS), viewDir));

                float pulse = (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5) * _PulseStrength;

                // ── Core beacon: tight bright spot at the visible centre ──────────
                float core = pow(ndv, _CorePower);

                // ── Inner glow: wider halo bleeding outward from the core ─────────
                float glow = pow(ndv, _GlowPower);

                // ── Aerogel rim haze: peaks at silhouette (reverse Fresnel) ───────
                float haze = pow(1.0 - ndv, _HazePower) * _HazeStrength;

                // ── Combined alpha ────────────────────────────────────────────────
                // Core drives solid centre, glow adds translucent body, haze the rim.
                float alpha = saturate(core * _CoreOpacity
                                     + glow  * _GlowStrength
                                     + haze
                                     + pulse * 0.12);

                // ── Color ─────────────────────────────────────────────────────────
                // Blend from _HazeColor (aerogel body) to _CoreColor (beacon centre).
                float3 col = lerp(_HazeColor.rgb, _CoreColor.rgb, glow);

                // Additive emission boost at the core hot-spot — drives HDR bloom.
                col += _CoreColor.rgb * core * _EmissionBoost * (1.0 + pulse * 0.6);

                return half4(col, alpha);
            }

            ENDHLSL
        }
    }
}
