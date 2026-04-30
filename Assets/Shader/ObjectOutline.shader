Shader "Custom/ObjectOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        // Object-space normal extrusion: hugs the geometry tightly.
        _OutlineWidth ("Outline Width", Range(0, 0.15)) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }

        Pass
        {
            Name "Outline"
            Cull Front
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _OutlineColor;
                float  _OutlineWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings vert(Attributes IN)
            {
                // Push the back-face mesh outward along its object-space normal.
                // This stays glued to the surface (no screen-space drift on flat
                // panels) and creates a clean halo around the silhouette.
                float3 expanded = IN.positionOS.xyz + normalize(IN.normalOS) * _OutlineWidth;
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(expanded);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
