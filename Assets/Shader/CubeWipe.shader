Shader "GeoWorld/CubeWipe"
{
    // The screen being cut away to the shape of the settings cube, and let back out
    // of it.
    //
    // Draws a captured frame masked to the CUBE'S OWN SILHOUETTE; everything outside
    // is flat paper. The mask is not a shape that resembles the cube — it is the
    // alpha of the very render the cube is drawn from, so the shrinking outline and
    // the solid object that lands in it are the same thing seen twice. That is the
    // whole point: two shapes, however carefully matched, read as two objects.
    //
    // Three rules hold it to the house style:
    //
    //  · The edge is HARD. One `step`, no feather. Every other edge in this game is
    //    hard, and a soft vignette here would be the one blurred boundary on screen.
    //
    //  · The captured frame HOLDS STILL. Only the mask moves. The game is being cut
    //    away, not pushed into the distance — and a picture that scales with its own
    //    outline reads as a zoom, which says the camera moved rather than that the
    //    screen was taken.
    //
    //  · The mask turns because the CUBE turns. Nothing here spins anything; it just
    //    samples whatever pose the cube is in this frame.
    //
    // The old diamond is kept as a fallback for the case where there is no cube to
    // sample — a missing shader or a torn-down stage. This is the pause menu, and it
    // failing to a plain cut is very much worse than it failing to a diamond.
    Properties
    {
        _MainTex   ("Captured Frame", 2D)   = "black" {}
        _MaskTex   ("Cube Silhouette", 2D)  = "white" {}
        _Paper     ("Paper", Color)         = (0.949, 0.937, 0.902, 1)
        _EdgeColor ("Edge", Color)          = (0.086, 0.086, 0.086, 1)

        // 0 = full screen, untouched. 1 = closed onto the cube.
        _Progress  ("Progress", Range(0, 1)) = 0

        _EdgeWidth ("Edge Width", Range(0, 0.05))   = 0.006
        _MinScale  ("Smallest Frame", Range(0, 0.6)) = 0.13
        _Spin      ("Spin", Range(-2, 2))           = 0.35

        // 1 = cut to the cube. 0 = fall back to the diamond.
        _UseMask   ("Use Mask", Float) = 0
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
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
            TEXTURE2D(_MaskTex);  SAMPLER(sampler_MaskTex);

            float4 _Paper, _EdgeColor;
            float  _Progress, _EdgeWidth, _MinScale, _Spin, _UseMask;

            // Where the cube's square render lands on screen, in screen UV:
            // xy = centre, zw = half-width and half-height. Written every frame by
            // CubeWipe from the cube's actual rect, so the mask tracks it exactly
            // rather than being animated alongside it and hoping they agree.
            float4 _MaskRect;

            // Aspect, so the fallback ◇ is a real diamond on screen instead of one
            // stretched into a lozenge by a wide window.
            float  _Aspect;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            // 1 where this point — in the cube render's own 0..1 space — falls
            // inside the silhouette.
            float MaskAt(float2 m)
            {
                // Outside the square the render occupies there is no cube, and the
                // clamped sampler would otherwise smear its edge pixels across the
                // rest of the screen.
                float within = step(0.0, m.x) * step(m.x, 1.0)
                             * step(0.0, m.y) * step(m.y, 1.0);
                return within * step(0.5, SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, m).a);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float inside = 0.0;
                float ring   = 0.0;

                if (_UseMask > 0.5)
                {
                    // Screen UV into the cube render's own square.
                    float2 m = (IN.uv - _MaskRect.xy) / max(_MaskRect.zw * 2.0, 1e-6) + 0.5;
                    inside = MaskAt(m);

                    // The rule on the boundary, found by asking again a little further
                    // out from the middle: inside here and outside there means this is
                    // the edge. Cheaper and more exact than any distance field, since
                    // the silhouette is already a hard-edged picture — and measured in
                    // the MASK's space, so the line stays the same weight relative to
                    // the cube however large it is drawn.
                    float2 outward = normalize(m - 0.5 + 1e-6) * _EdgeWidth * 2.0;
                    ring = inside * (1.0 - MaskAt(m + outward));
                }
                else
                {
                    // ── Fallback: the diamond ──────────────────────────────────
                    float2 p = IN.uv - 0.5;
                    p.x *= _Aspect;

                    float t = saturate(_Progress);
                    float e = 1.0 - (1.0 - t) * (1.0 - t);

                    float a = e * _Spin;
                    float2 r = float2(p.x * cos(a) - p.y * sin(a),
                                      p.x * sin(a) + p.y * cos(a));

                    float startHalf = 0.5 * (abs(_Aspect) + 1.0);
                    float half_     = lerp(startHalf, _MinScale, e);

                    float dist = abs(r.x) + abs(r.y);
                    inside = 1.0 - step(half_, dist);
                    ring   = inside * step(half_ - _EdgeWidth, dist);
                }

                // The frame does NOT move. The mask is a window closing over a picture
                // that stays exactly where it was — so what the player watches is the
                // game being cut away, not the game receding into the distance. A
                // shrinking picture reads as a zoom-out; a shrinking hole over a still
                // one reads as the screen being taken.
                half4 shot = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);

                half3 col = lerp(_Paper.rgb, shot.rgb, inside);
                col = lerp(col, _EdgeColor.rgb, ring);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
