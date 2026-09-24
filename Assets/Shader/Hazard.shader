Shader "GeoWorld/Hazard"
{
    // Flowing caution stripes, for a piece that is on the board but not part of the
    // build (see BoardValidity): a block that does not connect back to an endpoint,
    // or a turret with nothing holding it up.
    //
    // ONLY THE STRIPES ARE SOLID. Everything between them is cut away, so a flagged
    // piece reads as present-but-not-live — a taped-off outline of a block rather
    // than a block. Culling is OFF so the far faces show through the gaps: without
    // that the piece would be a single striped sheet facing the camera, and with it
    // it reads as a three-dimensional cage. The shadow is cut the same way, so what
    // lands on the ground is striped too.
    //
    // Stripes are laid in WORLD space along (1,1,1). That gives a diagonal on every
    // face of an axis-aligned block — top and sides alike — and makes the stripes
    // CONTINUOUS from one block to the next, so a detached group reads as one
    // taped-off region with the flow running through the whole of it.
    //
    // A hard cut, per the house rule: a clip has no partial pixels.
    Properties
    {
        _BaseColor   ("Unused (MPB writes here)", Color) = (1, 1, 1, 1)
        _StripeColor ("Stripe",            Color)       = (0.98, 0.85, 0.20, 1)
        _Period      ("Stripe pair width", Float)       = 0.42
        _Fill        ("Stripe share",      Range(0.1, 0.9)) = 0.5
        _Speed       ("Flow (pairs/sec)",  Float)       = 0.6
        _Bands       ("Shading steps",     Range(2, 8)) = 3
        _Ambient     ("Shadow floor",      Range(0, 1)) = 0.78
        _ShadeTint   ("Shadow tint",       Color)       = (0.66, 0.72, 0.80, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "RenderPipeline" = "UniversalPipeline" "Queue" = "AlphaTest" }
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;      // blocks are tinted via MPB; declared so the batcher layout matches
            float4 _StripeColor;
            float4 _ShadeTint;
            float  _Period;
            float  _Fill;
            float  _Speed;
            float  _Bands;
            float  _Ambient;
        CBUFFER_END

        // Discards everything that is not stripe. A triangle wave rather than frac():
        // frac jumps from 1 back to 0 once per pair, and a threshold on that would put
        // one edge of every stripe somewhere other than where _Fill says.
        void ClipStripe(float3 positionWS)
        {
            float s   = dot(positionWS, float3(1, 1, 1)) / max(_Period, 1e-3) - _Time.y * _Speed;
            float tri = abs(frac(s) - 0.5) * 2.0;          // 0 mid-gap … 1 mid-stripe
            clip(tri - (1.0 - _Fill));
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
                ClipStripe(IN.positionWS);

                // The far faces are visible through the gaps now; light them as the
                // side of the stripe we are actually looking at.
                float3 N = normalize(IN.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);

                Light main = GetMainLight(TransformWorldToShadowCoord(IN.positionWS));
                float steps = max(_Bands, 2.0);
                float lit = saturate(floor(saturate(dot(N, main.direction)) * steps) / (steps - 1.0))
                          * main.shadowAttenuation;

                half3 albedo = _StripeColor.rgb;
                half3 rgb = lerp(albedo * _ShadeTint.rgb * _Ambient, albedo * main.color, lit);
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
                ClipStripe(IN.positionWS);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct DAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct DVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            DVaryings dnVert(DAttributes IN)
            {
                DVaryings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 dnFrag(DVaryings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                ClipStripe(IN.positionWS);
                float3 N = normalize(IN.normalWS) * IS_FRONT_VFACE(face, 1.0, -1.0);
                return half4(NormalizeNormalPerPixel(N), 0.0);
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
                ClipStripe(IN.positionWS);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
