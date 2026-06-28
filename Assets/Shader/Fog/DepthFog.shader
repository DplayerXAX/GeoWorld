// Volumetric depth fog with light scattering (Tyndall) for URP.
//
// Rendered by an inward-facing sphere around the backdrop (perspective) camera.
// The fragment ray-marches from the camera to the reconstructed scene position,
// accumulating fog density (height falloff × animated 3D fbm noise, so the fog
// breaks into clumps/layers instead of a flat wash) and in-scattered MAIN LIGHT
// (Henyey-Greenstein forward scatter × shadow attenuation, so light beams through
// gaps and is blocked by occluders → god rays). Premultiplied-alpha output.
//
// Drive every property from AtmosphereDecor for live tuning.
Shader "GeoWorld/DepthFog"
{
    Properties
    {
        _FogColor        ("Fog Color", Color)            = (0.70, 0.63, 0.56, 1)
        _ScatterTint     ("Scatter Tint", Color)         = (1, 1, 1, 1)
        _BaseDensity     ("Base Density", Float)          = 0.04
        _Extinction      ("Extinction", Float)            = 1.0
        _FogStart        ("Fog Start (clear radius)", Float) = 40
        _FogRamp         ("Fog Ramp Distance", Float)     = 140
        _MaxDistance     ("Max March Distance", Float)    = 320
        _FogHeight       ("Fog Height", Float)            = 6
        _HeightFalloff   ("Height Falloff", Float)        = 0.03
        _FogStrength     ("Fog Strength", Range(0,2))     = 1.0
        _Anisotropy      ("Scatter Anisotropy", Range(-0.95,0.95)) = 0.6
        _ScatterIntensity("Scatter Intensity", Range(0,4))= 1.2
        _Steps           ("March Steps", Range(4,40))     = 14
        _NoiseScale      ("Noise Scale", Float)           = 0.02
        _NoiseAmount     ("Noise Amount", Range(0,1))     = 0.7
        _Wind            ("Wind (xyz)", Vector)           = (0.5, 0.05, 0.3, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" }

        Pass
        {
            Cull Front
            ZTest Always
            ZWrite Off
            Blend One OneMinusSrcAlpha     // premultiplied alpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FogColor;
                float4 _ScatterTint;
                float4 _Wind;
                float  _BaseDensity;
                float  _Extinction;
                float  _FogStart;
                float  _FogRamp;
                float  _MaxDistance;
                float  _FogHeight;
                float  _HeightFalloff;
                float  _FogStrength;
                float  _Anisotropy;
                float  _ScatterIntensity;
                float  _Steps;
                float  _NoiseScale;
                float  _NoiseAmount;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings vert (Attributes IN)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return o;
            }

            // ── 3D value-noise fbm ──────────────────────────────────────────────
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
                float n000 = hash13(i + float3(0,0,0));
                float n100 = hash13(i + float3(1,0,0));
                float n010 = hash13(i + float3(0,1,0));
                float n110 = hash13(i + float3(1,1,0));
                float n001 = hash13(i + float3(0,0,1));
                float n101 = hash13(i + float3(1,0,1));
                float n011 = hash13(i + float3(0,1,1));
                float n111 = hash13(i + float3(1,1,1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                float nxy0 = lerp(nx00, nx10, f.y);
                float nxy1 = lerp(nx01, nx11, f.y);
                return lerp(nxy0, nxy1, f.z);
            }
            float fbm (float3 p)
            {
                float a = 0.5, s = 0.0;
                [unroll] for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.02; a *= 0.5; }
                return s;
            }

            // Henyey-Greenstein phase
            float hg (float c, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (12.566370f * pow(max(1.0 + g2 - 2.0 * g * c, 1e-4), 1.5));
            }

            half4 frag (Varyings IN) : SV_Target
            {
                float2 uv    = IN.positionHCS.xy / _ScaledScreenParams.xy;
                float  depth = SampleSceneDepth(uv);
                float3 wpos  = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);

                float3 camPos  = _WorldSpaceCameraPos;
                float3 toFrag  = wpos - camPos;
                float  sceneDist = length(toFrag);
                float3 rayDir  = toFrag / max(sceneDist, 1e-4);

            #if UNITY_REVERSED_Z
                bool isSky = depth < 1e-6;
            #else
                bool isSky = depth > 0.99999;
            #endif
                float marchDist = min(isSky ? _MaxDistance : sceneDist, _MaxDistance);

                Light ml = GetMainLight();
                float phase = hg(dot(rayDir, ml.direction), _Anisotropy);

                int   steps    = (int)_Steps;
                float stepLen  = marchDist / steps;
                float3 windOff = _Wind.xyz * _Time.y;
                // Dither the start to hide banding.
                float jitter   = frac(hash13(float3(uv * _ScaledScreenParams.xy, _Time.y)) );

                float  transmittance = 1.0;
                float3 scattered     = 0.0;

                [loop]
                for (int i = 0; i < steps; i++)
                {
                    float t = (i + jitter) * stepLen;
                    float3 p = camPos + rayDir * t;

                    // Atmospheric perspective: clear within _FogStart, then ramp up
                    // with distance so far is thick, near is clear (not global).
                    float distRamp = saturate((t - _FogStart) / max(_FogRamp, 0.01));
                    if (distRamp <= 0.0) continue;

                    float heightF = saturate(exp(-max(p.y - _FogHeight, 0.0) * _HeightFalloff));
                    float n       = fbm(p * _NoiseScale + windOff);
                    n             = lerp(1.0, n, _NoiseAmount);
                    float density = _BaseDensity * heightF * n * distRamp;
                    if (density <= 0.0) continue;

                    float4 sc  = TransformWorldToShadowCoord(p);
                    float  sh  = MainLightRealtimeShadow(sc);

                    float3 inscat = ml.color.rgb * _ScatterTint.rgb * (phase * _ScatterIntensity) * sh;
                    scattered     += transmittance * inscat * density * stepLen;
                    transmittance *= exp(-density * _Extinction * stepLen);
                    if (transmittance < 0.01) break;
                }

                float fogAmount = saturate((1.0 - transmittance) * _FogStrength);
                float3 col = _FogColor.rgb * fogAmount + scattered;   // premultiplied
                return half4(col, fogAmount);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
