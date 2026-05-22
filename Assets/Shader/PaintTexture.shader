Shader "GeoWorld/PaintTexture"
{
    // Abstract-painting post-process — replaces the original micro-jitter
    // "filter" approach with a large-scale fBM flow field that warps colour
    // regions into wet, gestural masses. Adds a directional brush layer
    // and soft posterization to flatten the image into painted patches.
    Properties
    {
        [Header(Flow Field)]
        _FlowScale       ("Flow Scale (low = huge regions)", Range(0.3, 20)) = 3.0
        _FlowStrength    ("Flow Strength (px)",              Range(0, 80))   = 28
        _FlowOctaves     ("Flow Octaves",                    Range(1, 6))    = 4
        _FlowDomainWarp  ("Flow Domain-Warp Strength",       Range(0, 1.5))  = 0.7
        _FlowTimeSpeed   ("Flow Time Speed",                 Range(0, 0.5))  = 0.03

        [Header(Brush)]
        _BrushScale      ("Brush Scale",        Float)                       = 12
        _BrushAngle      ("Brush Angle (deg)",  Range(-180, 180))            = 35
        _BrushStrength   ("Brush Strength",     Range(0, 1))                 = 0.30
        _BrushTint       ("Brush Tint",         Range(0, 1))                 = 0.30

        [Header(Flattening)]
        _PosterizeLevels ("Posterize Levels",   Range(2, 32))                = 14
        _PosterizeMix    ("Posterize Mix",      Range(0, 1))                 = 0.35

        [Header(Grain)]
        _GrainStrength   ("Grain Strength",     Range(0, 0.3))               = 0.08
        _GrainFrequency  ("Grain Frequency",    Float)                       = 1200

        [Header(Palette)]
        _Saturation      ("Saturation",         Range(0.5, 2))               = 1.20
        _Contrast        ("Contrast",           Range(0.5, 2))               = 1.08
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "PaintTexturePass"
            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _FlowScale, _FlowStrength, _FlowOctaves, _FlowDomainWarp, _FlowTimeSpeed;
            float _BrushScale, _BrushAngle, _BrushStrength, _BrushTint;
            float _PosterizeLevels, _PosterizeMix;
            float _GrainStrength, _GrainFrequency;
            float _Saturation, _Contrast;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

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

            // Multi-octave fBM. 4 octaves by default → soft, low-freq variation.
            float Fbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                int oct = (int)_FlowOctaves;
                [unroll(6)]
                for (int i = 0; i < oct; i++)
                {
                    v += a * ValueNoise(p);
                    p *= 2.07;
                    a *= 0.5;
                }
                return v;
            }

            // Two fBMs offset → produces a 2D flow direction.
            // Domain warping (fbm-of-fbm) adds curling / vortex-like motion.
            float2 FlowVector(float2 uv)
            {
                float2 p = uv * _FlowScale + _Time.y * _FlowTimeSpeed * float2(0.3, 0.11);

                // Domain warp: shift p by another fbm pair, gives curl-ish feel.
                float2 w = float2(Fbm(p + 5.2), Fbm(p + 17.3)) - 0.5;
                p += w * _FlowDomainWarp;

                float fx = Fbm(p);
                float fy = Fbm(p + 31.7);
                return float2(fx, fy) - 0.5;   // [-0.5, 0.5]
            }

            // Same directional brush layer as before, but driven by smoother fbm.
            float BrushPattern(float2 uv)
            {
                float a = _BrushAngle * 0.01745329;
                float2 d = float2(cos(a), sin(a));
                float2 q = float2(dot(uv, d), dot(uv, float2(-d.y, d.x)));
                q.x *= 0.35; q.y *= 1.8;
                q *= _BrushScale;

                float n  = Fbm(q) * 0.7;
                n       += Fbm(q * 2.3 + 7.1) * 0.3;
                return saturate(n);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                float2 ts = _BlitTexture_TexelSize.xy;

                // 1) FLOW FIELD — large-scale UV displacement.
                // Colours get carried around like wet pigment.
                float2 flow     = FlowVector(uv);
                float2 warpedUV = uv + flow * _FlowStrength * ts;

                half3 col = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, warpedUV).rgb;

                // 2) DIRECTIONAL BRUSH overlay — visible on flat areas.
                float brush  = BrushPattern(uv);
                float bDelta = brush - 0.5;
                col *= 1.0 + bDelta * _BrushStrength;
                col.r *= 1.0 + bDelta * _BrushTint;
                col.b *= 1.0 - bDelta * _BrushTint;
                col.g *= 1.0 + bDelta * _BrushTint * 0.25;

                // 3) SOFT POSTERIZE — quantise colours into painted patches.
                // Mix back with original so it's not flat-cel-shaded, just clumpier.
                half3 quantised = floor(col * _PosterizeLevels + 0.5) / _PosterizeLevels;
                col = lerp(col, quantised, _PosterizeMix);

                // 4) GRAIN — fine canvas texture.
                float grain = Hash21(uv * _GrainFrequency) * 2.0 - 1.0;
                col *= 1.0 + grain * _GrainStrength;

                // 5) SATURATION / CONTRAST.
                float lum = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(float3(lum, lum, lum), col, _Saturation);
                col = (col - 0.5) * _Contrast + 0.5;

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
