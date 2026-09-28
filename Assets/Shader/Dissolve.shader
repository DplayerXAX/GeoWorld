Shader "GeoWorld/Dissolve"
{
    // A block coming apart (BlockDissolveFx): eaten away through a 3D noise field
    // as _Dissolve runs 0 → 1, with a glowing rim along the edge that is about to go.
    //
    // Same lighting as the rest of the board — banded main light, flat shadow tint —
    // so up to the moment it starts to go, it is simply the block. The noise is in
    // WORLD space, offset per block by _Seed, so neighbouring blocks don't crumble
    // in the same pattern. Culling is off: as holes open, the far faces show
    // through and it reads as a hollowing shell rather than a sheet with holes.
    //
    // The shadow is cut by the same noise, so it wears away with the block.
    Properties
    {
        _BaseColor ("Colour (MPB)",       Color)            = (1, 1, 1, 1)
        _EdgeColor ("Edge glow",          Color)            = (1, 0.85, 0.55, 1)
        _Dissolve  ("Dissolve",           Range(0, 1))      = 0
        _EdgeWidth ("Edge width",         Range(0.01, 0.3)) = 0.09
        _EdgeGlow  ("Edge brightness",    Range(0, 6))      = 2.5
        _NoiseScale("Noise scale",        Float)            = 2.2
        _Seed      ("Seed",               Float)            = 0
        _Bands     ("Shading steps",      Range(2, 8))      = 3
        _Ambient   ("Shadow floor",       Range(0, 1))      = 0.78
        _ShadeTint ("Shadow tint",        Color)            = (0.66, 0.72, 0.80, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _EdgeColor;
            float4 _ShadeTint;
            float  _Dissolve;
            float  _EdgeWidth;
            float  _EdgeGlow;
            float  _NoiseScale;
            float  _Seed;
            float  _Bands;
            float  _Ambient;
        CBUFFER_END

        float hash13(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 19.19);
            return frac((p.x + p.y) * p.z);
        }
        float vnoise(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            float a = hash13(i),               b = hash13(i + float3(1,0,0));
            float c = hash13(i + float3(0,1,0)), d = hash13(i + float3(1,1,0));
            float e = hash13(i + float3(0,0,1)), g = hash13(i + float3(1,0,1));
            float h = hash13(i + float3(0,1,1)), k = hash13(i + float3(1,1,1));
            return lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y),
                        lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y), f.z);
        }

        // 0..1 key for this point; it is gone once _Dissolve passes it. Two octaves:
        // big bites and a crumbly edge. Remapped so 0 and 1 are actually reached,
        // i.e. nothing is missing at the start and nothing is left at the end.
        float DissolveKey(float3 positionWS)
        {
            float3 p = positionWS * _NoiseScale + _Seed * 17.13;
            float n  = vnoise(p) * 0.68 + vnoise(p * 2.7 + 5.1) * 0.32;
            return saturate((n - 0.15) / 0.70);
        }

        // Discards what has dissolved; returns how close to the edge the rest is
        // (1 at the edge, 0 a full edge-width inside).
        float ClipDissolve(float3 positionWS)
        {
            float margin = DissolveKey(positionWS) - _Dissolve * 1.02;   // 1.02: fully gone at 1
            clip(margin);
            return _Dissolve > 0.001 ? 1.0 - saturate(margin / max(_EdgeWidth, 1e-3)) : 0.0;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fog        : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.fog        = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                float edge = ClipDissolve(IN.positionWS);

                float3 N = normalize(IN.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                Light main = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float steps = max(_Bands, 2.0);
                float lit = saturate(floor(saturate(dot(N, main.direction)) * steps) / (steps - 1.0))
                          * main.shadowAttenuation;

                half3 albedo = _BaseColor.rgb;
                half3 rgb = lerp(albedo * _ShadeTint.rgb * _Ambient, albedo * main.color, lit);

                // The rim: the block's own colour, pushed hot toward the edge colour —
                // a burn line, not a generic orange one on every block.
                half3 hot = lerp(albedo, _EdgeColor.rgb, 0.6) * _EdgeGlow;
                rgb = lerp(rgb, hot, edge * edge);

                rgb = MixFog(rgb, IN.fog);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct SAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct SVaryings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            SVaryings shadowVert(SAttributes IN)
            {
                SVaryings OUT;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(IN.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - posWS);
                #else
                    float3 L = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nrmWS, L));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                OUT.positionWS = posWS;
                return OUT;
            }

            half4 shadowFrag(SVaryings IN) : SV_Target
            {
                ClipDissolve(IN.positionWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex doVert
            #pragma fragment doFrag

            struct OAttributes { float4 positionOS : POSITION; };
            struct OVaryings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            OVaryings doVert(OAttributes IN)
            {
                OVaryings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            half4 doFrag(OVaryings IN) : SV_Target
            {
                ClipDissolve(IN.positionWS);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
