Shader "GeoWorld/DeepField"
{
    // The room the opened cube stands in — built from TONE, not from lines.
    //
    // This is how the rest of the game draws space. MinigameSkybox states half the rule
    // outright: the sunset ramp is BANDED, not smooth, because quantising a gradient
    // into a few flat steps reads as "printed with a limited set of inks" rather than
    // as an airbrush. DepthFog states the other half: distance is a warm, desaturated
    // wash. Neither of them draws a single boundary line anywhere.
    //
    // So: hexagonal steps receding toward the middle, each one a flat quantised value
    // of a single warm tone laid over the paper. No strokes, no rays, no rules. The
    // shape comes from where one flat field stops and the next begins — which is what
    // aerial perspective actually is.
    //
    // HEXAGONAL because a cube seen corner-on is a hexagon: the space recedes in the
    // shape of the thing standing in it. Spaced on a LOG scale so the steps compress
    // toward the vanishing point for ever, with no last one and no loop to spot; and
    // the six sectors are skewed against each other so the funnel reads as faceted
    // rather than as a stack of flat rings.
    Properties
    {
        // Declared and never sampled, on purpose. Every UI Graphic writes its texture
        // into _MainTex on whatever material it is given, and a material without the
        // property logs a warning FROM SendWillRenderCanvases every frame the menu is
        // open. This is just somewhere for the canvas to put it.
        _MainTex ("Unused (UI writes here)", 2D) = "white" {}

        // One tone, deeper and warmer than the paper, pulled toward DepthFog's own fog
        // colour — which is where this game already puts distance.
        _Deep    ("Deep tone", Color)               = (0.845, 0.820, 0.767, 1)
        _Alpha   ("Strength", Range(0, 1))          = 0.62

        _Bands   ("Steps per octave", Range(2, 12)) = 5
        _Rings   ("Octaves", Float)                 = 1.0
        _Sectors ("Sectors", Float)                 = 6
        _Skew    ("Sector skew", Range(0, 1))       = 0.18

        _Phase   ("Phase", Float)                          = 0
        _Fade    ("Mouth of the funnel", Range(0.02, 0.6)) = 0.18
        _Reach   ("Where it washes out", Range(0.2, 2))    = 0.95
        _Open    ("Opened radius", Float)                  = 2
    }

    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha

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

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            float4 _MainTex_ST;

            float4 _Deep;
            float  _Alpha, _Bands, _Rings, _Sectors, _Skew, _Phase, _Fade, _Reach;

            // How far out the field has been drawn so far. The room does not switch
            // on, it opens: this radius runs out from the middle with the cube, so the
            // space arrives from where the cube is rather than all at once behind it.
            float  _Open;

            // Aspect and phase come from script: this plays at timeScale 0, and the
            // built-in _Time stops dead with it.
            float _Aspect;

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
                float2 p = IN.uv - 0.5;
                p.x *= _Aspect;

                // Hexagonal distance: the silhouette of a cube seen corner-on.
                float2 a = abs(p);
                float  d = max(a.x * 0.866025 + a.y * 0.5, a.y);

                // Which facet of the funnel this is. QUANTISED, so a whole sector
                // shifts together — twisting it smoothly would put a gradient straight
                // back in.
                float ang    = atan2(p.y, p.x) * 0.15915494 + 0.5;
                float sector = floor(ang * _Sectors) / max(_Sectors, 1.0);

                // One octave of the recession, stepped into flat values.
                float ld   = log2(max(d, 1e-4));
                float q    = frac(ld * _Rings + _Phase + sector * _Skew);
                float tone = floor(q * _Bands) / max(_Bands - 1.0, 1.0);

                // Aerial perspective: deepest at the far end, washing out to clean
                // paper at the mouth. Nothing at all inside the mouth — the cube is
                // there, and log spacing packs infinitely many steps into it.
                float depth = smoothstep(_Fade * 0.7, _Fade, d)
                            * (1.0 - smoothstep(_Fade, _Reach, d));

                // The room opens outward with the cube rather than switching on.
                float opened = 1.0 - smoothstep(_Open, _Open + 0.18, d);

                return half4(_Deep.rgb, saturate(tone * depth * opened * _Alpha));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
