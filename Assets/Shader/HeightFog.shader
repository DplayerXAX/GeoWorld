// Height fog: a sea of fog lying UNDER the map.
//
// Full density below _FogTop, thinning exponentially above it, infinite in x/z.
// Not ray-marched — the optical depth along each view ray has a closed form for
// this profile, so the result is exact, smooth at any distance and costs a
// handful of instructions a pixel. A thin layer is exactly what a ray-march does
// badly (it needs many steps to resolve a steep height falloff); here there are no
// steps to band.
//
//   * The top undulates: where the ray crosses the layer, a slow 2D noise lifts or
//     lowers the surface by up to _TopWave, so the sea has swells instead of a
//     ruler-flat top.
//   * Lit by the main light with a forward-peaked phase (no shadows — it is under
//     everything), so it glows toward the sun.
//   * Its colour is pulled toward the SKY behind it on grazing rays (the skybox's
//     environment cubemap, as MistVolume does), so where the sea runs out to the
//     horizon it meets the sky with no colour step.
//
// Drawn on a cube that follows the camera (HeightFog.cs), by its back faces with
// ZTest Always, and cut against the scene depth. Premultiplied alpha.
Shader "GeoWorld/HeightFog"
{
    Properties
    {
        _FogColor    ("Fog colour",        Color)            = (0.90, 0.89, 0.87, 1)
        _ScatterTint ("Scatter tint",      Color)            = (1.00, 0.95, 0.85, 1)
        _Density     ("Density (below top, per unit)", Float) = 0.25
        _FogTop      ("Fog top (world Y)", Float)            = 0
        _Falloff     ("Falloff above top (per unit)", Float) = 0.9
        _TopWave     ("Top undulation (units)", Float)       = 0.6
        _WaveScale   ("Undulation scale",  Float)            = 0.08
        _Wind        ("Wind (xz)",         Vector)           = (0.12, 0, 0.08, 0)
        _MaxDistance ("Max distance",      Float)            = 400
        _Strength    ("Strength",          Range(0, 1))      = 1
        _Anisotropy  ("Forward scatter",   Range(-0.9, 0.9)) = 0.5
        _Scatter     ("Scatter intensity", Range(0, 4))      = 0.8
        _SkyBlend    ("Sky blend",         Range(0, 1))      = 0.7
        _SkyMip      ("Sky blur (mip)",    Float)            = 3
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-70" }

        Pass
        {
            Cull Front
            ZTest Always
            ZWrite Off
            Blend One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float4 _ScatterTint;
                float4 _Wind;
                float  _Density;
                float  _FogTop;
                float  _Falloff;
                float  _TopWave;
                float  _WaveScale;
                float  _MaxDistance;
                float  _Strength;
                float  _Anisotropy;
                float  _Scatter;
                float  _SkyBlend;
                float  _SkyMip;
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

            float hash12 (float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }
            float vnoise2 (float2 x)
            {
                float2 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash12(i), b = hash12(i + float2(1, 0));
                float c = hash12(i + float2(0, 1)), d = hash12(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            float hg (float c, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (12.566370 * pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5));
            }

            // Optical depth along y(t) = oy + dy·t, t in [0, L], through
            //   rho(y) = rho0                         for y <= top
            //          = rho0 · exp(-k (y - top))     above.
            // Split where the ray crosses `top`: constant below, the exponential's
            // integral in closed form above. Evaluated as exp of heights ABOVE the
            // top only (both >= 0), so nothing overflows however far the ray runs.
            float OpticalDepth (float oy, float dy, float L, float top, float k, float rho0)
            {
                if (abs(dy) < 1e-4)
                    return rho0 * L * exp(-k * max(oy - top, 0.0));

                float th = clamp((top - oy) / dy, 0.0, L);   // where the ray crosses the top
                float bs = dy > 0.0 ? 0.0 : th,  be = dy > 0.0 ? th : L;   // below-top segment
                float as_ = dy > 0.0 ? th : 0.0, ae = dy > 0.0 ? L  : th;  // above-top segment

                float tau = rho0 * max(be - bs, 0.0);
                if (ae > as_)
                {
                    float ya = max(oy + dy * as_ - top, 0.0);
                    float yb = max(oy + dy * ae  - top, 0.0);
                    tau += rho0 * (exp(-k * ya) - exp(-k * yb)) / (k * dy);
                }
                return max(tau, 0.0);
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv    = IN.positionCS.xy / _ScaledScreenParams.xy;
                float  depth = SampleSceneDepth(uv);
                float3 scene = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float3 cam = _WorldSpaceCameraPos;
                float3 rd  = normalize(IN.positionWS - cam);

                #if UNITY_REVERSED_Z
                    bool isSky = depth <= 1e-6;
                #else
                    bool isSky = depth >= 1.0 - 1e-6;
                #endif
                float L = isSky ? _MaxDistance : min(length(scene - cam), _MaxDistance);

                // Swell of the surface where this ray meets it.
                float top = _FogTop;
                if (abs(rd.y) > 1e-4)
                {
                    float tc = clamp((top - cam.y) / rd.y, 0.0, L);
                    float2 at = cam.xz + rd.xz * tc;
                    float n = vnoise2(at * _WaveScale + _Wind.xz * _Time.y)
                            * 0.65 + vnoise2(at * _WaveScale * 2.7 - _Wind.xz * _Time.y * 1.3) * 0.35;
                    top += (n - 0.5) * 2.0 * _TopWave;
                }

                float tau    = OpticalDepth(cam.y, rd.y, L, top, max(_Falloff, 1e-3), _Density);
                float amount = saturate((1.0 - exp(-tau)) * _Strength);
                if (amount <= 0.001) return 0;

                // Colour: the fog's own, pulled toward the sky on grazing rays.
                float3 col = _FogColor.rgb;
                if (_SkyBlend > 0.001)
                {
                    half4 enc = SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, rd, _SkyMip);
                    half3 sky = DecodeHDREnvironment(enc, unity_SpecCube0_HDR);
                    float grazing = pow(1.0 - saturate(abs(rd.y)), 3.0);
                    col = lerp(col, sky, _SkyBlend * saturate(0.2 + grazing));
                }

                // Sunlit: a forward-peaked glow toward the light, plus a little all
                // round so it isn't dead grey with the sun behind you.
                Light ml = GetMainLight();
                float phase = hg(dot(rd, ml.direction), _Anisotropy) + 0.02;
                col += ml.color.rgb * _ScatterTint.rgb * phase * _Scatter;

                return half4(col * amount, amount);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
