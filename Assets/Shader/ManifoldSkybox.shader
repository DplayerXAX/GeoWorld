Shader "Custom/ManifoldSkybox"
{
    Properties
    {
        _ZenithColor   ("Zenith Color",   Color) = (0.02, 0.02, 0.06, 1)
        _HorizonColor  ("Horizon Color",  Color) = (0.06, 0.08, 0.15, 1)
        _FogColor      ("Fog/Haze Color", Color) = (0.10, 0.12, 0.22, 1)

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

            // Quantised noise → blocky stained-glass cells (the look the user liked)
            float StainedGlassFog(float3 p)
            {
                float n1 = floor(Noise(p)       * 4.0) / 4.0;
                float n2 = floor(Noise(p * 2.0) * 4.0) / 4.0 * 0.5;
                float n3 = floor(Noise(p * 4.0) * 4.0) / 4.0 * 0.25;
                return saturate(n1 + n2 + n3);
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
                float  cm  = _CombatMode;   // 0 = calm build, 1 = intense combat

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
                        float fk  = float(k);
                        float ang = _Time.y * rotSpeed * (0.11 + fk * 0.06) + fk * 0.7854;
                        float fca = cos(ang), fsa = sin(ang);
                        foldedDir.xy = float2(fca * foldedDir.x - fsa * foldedDir.y,
                                               fsa * foldedDir.x + fca * foldedDir.y);
                    }
                    foldedDir = normalize(foldedDir);
                }
                float3 sampleDir = normalize(lerp(dir, foldedDir, cm * 0.65));

                // ── Music-driven boosts ─────────────────────────────────────────
                float beat      = _BeatPulse;
                float intensity = 0.4 + _MusicIntensity * 0.6 + cm * 0.15;
                float beatBoost = 1.0 + beat * lerp(2.0, 2.5, cm);
                float gridScaleM= 1.0 + beat * 0.04 + cm * 0.08;

                // ── Sky base gradient ───────────────────────────────────────────
                float up = dir.y * 0.5 + 0.5;
                float t  = pow(saturate(up), 1.0 / _HorizonSharp);
                // Combat: deep void — dark indigo/purple.  NO red-orange (avoids bloom).
                half3 combatZenith  = lerp(_ZenithColor.rgb,  half3(0.04, 0.01, 0.20), cm);
                half3 combatHorizon = lerp(_HorizonColor.rgb, half3(0.02, 0.04, 0.22), cm);
                half3 zenith = combatZenith * (1.0 + _PitchGlow * 3.5 * up);
                half3 sky    = lerp(combatHorizon, zenith, t);

                // ── Stained-glass fog ───────────────────────────────────────────
                // Drive spatial movement faster; IFS fold already warps the panels.
                float timeSpeed = _TimeSpeed * lerp(1.0, 2.0, cm);
                float3 p        = sampleDir * 8.0 + _Time.y * timeSpeed;
                float fogLayer  = StainedGlassFog(p);

                float baseFog = HorizonFog(dir);
                float height  = dir.y * 0.5 + 0.5;
                float fogStr  = _FogStrength * lerp(1.0, 1.25, cm);  // modest increase
                float fog     = saturate(baseFog * (0.6 + fogLayer))
                              * smoothstep(0.0, 0.6, 1.0 - height)
                              * fogStr;

                half3 fogCol = lerp(_FogColor.rgb, _ZenithColor.rgb, fogLayer);
                // Combat: fog takes on crystalline deep-purple instead of orange
                fogCol = lerp(fogCol, half3(0.06, 0.02, 0.35), cm * fogLayer * 0.7);

                float3 panel   = StainedGlassPanel(p);
                float panelHue = _ColorShift + panel.x + cm * 0.30;
                fogCol = HsvShift(fogCol, panelHue);
                fogCol = SaturationBoost(fogCol, panel.z + cm * 0.25);
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
                half3 grid2Col      = lerp(combatGridCol,  half3(0.12, 0.35, 0.72), cm * 0.35);
                half3 gridCol  = combatGridCol * beatBoost * intensity;
                half3 floorCol = gridCol * half3(1.10, 1.00, 0.85);

                half3 result = sky;
                result = lerp(result, gridCol,                            sphereGrid  * lerp(0.55, 0.85, cm));
                result = lerp(result, grid2Col * beatBoost * intensity,   sphereGrid2 * 0.28);
                result = lerp(result, floorCol,                           floorGrid   * lerp(0.85, 1.10, cm));
                result = lerp(result, gridCol * 1.3,                      cubes       * lerp(0.40, 0.75, cm));
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
                    result  += half3(0.50, 0.88, 1.00) * riftGlow * 0.30;
                }

                // ── Final hue rotation ──────────────────────────────────────────
                result = HsvShift(result, _ColorShift + cm * 0.04);

                // ── Combat comfort pass ─────────────────────────────────────────
                // Pull colors toward a cool-tinted grey and slightly darken.
                // Preserves geometric structure; removes the eye-straining saturation.
                // At cm=1: ~45% closer to cool grey, ~18% darker overall.
                float luma = dot(result, float3(0.299, 0.587, 0.114));
                result = lerp(result, luma * half3(0.80, 0.84, 0.98), cm * 0.45);
                result *= lerp(1.0, 0.82, cm);

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
