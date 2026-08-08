Shader "GeoWorld/MinigameSkybox"
{
    // Dusk over the Abundance farm — the horizon the Stacking Well actually sits
    // in. Colours are lifted straight from the farm decor (soil / crop / accent
    // gold / fence timber) so walking into the well doesn't change worlds.
    //
    // Two design rules hold the whole thing together:
    //
    //  · The sunset ramp is BANDED, not smooth. A soft gradient would be the one
    //    airbrushed surface in a game built entirely from flat, hard-edged marks;
    //    quantising it into a few flat steps reads as both "sunset" and "printed
    //    with a limited set of inks".
    //
    //  · Detail lives LOW. Wheat, hedgerow, windmill and sun all sit at or below
    //    the horizon, and the upper sky stays a calm ramp — that's the half the
    //    falling blocks are read against, so it stays quiet on purpose.
    Properties
    {
        [Header(Dusk Ramp)]
        _SkyZenith ("Zenith", Color)  = (0.10, 0.13, 0.32, 1)
        _SkyHigh   ("Upper", Color)   = (0.42, 0.24, 0.44, 1)
        _SkyMid    ("Mid", Color)     = (0.86, 0.40, 0.30, 1)
        _SkyLow    ("Lower", Color)   = (0.97, 0.62, 0.24, 1)
        _SkyGlow   ("At Horizon", Color) = (1.00, 0.85, 0.42, 1)
        _Bands     ("Ramp Bands", Range(3, 40)) = 11

        [Header(Sun)]
        _SunColor   ("Sun", Color) = (1.00, 0.95, 0.72, 1)
        _SunAzimuth ("Sun Azimuth", Range(0, 1)) = 0.5
        _SunHeight  ("Sun Height", Range(-0.2, 0.6)) = -0.055
        _SunRadius  ("Sun Radius", Range(0.01, 0.4)) = 0.115
        _SunCorona  ("Corona Bands", Range(0, 5)) = 3

        [Header(Clouds)]
        _CloudColor ("Cloud", Color) = (0.98, 0.72, 0.46, 1)
        _CloudLit   ("Cloud Underlight", Color) = (1.00, 0.88, 0.55, 1)
        _CloudAmount("Cloud Amount", Range(0, 1)) = 0.55
        _CloudDrift ("Cloud Drift", Range(-1, 1)) = 0.012

        [Header(Farm)]
        _WheatNear  ("Wheat Near", Color)  = (0.98, 0.80, 0.30, 1)
        _WheatFar   ("Wheat Far", Color)   = (0.62, 0.58, 0.24, 1)
        _SoilColor  ("Soil", Color)        = (0.34, 0.26, 0.18, 1)
        _HedgeColor ("Hedgerow", Color)    = (0.28, 0.24, 0.20, 1)
        _WheatLine  ("Horizon Height", Range(-0.3, 0.3)) = -0.13
        _WheatRagged("Wheat Raggedness", Range(0, 0.06)) = 0.018
        _StalkCount ("Stalk Density", Range(50, 600)) = 260
        _StalkDepth ("Stalk Reach", Range(0.01, 0.3)) = 0.075
        _Sway       ("Wind Sway", Range(0, 2)) = 0.55
        _FurrowCount("Furrow Bands", Range(0, 20)) = 7

        [Header(Windmill)]
        _MillColor   ("Windmill", Color) = (0.20, 0.16, 0.14, 1)
        _MillAzimuth ("Windmill Azimuth", Range(0, 1)) = 0.30
        _MillSize    ("Windmill Size", Range(0.02, 0.5)) = 0.12
        _MillSpin    ("Sail Spin", Range(-3, 3)) = 0.45
        _MillOn      ("Windmill On", Range(0, 1)) = 1
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
            float4 _WheatNear, _WheatFar, _SoilColor, _HedgeColor, _MillColor;
            float  _Bands, _SunAzimuth, _SunHeight, _SunRadius, _SunCorona;
            float  _CloudAmount, _CloudDrift;
            float  _WheatLine, _WheatRagged, _StalkCount, _StalkDepth, _Sway, _FurrowCount;
            float  _MillAzimuth, _MillSize, _MillSpin, _MillOn;

            float hash11(float p) { return frac(sin(p * 127.1) * 43758.5453); }

            // Seamless around the dome: every term is an integer multiple of the
            // full turn, so the pattern meets itself at ang = 0/1 with no seam.
            float Wobble(float ang, float t)
            {
                return sin(ang * 6.2831853 * 3.0  + t * 0.30) * 0.55
                     + sin(ang * 6.2831853 * 7.0  - t * 0.21) * 0.30
                     + sin(ang * 6.2831853 * 13.0 + t * 0.13) * 0.15;
            }

            // Four-stop dusk ramp, then quantised into flat bands.
            half3 DuskRamp(float e)
            {
                float k = saturate(e / 1.5707963);            // 0 at horizon, 1 at zenith
                k = pow(saturate(k), 0.62);                   // compress the warm end upward
                k = floor(k * _Bands) / max(1.0, _Bands - 1.0);

                half3 c = lerp(_SkyGlow.rgb, _SkyLow.rgb,  saturate(k * 4.0));
                c = lerp(c, _SkyMid.rgb,    saturate(k * 4.0 - 1.0));
                c = lerp(c, _SkyHigh.rgb,   saturate(k * 4.0 - 2.0));
                c = lerp(c, _SkyZenith.rgb, saturate(k * 4.0 - 3.0));
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

                half3 col = DuskRamp(e);

                // ── Cloud bands ──────────────────────────────────────────────
                // Flat, elongated, stacked low — and lit from BELOW, which is the
                // single cue that reads as "the sun is under them".
                for (int ci = 0; ci < 3; ci++)
                {
                    float fi   = (float)ci;
                    float band = 0.16 + fi * 0.13;                       // elevation of this deck
                    float w    = 0.030 + fi * 0.006;                     // thickness
                    float warp = Wobble(ang * (1.0 + fi * 0.35) + fi * 0.37, t + fi * 11.0) * 0.020;
                    float y    = e - (band + warp) - t * _CloudDrift * 0.02;

                    // Break each deck into separate clouds rather than one ring.
                    float gate = step(0.35, frac(ang * (2.0 + fi) + t * _CloudDrift + hash11(fi) * 7.0));
                    float body = (1.0 - step(w, abs(y))) * gate;

                    col = lerp(col, _CloudColor.rgb, body * _CloudAmount);
                    // Bright rim on the underside only.
                    float rim = (1.0 - step(w * 0.34, abs(y + w * 0.72))) * gate;
                    col = lerp(col, _CloudLit.rgb, rim * _CloudAmount);
                }

                // ── Sun ──────────────────────────────────────────────────────
                // Set just under the skyline so the wheat eats its lower edge —
                // a sun fully clear of the horizon reads as midday, not dusk.
                // Corona is concentric flat rings, not a falloff.
                float3 sunDir = float3(cos((_SunAzimuth - 0.5) * 6.2831853),
                                       _SunHeight,
                                       sin((_SunAzimuth - 0.5) * 6.2831853));
                float sd = length(d - normalize(sunDir));
                for (int si = (int)_SunCorona; si >= 1; si--)
                {
                    float r = _SunRadius * (1.0 + (float)si * 0.42);
                    float a = 0.20 / (float)si;
                    col = lerp(col, _SunColor.rgb, (1.0 - step(r, sd)) * a);
                }
                col = lerp(col, _SunColor.rgb, 1.0 - step(_SunRadius, sd));

                // ── Horizon geometry ─────────────────────────────────────────
                // _WheatLine sits BELOW eye level, so the camera reads as standing
                // above the field looking out over it. At 0 the wheat tops meet the
                // eye exactly and it reads as being waist-deep in the crop instead.
                float wheatTop = _WheatLine + Wobble(ang, t * 0.25) * _WheatRagged;

                // Distant hedgerow: a thin dark band riding just above the wheat.
                // Cheapest possible depth cue — one far dark layer behind one near
                // bright one, and the field suddenly has distance.
                float hedgeTop = wheatTop + 0.016 + Wobble(ang * 2.0 + 3.1, t * 0.11) * 0.010;
                float hedge = step(e, hedgeTop) * step(wheatTop, e);
                col = lerp(col, _HedgeColor.rgb, hedge);

                // ── Windmill ─────────────────────────────────────────────────
                // The farm's own landmark, in silhouette with the sails still
                // turning. More than any colour match, this is what says the well
                // is in THAT field.
                {
                    float da = ang - _MillAzimuth;
                    da -= round(da);                       // wrap to the short way round
                    float lx = da * 6.2831853 / _MillSize;
                    float ly = (e - wheatTop) / _MillSize;

                    // Tapered tower.
                    float towerW = lerp(0.30, 0.17, saturate(ly / 1.05));
                    float tower  = step(abs(lx), towerW) * step(0.0, ly) * step(ly, 1.05);
                    // Cap.
                    float cap = step(abs(lx), 0.24) * step(1.0, ly) * step(ly, 1.20);

                    // Sails: four blades on the hub, tested in rotated local space.
                    float2 h  = float2(lx, ly - 1.05);
                    float  sa = t * _MillSpin;
                    float2 r  = float2(h.x * cos(sa) - h.y * sin(sa),
                                       h.x * sin(sa) + h.y * cos(sa));
                    float blade = step(abs(r.x), 0.085) * step(abs(r.y), 0.85)
                                + step(abs(r.y), 0.085) * step(abs(r.x), 0.85);
                    blade = saturate(blade) * step(length(h), 0.95);

                    float mill = saturate(tower + cap + blade) * _MillOn;
                    col = lerp(col, _MillColor.rgb, mill);
                }

                // ── Wheat field ──────────────────────────────────────────────
                // Drawn last, over everything below its ragged edge.
                float inField = step(e, wheatTop);
                if (inField > 0.5)
                {
                    float depth = saturate((wheatTop - e) / 0.55);        // 0 at the skyline, 1 underfoot

                    // Far rows catch the low sun; near rows fall into soil shadow.
                    half3 field = lerp(_WheatNear.rgb, _WheatFar.rgb, saturate(depth * 1.6));
                    field = lerp(field, _SoilColor.rgb, saturate(depth * 1.25 - 0.45));

                    // Furrows: flat darker bands running with the rows, widening
                    // toward the viewer so the field reads as receding.
                    float furrow = step(0.62, frac(depth * _FurrowCount));
                    field = lerp(field, _SoilColor.rgb, furrow * 0.22);

                    // Stalk silhouettes, only in the top slice of the field —
                    // individual stalks are only legible near the skyline, and
                    // running them all the way down just turns to noise.
                    float sway   = sin(ang * 6.2831853 * 2.0 + t * _Sway) * 0.15;
                    float stalks = step(0.55, frac(ang * _StalkCount + sway));
                    float nearTop = 1.0 - saturate((wheatTop - e) / _StalkDepth);
                    field = lerp(field, _WheatFar.rgb, stalks * nearTop * 0.55);

                    col = field;
                }

                return half4(saturate(col), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
