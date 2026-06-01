// 启发 (Enlightenment) — concentric square frames pulsing outward from
// the face center while the whole pattern slowly rotates. Scan / radar feel.
//
// Drives entirely off _Time so the panel animates without any C# coroutines.
// Assign to a Material, drop into FaceTextureVisualizer.panelMaterial.
Shader "GeoWorld/Synergy/RadiantFrames"
{
    Properties
    {
        _FrameColor   ("Frame Color",          Color)        = (0.30, 0.65, 1.00, 1)
        _BgColor      ("Background",           Color)        = (0.05, 0.08, 0.18, 1)
        _Density      ("Frame Density",        Range(1, 20)) = 5
        _ExpandSpeed  ("Expand Speed",         Range(0, 5))  = 1.0
        _RotateSpeed  ("Rotate Speed (rad/s)", Range(-3, 3)) = 0.4
        _LineWidth    ("Line Width",           Range(0.01, 0.4)) = 0.08
        _Falloff      ("Edge Falloff",         Range(0, 2))  = 0.35
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
                float4 _FrameColor;
                float4 _BgColor;
                float  _Density;
                float  _ExpandSpeed;
                float  _RotateSpeed;
                float  _LineWidth;
                float  _Falloff;
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
                // Center the UV around (0,0)
                float2 uv = IN.uv - 0.5;

                // Rotate around center by _Time × _RotateSpeed
                float ang = _Time.y * _RotateSpeed;
                float s   = sin(ang);
                float c   = cos(ang);
                float2 r  = float2(c * uv.x - s * uv.y,
                                   s * uv.x + c * uv.y);

                // Chebyshev distance = square contours (0 at center, 1 at edge)
                float dist = max(abs(r.x), abs(r.y)) * 2.0;

                // Scrolling outward bands. Multiple bands = multiple frames
                // expanding simultaneously, staggered by _Density.
                float scrolled  = dist * _Density - _Time.y * _ExpandSpeed;
                float bandPhase = frac(scrolled);

                // Thin frame line where phase ≈ 0 (or 1, since they wrap).
                float band = step(bandPhase, _LineWidth);

                // Optional radial falloff — frames dim out at edges.
                float fade = saturate(1.0 - dist * _Falloff);

                half3 col = lerp(_BgColor.rgb, _FrameColor.rgb, band * fade);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
