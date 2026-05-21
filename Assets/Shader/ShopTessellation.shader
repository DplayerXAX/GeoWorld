Shader "GeoWorld/ShopTessellation"
{
    // Procedural triangle tessellation backdrop for the shop interior.
    // Each grid cell is split diagonally into two triangles; per-cell hash
    // decides which diagonal and which palette colour. Slow scroll + hue
    // breathing gives a "living dimension" feel without burning a texture.
    Properties
    {
        _ColorA      ("Palette A", Color) = (0.60, 0.30, 0.80, 1)
        _ColorB      ("Palette B", Color) = (0.92, 0.45, 0.20, 1)
        _ColorC      ("Palette C", Color) = (0.78, 0.74, 0.92, 1)
        _ColorD      ("Palette D", Color) = (0.30, 0.20, 0.55, 1)
        _TileSize    ("Tile Size",    Float) = 14
        _ScrollSpeed ("Scroll Speed", Float) = 0.035
        _BreathSpeed ("Breath Speed", Float) = 0.45
        _Dimming     ("Edge Dimming", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Cull Off
        ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _ColorA, _ColorB, _ColorC, _ColorD;
            float  _TileSize;
            float  _ScrollSpeed;
            float  _BreathSpeed;
            float  _Dimming;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv         = IN.uv;
                return OUT;
            }

            // Cheap 2D hash.
            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float t  = _Time.y;

                // Slow scroll so the pattern "drifts" through the dimension.
                float2 uv = IN.uv + float2(_ScrollSpeed * t, _ScrollSpeed * 0.7 * t);

                float2 cellUV = uv * _TileSize;
                float2 cell   = floor(cellUV);
                float2 lu     = frac(cellUV);

                // Per-cell flip of the diagonal so triangle orientations vary.
                bool  flipped = hash21(cell + 7.13) > 0.5;
                float pivot   = flipped ? (lu.x + lu.y) : (lu.y - lu.x + 1.0);
                int   triId   = pivot > 1.0 ? 1 : 0;

                float h = hash21(cell + float2(triId, 3.7));

                // Slow per-cell hue-walk so colours keep shifting subtly.
                float breath = 0.5 + 0.5 * sin(t * _BreathSpeed + h * 6.2831);
                float idx    = frac(h + breath * 0.2) * 4.0;

                half4 col;
                if      (idx < 1.0) col = lerp(_ColorA, _ColorB, frac(idx));
                else if (idx < 2.0) col = lerp(_ColorB, _ColorC, frac(idx));
                else if (idx < 3.0) col = lerp(_ColorC, _ColorD, frac(idx));
                else                col = lerp(_ColorD, _ColorA, frac(idx));

                // Radial vignette so the backdrop fades into the rift's dark.
                float r    = length(IN.uv - 0.5) * 2.0;
                float dim  = 1.0 - smoothstep(1.0 - _Dimming, 1.2, r) * _Dimming;
                col.rgb   *= dim;

                return col;
            }
            ENDHLSL
        }
    }
}
