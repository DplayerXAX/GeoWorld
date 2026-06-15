Shader "GeoWorld/TitleConstructivist"
{
    // Full-screen constructivist backdrop: paper base, a slow rotating field of
    // grid lines, a sweeping signal-red diagonal band, gold stripes, and scattered
    // ink / blue rectangles. Cursor (_Mouse, 0..1) tilts and parallaxes the field.
    Properties
    {
        _Paper  ("Paper",  Color) = (0.949, 0.937, 0.902, 1)
        _Ink    ("Ink",    Color) = (0.086, 0.086, 0.086, 1)
        _Signal ("Signal", Color) = (0.886, 0.141, 0.106, 1)
        _Gold   ("Gold",   Color) = (0.910, 0.698, 0.227, 1)
        _Blue   ("Blue",   Color) = (0.169, 0.424, 0.690, 1)
        _Mouse  ("Mouse (xy 0..1)", Vector) = (0.5, 0.5, 0, 0)
        _Speed  ("Speed", Float) = 1
        _Density("Grid Density", Float) = 14
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _Paper, _Ink, _Signal, _Gold, _Blue;
            float4 _Mouse;
            float  _Speed, _Density;

            struct Attributes { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.pos = TransformObjectToHClip(i.pos.xyz);
                o.uv  = i.uv;
                return o;
            }

            float2 rot(float2 p, float a) { float s = sin(a), c = cos(a); return float2(c * p.x - s * p.y, s * p.x + c * p.y); }
            float  hash(float2 p) { return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float  t  = _Time.y * _Speed;
                float2 uv = i.uv - 0.5;
                uv.x *= _ScreenParams.x / max(1.0, _ScreenParams.y);

                float2 m   = _Mouse.xy - 0.5;
                float  ang = 0.15 + t * 0.03 + m.x * 0.25;
                float2 p   = rot(uv + m * 0.10, ang);

                half3 col = _Paper.rgb;

                // Thin drifting grid (ink).
                float2 g    = abs(frac(p * _Density + float2(t * 0.05, -t * 0.04)) - 0.5);
                float  grid = smoothstep(0.47, 0.5, max(g.x, g.y));
                col = lerp(col, _Ink.rgb, grid * 0.12);

                // Big diagonal signal band sweeping across.
                float band = sin((p.x * 2.2 + p.y * 0.6) * 3.14159 + t * 0.6);
                col = lerp(col, _Signal.rgb, smoothstep(0.86, 0.93, band));

                // Gold stripes on a second angle.
                float2 q    = rot(uv, -0.6 + m.y * 0.2);
                float  gold = frac(q.x * 6.0 - t * 0.15);
                col = lerp(col, _Gold.rgb, step(0.92, gold) * 0.9);

                // Scattered ink / blue rectangles via cell hash (branchless).
                float2 cp  = p * 5.0 + float2(t * 0.02, 0);
                float  h   = hash(floor(cp));
                float2 f   = frac(cp);
                float  box = step(0.15, f.x) * step(f.x, 0.5) * step(0.55, f.y) * step(f.y, 0.85);
                float  blueSel = step(0.93, h);
                float  inkSel  = saturate(step(0.86, h) - blueSel);
                col = lerp(col, _Blue.rgb, box * blueSel * 0.85);
                col = lerp(col, _Ink.rgb,  box * inkSel  * 0.5);

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    Fallback "Universal Render Pipeline/Unlit"
}
