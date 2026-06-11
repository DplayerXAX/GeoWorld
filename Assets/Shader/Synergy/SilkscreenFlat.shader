Shader "GeoWorld/SilkscreenFlat"
{
    // Flat silkscreen decoration material (Order cogs, etc.) matching
    // SilkscreenCube's printed look — flat poster colour + triplanar paper grain —
    // but DOUBLE-SIDED (Cull Off, so procedural mesh winding never hides a face)
    // and self-shaded by an EMULATED light direction, so the read is a consistent
    // graphic (a printed shape with implied depth) rather than scene-light-
    // dependent realism. Per-renderer colour via MaterialPropertyBlock _BaseColor.
    Properties
    {
        _BaseColor     ("Base Color",          Color)        = (0.16, 0.17, 0.20, 1)
        _FaceShade     ("Side Shade Depth",    Range(0, 0.8))= 0.42
        _EmuLightDir   ("Emulated Light Dir",  Vector)       = (0.35, 1, 0.25, 0)
        _GrainStrength ("Paper Grain",         Range(0, 1))  = 0.16
        _GrainScale    ("Grain Scale",         Float)        = 2.6
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "Flat"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _FaceShade;
                float4 _EmuLightDir;
                float  _GrainStrength;
                float  _GrainScale;
            CBUFFER_END

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i + float2(0, 0));
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm(float2 p)
            {
                float v = 0;
                float a = 0.55;
                [unroll]
                for (int i = 0; i < 3; i++) { v += a * ValueNoise(p); p *= 2.07; a *= 0.5; }
                return v;
            }

            float Triplanar(float3 pos, float3 n, float scale)
            {
                float3 absN = abs(normalize(n));
                float3 w    = pow(absN, 4);
                w /= (w.x + w.y + w.z + 0.0001);
                float nx = Fbm(pos.yz * scale);
                float ny = Fbm(pos.xz * scale);
                float nz = Fbm(pos.xy * scale);
                return nx * w.x + ny * w.y + nz * w.z;
            }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);

                // Consistent graphic shade from a fixed emulated light → faces
                // away from it print darker (implied depth), independent of scene.
                float ndl   = saturate(dot(n, normalize(_EmuLightDir.xyz)) * 0.5 + 0.5);
                float shade = lerp(1.0 - _FaceShade, 1.0, ndl);

                // Paper grain — same triplanar trick as the cubes.
                float grain    = Triplanar(IN.positionWS, n, _GrainScale);
                float grainMul = lerp(1.0, lerp(0.9, 1.08, grain), _GrainStrength);

                half3 col = _BaseColor.rgb * shade * grainMul;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
