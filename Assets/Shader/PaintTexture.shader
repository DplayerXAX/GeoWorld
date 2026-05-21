Shader "GeoWorld/PaintTexture"
{
    // Fullscreen "oil paint" overlay:
    //   1) Per-pixel hash-driven UV jitter → adjacent colours smear into each
    //      other at boundaries (gives the "wet paint" feel at edges).
    //   2) Multiplicative canvas grain → high-freq texture across the screen.
    //   3) Optional saturation / contrast bump to enrich the palette.
    Properties
    {
        // Jitter is now directional (along stroke axis). Perpendicular
        // boundaries stay crisp, parallel ones get the painted smear.
        _StrokeJitter    ("Stroke Jitter (px)",  Range(0, 6))    = 1.4
        _JitterFrequency ("Jitter Frequency",    Float)          = 320

        _GrainStrength   ("Grain Strength",      Range(0, 1))    = 0.18
        _GrainFrequency  ("Grain Frequency",     Float)          = 900

        _BrushStrength   ("Brush Stroke Strength", Range(0, 1))  = 0.35
        _BrushScale      ("Brush Scale",         Float)          = 18
        _BrushAngle      ("Brush Angle (deg)",   Range(-180,180))= 35
        // Channel-asymmetric tint per brush patch → painted "color patch" feel
        // inside flat regions (each stroke is a slightly different shade).
        _BrushTint       ("Brush Tint Strength", Range(0, 1))    = 0.30

        _Saturation      ("Saturation",          Range(0.5, 2))  = 1.18
        _Contrast        ("Contrast",            Range(0.5, 2))  = 1.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "PaintTexturePass"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _StrokeJitter;
            float _JitterFrequency;
            float _GrainStrength;
            float _GrainFrequency;
            float _BrushStrength;
            float _BrushScale;
            float _BrushAngle;
            float _BrushTint;
            float _Saturation;
            float _Contrast;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth value-noise — bilinear interp of hash corners.
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i + float2(0, 0));
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // Directional, multi-octave noise that LOOKS like brush strokes.
            // Visible on smooth gradients (skybox) — unlike pure UV jitter.
            float BrushPattern(float2 uv)
            {
                float a = _BrushAngle * 0.01745329; // deg → rad
                float2 d = float2(cos(a), sin(a));
                float2 p = float2(dot(uv, d), dot(uv, float2(-d.y, d.x)));
                // Anisotropic scaling: thin & long strokes.
                p.x *= 0.35;
                p.y *= 1.8;
                p *= _BrushScale;

                float n  = ValueNoise(p) * 0.6;
                n       += ValueNoise(p * 2.1 + 7.3) * 0.25;
                n       += ValueNoise(p * 4.3 + 13.1) * 0.15;
                return n;   // ~ 0..1
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 ts = _BlitTexture_TexelSize.xy;

                float a = _BrushAngle * 0.01745329;
                float2 strokeDir = float2(cos(a), sin(a));

                // 1) Directional jitter — sampling moves ALONG the stroke axis
                //    only. Boundaries perpendicular to strokes (most cube edges)
                //    stay crisp; boundaries parallel to strokes get painted bleed.
                float h = Hash21(uv * _JitterFrequency) - 0.5;
                float2 jitter = strokeDir * h * _StrokeJitter * ts.y;
                half3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv + jitter).rgb;

                // 2) Brush pattern drives BOTH brightness and a small per-channel
                //    tint, so each stroke patch inside a flat region looks like
                //    a slightly different shade.
                float brush  = BrushPattern(uv);
                float bDelta = brush - 0.5;

                // Brightness from brush.
                col *= 1.0 + bDelta * _BrushStrength;

                // Channel-asymmetric tint — R warms while B cools (or v.v.) per
                // stroke. Strokes within a uniform colour region get visible
                // colour variation, but the underlying hue is preserved.
                float tintR = bDelta * _BrushTint;
                float tintB = -bDelta * _BrushTint;
                col.r *= 1.0 + tintR;
                col.b *= 1.0 + tintB;
                // G picks up half of each so changes feel less "channel-y".
                col.g *= 1.0 + bDelta * _BrushTint * 0.25;

                // 3) Canvas grain — high-freq multiplicative noise.
                float grain = Hash21(uv * _GrainFrequency) * 2.0 - 1.0;
                col *= 1.0 + grain * _GrainStrength;

                // 4) Saturation / contrast bump.
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(float3(lum, lum, lum), col, _Saturation);
                col = (col - 0.5) * _Contrast + 0.5;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
