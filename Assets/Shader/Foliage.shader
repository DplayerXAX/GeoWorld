Shader "GeoWorld/Foliage"
{
    // Lit, shadow-casting, shadow-receiving surface for the Harmony vines.
    //
    // The vines used to be LineRenderers, which cannot be lit at all: a LineRenderer
    // builds a ribbon that always faces the camera, so every point on it carries the
    // same camera-facing normal. Any lit shader on one returns a single flat value
    // along the whole strip, and its shadow is the shadow of a flat sheet that turns
    // with the camera. Nothing to fix there — the geometry is the problem. So the
    // vines are real tubes now, and this is what lights them.
    //
    // BANDED, not smooth. MinigameSkybox states the house rule outright: quantising
    // a ramp into a few flat steps reads as "printed with a limited set of inks"
    // rather than as an airbrush. A smoothly-shaded branch would be the one object
    // in the scene rendered in a different medium from everything around it.
    //
    // Colour comes from VERTEX COLOURS, not from a per-renderer property.
    //
    // The alternative is a MaterialPropertyBlock per branch, and an MPB disables the
    // SRP Batcher for that renderer — with a lush canopy that is hundreds of separate
    // draws. Each branch already owns a unique generated mesh, so baking the
    // root->tip gradient into its vertices costs nothing, and every branch and every
    // leaf in the game can then share ONE material and batch.
    Properties
    {
        _BaseColor ("Tint",            Color)       = (1,1,1,1)
        _Bands     ("Shading steps",   Range(2, 8)) = 3

        // The shade side is a LIGHTER, COOLER version of the lit side — not a dark
        // one. A realistic falloff would take a woody albedo down to near black on
        // every back face, and a plant made of black sticks is what this looked like
        // before. Everything else in this game keeps its hue in shadow and only
        // steps down a notch, so foliage does too: shade lands around 55% of albedo.
        _Ambient   ("Shadow floor",    Range(0, 1)) = 0.78
        _ShadeTint ("Shadow tint",     Color)       = (0.66, 0.72, 0.80, 1)

        // Leaves are flat and must light from both sides; bark is a closed tube and
        // should cull. One shader, two materials.
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _ShadeTint;
            float  _Bands;
            float  _Ambient;
            float  _Cull;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : TEXCOORD2;
                float  fog        : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionWS = posWS;
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.color      = IN.color;
                OUT.fog        = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                // A leaf is one quad. Seen from behind, its authored normal points
                // away from the light and the leaf goes black — so flip it on the
                // back face and both sides read as the same piece of foliage.
                float3 N = normalize(IN.normalWS);
                N *= IS_FRONT_VFACE(face, 1.0, -1.0);

                float4 shadowCoord = TransformWorldToShadowCoord(IN.positionWS);
                Light main = GetMainLight(shadowCoord);

                // Quantise BEFORE the shadow multiplies in, so the terminator lands
                // on a band edge and the steps stay parallel across the whole plant
                // instead of breaking up wherever a shadow crosses one.
                float ndl   = saturate(dot(N, main.direction));
                float steps = max(_Bands, 2.0);
                float lit   = floor(ndl * steps) / (steps - 1.0);
                lit = saturate(lit) * main.shadowAttenuation;

                half3 albedo = IN.color.rgb * _BaseColor.rgb;

                // The unlit side is TINTED, not merely darkened. A branch that only
                // loses value in shade reads as grey plastic; shifting it cool is
                // what makes it read as a thing standing in daylight.
                half3 shade = albedo * _ShadeTint.rgb * _Ambient;
                half3 rgb   = lerp(shade, albedo * main.color, lit);

                #ifdef _ADDITIONAL_LIGHTS
                uint count = GetAdditionalLightsCount();
                for (uint i = 0u; i < count; i++)
                {
                    Light add = GetAdditionalLight(i, IN.positionWS);
                    float a = floor(saturate(dot(N, add.direction)) * steps) / (steps - 1.0);
                    rgb += albedo * add.color * saturate(a) * add.distanceAttenuation * add.shadowAttenuation;
                }
                #endif

                #if defined(_SCREEN_SPACE_OCCLUSION)
                float2 normalizedSC = GetNormalizedScreenSpaceUV(IN.positionCS);
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(normalizedSC);
                rgb *= ao.directAmbientOcclusion;
                #endif

                rgb = MixFog(rgb, IN.fog);
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }

        // Without this the vines light correctly and then cast nothing, which looks
        // worse than the LineRenderer did — a lit object with no shadow reads as
        // pasted on. Two-sided for leaves, via the same _Cull.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex shadowVert
            #pragma fragment shadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct SAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct SVaryings   { float4 positionCS : SV_POSITION; };

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
                return OUT;
            }

            half4 shadowFrag(SVaryings IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // So SSAO (enabled on the PC tier) can see the foliage at all. Without a
        // DepthNormals pass the vines are invisible to the occlusion buffer and sit
        // in front of a scene that is occluded around them but not by them.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex dnVert
            #pragma fragment dnFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct DAttributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct DVaryings   { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };

            DVaryings dnVert(DAttributes IN)
            {
                DVaryings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 dnFrag(DVaryings IN, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
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
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex doVert
            #pragma fragment doFrag
            float4 doVert(float4 positionOS : POSITION) : SV_POSITION
            { return TransformObjectToHClip(positionOS.xyz); }
            half4 doFrag() : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
