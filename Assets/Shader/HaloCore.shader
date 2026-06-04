// Halo Core — painterly supernova (mirror of ChaosCore architecture), tuned
// to match the game's silkscreen block-art palette.
//
// Architecturally identical to the painterly version: camera-aligned plane,
// polar (r, θ), log-spiral phase, fbm-warped streaks, 2-tone color mix,
// soft radial taper, fresnel halo, additive bright core.
//
// What changed for the Zelda / silkscreen-block look:
//   • Palette is warm earthy gold/rust instead of neon HDR.
//   • Core blast and streak intensity brought into LDR — still the brightest
//     point on screen but no bloom blowout, no over-saturation.
//   • Paper grain overlay so the painted surface reads as hand-made.
//   • Lower spiral twist + higher arm count keeps the radial-ray reading
//     (light pours out) without sliding back into a tight inward vortex.
Shader "GeoWorld/HaloCore"
{
    Properties
    {
        [Header(Colors)]
        _CoreColor       ("Core (warm gold)",         Color) = (0.95, 0.85, 0.55, 1)
        _VoidColor       ("Outer Void",               Color) = (0.18, 0.10, 0.08, 1)
        _ColorA          ("Streak A (muted gold)",    Color) = (0.92, 0.72, 0.32, 1)
        _ColorB          ("Streak B (rust orange)",   Color) = (0.78, 0.38, 0.20, 1)

        [Header(Geometry)]
        _CoreRadius      ("Core Radius",              Range(0.05, 0.5))  = 0.20
        _CoreSoftness    ("Core Edge Softness",       Range(0.0, 0.4))   = 0.16
        _OuterFade       ("Outer Fade Start",         Range(0.5, 1.0))   = 0.80
        _OuterRadius     ("Outer Fade End",           Range(0.6, 1.2))   = 1.02

        [Header(Spiral Rays)]
        _SwirlArms       ("Ray Count",                Range(1, 16))      = 9.0
        _SwirlTwist      ("Spiral Twist",             Range(0, 12))      = 1.4
        _SwirlWarp       ("Streak Warp Amount",       Range(0, 6))       = 1.8
        _StreakSharp     ("Streak Sharpness",         Range(0.3, 6))     = 1.6
        _SpinSpeed       ("Spin Speed",               Range(-5, 5))      = 0.55

        [Header(Noise Variation)]
        _NoiseScale      ("Noise Scale",              Range(0.5, 12))    = 3.0
        _NoiseSpeed      ("Noise Drift",              Range(-3, 3))      = 0.8
        _ColorMixScale   ("Color Mix Scale",          Range(0.5, 6))     = 1.8
        _ColorContrast   ("Color Contrast",           Range(0.5, 4))     = 1.6

        [Header(Brightness)]
        _StreakIntensity ("Streak Intensity",         Range(0.3, 3))     = 1.1
        _CoreGlow        ("Core Blast Strength",      Range(0, 3))       = 1.4
        _Brightness      ("Overall Brightness",       Range(0.2, 2))     = 1.05

        [Header(Halo)]
        _FresnelPower    ("Fresnel Power",            Range(0.5, 8))     = 2.4
        _FresnelStrength ("Halo Strength",            Range(0, 3))       = 1.0

        [Header(Paper Grain)]
        _GrainAmount     ("Paper Grain",              Range(0, 0.08))    = 0.028

        [Header(Pulse Heartbeat)]
        _PulseSpeed      ("Pulse Speed",              Range(0, 6))       = 1.6
        _PulseDepth      ("Pulse Depth",              Range(0, 0.4))     = 0.10
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
                // Lower _SwirlTwist than ChaosCore so arms read as radial
                // rays bursting outward, not tight inward vortex.
                float lr   = log(max(r, 0.02));
                float spin = theta * _SwirlArms - lr * _SwirlTwist + _Time.y * _SpinSpeed;

                float2 warpUV = float2(spin * 0.15 - lr * 0.5,
                                       r * _NoiseScale + _Time.y * _NoiseSpeed * 0.08);
                float  warp   = Fbm2(warpUV);
                float  spinW  = spin + (warp - 0.5) * _SwirlWarp * 6.28;

                float streak = pow(abs(sin(spinW * 0.5)), _StreakSharp);

                // === Color mix: per-streak muted-gold ↔ rust-orange ===
                float colorN = Fbm2(warpUV * _ColorMixScale + float2(17.3, 5.1));
                float mixT   = saturate((colorN - 0.5) * _ColorContrast + 0.5);
                half3 streakCol = lerp(_ColorA.rgb, _ColorB.rgb, mixT);

                // === Radial windows (INVERTED from chaos: bright center) ===
                float outerFade = 1.0 - smoothstep(_OuterFade, _OuterRadius, r);

                // Light pours OUT — brightest near the core, fading outward.
                float radialTaper = (1.0 - smoothstep(_CoreRadius, _OuterFade, r)) * outerFade;

                // Warm core blast (LDR — bright but never blowout).
                float coreBlast = saturate(1.0 - r / max(_CoreRadius + _CoreSoftness, 1e-4));
                coreBlast = pow(coreBlast, 2.0);

                // === Compose ===
                half3 col = _VoidColor.rgb;
                col += streakCol * streak * radialTaper * _StreakIntensity;
                col += _CoreColor.rgb * coreBlast * _CoreGlow;

                // Fresnel halo at silhouette.
                float fres = pow(1.0 - saturate(dot(nWS, vWS)), _FresnelPower) * _FresnelStrength;
                col += _ColorA.rgb * fres;

                // === Paper grain ===
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
