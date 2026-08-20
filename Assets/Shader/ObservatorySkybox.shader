Shader "GeoWorld/ObservatorySkybox"
{
    // The night the observatory was built to look at.
    //
    // Third of the family, and it keeps the two rules that make MinigameSkybox and
    // OrderHallSkybox read as one game rather than three shaders:
    //
    //  · The ramp is BANDED, not smooth. A soft gradient would be the one airbrushed
    //    surface in a project built entirely from flat, hard-edged marks; quantising
    //    it into a few flat steps reads as both "deep sky" and "printed with a
    //    limited set of inks".
    //
    //  · Detail lives LOW. Horizon instruments, the precinct wall and the dome all
    //    sit at or below the skyline; above them the sky carries only stars, which
    //    are points rather than shapes and so never compete with what is in front.
    //
    // Where the farm sky is a sunset and the workshop is a lit interior, this one
    // INVERTS the ramp: it is darkest at the horizon and opens out toward the zenith,
    // because that is where an observatory points and what it wants you to look at.
    //
    // Stars are drawn on a HASHED LATTICE rather than from noise. A field of random
    // dots is a texture; a field with a few deliberately brighter members reads as
    // constellations, and Enlightenment's whole visual language in this game is
    // "points, some of which are joined".
    Properties
    {
        [Header(Night Ramp)]
        _SkyZenith ("Zenith", Color)      = (0.055, 0.075, 0.150, 1)
        _SkyHigh   ("Upper", Color)       = (0.075, 0.095, 0.185, 1)
        _SkyMid    ("Mid", Color)         = (0.095, 0.110, 0.200, 1)
        _SkyLow    ("Lower", Color)       = (0.120, 0.125, 0.195, 1)
        _SkyGlow   ("At Horizon", Color)  = (0.185, 0.170, 0.210, 1)
        _Bands     ("Ramp Bands", Range(3, 40)) = 10
        _GlowExtent("At Horizon Size", Range(0.02, 0.6)) = 0.14
        _RampPress ("Press Bands Down", Range(0.2, 1.5)) = 0.60

        [Header(Stars)]
        _StarColor ("Star", Color)        = (1.00, 0.97, 0.88, 1)
        _StarWarm  ("Bright Star", Color) = (1.00, 0.86, 0.62, 1)
        _StarCool  ("Cool Star", Color)   = (0.72, 0.84, 1.00, 1)
        _StarDensity("Star Lattice", Range(20, 200)) = 92
        _StarAmount ("Star Amount", Range(0, 1)) = 0.34
        _StarSize   ("Star Size", Range(0.01, 0.5)) = 0.115
        _BrightOdds ("Bright Star Odds", Range(0, 0.4)) = 0.07
        _Twinkle    ("Twinkle", Range(0, 3)) = 0.9

        [Header(Milky Way)]
        _BandColor ("Band", Color) = (0.42, 0.44, 0.62, 1)
        _BandTilt  ("Band Tilt", Range(-1, 1)) = 0.32
        _BandWidth ("Band Width", Range(0.02, 0.7)) = 0.26
        _BandAmount("Band Amount", Range(0, 1)) = 0.30
        _BandSteps ("Band Bands", Range(1, 8)) = 3

        [Header(Moon)]
        _MoonColor  ("Moon", Color) = (0.96, 0.95, 0.90, 1)
        _MoonAzimuth("Moon Azimuth", Range(0, 1)) = 0.72
        _MoonHeight ("Moon Height", Range(-0.1, 0.9)) = 0.42
        _MoonRadius ("Moon Radius", Range(0.01, 0.3)) = 0.075
        _MoonPhase  ("Moon Phase", Range(-1, 1)) = 0.45
        _MoonHalo   ("Halo Bands", Range(0, 5)) = 2

        [Header(Skyline)]
        _GroundColor ("Ground", Color)   = (0.055, 0.060, 0.085, 1)
        _RidgeColor  ("Ridge", Color)    = (0.085, 0.090, 0.125, 1)
        _StoneColor  ("Instruments", Color) = (0.135, 0.140, 0.175, 1)
        _BrassColor  ("Brass", Color)    = (0.72, 0.56, 0.26, 1)
        _Horizon     ("Horizon Height", Range(-0.3, 0.3)) = -0.055
        _RidgeRough  ("Ridge Roughness", Range(0, 0.08)) = 0.022
        _DomeAzimuth ("Dome Azimuth", Range(0, 1)) = 0.30
        _DomeSize    ("Dome Size", Range(0.02, 0.5)) = 0.135
        _PillarCount ("Sight Pillars", Range(0, 40)) = 14
        _PillarRise  ("Pillar Rise", Range(0.005, 0.15)) = 0.038
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS      : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float4 _SkyZenith, _SkyHigh, _SkyMid, _SkyLow, _SkyGlow;
            float4 _StarColor, _StarWarm, _StarCool, _BandColor, _MoonColor;
            float4 _GroundColor, _RidgeColor, _StoneColor, _BrassColor;
            float  _Bands, _GlowExtent, _RampPress;
            float  _StarDensity, _StarAmount, _StarSize, _BrightOdds, _Twinkle;
            float  _BandTilt, _BandWidth, _BandAmount, _BandSteps;
            float  _MoonAzimuth, _MoonHeight, _MoonRadius, _MoonPhase, _MoonHalo;
            float  _Horizon, _RidgeRough, _DomeAzimuth, _DomeSize, _PillarCount, _PillarRise;

            float hash11(float p) { return frac(sin(p * 127.1) * 43758.5453); }
            float hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }

            // Seamless around the dome: every term is an integer multiple of the full
            // turn, so the pattern meets itself at ang = 0/1 with no seam.
            float Wobble(float ang, float t)
            {
                return sin(ang * 6.2831853 * 3.0  + t * 0.30) * 0.55
                     + sin(ang * 6.2831853 * 7.0  - t * 0.21) * 0.30
                     + sin(ang * 6.2831853 * 13.0 + t * 0.13) * 0.15;
            }

            // Five-stop night ramp, quantised into flat bands. Same construction as the
            // other two skies — including the uneven stop spacing, so _GlowExtent
            // tightens the horizon haze without disturbing how the sky above it divides.
            half3 NightRamp(float e)
            {
                float k = saturate(e / 1.5707963);            // 0 at horizon, 1 at zenith
                k = pow(saturate(k), _RampPress);
                k = floor(k * _Bands) / max(1.0, _Bands - 1.0);

                float s1 = saturate(_GlowExtent);
                float s2 = s1 + (1.0 - s1) / 3.0;
                float s3 = s1 + (1.0 - s1) * 2.0 / 3.0;

                half3 c = lerp(_SkyGlow.rgb, _SkyLow.rgb,  saturate(k / max(1e-4, s1)));
                c = lerp(c, _SkyMid.rgb,    saturate((k - s1) / max(1e-4, s2 - s1)));
                c = lerp(c, _SkyHigh.rgb,   saturate((k - s2) / max(1e-4, s3 - s2)));
                c = lerp(c, _SkyZenith.rgb, saturate((k - s3) / max(1e-4, 1.0 - s3)));
                return c;
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirOS = IN.positionOS.xyz;   // skybox mesh is centred on the camera
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d   = normalize(IN.dirOS);
                float  ang = atan2(d.z, d.x) / 6.2831853 + 0.5;
                float  e   = asin(clamp(d.y, -1.0, 1.0));   // elevation, radians
                float  t   = _Time.y;

                half3 col = NightRamp(e);

                // ── Milky way ────────────────────────────────────────────────
                // A tilted great circle, thickened and STEPPED into a few flat
                // shells. Drawn before the stars so they sit on top of it, which is
                // the right order: the band is depth, the stars are objects.
                float3 axis = normalize(float3(_BandTilt, 1.0, -_BandTilt * 0.6));
                float  dist = abs(dot(d, axis));                     // 0 on the band
                float  bandK = 1.0 - saturate(dist / max(1e-4, _BandWidth));
                bandK = floor(bandK * _BandSteps) / max(1.0, _BandSteps);
                col = lerp(col, _BandColor.rgb, bandK * _BandAmount);

                // ── Stars ────────────────────────────────────────────────────
                // On a hashed lattice in (azimuth, elevation), so they are evenly
                // spread instead of clumping the way pure noise does — and so a
                // handful can be picked out as bright without the rest turning into
                // grain. Cells nearer the milky way get more of them.
                float2 uv   = float2(ang * _StarDensity, (e / 1.5707963) * _StarDensity * 0.5);
                float2 cell = floor(uv);
                float2 frc  = frac(uv);

                float pick = hash21(cell);
                float local = _StarAmount * (0.65 + 0.55 * bandK);
                if (pick < local)
                {
                    // Jitter inside the cell so the lattice itself never shows.
                    float2 at = float2(hash21(cell + 17.3), hash21(cell + 41.7));
                    float  r  = length(frc - at);

                    float mag  = hash21(cell + 91.1);
                    float size = _StarSize * (0.55 + mag * 0.9);
                    bool  big  = mag > 1.0 - _BrightOdds;
                    if (big) size *= 2.1;

                    // Twinkle is a STEP between two brightnesses, not a sine fade —
                    // a smoothly pulsing star is a light, a blinking one is a star.
                    float ph = hash21(cell + 5.9) * 6.2831853;
                    float tw = step(0.0, sin(t * _Twinkle + ph)) * 0.35 + 0.65;

                    half3 sc = _StarColor.rgb;
                    if (big) sc = (mag > 1.0 - _BrightOdds * 0.4) ? _StarWarm.rgb : _StarCool.rgb;

                    float core = 1.0 - step(size, r);
                    col = lerp(col, sc * tw, core);

                    // Bright members get a four-point flare, which is the one mark
                    // that says "instrument" rather than "speck".
                    if (big)
                    {
                        float2 v = frc - at;
                        float  spike = (1.0 - step(size * 0.22, abs(v.x))) * (1.0 - step(size * 2.6, abs(v.y)))
                                     + (1.0 - step(size * 0.22, abs(v.y))) * (1.0 - step(size * 2.6, abs(v.x)));
                        col = lerp(col, sc * tw, saturate(spike) * 0.55);
                    }
                }

                // ── Moon ─────────────────────────────────────────────────────
                // Flat disc, flat halo rings, and the phase cut by a second disc
                // rather than by shading — a terminator with a gradient would be the
                // only soft edge in the sky.
                float3 moonDir = normalize(float3(cos((_MoonAzimuth - 0.5) * 6.2831853),
                                                  _MoonHeight,
                                                  sin((_MoonAzimuth - 0.5) * 6.2831853)));
                float md = length(d - moonDir);
                for (int hi = (int)_MoonHalo; hi >= 1; hi--)
                {
                    float r = _MoonRadius * (1.0 + (float)hi * 0.85);
                    col = lerp(col, _MoonColor.rgb, (1.0 - step(r, md)) * (0.10 / (float)hi));
                }
                float disc = 1.0 - step(_MoonRadius, md);
                float3 shadowDir = normalize(moonDir + float3(_MoonPhase, 0.0, 0.0) * 0.5);
                float  shadow = 1.0 - step(_MoonRadius, length(d - shadowDir));
                col = lerp(col, _MoonColor.rgb, saturate(disc - shadow * step(0.02, abs(_MoonPhase))));

                // ── Skyline ──────────────────────────────────────────────────
                float ragged  = Wobble(ang * 2.0, t * 0.05) * _RidgeRough;
                float skyline = _Horizon + ragged;

                // Sight pillars: hard verticals standing on the ridge, the same
                // instruments the plot below is covered in. Drawn BEFORE the ground so
                // the ground reads as being in front of them.
                {
                    float idx  = floor(ang * _PillarCount);
                    float cellA = frac(ang * _PillarCount);
                    float rise = _PillarRise * (0.5 + hash11(idx * 3.7));
                    float wide = 0.16 + hash11(idx * 1.3) * 0.10;

                    float inCol = 1.0 - step(wide, abs(cellA - 0.5) * 2.0);
                    float inRow = step(skyline, e) * (1.0 - step(skyline + rise, e));
                    col = lerp(col, _StoneColor.rgb, inCol * inRow);
                }

                // The dome itself, on the skyline: a half-disc with a brass slit. It
                // is the one silhouette that names the place.
                {
                    float3 dm = normalize(float3(cos((_DomeAzimuth - 0.5) * 6.2831853),
                                                 skyline + _DomeSize * 0.35,
                                                 sin((_DomeAzimuth - 0.5) * 6.2831853)));
                    float2 v = float2(dot(d - dm, normalize(cross(dm, float3(0,1,0)))), d.y - dm.y);
                    float  r = length(v);
                    float dome = (1.0 - step(_DomeSize, r)) * step(-0.02, v.y);
                    float drum = (1.0 - step(_DomeSize * 0.92, abs(v.x)))
                               * step(skyline, e) * (1.0 - step(dm.y, e));
                    col = lerp(col, _StoneColor.rgb, saturate(dome + drum));

                    // Open slit, aimed up — the observatory is working.
                    float slit = (1.0 - step(_DomeSize * 0.13, abs(v.x))) * dome;
                    col = lerp(col, _BrassColor.rgb, slit);
                }

                if (e < skyline)
                {
                    float depth = saturate((skyline - e) / 0.5);
                    col = lerp(_RidgeColor.rgb, _GroundColor.rgb, 1.0 - depth);
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
