Shader "GeoWorld/BlockWeather"
{
    // An OVERLAY on a board block — an extra material slot drawn over the block's
    // own (URP Lit) material, the same trick as the outline slot. It never replaces
    // the block's look; a new, dry block with nothing to show is untouched.
    //
    // Two passes:
    //
    //   "Weather" (SRPDefaultUnlit) MULTIPLIES onto the block (detail blend: 0.5 =
    //   no change): contact shadow, age, the darkening of a wet surface.
    //
    //   "Wet" (UniversalForward) ADDS what water brings that the block's own dry
    //   material doesn't: a sharper highlight from the sun and the sky reflected in
    //   it, strongest where water stands. After Lagarde, "Water drop 2b – Dynamic
    //   rain and its effects": wet = darker diffuse + higher gloss; standing water
    //   (cracks first, then puddles) = gloss 1, water F0 0.02, a flat water normal
    //   with raindrop ripples in it.
    //
    // What it draws:
    //
    //   CONTACT SHADOW — darkening along the edges and corners of a face where a
    //     neighbouring block stands against it (voxel-style AO). _Nbr0/_Nbr1 carry
    //     which of the 26 neighbouring cells are occupied (13 bits each, exact in a
    //     float), in WORLD axes, so the block's own rotation doesn't matter.
    //
    //   AGE — _Age01 (0 new … 1 ancient, from PlacedBlockInstance.age): chipped,
    //     lighter edges; grime gathering low down; cracks; a patina on top faces in
    //     the level environment's wear tint (moss, rust, sand…).
    //
    //   WEATHER — rain streaks down the sides, corrosion pits; and RAIN proper:
    //     _GeoWetness (0 dry … 1 soaked) rises over the level's first seconds, then
    //     _GeoFlood fills the cracks and, on tops open to the sky, puddles, where
    //     the drops ripple (_GeoRain). Drips run down the sides.
    //
    // Globals (_Geo*) are set by LevelEnvironmentDriver / BlockSurface.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+450" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Nbr0;
            float _Nbr1;
            float _Age01;
            float _WearSeed;
        CBUFFER_END

        float  _GeoCellSize;
        float4 _GeoWearTint;
        float  _GeoWearStrength;
        float  _GeoCrackStrength;
        float  _GeoAOStrength;
        float  _GeoAORadius;
        float  _GeoWetness;     // current wet level, 0..1
        float  _GeoFlood;       // current standing-water level, 0..1
        float  _GeoPuddles;     // how much of an open top can puddle, 0..1
        float  _GeoRain;        // rain intensity 0..1 — ripple and drip density
        float  _GeoStreaks;
        float  _GeoCorrosion;

        struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS   : TEXCOORD1;
            float3 centerWS   : TEXCOORD2;
        };

        Varyings vert(Attributes IN)
        {
            Varyings o;
            o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
            o.centerWS   = TransformObjectToWorld(float3(0, 0, 0));   // the cube sits centred on its cell
            return o;
        }

        float hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 19.19);
            return frac((p.x + p.y) * p.z);
        }
        float2 hash22(float2 p)
        {
            float3 q = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
            q += dot(q, q.yzx + 33.33);
            return frac((q.xx + q.yz) * q.zy);
        }
        float vnoise(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            float a = hash13(i),                 b = hash13(i + float3(1,0,0));
            float c = hash13(i + float3(0,1,0)), d = hash13(i + float3(1,1,0));
            float e = hash13(i + float3(0,0,1)), g = hash13(i + float3(1,0,1));
            float h = hash13(i + float3(0,1,1)), k = hash13(i + float3(1,1,1));
            return lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y),
                        lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y), f.z);
        }
        float fbm(float3 p) { return vnoise(p) * 0.6 + vnoise(p * 2.13) * 0.3 + vnoise(p * 4.7) * 0.1; }

        // Is neighbour `idx` (0..25, x,y,z nested order, centre skipped) occupied?
        float Bit(int idx)
        {
            float word = idx < 13 ? _Nbr0 : _Nbr1;
            int   b    = idx < 13 ? idx : idx - 13;
            return fmod(floor(word / exp2(b) + 1e-3), 2.0);
        }

        // ── Raindrop ripples ─────────────────────────────────────────────────
        // Lagarde's ripple texture, made procedurally: per tile one drop at a
        // random spot. R = strength falling off from the drop's centre, GB = the
        // direction out from it, A = the drop's random time offset.
        float4 RippleTex(float2 uv)
        {
            float2 cell = floor(uv);
            float2 f    = frac(uv);
            float2 h    = hash22(cell);
            float2 d    = f - (0.28 + 0.44 * h);
            float  r    = length(d);
            float  x    = saturate(1.0 - r / 0.28);
            float2 dir  = r > 1e-4 ? d / r : float2(0, 0);
            return float4(x, dir, hash22(cell + 17.3).x);
        }

        float3 ComputeRipple(float2 uv, float t, float weight)
        {
            float4 R = RippleTex(uv);
            float dropFrac  = frac(R.w + t);
            float timeFrac  = dropFrac - 1.0 + R.x;
            float dropFac   = saturate(0.2 + weight * 0.8 - dropFrac);
            float finalFac  = dropFac * R.x * sin(clamp(timeFrac * 9.0, 0.0, 3.0) * PI);
            return float3(R.yz * finalFac * 0.35, 1.0);
        }

        // Tangent-space ripple normal (xy across the puddle, z up). Four layers,
        // each switched on by a quarter of the rain intensity.
        float3 RippleNormal(float2 uv, float rain)
        {
            float4 times   = frac((_Time.y * float4(1.0, 0.85, 0.93, 1.13) + float4(0.0, 0.2, 0.45, 0.7)) * 1.6);
            float4 weights = saturate((rain - float4(0, 0.25, 0.5, 0.75)) * 4.0);

            float3 r1 = ComputeRipple(uv + float2( 0.25, 0.0 ), times.x, weights.x);
            float3 r2 = ComputeRipple(uv + float2(-0.55, 0.3 ), times.y, weights.y);
            float3 r3 = ComputeRipple(uv + float2( 0.6,  0.85), times.z, weights.z);
            float3 r4 = ComputeRipple(uv + float2( 0.5, -0.75), times.w, weights.w);

            float4 z = lerp(1.0, float4(r1.z, r2.z, r3.z, r4.z), weights);
            return normalize(float3(weights.x * r1.xy + weights.y * r2.xy +
                                    weights.z * r3.xy + weights.w * r4.xy,
                                    z.x * z.y * z.z * z.w));
        }

        // Everything both passes need to know about this pixel.
        struct Surface
        {
            float  mult;    // multiplies the block's colour
            float3 tint;
            float  wet;     // wet sheen, 0..1
            float  water;   // standing water, 0..1
            float  gloss;
            float3 N;       // shading normal (flattened to the water where it stands)
            float  ao;
        };

        Surface Evaluate(Varyings IN)
        {
            Surface s;
            float  cs = max(_GeoCellSize, 1e-3);
            float3 N  = normalize(IN.normalWS);
            float3 an = abs(N);
            float3 Na = (an.x >= an.y && an.x >= an.z) ? float3(sign(N.x), 0, 0)
                      : (an.y >= an.z ? float3(0, sign(N.y), 0) : float3(0, 0, sign(N.z)));
            float3 d  = clamp((IN.positionWS - IN.centerWS) / cs, -0.5, 0.5);   // position within the cell
            float3 tangentMask = 1.0 - abs(Na);

            // ── Contact shadow ──────────────────────────────────────────────
            // A neighbour in the layer this face looks into, off to one side
            // (edge) or diagonally (corner), shades the part of the face nearest it.
            float ao = 0.0;
            int idx = 0;
            [unroll] for (int x = -1; x <= 1; x++)
            [unroll] for (int y = -1; y <= 1; y++)
            [unroll] for (int z = -1; z <= 1; z++)
            {
                if (x == 0 && y == 0 && z == 0) continue;
                float on = Bit(idx);
                idx++;
                if (on < 0.5) continue;

                float3 o = float3(x, y, z);
                float  k = dot(o, Na);
                if (k < 0.5) continue;                 // not in front of this face
                float3 t = o - Na * k;
                if (dot(t, t) < 0.5) continue;         // straight in front: face is buried anyway

                float r = max(_GeoAORadius, 1e-3);
                float w = 1.0;
                if (t.x != 0) w *= saturate(1.0 - (0.5 - d.x * t.x) / r);
                if (t.y != 0) w *= saturate(1.0 - (0.5 - d.y * t.y) / r);
                if (t.z != 0) w *= saturate(1.0 - (0.5 - d.z * t.z) / r);
                ao = max(ao, w);
            }
            ao = ao * ao * (3.0 - 2.0 * ao);
            s.ao = ao;
            float mult = 1.0 - _GeoAOStrength * ao;

            // ── Age ─────────────────────────────────────────────────────────
            float  a  = saturate(_Age01) * _GeoWearStrength;
            float3 p  = IN.positionWS / cs + _WearSeed * 13.7;
            float  n1 = fbm(p * 2.3);
            float  n2 = vnoise(p * 7.0);
            float3 dt = abs(d) * tangentMask;
            float  edge = 0.5 - max(dt.x, max(dt.y, dt.z));               // 0 at the face's rim

            float chip   = a * smoothstep(0.10, 0.02, edge) * step(0.45, n2);
            float grime  = a * saturate(0.5 - d.y) * (0.4 + n1) * (1.0 - saturate(Na.y));
            float crack  = a * _GeoCrackStrength * step(0.55, n1)
                         * smoothstep(0.035, 0.0, abs(vnoise(p * 3.1 + 5.7) - 0.5));
            float patina = a * saturate(Na.y) * smoothstep(0.45, 0.7, n1);

            // ── Weather ─────────────────────────────────────────────────────
            float side   = 1.0 - Na.y * Na.y;
            float streak = _GeoStreaks * side * smoothstep(0.55, 0.8, vnoise(float3(p.x * 9.0, p.y * 0.7, p.z * 9.0)));
            float pits   = _GeoCorrosion * max(a, 0.35) * smoothstep(0.78, 0.86, vnoise(p * 11.0 + 2.3));

            // ── Rain ────────────────────────────────────────────────────────
            // A top face with nothing on it (neighbour 15 = the cell above) is the
            // only place water can stand.
            float openTop = saturate(Na.y) * (1.0 - Bit(15));
            float wet     = saturate(_GeoWetness * (1.0 + 0.6 * streak));   // streaks run wetter

            // Cracks fill first (Lagarde's crack flood: min(level, depth)), then
            // puddles: water gathers in the dips of a noise "height map", kept off
            // the rim where it would run off the block.
            float crackFlood  = saturate(_GeoFlood * 2.0);
            float crackWater  = min(crackFlood, crack) * saturate(openTop + 0.25);
            float puddleDepth = saturate((fbm(float3(p.x * 0.8, 3.1, p.z * 0.8)) - 0.3) * 2.4)
                              * smoothstep(0.02, 0.16, edge);
            float puddleFlood = _GeoFlood * _GeoPuddles;
            float puddle      = openTop * saturate((puddleFlood - (1.0 - puddleDepth * 2.0)) / 0.2);

            // Drips running down the sides while it rains.
            float drip = side * _GeoRain * wet
                       * smoothstep(0.82, 0.9, vnoise(float3(p.x * 16.0, p.y * 2.2 + _Time.y * 1.7, p.z * 16.0)));

            float water = max(max(crackWater, puddle), drip * 0.8);

            mult *= 1.0 + 0.45 * chip;
            mult *= 1.0 - 0.40 * saturate(grime);
            mult *= 1.0 - 0.60 * crack * (1.0 - crackWater * 0.5);
            mult *= 1.0 - 0.28 * streak;
            mult *= 1.0 - 0.45 * pits;
            mult *= lerp(1.0, 0.62, wet);          // wet surfaces darken…
            mult *= lerp(1.0, 0.78, water);        // …and more where water stands

            s.mult = mult;
            s.tint = lerp(float3(1, 1, 1), _GeoWearTint.rgb * 1.6, saturate(patina * 0.55 + pits * 0.5));
            s.wet   = wet;
            s.water = water;
            s.gloss = lerp(lerp(0.35, 0.78, wet), 1.0, water);

            // Water surface normal: flat up, rippled by the drops. Tangent space of
            // a top face: x → world x, y → world z, z → world up.
            float3 n = N;
            if (puddle > 0.001 || crackWater > 0.001)
            {
                float3 rn = RippleNormal(IN.positionWS.xz / cs * 2.5, _GeoRain);
                float3 waterN = normalize(float3(rn.x, rn.z, rn.y));
                n = normalize(lerp(N, waterN, saturate(max(puddle, crackWater * openTop))));
            }
            s.N = n;
            return s;
        }
        ENDHLSL

        Pass
        {
            Name "Weather"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Blend DstColor SrcColor
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragWeather

            half4 fragWeather(Varyings IN) : SV_Target
            {
                Surface s = Evaluate(IN);
                return half4(0.5 * s.mult * s.tint, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Wet"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Offset -1, -1
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragWet
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            half4 fragWet(Varyings IN) : SV_Target
            {
                if (_GeoWetness <= 0.001 && _GeoFlood <= 0.001) return half4(0, 0, 0, 0);

                Surface s = Evaluate(IN);
                float amount = saturate(s.wet * 0.55 + s.water);
                if (amount <= 0.001) return half4(0, 0, 0, 0);

                float3 N = s.N;
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                Light  L = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float3 H = normalize(V + L.direction);

                float dotVH = saturate(dot(V, H));
                float dotNH = saturate(dot(N, H));
                float dotNL = saturate(dot(N, L.direction));
                float dotNV = saturate(dot(N, V));

                // Water's F0 where it stands; a damp surface's a little higher.
                float  F0     = lerp(0.04, 0.02, s.water);
                float  gloss  = s.gloss;
                float  specVH = F0 + (1.0 - F0) * pow(1.0 - dotVH, 5.0);
                float  specNV = F0 + (1.0 - F0) * pow(1.0 - dotNV, 5.0) / (4.0 - 3.0 * gloss);
                float  power  = exp2(gloss * 11.0);

                float3 sun  = L.color * (L.shadowAttenuation * L.distanceAttenuation);
                float3 spec = specVH * ((power + 2.0) / 8.0) * pow(dotNH, power) * dotNL * sun;
                float3 sky  = GlossyEnvironmentReflection(reflect(-V, N), 1.0 - gloss, 1.0) * specNV;

                float  occl = 1.0 - _GeoAOStrength * s.ao * 0.8;
                float3 c = (spec + sky) * amount * occl;
                return half4(min(c, 4.0), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
