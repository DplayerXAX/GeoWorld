// Chaos Core — painterly vortex (fbm domain-warped streaks) tuned to match
// the game's silkscreen block-art palette.
//
// Architecturally identical to the painterly version: camera-aligned plane,
// polar (r, θ), log-spiral phase, fbm-warped streaks, 2-tone color mix,
// soft radial fades, fresnel rim, solid black event horizon.
//
// What changed for the Zelda / silkscreen-block look:
//   • Palette is muted earthy (dusty teal + dusty magenta) instead of neon.
//   • All intensity multipliers brought down into LDR range — no HDR boost,
//     no bloom blowout, just well-mixed pigment.
//   • A faint paper grain overlay so the painted streaks read as hand-made.
Shader "GeoWorld/ChaosCore"
{
    Properties
    {
        [Header(Colors)]
        _CoreColor       ("Event Horizon",          Color) = (0.03, 0.01, 0.06, 1)
        _VoidColor       ("Outer Void",             Color) = (0.08, 0.05, 0.18, 1)
        _ColorA          ("Streak A (dusty blue)",  Color) = (0.20, 0.25, 0.62, 1)
        _ColorB          ("Streak B (dusty rose)",  Color) = (0.62, 0.22, 0.48, 1)

        [Header(Geometry)]
        _CoreRadius      ("Core Radius",            Range(0.02, 0.5)) = 0.18
        _CoreSoftness    ("Core Edge Softness",     Range(0.0, 0.2))  = 0.04
        _OuterFade       ("Outer Fade Start",       Range(0.5, 1.0))  = 0.82
        _OuterRadius     ("Outer Fade End",         Range(0.6, 1.2))  = 1.00

        [Header(Spiral)]
        _SwirlArms       ("Swirl Arms",             Range(1, 16))     = 7.0
        _SwirlTwist      ("Spiral Twist",           Range(0, 12))     = 3.8
        _SwirlWarp       ("Streak Warp Amount",     Range(0, 6))      = 2.2
        _StreakSharp     ("Streak Sharpness",       Range(0.3, 6))    = 1.4
        _SpinSpeed       ("Spin Speed",             Range(-5, 5))     = 0.45

        [Header(Noise Variation)]
        _NoiseScale      ("Noise Scale",            Range(0.5, 12))   = 3.5
        _NoiseSpeed      ("Noise Drift",            Range(-3, 3))     = 0.6
        _ColorMixScale   ("Color Mix Scale",        Range(0.5, 6))    = 2.0
        _ColorContrast   ("Color Contrast",         Range(0.5, 4))    = 1.6

        [Header(Brightness)]
        _StreakIntensity ("Streak Intensity",       Range(0.3, 3))    = 1.0
        _CoreGlow        ("Inner Glow",             Range(0, 2))      = 0.35
        _Brightness      ("Overall Brightness",     Range(0.2, 2))    = 1.0

        [Header(Halo)]
        _FresnelPower    ("Fresnel Power",          Range(0.5, 8))    = 4.0
        _FresnelStrength ("Fresnel Strength",       Range(0, 2))      = 0.55

        [Header(Paper Grain)]
        _GrainAmount     ("Paper Grain",            Range(0, 0.08))   = 0.028

        [Header(Pulse)]
        _PulseSpeed      ("Pulse Speed",            Range(0, 6))      = 1.2
        _PulseDepth      ("Pulse Depth",            Range(0, 0.4))    = 0.08
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _VoidColor;
                float4 _ColorA;
                float4 _ColorB;

                float  _CoreRadius;
                float  _CoreSoftness;
                float  _OuterFade;
                float  _OuterRadius;

                float  _SwirlArms;
                float  _SwirlTwist;
                float  _SwirlWarp;
                float  _StreakSharp;
                float  _SpinSpeed;

                float  _NoiseScale;
                float  _NoiseSpeed;
                float  _ColorMixScale;
                float  _ColorContrast;

                float  _StreakIntensity;
                float  _CoreGlow;
                float  _Brightness;

                float  _FresnelPower;
                float  _FresnelStrength;

                float  _GrainAmount;

                float  _PulseSpeed;
                float  _PulseDepth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : NORMAL_WS;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            // --- Noise helpers (procedural, no textures) ------------------

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm2(float2 p)
            {
                float v = 0, a = 0.5;
                for (int i = 0; i < 4; i++) { v += a * Noise2(p); p *= 2.07; a *= 0.5; }
                return v;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                float3 vWS = normalize(GetCameraPositionWS() - IN.positionWS);

                // === Camera-facing plane through sphere center ===
                float3 worldUp = float3(0, 1, 0);
                float3 rightWS = normalize(cross(worldUp, vWS) + float3(1e-4, 0, 0));
                float3 upWS    = normalize(cross(vWS, rightWS));

                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 offsetWS = IN.positionWS - centerWS;

                float2 planar = float2(dot(offsetWS, rightWS), dot(offsetWS, upWS));

                float worldRadius = length(TransformObjectToWorldDir(float3(0.5, 0, 0)));
                float r     = length(planar) / max(worldRadius, 1e-4);
                float theta = atan2(planar.y, planar.x);

                // === Logarithmic spiral with fbm domain-warp ===
                float lr   = log(max(r, 0.02));
                float spin = theta * _SwirlArms - lr * _SwirlTwist + _Time.y * _SpinSpeed;

                float2 warpUV = float2(spin * 0.15 - lr * 0.5,
                                       r * _NoiseScale + _Time.y * _NoiseSpeed * 0.08);
                float  warp   = Fbm2(warpUV);
                float  spinW  = spin + (warp - 0.5) * _SwirlWarp * 6.28;

                float streak = pow(abs(sin(spinW * 0.5)), _StreakSharp);

                // === Color mix: per-streak dusty-blue ↔ dusty-rose ===
                float colorN = Fbm2(warpUV * _ColorMixScale + float2(17.3, 5.1));
                float mixT   = saturate((colorN - 0.5) * _ColorContrast + 0.5);
                half3 streakCol = lerp(_ColorA.rgb, _ColorB.rgb, mixT);

                // === Radial windows ===
                float coreMask = 1.0 - smoothstep(_CoreRadius - _CoreSoftness,
                                                  _CoreRadius + _CoreSoftness, r);
                float outerFade = 1.0 - smoothstep(_OuterFade, _OuterRadius, r);
                float radialBoost = smoothstep(_CoreRadius * 0.9, _CoreRadius * 1.6, r)
                                  * outerFade;

                // === Compose ===
                half3 swirlCol = streakCol * streak * _StreakIntensity;
                half3 col = lerp(_VoidColor.rgb, swirlCol, radialBoost);

                // Inner soft glow band just outside the horizon (LDR, not blast).
                float glowBand = smoothstep(_CoreRadius * 1.4, _CoreRadius, r)
                               * (1.0 - coreMask);
                col += streakCol * glowBand * _CoreGlow;

                // Event horizon wins.
                col = lerp(col, _CoreColor.rgb, coreMask);

                // Faint fresnel rim — keeps the orb feeling 3D, doesn't bloom out.
                float fres = pow(1.0 - saturate(dot(nWS, vWS)), _FresnelPower) * _FresnelStrength;
                col += _ColorB.rgb * fres * (1.0 - coreMask);

                // === Paper grain (subtle hand-painted surface) ===
                float grain = Hash21(planar * 240.0 + _Time.y * 0.05) - 0.5;
                col += grain * _GrainAmount;
                
                // Heartbeat pulse + master brightness.
                float pulse = 1.0 + sin(_Time.y * _PulseSpeed) * _PulseDepth;
                col *= pulse * _Brightness;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
