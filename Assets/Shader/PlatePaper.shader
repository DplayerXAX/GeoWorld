Shader "GeoWorld/PlatePaper"
{
    // A flat panel, told apart from its neighbours by its TONE.
    //
    // The marks this used to carry — a grid, a hatch, a field of dots, and an inset
    // rule round the edge — were all lines, and the game does not draw lines. Its
    // backdrops separate one thing from another by value: MinigameSkybox bands a
    // sunset into flat steps, DepthFog washes distance warm. Nothing is outlined.
    //
    // So each plate is one flat colour, and the six are told apart by sitting at
    // different steps of a very short ramp between the paper and one warm deep tone.
    // The rim behind each plate is another step of the same ramp rather than ink: it
    // still reads as a border, but as the shadowed side of a stack of card rather
    // than as a drawn outline.
    //
    // The one piece of shading left is a soft two-step darkening at the very edge of
    // the face — quantised, so it is a narrow flat band and not a bevel. That is what
    // gives the plate a thickness the eye can read without a stroke.
    //
    // UNLIT on purpose. The cube's stage carries no lights of its own, so a lit
    // material would render at whatever ambient the current scene happens to have and
    // the menu would change brightness between the title screen and a level.
    Properties
    {
        // Named _BaseColor so MpbColor can keep tinting these per renderer.
        _BaseColor ("Colour", Color)                 = (0.949, 0.937, 0.902, 1)
        _EdgeTone  ("Edge tone", Color)              = (0.845, 0.820, 0.767, 1)
        _EdgeWidth ("Edge width", Range(0, 0.4))     = 0.13
        _EdgeDepth ("Edge strength", Range(0, 1))    = 0.55
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // Plain uniforms rather than a CBUFFER: these are set per renderer through
            // a MaterialPropertyBlock, which opts the draw out of the SRP batcher
            // anyway. Twelve small boxes do not need batching, and a CBUFFER here
            // would only invite someone to assume they were batched.
            float4 _BaseColor, _EdgeTone;
            float  _EdgeWidth, _EdgeDepth;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 b    = min(IN.uv, 1.0 - IN.uv);
                float  edge = min(b.x, b.y);

                // Two flat steps, not a ramp. A smooth falloff here would be the one
                // airbrushed surface on the screen.
                float t = saturate(1.0 - edge / max(_EdgeWidth, 1e-4));
                t = floor(t * 2.0 + 0.001) * 0.5;

                half3 col = lerp(_BaseColor.rgb, _EdgeTone.rgb, t * _EdgeDepth);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
