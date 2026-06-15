Shader "GeoWorld/RangeShadow"
{
    // Translucent red volume for a turret's unreachable ("blocked") range. The
    // volume is a union of overlapping silhouette frustums; with normal alpha
    // blending every overlap darkens. The Stencil here draws each screen pixel
    // exactly ONCE (first fragment writes ref 1, later fragments at that pixel are
    // rejected) so overlaps don't stack — the whole region reads as one flat red.
    // Colour + alpha come from the mesh vertex colours.
    Properties { }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Cull  Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil { Ref 1  Comp NotEqual  Pass Replace }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.color      = i.color;
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return i.color; }
            ENDHLSL
        }
    }
    Fallback "Sprites/Default"
}
