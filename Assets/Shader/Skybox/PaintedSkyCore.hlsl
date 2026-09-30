#ifndef GEOWORLD_PAINTED_SKY_CORE_INCLUDED
#define GEOWORLD_PAINTED_SKY_CORE_INCLUDED

// The painted sky's brushwork as a function of direction, shared by the skybox
// (Skybox/PaintedSky.shader) and anything that shows the sky along another ray —
// the lake mirroring it (SkyLake.shader). Both read the same uniforms: the
// skybox's from its own material, the lake's copied from the skybox each frame
// (SkyLake.cs), so the reflection reacts with the sky — combat, clear, flashes.
// Needs Core.hlsl (and _MainLightPosition, which URP always sets).

            half4 _Deep, _Blue, _Teal, _Cream, _Warm, _Hot, _Ground, _Sun, _FlashColor, _IntroColor;
            float _SunSize, _StrokeScale, _StrokeLength, _StrokeWidth, _Swirl, _Warmth, _Layers;
            float _BeatPulse, _MusicIntensity, _PitchGlow, _CombatMode, _ClearReact, _KillReact, _Collapse;
            float _DamageTint, _FlashAmount, _IntroBlend;

            #define WHIRLS 5
            // Canvas: x = azimuth × K, y = elevation, both in radians. K = cos 20°,
            // so a stroke is about as wide as it is tall in the low sky, where the
            // camera mostly looks. X is the canvas's width: it wraps.
            static const float K = 0.9397;
            static const float X = 6.2831853 * 0.9397;

            float hash12(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float2 hash22(float2 p)
            {
                float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                q += dot(q, q.yzx + 33.33);
                return frac((q.xx + q.yz) * q.zy);
            }

            // A difference on the canvas, taken the short way round in azimuth.
            float2 Wrap(float2 v) { v.x -= X * round(v.x / X); return v; }

            // Value noise whose lattice repeats every `per` cells in x. Sample it
            // at x × (per / X) and it wraps exactly once round the sky.
            float PNoise(float2 p, float per)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float i0 = i.x - per * floor(i.x / per);
                float i1 = i0 + 1.0;
                i1 -= per * floor(i1 / per);
                float a = hash12(float2(i0, i.y)),       b = hash12(float2(i1, i.y));
                float c = hash12(float2(i0, i.y + 1.0)), d = hash12(float2(i1, i.y + 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // The whirls: vortices drifting slowly round the sky. xy = centre on the
            // canvas, z = radius, w = spin (its sign is the direction).
            float4 Whirl(int k, float t)
            {
                float fk = (float)k;
                float x  = frac(0.13 + fk * 0.618 + t * 0.004 * (1.0 + fk * 0.3)) * X;
                float y  = 0.30 + 0.48 * frac(0.37 + fk * 0.414) + 0.05 * sin(t * 0.07 + fk * 2.0);
                float r  = 0.34 + 0.26 * frac(fk * 0.73 + 0.2);
                float s  = ((k & 1) == 0 ? 1.0 : -1.0) * (0.8 + 0.4 * frac(fk * 0.31));
                return float4(x, y, r, s);
            }

            // The direction strokes are laid in: a meandering drift, wound round each
            // whirl, and always ringed round the sun. As the level is cleared, all of
            // it turns to rings about the sun and rays beyond them.
            float2 Flow(float2 p, float2 sun, float4 W[WHIRLS], float t, float cm, float cr)
            {
                float a = (PNoise(float2(p.x * (6.0 / X), p.y * 1.2 + t * 0.02), 6.0) - 0.5) * (2.0 + cm * 3.0) + 0.3;
                float2 f = float2(cos(a), sin(a) * 0.6);
                [unroll] for (int k = 0; k < WHIRLS; k++)
                {
                    float2 r = Wrap(p - W[k].xy);
                    float d2 = dot(r, r);
                    float g  = exp(-d2 / (W[k].z * W[k].z)) * W[k].w * _Swirl * (1.0 + cm * 1.6);
                    f += float2(-r.y, r.x) / (sqrt(d2) + 0.08) * g;
                }
                float2 rs = Wrap(p - sun);
                float  ds = length(rs);
                float2 ring = float2(-rs.y, rs.x) / (ds + 1e-3);
                f += ring * exp(-ds * 2.2) * 3.0;

                float2 ray = rs / (ds + 1e-3);
                float2 ordered = lerp(ring, ray, smoothstep(0.45, 0.9, ds));
                f = lerp(normalize(f + 1e-4), ordered, cr);
                return normalize(f + 1e-4);
            }

            // Where on the palette a stroke's paint sits: 0 deep blue ... 1 orange.
            // High sky deep and low sky light. Bands of light and dark wind round each
            // whirl, and the sun is ringed in cream and yellow. Combat pulls it all
            // toward the dark end; clearing lifts it into gold.
            float PaintIndex(float2 q, float2 sun, float4 W[WHIRLS], float t, float cm, float cr, float jitter)
            {
                float e = saturate(q.y / 1.2);
                float v = lerp(0.62, 0.12, pow(e, 0.7));
                v += _Warmth * pow(1.0 - saturate(q.y / 0.45), 2.0) * 0.28;
                [unroll] for (int k = 0; k < WHIRLS; k++)
                {
                    float d = length(Wrap(q - W[k].xy));
                    float g = exp(-d * d / (W[k].z * W[k].z * 1.6));
                    v += g * 0.16 * sin(d * 26.0 / W[k].z - t * (0.3 + cm) * W[k].w);
                }
                v += (PNoise(float2(q.x * (12.0 / X), q.y * 3.0), 12.0) - 0.5) * 0.22;

                float ds    = length(Wrap(q - sun));
                float zone  = saturate(exp(-ds * 3.0) * 1.6);
                float rings = 0.5 + 0.5 * sin(ds * 38.0 - t * 0.8 * (1.0 + _BeatPulse));
                v = lerp(v, 0.72 + rings * 0.22, zone);

                v += (jitter - 0.5) * 0.08;
                v = lerp(v, v * 0.55, cm * 0.6);
                v = lerp(v, 0.6 + v * 0.4, cr * 0.7);
                return saturate(v);
            }

            float3 Palette(float v, float cm, float cr)
            {
                float3 deep = _Deep.rgb, blue = _Blue.rgb, teal = _Teal.rgb, cream = _Cream.rgb;
                // Storm: night indigo and violet, the cream going to ash.
                deep  = lerp(deep,  float3(0.05, 0.03, 0.18), cm * 0.8);
                blue  = lerp(blue,  float3(0.14, 0.10, 0.42), cm * 0.7);
                teal  = lerp(teal,  float3(0.30, 0.24, 0.55), cm * 0.6);
                cream = lerp(cream, float3(0.78, 0.70, 0.78), cm * 0.4);
                // Cleared: gold through and through.
                deep  = lerp(deep,  float3(0.55, 0.30, 0.10), cr * 0.75);
                blue  = lerp(blue,  float3(0.85, 0.55, 0.18), cr * 0.70);
                teal  = lerp(teal,  float3(0.98, 0.78, 0.35), cr * 0.70);
                cream = lerp(cream, float3(1.00, 0.94, 0.70), cr * 0.50);

                float3 c = deep;
                c = lerp(c, blue,      smoothstep(0.10, 0.28, v));
                c = lerp(c, teal,      smoothstep(0.32, 0.50, v));
                c = lerp(c, cream,     smoothstep(0.55, 0.70, v));
                c = lerp(c, _Warm.rgb, smoothstep(0.74, 0.86, v));
                c = lerp(c, _Hot.rgb,  smoothstep(0.90, 1.00, v));
                return c;
            }

            // Raw canvas, for strokes that have flaked off in a collapse.
            float3 Canvas(float2 g)
            {
                float weave = 0.5 + 0.25 * sin(g.x * 40.0) + 0.25 * sin(g.y * 40.0);
                return float3(0.86, 0.82, 0.72) * (0.92 + 0.08 * weave);
            }

            // One layer of strokes over `under`. Strokes sit one to a cell of a
            // jittered grid (`per` cells round the sky, `rows` per radian up it).
            // Each is laid along the flow at this pixel, so long strokes bend with
            // it. Half-length stays under a cell, so the 3×3 search never clips one.
            // Only the topmost stroke here is painted; its paint is looked up once.
            float3 Strokes(float3 under, float2 p, float2 flow, float2 sun, float4 W[WHIRLS], float t,
                           float cm, float cr, float cl, float rows, float per, float seed)
            {
                float2 sc  = float2(per / X, rows);
                float2 g   = p * sc;
                float2 gi  = floor(g);
                float2 nrm = float2(-flow.y, flow.x);

                float  best = -1.0, bestM = 0.0, bestU = 0.0;
                float2 bestKey = 0.0, bestCentre = 0.0;
                [unroll] for (int j = -1; j <= 1; j++)
                {
                    [unroll] for (int i = -1; i <= 1; i++)
                    {
                        float2 cell = gi + float2(i, j);
                        float2 key  = float2(cell.x - per * floor(cell.x / per), cell.y) + seed;
                        float2 h2   = hash22(key);
                        float2 r    = g - (cell + h2);
                        float along  = dot(r, flow);
                        float across = dot(r, nrm);
                        float L  = _StrokeLength * (0.72 + 0.28 * h2.x);
                        float Wd = _StrokeWidth * (0.8 + 0.4 * h2.y) * (1.0 - 0.3 * saturate(along / L));   // tapers to the tail
                        float m  = 1.0 - (along * along) / (L * L) - (across * across) / (Wd * Wd);
                        float pri = hash12(key + 3.7);
                        if (m > 0.0 && pri > best)
                        {
                            best = pri; bestM = m; bestU = across / Wd;
                            bestKey = key; bestCentre = cell + h2;
                        }
                    }
                }
                if (best < 0.0) return under;

                float v = PaintIndex(bestCentre / sc, sun, W, t, cm, cr, best);
                float3 c = Palette(v, cm, cr);
                c *= 0.92 + 0.16 * hash12(bestKey + 9.1);
                // Bristle lines along the stroke, and the impasto ridge: lit on one
                // side, in shadow on the other.
                c *= 0.93 + 0.07 * sin(bestU * 11.0 + best * 40.0);
                c *= 1.0 + 0.10 * smoothstep(0.2, 0.9, bestU) - 0.08 * smoothstep(0.3, 1.0, -bestU);
                // Combat: a few strokes burn.
                c = lerp(c, _Hot.rgb * 1.15, step(1.0 - cm * 0.22, hash12(bestKey + 5.3)));
                // Collapse: strokes flake off to the canvas under them.
                c = lerp(c, Canvas(g), step(1.0 - cl * 0.55, hash12(bestKey + 1.9)));
                return lerp(under, c, smoothstep(0.0, 0.18, bestM));
            }

            // The colour of the painted sky looking along unit direction d.
            float3 PaintedSkyColor(float3 d)
            {
                // Collapse: bands of sky slip sideways, re-rolled on a coarse clock
                // so it reads as a broken signal, not noise (as in the Manifold sky).
                float cl = saturate(_Collapse);
                if (cl > 0.001)
                {
                    float tick = floor(_Time.y * (5.0 + cl * 13.0));
                    float band = floor(d.y * (12.0 + cl * 24.0));
                    float live = step(1.0 - cl * 0.8, hash12(float2(band, tick)));
                    float slip = (hash12(float2(band + 17.0, tick)) - 0.5) * live * cl * 0.5;
                    d.xz += slip;
                    d = normalize(d);
                }

                float cm = saturate(_CombatMode - _KillReact);
                float cr = saturate(_ClearReact);
                float t  = _Time.y * (0.6 + _MusicIntensity * 0.8) * (1.0 + cm * 1.8) * (1.0 - cr * 0.6);

                float3 Ld  = normalize(_MainLightPosition.xyz);
                float2 p   = float2((atan2(d.z, d.x) + PI) * K, asin(clamp(d.y, -1.0, 1.0)));
                float2 sun = float2((atan2(Ld.z, Ld.x) + PI) * K, max(asin(clamp(Ld.y, -1.0, 1.0)), 0.05));

                // Combat: the whole painting twists round the sun, back and forth,
                // and every stroke shivers.
                if (cm > 0.001)
                {
                    float2 rs = Wrap(p - sun);
                    float tw = cm * 0.55 * sin(_Time.y * 0.6) * exp(-length(rs) * 0.9);
                    float ca = cos(tw), sa = sin(tw);
                    p = sun + float2(ca * rs.x - sa * rs.y, sa * rs.x + ca * rs.y);
                    p += cm * 0.03 * float2(PNoise(float2(p.x * (20.0 / X), p.y * 6.0 + _Time.y * 2.0), 20.0) - 0.5,
                                            PNoise(float2(p.x * (20.0 / X) + 7.0, p.y * 6.0 - _Time.y * 2.0), 20.0) - 0.5);
                }

                float4 W[WHIRLS];
                [unroll] for (int k = 0; k < WHIRLS; k++) W[k] = Whirl(k, t);

                // Underpainting, then two layers of strokes: broad ones, then finer
                // ones over them.
                float3 col  = Palette(PaintIndex(p, sun, W, t, cm, cr, 0.5), cm, cr) * 0.8;
                float2 flow = Flow(p, sun, W, t, cm, cr);
                float per1 = round(X * _StrokeScale);
                float per2 = round(X * _StrokeScale * 1.35);
                // Lower graphics presets drop the finer layer (_Layers, set per preset).
                if (_Layers > 0.5) col = Strokes(col, p, flow, sun, W, t, cm, cr, cl, _StrokeScale,        per1, 0.0);
                if (_Layers > 1.5) col = Strokes(col, p, flow, sun, W, t, cm, cr, cl, _StrokeScale * 1.35, per2, 71.3);

                // The sun: a thick disc, and light spilling round it. Cleared, it grows
                // and throws rays.
                float mu   = dot(d, Ld);
                float size = radians(_SunSize) * (1.0 + _BeatPulse * 0.25 + cr * 0.4);
                float disc = smoothstep(cos(size), cos(size * 0.55), mu);
                col = lerp(col, _Sun.rgb, disc);
                col += _Sun.rgb * pow(saturate(mu), 60.0) * 0.25;
                if (cr > 0.001)
                {
                    float2 rs = Wrap(p - sun);
                    float rays = pow(saturate(cos(atan2(rs.y, rs.x) * 12.0)), 8.0) * exp(-length(rs) * 0.8);
                    col += _Warm.rgb * rays * cr * 0.3;
                }

                // Music: high notes light the upper sky, beats lift it all a touch.
                col *= 1.0 + _PitchGlow * 0.5 * saturate(p.y / 1.2);
                col *= 1.0 + _BeatPulse * 0.12;

                // Below the horizon: into the ground colour (the land hides it anyway).
                col = lerp(col, _Ground.rgb, saturate(-d.y * 8.0));

                // Damage: pressed toward blood red, keeping the strokes' light and dark.
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                float3 damage = float3(col.r * 1.3 + luma * 0.3, col.g * 0.1, col.b * 0.12);
                col = lerp(col, damage, _DamageTint);

                // Collapse: the picture loses its colour and its grip.
                col = lerp(col, luma.xxx, cl * 0.45) * (1.0 - cl * 0.18);

                // Theme flash (synergy activation).
                float flashLuma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(col, _FlashColor.rgb * (0.35 + flashLuma * 1.1), _FlashAmount);

                // Intro reveal: one hue, then the full colour.
                float introLuma = dot(col, float3(0.299, 0.587, 0.114));
                col = lerp(_IntroColor.rgb * (0.25 + introLuma * 1.7), col, saturate(_IntroBlend));

                return col;
            }

#endif
