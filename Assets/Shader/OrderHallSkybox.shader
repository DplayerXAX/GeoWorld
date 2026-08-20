Shader "GeoWorld/OrderHallSkybox"
{
    // The Order workshop hall — the room the Balancing Yard actually stands in.
    // Colours are lifted from 1-2's map decor (oiled steel, brass gearing, lamp
    // filament) so walking into the minigame doesn't change worlds.
    //
    // The same two rules that hold MinigameSkybox together, because they are what
    // make the two read as one game rather than two shaders:
    //
    //  · The ramp is BANDED, not smooth. A soft gradient would be the one airbrushed
    //    surface in a game built entirely from flat, hard-edged marks; quantising it
    //    into a few flat steps reads as both "lit volume" and "printed with a limited
    //    set of inks".
    //
    //  · Detail lives LOW. Gear train, machinery and lamps all sit at or below the
    //    skyline, and the upper vault stays a calm ramp — that's the half the leaning
    //    tower is read against, so it stays quiet on purpose.
    //
    // Where the farm sky says "outdoors at dusk", this one says "inside, under a
    // roof": the ramp DARKENS toward the zenith instead of deepening to a night sky,
    // and the brightest band is the working level, not the horizon.
    Properties
    {
        [Header(Hall Ramp)]
        _SkyZenith ("Vault", Color)      = (0.055, 0.065, 0.085, 1)
        _SkyHigh   ("Upper Truss", Color)= (0.115, 0.135, 0.165, 1)
        _SkyMid    ("Mid Wall", Color)   = (0.200, 0.225, 0.265, 1)
        _SkyLow    ("Lower Wall", Color) = (0.300, 0.320, 0.350, 1)
        _SkyGlow   ("Working Level", Color) = (0.470, 0.430, 0.340, 1)
        _Bands     ("Ramp Bands", Range(3, 40)) = 9
        _GlowExtent("Working Level Size", Range(0.02, 0.6)) = 0.10
        _RampPress ("Press Bands Down", Range(0.2, 1.5)) = 0.55

        [Header(Lamp)]
        _SunColor   ("Lamp", Color) = (1.00, 0.86, 0.52, 1)
        _SunAzimuth ("Lamp Azimuth", Range(0, 1)) = 0.5
        _SunHeight  ("Lamp Height", Range(-0.2, 0.6)) = 0.16
        _SunRadius  ("Lamp Radius", Range(0.01, 0.4)) = 0.055
        _SunCorona  ("Halo Bands", Range(0, 5)) = 3

        [Header(Steam)]
        _CloudColor ("Steam", Color)       = (0.30, 0.33, 0.38, 1)
        _CloudLit   ("Steam Underlight", Color) = (0.52, 0.48, 0.40, 1)
        _CloudAmount("Steam Amount", Range(0, 1)) = 0.42
        _CloudDrift ("Steam Drift", Range(-1, 1)) = 0.030

        [Header(Machinery)]
        _DeckNear   ("Machine Near", Color) = (0.185, 0.200, 0.230, 1)
        _DeckFar    ("Machine Far", Color)  = (0.130, 0.145, 0.175, 1)
        _FloorColor ("Floor", Color)        = (0.085, 0.090, 0.105, 1)
        _BrassColor ("Brass", Color)        = (0.62, 0.47, 0.21, 1)
        _DeckLine   ("Skyline Height", Range(-0.3, 0.3)) = -0.10
        _PistonCount("Piston Density", Range(6, 60)) = 22
        _PistonReach("Piston Reach", Range(0.01, 0.3)) = 0.055
        _Stroke     ("Piston Stroke", Range(0, 2)) = 0.70
        _FloorBands ("Floor Bands", Range(0, 20)) = 6

        [Header(Gear Train)]
        _GearColor  ("Gear", Color) = (0.16, 0.17, 0.20, 1)
        _GearCount  ("Gear Count", Range(0, 8)) = 5
        _GearSize   ("Gear Size", Range(0.02, 0.5)) = 0.115
        _GearSpin   ("Gear Spin", Range(-3, 3)) = 0.40
        _GearTeeth  ("Teeth", Range(6, 20)) = 12
        _GearOn     ("Gear Train On", Range(0, 1)) = 1

        [Header(Vault)]
        _TrussColor ("Truss", Color) = (0.085, 0.095, 0.115, 1)
        _TrussCount ("Truss Ribs", Range(0, 24)) = 10
        _TrussWidth ("Rib Width", Range(0.002, 0.06)) = 0.010
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
            float4 _SunColor, _CloudColor, _CloudLit;
            float4 _DeckNear, _DeckFar, _FloorColor, _BrassColor, _GearColor, _TrussColor;
            float  _Bands, _GlowExtent, _RampPress, _SunAzimuth, _SunHeight, _SunRadius, _SunCorona;
            float  _CloudAmount, _CloudDrift;
            float  _DeckLine, _PistonCount, _PistonReach, _Stroke, _FloorBands;
            float  _GearCount, _GearSize, _GearSpin, _GearTeeth, _GearOn;
            float  _TrussCount, _TrussWidth;

            float hash11(float p) { return frac(sin(p * 127.1) * 43758.5453); }

            // Seamless around the dome: every term is an integer multiple of the full
            // turn, so the pattern meets itself at ang = 0/1 with no seam.
            float Wobble(float ang, float t)
            {
                return sin(ang * 6.2831853 * 3.0  + t * 0.30) * 0.55
                     + sin(ang * 6.2831853 * 7.0  - t * 0.21) * 0.30
                     + sin(ang * 6.2831853 * 13.0 + t * 0.13) * 0.15;
            }

            // Five-stop hall ramp, then quantised into flat bands. Same construction as
            // the farm's dusk ramp — including the uneven stop spacing, so _GlowExtent
            // tightens the working level without disturbing how the wall above it
            // divides up what is left.
            half3 HallRamp(float e)
            {
                float k = saturate(e / 1.5707963);            // 0 at skyline, 1 at vault
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
                float  up  = d.y;
                float  ang = atan2(d.z, d.x) / 6.2831853 + 0.5;
                float  e   = asin(clamp(up, -1.0, 1.0));   // elevation, radians
                float  t   = _Time.y;

                half3 col = HallRamp(e);

                // ── Roof trusses ─────────────────────────────────────────────
                // Ribs converging on the vault, which is what says "there is a ceiling
                // up there" — the one cue that separates this from an outdoor sky.
                // Drawn as hard vertical marks that fade out before the skyline so they
                // never compete with the machinery.
                if (_TrussCount > 0.5)
                {
                    float rib   = abs(frac(ang * _TrussCount) - 0.5) * 2.0;
                    float onRib = 1.0 - step(_TrussWidth * _TrussCount, 1.0 - rib);
                    // Only in the upper half, strongest at the vault.
                    float high  = saturate((e - 0.35) / 1.2);
                    col = lerp(col, _TrussColor.rgb, onRib * high * 0.85);
                }

                // ── Steam decks ──────────────────────────────────────────────
                // Flat, elongated, stacked low, lit from BELOW — the same single cue
                // the farm's clouds use, except here the light under them is the work
                // lamp rather than a setting sun.
                for (int ci = 0; ci < 3; ci++)
                {
                    float fi   = (float)ci;
                    float band = 0.13 + fi * 0.11;
                    float w    = 0.026 + fi * 0.005;
                    float warp = Wobble(ang * (1.0 + fi * 0.35) + fi * 0.37, t + fi * 11.0) * 0.018;
                    float y    = e - (band + warp) - t * _CloudDrift * 0.02;

                    float gate = step(0.42, frac(ang * (2.0 + fi) + t * _CloudDrift + hash11(fi) * 7.0));
                    float body = (1.0 - step(w, abs(y))) * gate;

                    col = lerp(col, _CloudColor.rgb, body * _CloudAmount);
                    float rim = (1.0 - step(w * 0.34, abs(y + w * 0.72))) * gate;
                    col = lerp(col, _CloudLit.rgb, rim * _CloudAmount);
                }

                // ── Work lamp ────────────────────────────────────────────────
                // Hung ABOVE the skyline, unlike the farm's setting sun: a lamp that
                // sank into the floor would read as a sunset, and this room has no sky
                // to set into. Halo is concentric flat rings, not a falloff.
                float3 lampDir = float3(cos((_SunAzimuth - 0.5) * 6.2831853),
                                        _SunHeight,
                                        sin((_SunAzimuth - 0.5) * 6.2831853));
                float sd = length(d - normalize(lampDir));
                for (int si = (int)_SunCorona; si >= 1; si--)
                {
                    float r = _SunRadius * (1.0 + (float)si * 0.55);
                    float a = 0.16 / (float)si;
                    col = lerp(col, _SunColor.rgb, (1.0 - step(r, sd)) * a);
                }
                col = lerp(col, _SunColor.rgb, 1.0 - step(_SunRadius, sd));

                // ── Gear train ───────────────────────────────────────────────
                // Standing on the skyline, turning, alternating direction. Teeth are a
                // hard angular ripple on the rim rather than modelled cuts — at this
                // size a cut tooth is one pixel, and the silhouette is the whole point.
                if (_GearOn > 0.5 && _GearCount > 0.5)
                {
                    for (int gi = 0; gi < (int)_GearCount; gi++)
                    {
                        float fg  = (float)gi;
                        float az  = (fg + 0.5) / _GearCount + hash11(fg * 3.7) * 0.06;
                        float sz  = _GearSize * (0.55 + hash11(fg * 9.1) * 0.9);

                        float3 gd = float3(cos((az - 0.5) * 6.2831853),
                                           _DeckLine + sz * 0.55,
                                           sin((az - 0.5) * 6.2831853));
                        float3 od = normalize(gd);
                        float2 v  = float2(dot(d - od, normalize(cross(od, float3(0,1,0)))),
                                           d.y - od.y);
                        float  r  = length(v);
                        float  a2 = atan2(v.y, v.x);

                        // Direction alternates and speed falls with size — a train
                        // where every wheel turns the same way at the same rate is the
                        // one thing that reads instantly as fake.
                        float dir  = (gi % 2 == 0) ? 1.0 : -1.0;
                        float spin = t * _GearSpin * dir * (0.6 / max(0.05, sz));
                        float tooth = sz * (1.0 + 0.12 * step(0.5, frac((a2 + spin) * _GearTeeth / 6.2831853)));

                        float body = 1.0 - step(tooth, r);
                        float hub  = 1.0 - step(sz * 0.22, r);
                        // Spokes cut through so the wheel is visibly TURNING; a solid
                        // disc silhouette rotates invisibly.
                        float spoke = step(0.82, frac((a2 + spin) * 3.0 / 6.2831853));
                        float arm   = (1.0 - step(sz * 0.78, r)) * step(sz * 0.30, r) * spoke;

                        col = lerp(col, _GearColor.rgb, body);
                        col = lerp(col, _BrassColor.rgb, saturate(hub + arm) * body);
                    }
                }

                // ── Machinery skyline + floor ────────────────────────────────
                // NOT called `line` — that is a reserved HLSL word (the geometry-shader
                // primitive type), and using it fails with a bare "unexpected token".
                float ragged  = Wobble(ang * 2.0, t * 0.1) * 0.008;
                float skyline = _DeckLine + ragged;

                if (e < skyline)
                {
                    // Two flat depths, near and far — the same trick the wheat uses to
                    // get depth out of two inks and no shading.
                    float depth = saturate((skyline - e) / 0.5);
                    half3 deck  = lerp(_DeckNear.rgb, _DeckFar.rgb, 1.0 - depth);

                    // Floor bands: concentric flat rings, the room's equivalent of the
                    // farm's furrows. They give the lean something to be measured
                    // against without adding a single soft edge.
                    if (_FloorBands > 0.5)
                    {
                        float band = frac((skyline - e) * _FloorBands * 3.0);
                        deck = lerp(deck, _FloorColor.rgb, step(0.5, band) * 0.55);
                    }
                    col = deck;
                }

                // Pistons: hard vertical marks rising off the skyline, stroking up and
                // down out of phase. Drawn AFTER the deck so they stand in front of it.
                {
                    float idx  = floor(ang * _PistonCount);
                    float cell = frac(ang * _PistonCount);
                    float ph   = hash11(idx * 5.3);
                    float rise = _PistonReach * (0.45 + 0.55 * abs(sin(t * _Stroke + ph * 6.2831853)));
                    float wide = 0.30 + hash11(idx * 2.1) * 0.18;

                    float inCol = 1.0 - step(wide, abs(cell - 0.5) * 2.0);
                    float inRow = step(skyline, e) * (1.0 - step(skyline + rise, e));
                    col = lerp(col, _DeckNear.rgb, inCol * inRow);

                    // Brass cap on the head of the stroke — the only warm mark up here,
                    // and what makes the motion legible at a glance.
                    float cap = inCol * (1.0 - step(skyline + rise, e)) * step(skyline + rise - 0.008, e);
                    col = lerp(col, _BrassColor.rgb, cap);
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
