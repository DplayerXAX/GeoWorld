Shader "Custom/ObjectOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (1, 1, 0, 1)
        // Now expressed in normalised screen units (e.g. 0.02 ≈ 2% of screen height).
        // The outline keeps a constant pixel thickness regardless of camera distance.
        _OutlineWidth ("Outline Width (screen)", Range(0, 0.05)) = 0.018
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
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);

                // Project world normal into clip-space, then expand the vertex
                // along that direction by a constant fraction of the screen.
                float3 normalWS = TransformObjectToWorldNormal(IN.normalOS);
                float3 normalCS = mul((float3x3)UNITY_MATRIX_VP, normalWS);
                float2 offset   = normalize(normalCS.xy + 1e-5) * _OutlineWidth;

                // Multiplying by w preserves the offset after perspective divide,
                // so the outline is the same pixel width at all depths.
                OUT.positionCS.xy += offset * OUT.positionCS.w;
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
