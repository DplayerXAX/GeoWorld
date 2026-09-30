Shader "GeoWorld/PaintedSky"
{
    // A painted sky for landscape levels (LevelEnvironment.paintedSky), after the
    // swirling skies over Van Gogh's wheat fields. The whole dome is laid in
    // short, thick brush strokes. Each is filled flat with one paint, with bristle
    // lines and a lit ridge of impasto down one side, and laid along a flow that
    // winds round slow whirls. The sun is ringed by concentric strokes of cream
    // and yellow. Gaps between strokes show a darker underpainting.
    //
    // The stroke grid is laid out on (azimuth, elevation), so strokes keep one
    // size from the horizon up. It wraps all the way round with no seam: grid
    // cells and noise lattices are periodic in azimuth, and every distance is
    // taken the short way round.
    //
    // It reacts to the game through the same properties as Custom/ManifoldSkybox,
    // so BackgroundReactor drives it unchanged:
    //   _CombatMode   the whirls wind up and the whole painting twists round the
    //                 sun, strokes shiver, the palette storms to indigo and a few
    //                 strokes burn orange. _KillReact eases it back for an instant.
    //   _ClearReact   the sky settles: every stroke turns to rings round the sun
    //                 and rays beyond them, and the paint goes to gold.
    //   _Collapse     bands of sky slip, and strokes flake off to bare canvas.
    //   _DamageTint, _FlashColor/_FlashAmount, _BeatPulse, _PitchGlow,
    //   _MusicIntensity, _IntroBlend: as in the Manifold sky.
    Properties
    {
        _Deep   ("Paint: deep blue",    Color) = (0.06, 0.18, 0.50, 1)
        _Blue   ("Paint: blue",         Color) = (0.15, 0.40, 0.78, 1)
        _Teal   ("Paint: teal",         Color) = (0.34, 0.70, 0.82, 1)
        _Cream  ("Paint: cream",        Color) = (0.97, 0.92, 0.74, 1)
        _Warm   ("Paint: warm yellow",  Color) = (1.00, 0.78, 0.28, 1)
        _Hot    ("Paint: orange",       Color) = (0.96, 0.44, 0.14, 1)
        _Ground ("Below the horizon",   Color) = (0.80, 0.62, 0.32, 1)
        [HDR] _Sun ("Sun", Color) = (1.8, 1.45, 0.85, 1)
        _SunSize      ("Sun size (deg)",        Range(1, 12)) = 4
        _StrokeScale  ("Strokes per radian",    Range(4, 40)) = 16
        _StrokeLength ("Stroke length",         Range(0.4, 0.98)) = 0.95
        _StrokeWidth  ("Stroke width",          Range(0.15, 0.5)) = 0.3
        _Swirl        ("Swirl",                 Range(0, 2)) = 1
        _Warmth       ("Warmth low in the sky", Range(0, 1)) = 0.6
        _Layers       ("Stroke layers (graphics preset)", Range(0, 2)) = 2

        // Driven by BackgroundReactor (same names as the Manifold sky).
        _BeatPulse      ("Beat Pulse",      Range(0, 1)) = 0
        _MusicIntensity ("Music Intensity", Range(0, 1)) = 0.5
        _PitchGlow      ("Pitch Glow",      Range(0, 1)) = 0
        _CombatMode     ("Combat Mode",     Range(0, 1)) = 0
        _ClearReact     ("Clear Reaction",  Range(0, 1)) = 0
        _KillReact      ("Kill Reaction",   Range(0, 1)) = 0
        _Collapse       ("World Collapse",  Range(0, 1)) = 0
        _DamageTint     ("Damage Tint",     Range(0, 1)) = 0
        _FlashColor     ("Flash Color",     Color) = (1, 1, 1, 1)
        _FlashAmount    ("Flash Amount",    Range(0, 1)) = 0
        _IntroBlend     ("Intro Blend",     Range(0, 1)) = 1
        _IntroColor     ("Intro Color",     Color) = (0.12, 0.14, 0.26, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Assets/Shader/Skybox/PaintedSkyCore.hlsl"


            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.dir = IN.positionOS.xyz;
                return o;
            }

            // The painting itself: PaintedSkyCore.hlsl (the lake paints it too).
            half4 frag(Varyings IN) : SV_Target
            {
                return half4(PaintedSkyColor(normalize(IN.dir)), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
