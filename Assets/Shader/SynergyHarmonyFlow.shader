// 和谐 (Harmony) — flowing watercolor field of greens. Two layers of
// domain-warped fbm noise drive a 5-stop green palette, producing organic
// painterly transitions with no hard edges. Subtle paint-grain on top.
//
// Reads as: balanced, breathing, hand-painted — closer to a Chinese
// landscape wash than a tiled mosaic.
Shader "GeoWorld/Synergy/HarmonyFlow"
{
    Properties
    {
        _Green1      ("Deepest Green",     Color) = (0.05, 0.18, 0.10, 1)
        _Green2      ("Forest",            Color) = (0.15, 0.40, 0.20, 1)
        _Green3      ("Sage",              Color) = (0.38, 0.65, 0.35, 1)
        _Green4      ("Mint",              Color) = (0.62, 0.85, 0.55, 1)
        _Green5      ("Highlight",         Color) = (0.88, 0.96, 0.78, 1)

        _Scale       ("Pattern Scale",     Range(0.3, 8))    = 2.2
        _WarpAmount  ("Domain Warp",       Range(0, 3))      = 1.4
        _Contrast    ("Color Contrast",    Range(0.5, 3))    = 1.3
        _GrainAmount ("Paint Grain",       Range(0, 0.15))   = 0.04
        _LowSat      ("Shadow Saturation", Range(0.5, 1.3))  = 0.95
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Green1, _Green2, _Green3, _Green4, _Green5;
                float  _Scale;
                float  _WarpAmount;
                float  _Contrast;
                float  _GrainAmount;
                float  _LowSat;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                return OUT;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float a = 0.5, v = 0;
                [unroll]
                for (int i = 0; i < 5; i++) { v += a * Noise(p); p *= 2.05; a *= 0.5; }
                return v;
            }

            // 5-stop palette lookup with smoothstep transitions.
            half3 GreenRamp(float t)
            {
                t = saturate(t);
                if (t < 0.25)      return lerp(_Green1.rgb, _Green2.rgb, smoothstep(0.00, 0.25, t));
                else if (t < 0.50) return lerp(_Green2.rgb, _Green3.rgb, smoothstep(0.25, 0.50, t));
                else if (t < 0.75) return lerp(_Green3.rgb, _Green4.rgb, smoothstep(0.50, 0.75, t));
                else               return lerp(_Green4.rgb, _Green5.rgb, smoothstep(0.75, 1.00, t));
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv = IN.uv * _Scale;

                // Two-layer domain warp — classic IQ-style "warped warp" for
                // rich painterly flow instead of plain fbm blobs.
                float2 q = float2(Fbm(uv + float2(0.0, 0.0)),
                                  Fbm(uv + float2(5.2, 1.3)));
                float2 r = float2(Fbm(uv + 4.0 * q + float2(1.7, 9.2)),
                                  Fbm(uv + 4.0 * q + float2(8.3, 2.8)));
                uv += r * _WarpAmount;

                // Base noise → contrast-shaped → palette
                float n = Fbm(uv);
                n = saturate((n - 0.5) * _Contrast + 0.5);

                half3 col = GreenRamp(n);

                // Darker areas slightly desaturated so shadows feel like wash
                // pigment rather than pure tint.
                float lum = dot(col, half3(0.299, 0.587, 0.114));
                col = lerp(half3(lum, lum, lum), col, lerp(_LowSat, 1.0, n));

                // Paint grain — high-freq dither for "brush" feel.
                float grain = (Hash(IN.uv * 1234.0) - 0.5) * _GrainAmount;
                col += grain;

                return half4(saturate(col), 1);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
