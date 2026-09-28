// A LOCAL volume of mist, ray-marched inside its own box, with main-light
// scattering and shadowing — the Tyndall look: light through a gap stands out as
// a bright shaft in the mist, and whatever blocks the sun leaves a dark lane
// through it.
//
// The same technique as GeoWorld/DepthFog (3D fbm clumps, Henyey-Greenstein
// scatter, MainLightRealtimeShadow at every sample), confined to a box instead of
// the whole view, so it can sit over one patch of the map:
//
//   * Drawn by the box's BACK faces with ZTest Always, so it still renders with
//     the camera inside the box; the march is then cut against the scene depth,
//     so a block in front of the mist hides it and a block inside it stands in it.
//   * Its SHAPE comes from _Mask, a soft top-down footprint baked from the cells it
//     is meant to cover — the mist follows what is under it rather than filling a
//     rectangle.
//   * _Dissolve burns it off from the thin parts inward (a raised threshold on the
//     noise, not a uniform fade), so it goes the way real mist goes; _Lift carries
//     what is left upward.
//
// Premultiplied alpha, like DepthFog.
Shader "GeoWorld/MistVolume"
{
    Properties
    {
        _FogColor    ("Mist colour",        Color)            = (0.93, 0.90, 0.85, 1)
        _ScatterTint ("Scatter tint",       Color)            = (1.00, 0.95, 0.85, 1)
        _Density     ("Density",            Float)            = 0.9
        _Extinction  ("Extinction",         Float)            = 1.2
        _Strength    ("Strength",           Range(0, 1))      = 0.85
        _Anisotropy  ("Forward scatter",    Range(-0.9, 0.9)) = 0.55
        _Isotropic   ("Omni scatter",       Range(0, 1))      = 0.25
        _Scatter     ("Scatter intensity",  Range(0, 6))      = 1.6
        _Steps       ("March steps",        Range(6, 48))     = 20
        _NoiseScale  ("Noise scale",        Float)            = 0.35
        _NoiseAmount ("Noise amount",       Range(0, 1))      = 0.75
        _Wind        ("Wind (xyz)",         Vector)           = (0.25, 0.03, 0.18, 0)
        _Offset      ("Noise offset",       Vector)           = (0, 0, 0, 0)
        _Dissolve    ("Dissolve",           Range(0, 1))      = 0
        _Lift        ("Lift",               Range(0, 1))      = 0
        _Mask        ("Footprint",          2D)               = "white" {}
        _BoxSize     ("Box size (world)",   Vector)           = (1, 1, 1, 0)
        _EdgeWarp    ("Edge warp (world)",  Float)            = 1.2
        _WarpScale   ("Edge warp scale",    Float)            = 0.22
        _Protect     ("Keep visible map clear", Float)        = 0
        _Floor       ("Ground level (box fraction)", Range(0, 1)) = 0.1
        _Falloff     ("Upward falloff",     Float)            = 1.1
        _SkyBlend    ("Sky blend",          Range(0, 1))      = 0
        _SkyMip      ("Sky blur (mip)",     Float)            = 3
        _SinkCentre  ("Sink centre (world xz)", Vector)       = (0, 0, 0, 0)
        _Sink        ("Sink: start, rate, max, soft (world)", Vector) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-50" }

        Pass
        {
            Cull Front
            ZTest Always
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Mask); SAMPLER(sampler_Mask);

            // GLOBAL, set by MistBank.SetProtected: a top-down map of the ground that
            // is on show, and its rect (x0, z0, 1/width, 1/depth). Where a pixel's
            // scene surface lies on that ground, the mist in FRONT of it is removed —
            // so a bank standing between the camera and the visible map can never
            // cover it, from any angle. Carving the mask alone only keeps the mist off
            // the ground itself; a tall bank beside a block still hides it on screen.
            TEXTURE2D(_MistProtect); SAMPLER(sampler_MistProtect);
            float4 _MistProtectRect;

            CBUFFER_START(UnityPerMaterial)
                float  _Protect;
                float  _Floor;
                float  _Falloff;
                float  _SkyBlend;
                float  _SkyMip;
                float4 _SinkCentre;
                float4 _Sink;
                float4 _FogColor;
                float4 _ScatterTint;
                float4 _Wind;
                float4 _Offset;
                float4 _Mask_ST;
                float4 _BoxSize;
                float  _EdgeWarp;
                float  _WarpScale;
                float  _Density;
                float  _Extinction;
                float  _Strength;
                float  _Anisotropy;
                float  _Isotropic;
                float  _Scatter;
                float  _Steps;
                float  _NoiseScale;
                float  _NoiseAmount;
                float  _Dissolve;
                float  _Lift;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            // ── 3D value-noise fbm (as DepthFog) ────────────────────────────────
            float hash13 (float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }
            float vnoise (float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = hash13(i + float3(0,0,0)), n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0)), n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1)), n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1)), n111 = hash13(i + float3(1,1,1));
                float nxy0 = lerp(lerp(n000, n100, f.x), lerp(n010, n110, f.x), f.y);
                float nxy1 = lerp(lerp(n001, n101, f.x), lerp(n011, n111, f.x), f.y);
                return lerp(nxy0, nxy1, f.z);
            }
            float fbm (float3 p)
            {
                float a = 0.5, s = 0.0;
                [unroll] for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; }
                return s;
            }

            float hg (float c, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (12.566370 * pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv    = IN.positionCS.xy / _ScaledScreenParams.xy;
                float  depth = SampleSceneDepth(uv);
                float3 scene = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float3 cam  = _WorldSpaceCameraPos;
                float3 rdW  = normalize(IN.positionWS - cam);
                float  sceneDist = length(scene - cam);

                // The box in its own space is [-0.5, 0.5]^3. The ray is carried into
                // that space WITHOUT renormalising, so the slab test's t stays in
                // world units — the same t marches the world-space ray below.
                float3 ro = mul(unity_WorldToObject, float4(cam, 1.0)).xyz;
                float3 rd = mul((float3x3)unity_WorldToObject, rdW);
                float3 inv = 1.0 / (rd + 1e-6 * (step(0.0, rd) * 2.0 - 1.0));
                float3 t0 = (-0.5 - ro) * inv;
                float3 t1 = ( 0.5 - ro) * inv;
                float3 tl = min(t0, t1), th = max(t0, t1);
                float tNear = max(max(max(tl.x, tl.y), tl.z), 0.0);
                float tFar  = min(min(min(th.x, th.y), th.z), sceneDist);
                if (tFar <= tNear) return 0;

                Light ml = GetMainLight();
                // Forward-peaked scatter for the shafts when looking toward the
                // sun, plus an omnidirectional share so the lit/shadowed contrast —
                // the shafts themselves — still reads from the map's usual angles.
                float phase = hg(dot(rdW, ml.direction), _Anisotropy) + _Isotropic * 0.0796;

                int   steps   = (int)_Steps;
                float stepLen = (tFar - tNear) / steps;
                // Stable per-pixel offset (interleaved gradient noise) — hides the
                // step banding without the frame-to-frame crawl a time-seeded
                // jitter adds when there is no TAA to resolve it.
                float ign = frac(52.9829189 * frac(dot(IN.positionCS.xy, float2(0.06711056, 0.00583715))));

                float3 wind = _Wind.xyz * _Time.y + _Offset.xyz;
                float  transmittance = 1.0;
                float3 scattered     = 0.0;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float  t  = tNear + (i + ign) * stepLen;
                    float3 pW = cam + rdW * t;
                    float3 pO = ro  + rd  * t;

                    // Footprint, read through a slow 3D warp: the edge is pushed in
                    // and out by up to _EdgeWarp, differently at each height, so it
                    // wanders and billows instead of tracing the grid underneath.
                    float3 wp = pW * _WarpScale + wind * 0.6;
                    float2 warp = float2(vnoise(wp), vnoise(wp + 17.31)) * 2.0 - 1.0;
                    float2 muv  = pO.xz + 0.5 + warp * _EdgeWarp / max(_BoxSize.xz, 1e-3);
                    float mask = SAMPLE_TEXTURE2D_LOD(_Mask, sampler_Mask, muv, 0).r;
                    if (mask <= 0.002) continue;

                    // Height, the way mist wells up from below (like the atmosphere's
                    // height fog, but local):
                    //   * BELOW the ground line (_Floor) it is dense all the way
                    //     down, fading only toward the very bottom of the box — the
                    //     body of it is underneath, and you look down into it;
                    //   * ABOVE the line it thins roughly exponentially, up to a
                    //     ceiling that rolls on a LARGE, slow noise — low here, heaped
                    //     into a billow there — and drops toward the edge, so what
                    //     shows over the ground is the ragged top of something deeper;
                    //   * and the higher above the line, the more of the fine noise
                    //     is eaten away, so the top breaks into separate wisps.
                    // _Lift carries the whole body up as it burns off.
                    float n   = fbm(pW * _NoiseScale + wind);
                    float big = vnoise(pW * (_NoiseScale * 0.3) + wind * 0.5 + 5.7);

                    float h    = pO.y + 0.5 - _Lift;

                    // Sinking away from the centre: every sample is read as if it
                    // stood HIGHER by an amount that grows with its distance from
                    // _SinkCentre — i.e. the whole layer drops away toward the
                    // horizon, so the fog falls off the edge of the world rather than
                    // lying flat to it. Quadratic at first (no crease where it starts),
                    // then a steady slope, capped at _Sink.z.
                    if (_Sink.y > 0.0)
                    {
                        float x    = max(0.0, length(pW.xz - _SinkCentre.xy) - _Sink.x);
                        float sink = min(_Sink.z, _Sink.y * x * x / (x + max(_Sink.w, 1e-3)));
                        h += sink / max(_BoxSize.y, 1e-3);
                    }

                    float fl   = _Floor;
                    float top  = fl + (1.0 - fl) * lerp(0.15, 1.0, mask) * (0.35 + 0.8 * big);
                    top        = clamp(top, fl + 0.02, 0.97);
                    float hn   = max(0.0, (h - fl) / (top - fl));      // 0 at and below the ground line
                    // Gentle exponential thinning, and a long soft fade into the
                    // ceiling rather than a cut — so there is no line where the mist
                    // stops, only less and less of it.
                    float body = mask * smoothstep(0.0, max(0.05, fl * 0.6), h)
                               * exp(-_Falloff * hn) * smoothstep(1.0, 0.3, hn);
                    if (body <= 0.001) continue;

                    float wisp = saturate((n - 0.22 - 0.35 * hn) * 2.6);
                    float nn   = lerp(1.0, wisp * 1.5, _NoiseAmount);

                    // Burn-off: raise the threshold, so the thin fringes go first
                    // and the dense cores last.
                    float cover = saturate((body * nn - _Dissolve) / max(1e-3, 1.0 - _Dissolve));
                    float density = _Density * cover;
                    if (density <= 0.001) continue;

                    float sh = MainLightRealtimeShadow(TransformWorldToShadowCoord(pW));
                    float3 inscat = ml.color.rgb * _ScatterTint.rgb * (phase * _Scatter) * sh;

                    scattered     += transmittance * inscat * density * stepLen;
                    transmittance *= exp(-density * _Extinction * stepLen);
                    if (transmittance < 0.02) break;
                }

                float keep = 1.0;
                if (_Protect > 0.5)
                {
                    float2 puv = (scene.xz - _MistProtectRect.xy) * _MistProtectRect.zw;
                    if (all(puv > 0.0) && all(puv < 1.0))
                        keep = 1.0 - SAMPLE_TEXTURE2D_LOD(_MistProtect, sampler_MistProtect, puv, 0).r;
                }

                // Sky blend: the mist's own colour is pulled toward the SKY behind
                // it — read from the skybox's environment cubemap along this very
                // ray, blurred a few mips so the sky's detail doesn't print onto the
                // fog. Strongest on grazing rays toward the horizon, where the haze
                // actually meets the sky; weak looking down at the ground, where it
                // should stay mist-coloured. Where haze fades out against the sky
                // there is then no colour step, only thinning.
                float3 fogCol = _FogColor.rgb;
                if (_SkyBlend > 0.001)
                {
                    half4 enc = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, rdW, _SkyMip);
                    half3 sky = DecodeHDREnvironment(enc, unity_SpecCube0_HDR);
                    float grazing = pow(1.0 - saturate(abs(rdW.y)), 3.0);
                    fogCol = lerp(fogCol, sky, _SkyBlend * saturate(0.25 + grazing));
                }

                float fogAmt = saturate((1.0 - transmittance) * _Strength);
                float amount = fogAmt * keep;
                float3 col   = (fogCol * fogAmt + scattered * _Strength) * keep;
                return half4(col, amount);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
