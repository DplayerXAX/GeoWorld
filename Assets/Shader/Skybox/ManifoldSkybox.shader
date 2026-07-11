Shader "Custom/ManifoldSkybox"
{
    Properties
    {
        _ZenithColor   ("Zenith Color",   Color) = (0.02, 0.02, 0.06, 1)
        _HorizonColor  ("Horizon Color",  Color) = (0.06, 0.08, 0.15, 1)
        _FogColor      ("Fog/Haze Color", Color) = (0.10, 0.12, 0.22, 1)
        _DamageTint ("Damage Tint", Range(0,1)) = 0
        // Synergy-activation flash (driven from BackgroundReactor.cs)
        _FlashColor  ("Flash Color",  Color)      = (1,1,1,1)
        _FlashAmount ("Flash Amount", Range(0,1)) = 0
        _GridColor     ("Grid Line Color",Color) = (0.25, 0.35, 0.65, 1)
        _GridScale     ("Grid Scale",     Float) = 12.0
        _GridThickness ("Grid Thickness", Range(0.01, 0.1)) = 0.03

        _FogStart      ("Fog Start (horizon band)", Range(0, 1)) = 0.08
        _FogDensity    ("Fog Density",    Range(0, 8)) = 3.5
        _FogStrength   ("Fog Strength",   Range(0, 1)) = 0.85

        _HorizonSharp  ("Horizon Sharpness", Range(1, 20)) = 6.0
        _TimeSpeed     ("Time Speed", Float) = 0.2

        // Music reactivity (driven from BackgroundReactor.cs)
        _BeatPulse     ("Beat Pulse",     Range(0,1)) = 0
        _MusicIntensity("Music Intensity",Range(0,1)) = 0.5
        _ColorShift    ("Color Shift",    Range(0,1)) = 0
        // Block-type hue target (0-1, rotates palette toward chord colour)
        _TypeHue       ("Type Hue",       Range(0,1)) = 0.6
        // Pitch glow (0-1, high notes → bright zenith flash)
        _PitchGlow     ("Pitch Glow",     Range(0,1)) = 0
        // Combat mode (0=calm build, 1=intense battle) — driven by BackgroundReactor
        _CombatMode    ("Combat Mode",    Range(0,1)) = 0
        // Level-clear reaction (0=normal, 1=ordered crystalline geometry) — driven by BackgroundReactor
        _ClearReact    ("Clear Reaction", Range(0,1)) = 0
        // Kill reaction: briefly YANKS combat mode back toward calm on each enemy kill
        // (opposite direction of the damage tint) — driven by BackgroundReactor
        _KillReact     ("Kill Reaction",  Range(0,1)) = 0

        // Intro reveal: 0 = flat _IntroColor, 1 = full sky. Driven by IntroDirector.
        _IntroBlend    ("Intro Blend",    Range(0,1)) = 1
        _IntroColor    ("Intro Color (single hue)", Color) = (0.12, 0.14, 0.26, 1)
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _ZenithColor, _HorizonColor, _FogColor, _GridColor;
            float _GridScale, _GridThickness;
            float _FogStart, _FogDensity, _FogStrength;
            float _HorizonSharp, _TimeSpeed;
            float _BeatPulse, _MusicIntensity, _ColorShift, _TypeHue, _PitchGlow;
            float _CombatMode;
            float _ClearReact;
            float _KillReact;
            float _DamageTint;
            half4 _FlashColor;
            float _FlashAmount;
            float _IntroBlend;
            half4 _IntroColor;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir        = IN.positionOS.xyz;
                return OUT;
            }

            // ── Hash & noise (for the stained-glass fog cells) ───────────
            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float n000 = Hash3(i + float3(0,0,0)); float n100 = Hash3(i + float3(1,0,0));
                float n010 = Hash3(i + float3(0,1,0)); float n110 = Hash3(i + float3(1,1,0));
                float n001 = Hash3(i + float3(0,0,1)); float n101 = Hash3(i + float3(1,0,1));
                float n011 = Hash3(i + float3(0,1,1)); float n111 = Hash3(i + float3(1,1,1));
                float3 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(n000,n100,u.x), lerp(n010,n110,u.x), u.y),
                            lerp(lerp(n001,n101,u.x), lerp(n011,n111,u.x), u.y), u.z);
            }

            // Voronoi cell-edge distance (F2 - F1). Small near a cell boundary, which
            // is a STRAIGHT edge between two feature points — no curved iso-contours.
            // Used for the clear-reaction crystalline facets.
            float VoronoiEdge(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                [unroll] for (int x = -1; x <= 1; x++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int z = -1; z <= 1; z++)
                {
                    float3 g = float3(x, y, z);
                    float3 c = i + g;
                    float3 o = float3(Hash3(c), Hash3(c + 19.1), Hash3(c + 43.7));
                    float3 r = g + o - f;
                    float d = dot(r, r);
                    if (d < d1)      { d2 = d1; d1 = d; }
                    else if (d < d2) { d2 = d; }
                }
                return sqrt(d2) - sqrt(d1);   // ~0 on straight cell borders
            }

            // Quantised noise → blocky stained-glass cells (the look the user liked)
            float StainedGlassFog(float3 p)
            {
                float n1 = floor(Noise(p)       * 4.0) / 4.0;
                float n2 = floor(Noise(p * 2.0) * 4.0) / 4.0 * 0.5;
                float n3 = floor(Noise(p * 4.0) * 4.0) / 4.0 * 0.25;
                return saturate(n1 + n2 + n3);
            }

            // Flooring smooth Noise() still leaves ROUNDED cell boundaries (it quantises
            // a curved value, the contour itself stays organic/blobby). This instead reads
            // one constant hash per unit cube — dead-straight cube-facet edges, no curve at
            // all — used to replace the fog layer during the clear reaction so the "ordered"
            // look doesn't inherit the fog's blobby transitions.
            float OrderedCellLayer(float3 p)
            {
                return Hash3(floor(p));
            }

            // Per-cell color modulation. Returns:
            //   x → hue offset (turns; ±0.25 = ±90° = clearly different colors)
            //   y → brightness multiplier
            //   z → extra saturation push
            //
            // Each panel carries a base hash AND animates over time, so the
            // chapel-glass palette continually re-rotates: hues drift, panels
            // breathe brighter/dimmer, jewel-tone bursts come and go.
            // Music drives the speed and amplitude — beat pulse flares all
            // panels at once, music intensity speeds up the cycling.
            float3 StainedGlassPanel(float3 p)
            {
                float3 cellId = floor(p);
                float h1 = Hash3(cellId);
                float h2 = Hash3(cellId + float3(7.3, 1.7, 4.1));
                float h3 = Hash3(cellId + float3(0.9, 5.1, 8.6));

                // Time advances faster when music is intense
                float t = _Time.y * (0.15 + _MusicIntensity * 0.55);

                // ── Hue: each panel's base offset (±0.25) plus a slow oscillation,
                //    plus a bias toward the current block-type's characteristic hue.
                float hueBase   = (h1 - 0.5) * 0.5;
                float hueOsc    = sin(t * 0.6 + h1 * 6.2832) * 0.10;
                // _TypeHue biases panels toward the chord colour; different panels
                // are pulled by different amounts (h2 as a per-panel weight 0-0.6).
                float hue       = hueBase + hueOsc + (_TypeHue - 0.5) * h2 * 0.6;

                // ── Brightness: hash baseline + per-panel breathing + global beat flash.
                float briBreath = sin(t * 0.9 + h2 * 6.2832) * 0.25;
                float bri       = (0.7 + h2 * 0.8 + briBreath) * (1.0 + _BeatPulse * 0.55);

                // ── Saturation: hash baseline + slower oscillation, jewel-tone bursts.
                float satBreath = sin(t * 0.5 + h3 * 6.2832) * 0.18;
                float satEx     = saturate(h3 * 0.45 + satBreath);

                return float3(hue, bri, satEx);
            }

            // Boost saturation by pushing colors away from their luma
            float3 SaturationBoost(float3 col, float amount)
            {
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                return luma + (col - luma) * (1.0 + amount);
            }

            // ── HSV-style hue rotation for the music color shift ─────────
            float3 HsvShift(float3 rgb, float shift)
            {
                float c = cos(shift * 6.2832);
                float s = sin(shift * 6.2832);
                float3x3 m = float3x3(
                    0.299 + 0.701*c + 0.168*s,  0.587 - 0.587*c + 0.330*s,  0.114 - 0.114*c - 0.497*s,
                    0.299 - 0.299*c - 0.328*s,  0.587 + 0.413*c + 0.035*s,  0.114 - 0.114*c + 0.292*s,
                    0.299 - 0.300*c + 1.250*s,  0.587 - 0.588*c - 1.050*s,  0.114 + 0.886*c - 0.203*s
                );
                return saturate(mul(m, rgb));
            }

            // ── 3-axis spherical grid ────────────────────────────────────
            float SphericalGrid(float3 dir, float scaleMul)
            {
                float3 d = normalize(dir);
                float scale = _GridScale * scaleMul;

                float2 uvXY = d.xy * scale;
                float2 uvXZ = d.xz * scale;
                float2 uvYZ = d.yz * scale;

                float2 gXY = abs(frac(uvXY - 0.5) - 0.5) / fwidth(uvXY);
                float2 gXZ = abs(frac(uvXZ - 0.5) - 0.5) / fwidth(uvXZ);
                float2 gYZ = abs(frac(uvYZ - 0.5) - 0.5) / fwidth(uvYZ);

                float lXY = 1.0 - min(min(gXY.x, gXY.y), 1.0 / _GridThickness);
                float lXZ = 1.0 - min(min(gXZ.x, gXZ.y), 1.0 / _GridThickness);
                float lYZ = 1.0 - min(min(gYZ.x, gYZ.y), 1.0 / _GridThickness);

                float3 w = abs(d);
                w = pow(w, 6.0);
                w /= (w.x + w.y + w.z + 1e-5);

                return saturate(lXZ * w.y + lXY * w.z + lYZ * w.x);
            }

            // ── Perspective floor grid (depth cue) ───────────────────────
            float PerspectiveFloor(float3 dir)
            {
                if (dir.y >= -0.005) return 0.0;

                float t  = -1.0 / dir.y;
                float2 xz = dir.xz * t;
                float scale = _GridScale * 0.25;
                float2 uv   = xz * scale;
                float2 g    = abs(frac(uv - 0.5) - 0.5) / fwidth(uv);
                float  ln   = 1.0 - min(min(g.x, g.y), 1.0 / _GridThickness);

                float elevFade = saturate(-dir.y * 4.0);
                float distFade = saturate(1.0 - exp(-t * 0.15));
                float distFar  = exp(-t * 0.04);
                return saturate(ln) * elevFade * distFade * distFar;
            }

            // ── Floating cube silhouettes (3D feel through the fog) ──────
            float FloatingCubes(float3 dir)
            {
                float3 d = normalize(dir);
                float result = 0.0;

                [unroll] for (int shell = 0; shell < 3; shell++)
                {
                    float depth = 2.5 + float(shell) * 2.0;
                    float3 p = d * depth + float3(0.3, 0.1, 0.7) * _Time.y * _TimeSpeed * 0.15;
                    float3 cellId  = floor(p);
                    float3 cellPos = frac(p) - 0.5;

                    float rnd = Hash3(cellId + float3(float(shell), 0, 0));
                    if (rnd < 0.74) continue;

                    float3 absP = abs(cellPos);
                    float cubeSize = 0.18 + rnd * 0.10;
                    float3 d3 = absP - cubeSize;
                    float sdf = length(max(d3, 0.0)) + min(max(d3.x, max(d3.y, d3.z)), 0.0);

                    float edge = saturate(1.0 - abs(sdf) * 22.0);
                    float weight = 1.0 - float(shell) * 0.28;
                    result = max(result, edge * weight * 0.5);
                }
                return result;
            }

            // ── Beat shockwave (radial pulse from horizon on each beat) ──
            float BeatRipple(float3 dir, float pulse)
            {
                if (pulse < 0.01) return 0.0;
                float horiz = abs(dir.y);
                float wave  = sin(horiz * 24.0 - _Time.y * 6.0) * 0.5 + 0.5;
                float band  = smoothstep(0.0, 0.5, horiz) * (1.0 - smoothstep(0.4, 0.9, horiz));
                return wave * band * pulse;
            }

            // ── Horizon fog gradient ─────────────────────────────────────
            float HorizonFog(float3 dir)
            {
                float t = abs(normalize(dir).y);
                return saturate(exp(-_FogDensity * max(0, t - _FogStart)));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                // 0 = calm build, 1 = intense combat. _KillReact yanks this back toward 0
                // for an instant on every kill — the mirror of _DamageTint's push toward
                // red/chaos on damage taken — so the fold/gradient/beat boosts below all
                // relax together instead of needing separate per-term overrides.
                float  cm  = saturate(_CombatMode - _KillReact);

                // ── Geometric IFS fold (combat warp) ───────────────────────────
                // Each iteration: abs-mirror the direction, sort axes (octahedral
                // fold → tetrahedral symmetry), then rotate by a time-driven angle.
                // This creates self-similar crystalline distortion — not random noise.
                // At cm=0 the lerp cancels it entirely; at cm=1 space visibly fractures.
                float3 foldedDir = dir;
                {
                    float rotSpeed = lerp(0.0, 0.85, cm);
                    [unroll] for (int k = 0; k < 4; k++)
                    {
                        foldedDir = abs(foldedDir);
                        // Sort so greatest axis is x (octahedral fold)
                        if (foldedDir.x < foldedDir.y) { float tmp = foldedDir.x; foldedDir.x = foldedDir.y; foldedDir.y = tmp; }
                        if (foldedDir.x < foldedDir.z) { float tmp = foldedDir.x; foldedDir.x = foldedDir.z; foldedDir.z = tmp; }
                        float fk   = float(k);
                        float ang = _Time.y * rotSpeed * (0.11 + fk * 0.06) + fk * 0.7854;
                        float fca = cos(ang), fsa = sin(ang);
                        foldedDir.xy = float2(fca * foldedDir.x - fsa * foldedDir.y,
                                               fsa * foldedDir.x + fca * foldedDir.y);
                    }
                    foldedDir = normalize(foldedDir);
                }
                float3 sampleDir = normalize(lerp(dir, foldedDir, cm * 0.65));

                // ── Clear reaction: ORDERED octahedral kaleidoscope ────────────
                // The mirror of the combat fracture — a STABLE (unrotating) octahedral
                // fold that snaps the sky into clean, symmetric crystalline facets, so
                // on level clear the whole sky reorganises into geometric order.
                float cr = _ClearReact;
                if (cr > 0.001)
                {
                    float3 od = dir;
                    [unroll] for (int ko = 0; ko < 4; ko++)
                    {
                        od = abs(od);
                        if (od.x < od.y) { float tmp = od.x; od.x = od.y; od.y = tmp; }
                        if (od.x < od.z) { float tmp = od.x; od.x = od.z; od.z = tmp; }
                        if (od.y < od.z) { float tmp = od.y; od.y = od.z; od.z = tmp; }
                        const float a = 0.39269908;   // fixed 22.5° — geometric, not animated
                        float c = cos(a), s = sin(a);
                        od.xy = float2(c * od.x - s * od.y, s * od.x + c * od.y);
                    }
                    od = normalize(od);
                    sampleDir = normalize(lerp(sampleDir, od, cr));   // full fold → clean symmetry
                }

                // ── Music-driven boosts ─────────────────────────────────────────
                float beat      = _BeatPulse;
                float intensity = 0.4 + _MusicIntensity * 0.6 + cm * 0.15;
                float beatBoost = 1.0 + beat * lerp(2.0, 2.5, cm);
                float gridScaleM= 1.0 + beat * 0.04 + cm * 0.08;

                // ── Sky base gradient ───────────────────────────────────────────
                float up = dir.y * 0.5 + 0.5;
                float t  = pow(saturate(up), 1.0 / _HorizonSharp);
                // Combat: deep void — dark indigo/purple.  NO red-orange (avoids bloom).
               half3 damageRed =
    half3(0.35, 0.02, 0.04);

half3 combatZenith =
    lerp(
        lerp(_ZenithColor.rgb,
             half3(0.04,0.01,0.20),
             cm),
        damageRed,
        _DamageTint
    );

half3 combatHorizon =
    lerp(
        lerp(_HorizonColor.rgb,
             half3(0.02,0.04,0.22),
             cm),
        damageRed * 0.6,
        _DamageTint
    );
                // Clear reaction: push the base palette itself toward warm gold — not
                // just an overlay — so the whole sky visibly turns golden, not only
                // its facet lines.
                combatZenith  = lerp(combatZenith,  half3(0.12, 0.08, 0.03), cr * 0.5);
                combatHorizon = lerp(combatHorizon, half3(0.62, 0.44, 0.16), cr * 0.55);

                half3 zenith = combatZenith * (1.0 + _PitchGlow * 3.5 * up);
                half3 sky    = lerp(combatHorizon, zenith, t);

                // ── Stained-glass fog ───────────────────────────────────────────
                // Drive spatial movement faster; IFS fold already warps the panels.
                float timeSpeed = _TimeSpeed * lerp(1.0, 2.0, cm);
                float3 p        = sampleDir * 8.0 + _Time.y * timeSpeed;
                float fogLayer  = StainedGlassFog(p);
                // Clear: swap the blobby noise-quantised layer for the hard cube-cell one —
                // same grid the panel hues already use, so fog density and panel colour
                // share dead-straight facet edges instead of soft round blob boundaries.
                fogLayer = lerp(fogLayer, OrderedCellLayer(p), cr);

                float baseFog = HorizonFog(dir);
                float height  = dir.y * 0.5 + 0.5;
                float fogStr  = _FogStrength * lerp(1.0, 1.25, cm);  // modest increase
                float fog     = saturate(baseFog * (0.6 + fogLayer))
                              * smoothstep(0.0, 0.6, 1.0 - height)
                              * fogStr;
                fog *= 1.0 - cr * 0.35;   // clear: thin the haze a bit, but keep enough for panel variety to read

                half3 fogCol = lerp(_FogColor.rgb, _ZenithColor.rgb, fogLayer);
                // Combat: fog takes on crystalline deep-purple instead of orange
                fogCol = lerp(fogCol, half3(0.06, 0.02, 0.35), cm * fogLayer * 0.7);

                float3 panel   = StainedGlassPanel(p);
                // Clear: keep panels in a GOLD FAMILY instead of one identical hue — each
                // panel's hash (panel.x) offsets it a little toward bronze/amber/champagne,
                // so neighbouring facets still contrast against each other instead of
                // reading as one flat sheet of yellow.
                float goldBandHue = 0.085;                         // amber-gold centre (not pure yellow)
                panel.x = lerp(panel.x, goldBandHue + (panel.x - 0.5) * 0.05, cr);
                panel.y = saturate((panel.y - 0.5) * (1.0 + cr * 0.9) + 0.5 + cr * 0.10);
                // Clear: fade _ColorShift's contribution out too — same reasoning as the
                // final hue rotation below, it's what was pulling fogCol off gold.
                float panelHue = (_ColorShift + panel.x + cm * 0.30) * (1.0 - cr) + panel.x * cr;
                fogCol = HsvShift(fogCol, panelHue);
                // Clear: a touch more saturation than a flat metallic read, short of
                // neon-pigment territory.
                fogCol = SaturationBoost(fogCol, panel.z + cm * 0.25 + cr * 0.12);
                fogCol *= panel.y * lerp(1.0, 1.12, cm);

                sky = lerp(fogCol, sky, 1.0 - fog);

                // ── Geometry ───────────────────────────────────────────────────
                float sphereGrid = SphericalGrid(dir, gridScaleM) * (1.0 - fog * 0.55);
                float floorGrid  = PerspectiveFloor(dir);
                float cubes      = FloatingCubes(dir) * (1.0 - fog * 0.4);
                float ripple     = BeatRipple(dir, beat);

                // Combat: second grid at golden-ratio scale, slowly rotating →
                // geometric moiré interference that reads as "dimensional overlap".
                float sphereGrid2 = 0.0;
                if (cm > 0.01)
                {
                    float rotA = _Time.y * cm * 0.18;
                    float gca  = cos(rotA), gsa = sin(rotA);
                    float3 rotD = dir;
                    rotD.xy = float2(gca * rotD.x - gsa * rotD.y, gsa * rotD.x + gca * rotD.y);
                    rotD.xz = float2(gca * rotD.x - gsa * rotD.z, gsa * rotD.x + gca * rotD.z);
                    sphereGrid2 = SphericalGrid(rotD, gridScaleM * 1.618)
                                * (1.0 - fog * 0.6) * cm;
                }

                // Grid color: muted teal in combat — present but not demanding attention
                half3 combatGridCol = lerp(_GridColor.rgb, half3(0.08, 0.48, 0.58), cm * 0.55);
                // Clear: pull the grid lines gold too, so they read WITH the sky instead
                // of leaving a leftover cool teal cast fighting the warm tint.
                combatGridCol = lerp(combatGridCol, half3(0.95, 0.78, 0.40), cr * 0.6);
                half3 grid2Col      = lerp(combatGridCol,  half3(0.12, 0.35, 0.72), cm * 0.35);
                half3 gridCol  = combatGridCol * beatBoost * intensity;
                half3 floorCol = gridCol * half3(1.10, 1.00, 0.85);

                half3 result = sky;
                result = lerp(result, gridCol,                            sphereGrid  * lerp(0.55, 0.85, cm));
                result = lerp(result, grid2Col * beatBoost * intensity,   sphereGrid2 * 0.28);
                result = lerp(result, floorCol,                            floorGrid   * lerp(0.85, 1.10, cm));
                result = lerp(result, gridCol * 1.3,                        cubes       * lerp(0.40, 0.75, cm));
                result += gridCol * ripple * lerp(0.5, 1.0, cm);

                // ── Geometric orbit-trap rings (rift-tear lines) ────────────────
                // Five tilted ring systems rotate at different rates and frequencies.
                // Individually: latitude circles on a rotated sphere.
                // Together: overlapping tilted great-circle families that feel like
                // geometry tearing open — the Mewgenics rift aesthetic.
                if (cm > 0.01)
                {
                    float riftGlow = 0.0;
                    [unroll] for (int ri = 0; ri < 5; ri++)
                    {
                        float fri  = float(ri);
                        // Each system gets a unique initial tilt (2π/5 spacing) and speed
                        float rang = _Time.y * cm * (0.18 + fri * 0.11) + fri * 1.2566;
                        float rca  = cos(rang), rsa = sin(rang);
                        float3 rd  = dir;   // use raw dir so rings are in world space
                        rd.xy = float2(rca * rd.x - rsa * rd.y, rsa * rd.x + rca * rd.y);
                        rd.xz = float2(rca * rd.x - rsa * rd.z, rsa * rd.x + rca * rd.z);
                        rd = normalize(rd);
                        // Latitude circles in this rotated frame
                        float phi   = asin(clamp(rd.y, -1.0, 1.0));
                        float ringN = 2.0 + fri * 0.5;   // increasing density per system
                        float ringD = abs(frac(phi * ringN / 3.14159 + 0.5) - 0.5) * 2.0;
                        riftGlow   += pow(max(0.0, 1.0 - ringD * 8.0), 3.0) * exp(-fri * 0.5);
                    }
                    riftGlow = saturate(riftGlow) * cm * (0.40 + beat * 0.30);
                    // Ring color: dim blue-white — structural, not glaring
                    result   += half3(0.50, 0.88, 1.00) * riftGlow * 0.30;
                }

                // ── Final hue rotation ──────────────────────────────────────────
                // Clear: fade out the ambient music-driven hue drift — otherwise
                // _ColorShift keeps rotating the whole sky regardless of cr and the
                // "gold" target below ends up fighting an arbitrary hue (that's what
                // was reading as a random pink/brown instead of gold).
                result = HsvShift(
                    result,
                    (_ColorShift + cm * 0.04) * (1.0 - cr)
                );
                
                // ── Combat comfort pass ─────────────────────────────────────────
                // Pull colors toward a cool-tinted grey and slightly darken.
                // Preserves geometric structure; removes the eye-straining saturation.
                // At cm=1: ~45% closer to cool grey, ~18% darker overall.
                float luma = dot(result, float3(0.299, 0.587, 0.114));
                result = lerp(result, luma * half3(0.80, 0.84, 0.98), cm * 0.45);
                result *= lerp(1.0, 0.82, cm);

                // ── Clear-reaction harmony pass ─────────────────────────────────
                // A single flat target colour reads as "yellow paint", not gold — real
                // gold is a GRADIENT: dark bronze in the shadows, warm (slightly
                // desaturated, almost white) highlights. Interpolate the target itself
                // by luma instead of tinting a fixed colour, and keep enough of the
                // upstream per-panel hue (from the fogCol pass above) that neighbouring
                // facets still contrast — this pass shapes tone, it doesn't flatten hue.
                if (cr > 0.001)
                {
                    float crS = cr * cr * (3.0 - 2.0 * cr);   // smoothstep(0,1,cr)

                    float oluma = dot(result, float3(0.299, 0.587, 0.114));
                    oluma = saturate((oluma - 0.5) * 1.3 + 0.5);   // contrast, no extra brighten here

                    half3 bronze    = half3(0.42, 0.26, 0.09);   // shadow gold — deep, coppery
                    // Brighter, more translucent-glass highlight — "金盈剔透" reads through
                    // a hotter, near-white-gold top end rather than a matte mid-gold.
                    half3 highlight = half3(1.55, 1.32, 0.86);
                    half3 goldTone  = lerp(bronze, highlight, oluma);

                    half3 ordered = lerp(result, goldTone, 0.55);   // keep 45% of upstream hue → panels still contrast
                    result = lerp(result, ordered, crS * 0.75);
                    result = SaturationBoost(result, crS * 0.15);
                    result *= 1.0 + crS * 0.12;

                    // ── Central crystal core ─────────────────────────────────────
                    // A radiant gem sitting at the zenith, with straight star-rays
                    // fanning out — gives the ordered facets something to visually
                    // radiate FROM, the "中心晶体" focal point, instead of reading as
                    // a flat all-over tint with no source.
                    float3 coreDir = float3(0.0, 1.0, 0.0);
                    float  coreDot = saturate(dot(dir, coreDir));
                    float  core    = pow(coreDot, 5.0);                 // tight hot core near the top
                    float  coreAng = atan2(dir.z, dir.x) + _Time.y * 0.05;
                    float  rays    = pow(saturate(cos(coreAng * 6.0)), 10.0) * pow(coreDot, 1.5);
                    half3  coreGlow = half3(1.7, 1.5, 1.05) * (core * 1.1 + rays * 0.6) * crS;
                    result += coreGlow;
                }

                // ── 全全局伤害滤镜 (Damage Tint Pass Fix) ──────────────────────────
                // 基于最终画面的亮度计算出一个兼顾结构亮度的血红/深红调色盘
                float finalLuma = dot(result, float3(0.299, 0.587, 0.114));
                // 将原画面压至红黑色调，同时保留发光的线条和几何体质感
                half3 damagePalette = half3(result.r * 1.3 + finalLuma * 0.3, result.g * 0.1, result.b * 0.12);
                result = lerp(result, damagePalette, _DamageTint);

                // ── Theme flash (synergy activation) ──────────────────────────
                // Wash the whole sky toward a theme colour; brightness follows the
                // scene luma so the grid / geometry still reads through the flash.
                float flashLuma    = dot(result, float3(0.299, 0.587, 0.114));
                half3 flashPalette = _FlashColor.rgb * (0.35 + flashLuma * 1.1);
                result = lerp(result, flashPalette, _FlashAmount);

                // ── Intro reveal: single-hue monochrome → full-colour sky ──────
                // At blend 0 the sky keeps its light/dark STRUCTURE but rendered in
                // one palette hue (_IntroColor); as blend → 1 the real colours bloom
                // back in. Reads as a natural desaturate → saturate reveal.
                float introLuma = dot(result, float3(0.299, 0.587, 0.114));
                half3 introMono = _IntroColor.rgb * (0.25 + introLuma * 1.7);
                result = lerp(introMono, result, saturate(_IntroBlend));

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}