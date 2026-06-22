// Cartoony bold outline using the classic inverse-hull technique.
//
// Designed for cube-primitive based blocks: vertex expansion via uniform
// scale from the object's origin, NOT along normals. This sidesteps the
// "corner gap" problem that hard-normal cubes have when expanded along
// per-face normals.
//
// Usage:
//   1. Create a Material with this shader (set color + width to taste)
//   2. Either:
//      A. Add it as the SECOND material slot on your cube prefab — every
//         instance renders the outline pass automatically.
//      B. Or attach BlockOutlineApplier.cs to append it at runtime.
Shader "GeoWorld/BlockOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.04, 0.04, 0.08, 1)
        _OutlineWidth ("Outline Width", Range(0, 0.3)) = 0.05
    }
    SubShader
    {
        // Queue > Geometry so the outline renders AFTER the main forward pass.
        // RenderType Opaque so it participates in URP's opaque queue.
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry+10" }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "UniversalForward" }

            // Backfaces of the expanded hull are what "peek out" behind the
            // original mesh — that peeking sliver IS the outline.
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _OutlineColor;
                float _OutlineWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                // Uniform scale from object-space origin. For a Unit cube
                // centered at (0,0,0) with vertices at ±0.5, this pushes every
                // vertex outward by the same proportion → corners stay closed.
                float3 expanded = IN.positionOS.xyz * (1.0 + _OutlineWidth);
                OUT.positionCS  = TransformObjectToHClip(expanded);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
