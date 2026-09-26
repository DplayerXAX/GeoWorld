Shader "GeoWorld/DepthMask"
{
    // Writes DEPTH and nothing else.
    //
    // The exploded menu is photographed by two cameras: one takes the cube in the
    // middle, the other takes the plates and the lines that hold them to it. That is
    // what lets the middle be a hole with the frozen game showing through while the
    // plates are solid — but it also means the two pictures know nothing about each
    // other, so a line running to the far side of the cube was drawn straight over the
    // cube instead of disappearing behind it.
    //
    // A copy of the cube, in this material, standing in the plates' stage fixes it: it
    // lays down the cube's depth before anything else draws, so lines and plates
    // behind it are rejected, and it paints no colour, so the picture stays empty
    // there and the real cube shows through from underneath.
    //
    // Queue is Geometry-100 so it runs FIRST. A depth mask that draws after the things
    // it is meant to hide has already lost.
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry-100" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return half4(0, 0, 0, 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
