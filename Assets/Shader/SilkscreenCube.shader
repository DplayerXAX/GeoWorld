Shader "GeoWorld/SilkscreenCube"
{
    // Constructivism-flavoured silkscreen cube material:
    //   - Flat poster-like base colour (per-renderer via MPB)
    //   - Triplanar fBM "ink" unevenness — colour varies subtly in world space
    //   - Finer triplanar paper grain
    //   - Tiny face-direction brightness so cubes still read as 3D
    //   - Main light shadow attenuation (soft if pipeline supports it)
    Properties
    {
        _BaseColor          ("Base Color",          Color)         = (0.85, 0.18, 0.12, 1)

        _InkStrength        ("Ink Strength",        Range(0, 1))   = 0.30
        _InkScale           ("Ink Scale",           Float)         = 0.55

        _GrainStrength      ("Paper Grain",         Range(0, 1))   = 0.18
        _GrainScale         ("Grain Scale",         Float)         = 2.4

        _FaceLightVariation ("Face Light Variation",Range(0, 0.5)) = 0.12
        _AmbientFloor       ("Ambient Floor",       Range(0, 1))   = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ── Main visible pass ───────────────────────────────────────────────
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS  : SV_POSITION;
                float3 positionWS  : TEXCOORD0;
                float3 normalWS    : TEXCOORD1;
                float4 shadowCoord : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            // Per-renderer base colour (set via MaterialPropertyBlock).
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(Props)

            CBUFFER_START(UnityPerMaterial)
                float _InkStrength;
                float _InkScale;
                float _GrainStrength;
                float _GrainScale;
                float _FaceLightVariation;
                float _AmbientFloor;
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

            // World-space triplanar noise — no UV needed, looks consistent
            // across all 6 cube faces with no seam.
            float Triplanar(float3 pos, float3 n, float scale)
            {
                float3 absN = abs(normalize(n));
                float3 w    = pow(absN, 4);
                w /= (w.x + w.y + w.z + 0.0001);

                float nx = Fbm(pos.yz * scale);
                float ny = Fbm(pos.xz * scale);
                float nz = Fbm(pos.xy * scale);
                return nx * w.x + ny * w.y + nz * w.z;   // ~ [0, 1]
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   nrm = GetVertexNormalInputs(IN.normalOS);

                OUT.positionCS  = pos.positionCS;
                OUT.positionWS  = pos.positionWS;
                OUT.normalWS    = nrm.normalWS;
                OUT.shadowCoord = GetShadowCoord(pos);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                half3 baseCol = UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor).rgb;
                float3 n      = normalize(IN.normalWS);

                // 1) Subtle face brightness — top slightly brighter, bottom
                //    darker, but very small (Constructivism stays mostly flat).
                float faceBright = 1.0 + n.y * _FaceLightVariation - _FaceLightVariation * 0.5;

                // 2) Ink unevenness — mid-frequency triplanar noise.
                float ink     = Triplanar(IN.positionWS, n, _InkScale);
                float inkMul  = lerp(1.0, lerp(0.75, 1.15, ink), _InkStrength);

                // 3) Paper grain — high-frequency triplanar noise.
                float grain    = Triplanar(IN.positionWS, n, _GrainScale);
                float grainMul = lerp(1.0, lerp(0.88, 1.08, grain), _GrainStrength);

                // 4) Main light shadow attenuation only (no diffuse — keeps it flat).
                Light mainLight = GetMainLight(IN.shadowCoord);
                float shadow    = mainLight.shadowAttenuation;
                float lightMul  = lerp(_AmbientFloor, 1.0, shadow);

                half3 col = baseCol * faceBright * inkMul * grainMul * lightMul;
                return half4(col, 1);
            }
            ENDHLSL
        }

        // ── Casts shadows onto other receivers ──────────────────────────────
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On ZTest LEqual ColorMask 0

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 worldPos    = TransformObjectToWorld(IN.positionOS.xyz);
                float3 worldNormal = TransformObjectToWorldNormal(IN.normalOS);
                float3 biased      = ApplyShadowBias(worldPos, worldNormal, _LightDirection);
                OUT.positionCS     = TransformWorldToHClip(biased);
                #if UNITY_REVERSED_Z
                    OUT.positionCS.z = min(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    OUT.positionCS.z = max(OUT.positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ── Depth + Normals (required for outline post-process) ─────────────
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return half4(normalize(IN.normalWS) * 0.5 + 0.5, 0);
            }
            ENDHLSL
        }
    }
}
