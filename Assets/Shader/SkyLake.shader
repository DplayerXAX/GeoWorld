Shader "GeoWorld/SkyLake"
{
    // Still water that mirrors the sky over it. Not a cubemap: every pixel paints
    // the level's own painted sky (PaintedSkyCore.hlsl) along its reflected ray,
    // so the brushwork, the sun and every reaction — combat twisting it, the gold
    // of a clear, flashes — show in the water as they happen. The camera looks
    // down most of the time; this is where the sky is actually seen.
    //
    //   * Ripples: two slow noise fields tip the normal, so the reflection wavers.
    //   * The water's own colour shows through where it doesn't mirror, more so
    //     looking straight down, less at grazing angles (Fresnel).
    //   * Shadows fall on it — the board and the hills shade the reflection — so
    //     it sits in the scene instead of floating as a picture.
    //   * At the banks it thins over `_LakeShore` of depth, so the land runs into
    //     the water instead of stopping at a line.
    // The sky's uniforms are copied from the skybox each frame by SkyLake.cs.
    Properties
    {
        _LakeTint        ("Water colour",           Color) = (0.14, 0.28, 0.36, 1)
        _LakeMirror      ("Mirror",                 Range(0, 1)) = 0.8
        _LakeRipple      ("Ripple strength",        Range(0, 0.4)) = 0.1
        _LakeRippleScale ("Ripple scale (per unit)", Float) = 0.35
        _LakeShore       ("Shore fade (world)",     Float) = 0.4
        _LakeClear       ("Depth to full colour (world)", Float) = 6
        _LakeMurk        ("Deep water opacity",     Range(0, 1)) = 0.92
        _LakeShade       ("Shadow on the water",    Range(0, 1)) = 0.4
    }
    SubShader
    {
        Tags { "Queue" = "Transparent-50" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Assets/Shader/Skybox/PaintedSkyCore.hlsl"

            // Outside any CBUFFER, like the sky's own: one lake, no batching to keep.
            half4 _LakeTint;
            float _LakeMirror, _LakeRipple, _LakeRippleScale, _LakeShore, _LakeShade, _LakeClear, _LakeMurk;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float LakeHash(float2 p)
            {
                float3 q = frac(float3(p.xyx) * 0.1031);
                q += dot(q, q.yzx + 33.33);
                return frac((q.x + q.y) * q.z);
            }
            float LakeNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(LakeHash(i), LakeHash(i + float2(1, 0)), f.x),
                            lerp(LakeHash(i + float2(0, 1)), LakeHash(i + float2(1, 1)), f.x), f.y);
            }
            // Slope of the noise, by finite differences — the ripple's tilt.
            float2 LakeSlope(float2 p)
            {
                const float e = 0.12;
                float n = LakeNoise(p);
                return float2(LakeNoise(p + float2(e, 0.0)) - n, LakeNoise(p + float2(0.0, e)) - n) / e;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 ws = IN.positionWS;
                float3 V  = normalize(ws - _WorldSpaceCameraPos);

                float2 q = ws.xz * _LakeRippleScale;
                float  t = _Time.y;
                // Broad, slow swells: the picture bends gently rather than breaking up.
                float2 g = LakeSlope(q * 0.45 + float2(t * 0.05, t * 0.035))
                         + LakeSlope(q * 1.1 + float2(-t * 0.04, t * 0.06)) * 0.35;
                float3 N = normalize(float3(-g.x * _LakeRipple, 1.0, -g.y * _LakeRipple));

                // The mirrored ray, kept above the horizon (a ripple can tip it under).
                float3 R = reflect(V, N);
                R.y = abs(R.y);
                float3 sky = PaintedSkyColor(normalize(R));

                // Water, not a mirror: it reflects most at grazing angles
                // (Fresnel) and least looking straight down, where you see INTO it.
                float fres = pow(1.0 - saturate(dot(-V, N)), 5.0);
                float refl = lerp(_LakeMirror, 1.0, fres);

                Light ml = GetMainLight(TransformWorldToShadowCoord(ws));
                float  lit  = lerp(1.0 - _LakeShade, 1.0, ml.shadowAttenuation);
                float3 body = _LakeTint.rgb * ml.color * (0.45 + 0.55 * saturate(ml.direction.y)) * lit;

                // What lies under it shows through the shallows and is swallowed by
                // the water's own colour with depth (no bottom at all = deepest).
                float2 suv   = IN.positionCS.xy / _ScaledScreenParams.xy;
                float  under = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float  self  = LinearEyeDepth(IN.positionCS.z, _ZBufferParams);
                float  deep  = max(0.0, under - self);
                float  murk  = _LakeMurk * (1.0 - exp(-3.0 * deep / max(_LakeClear, 1e-3)));

                // final = sky*refl + (body*murk + behind*(1-murk))*(1-refl),
                // written for SrcAlpha / OneMinusSrcAlpha.
                float  a   = refl + (1.0 - refl) * murk;
                float3 col = (sky * lit * refl + body * (1.0 - refl) * murk) / max(a, 1e-3);

                // Banks: the surface itself thins right at the waterline, with a
                // faint pale edge where it touches the land.
                float bank = saturate(deep / max(_LakeShore, 1e-3));
                col += (1.0 - saturate(deep / max(_LakeShore * 2.5, 1e-3))) * 0.12;
                return half4(col, a * bank);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
