// 秩序 (Order) — orthogonal blueprint pattern:
//   • Background: dark teal
//   • Base: thin grid of small squares (repeating, uniform)
//   • Accent: concentric square frames at evenly-spaced distances
//
// All angles 90°, no curves, perfectly symmetric — reads as discipline /
// architectural rigor / Constructivism order. Static; no time animation.
Shader "GeoWorld/Synergy/OrderGrid"
{
    Properties
    {
        _BgColor      ("Background",         Color)         = (0.04, 0.10, 0.12, 1)
        _GridColor    ("Grid Lines",         Color)         = (0.15, 0.55, 0.55, 1)
        _AccentColor  ("Concentric Frames",  Color)         = (0.35, 0.90, 0.90, 1)
        _GridDensity  ("Grid Cells / Face",  Range(2, 30))  = 8
        _GridWidth    ("Grid Line Width",    Range(0, 0.4)) = 0.05
        _FrameCount   ("Concentric Frames",  Range(0, 6))   = 3
        _FrameWidth   ("Frame Line Width",   Range(0, 0.1)) = 0.018
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BgColor;
                float4 _GridColor;
                float4 _AccentColor;
                float  _GridDensity;
                float  _GridWidth;
                float  _FrameCount;
                float  _FrameWidth;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // 1) Base grid: thin lines at every sub-cell boundary.
                float2 g    = frac(IN.uv * _GridDensity);
                float gridX = step(g.x, _GridWidth) + step(1.0 - _GridWidth, g.x);
                float gridY = step(g.y, _GridWidth) + step(1.0 - _GridWidth, g.y);
                float gridMask = saturate(gridX + gridY);

                // 2) Concentric square frames via Chebyshev distance from center.
                //    dist=0 at center, dist=1 at face edge.
                float2 cuv = IN.uv - 0.5;
                float  dist = max(abs(cuv.x), abs(cuv.y)) * 2.0;

                // Hard-cap iterations at 6 (shader compiler likes a constant bound).
                float accentMask = 0.0;
                [unroll(6)]
                for (int i = 1; i <= 6; i++)
                {
                    if ((float)i > _FrameCount) break;
                    float t = (float)i / (_FrameCount + 1.0);
                    accentMask += step(abs(dist - t), _FrameWidth);
                }
                accentMask = saturate(accentMask);

                // Layer bottom-to-top: bg → grid → accent frames.
                half3 col = lerp(_BgColor.rgb,   _GridColor.rgb,    gridMask);
                col       = lerp(col,            _AccentColor.rgb,  accentMask);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
