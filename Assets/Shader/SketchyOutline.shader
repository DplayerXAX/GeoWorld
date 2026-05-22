Shader "GeoWorld/SketchyOutline"
{
    // Fullscreen post-process: Sobel on depth + normals → black outline.
    // Per-pixel hash-noise jitters the sample UV so the outline "wobbles"
    // instead of being a clean line — reads as a brush / ink stroke.
    Properties
    {
        _Thickness          ("Thickness (px)",          Float)         = 1.8
        _OutlineColor       ("Outline Color",           Color)         = (0, 0, 0, 1)
        _DepthThreshold     ("Depth Threshold",         Float)         = 0.5
        _NormalThreshold    ("Normal Threshold",        Float)         = 0.4
        _OutlineOpacity     ("Outline Opacity",         Range(0, 1))   = 1

        [Header(Charcoal)]
        _SampleJitter       ("Sample UV Jitter (px)",   Range(0, 5))   = 1.4
        _DropoutScale       ("Dropout Pattern Scale",   Float)         = 7
        _DropoutThreshold   ("Dropout Cutoff",          Range(0, 1))   = 0.45
        _ThicknessNoiseScale("Thickness Noise Scale",   Float)         = 4
        _ThicknessVariation ("Thickness Variation",     Range(0, 1))   = 0.7
        _OffsetNoiseScale   ("Edge Drift Scale",        Float)         = 3
        _OffsetAmount       ("Edge Drift (px)",         Range(0, 4))   = 1.2
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "SketchyOutlinePass"

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float  _Thickness;
            float4 _OutlineColor;
            float  _DepthThreshold;
            float  _NormalThreshold;
            float  _OutlineOpacity;
            float  _SampleJitter;
            float  _DropoutScale;
            float  _DropoutThreshold;
            float  _ThicknessNoiseScale;
            float  _ThicknessVariation;
            float  _OffsetNoiseScale;
            float  _OffsetAmount;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // Smooth fbm for natural-looking variation (not high-freq hash).
            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i + float2(0, 0));
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Fbm3(float2 p)
            {
                float v = 0;
                float a = 0.5;
                [unroll]
                for (int i = 0; i < 3; i++) { v += a * ValueNoise(p); p *= 2.07; a *= 0.5; }
                return v;
            }

            float SobelDepth(float2 uv, float2 ts)
            {
                float c00 = SampleSceneDepth(uv + ts * float2(-1, -1));
                float c10 = SampleSceneDepth(uv + ts * float2( 0, -1));
                float c20 = SampleSceneDepth(uv + ts * float2( 1, -1));
                float c01 = SampleSceneDepth(uv + ts * float2(-1,  0));
                float c21 = SampleSceneDepth(uv + ts * float2( 1,  0));
                float c02 = SampleSceneDepth(uv + ts * float2(-1,  1));
                float c12 = SampleSceneDepth(uv + ts * float2( 0,  1));
                float c22 = SampleSceneDepth(uv + ts * float2( 1,  1));

                float gx = -c00 - 2.0 * c01 - c02 + c20 + 2.0 * c21 + c22;
                float gy = -c00 - 2.0 * c10 - c20 + c02 + 2.0 * c12 + c22;
                return sqrt(gx * gx + gy * gy);
            }

            float SobelNormal(float2 uv, float2 ts)
            {
                float3 n00 = SampleSceneNormals(uv + ts * float2(-1, -1));
                float3 n22 = SampleSceneNormals(uv + ts * float2( 1,  1));
                float3 n02 = SampleSceneNormals(uv + ts * float2(-1,  1));
                float3 n20 = SampleSceneNormals(uv + ts * float2( 1, -1));
                return length(n22 - n00) + length(n20 - n02);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv      = IN.texcoord;
                float2 texel   = _BlitTexture_TexelSize.xy;

                // ── 1) Thickness varies along the outline (fBM, low freq) ─
                float thickN     = Fbm3(uv * _ThicknessNoiseScale);
                float thickMul   = 1.0 + (thickN - 0.5) * _ThicknessVariation;
                float2 ts        = texel * _Thickness * thickMul;

                // ── 2) Edge sampling drift: offset edge detection by an fBM
                //       vector, so the line doesn't sit exactly on the
                //       geometry — like charcoal stroke applied near the edge.
                float2 drift = float2(
                    Fbm3(uv * _OffsetNoiseScale)         - 0.5,
                    Fbm3(uv * _OffsetNoiseScale + 13.7)  - 0.5
                ) * _OffsetAmount * texel * 2.0;

                // ── 3) Local high-freq sample jitter (kept small) ─────────
                float h1 = Hash21(uv * 480.0);
                float h2 = Hash21(uv * 480.0 + 17.31);
                float2 microJitter = (float2(h1, h2) - 0.5) * _SampleJitter * texel;

                float2 sampleUV = uv + drift + microJitter;

                float edgeD = SobelDepth (sampleUV, ts);
                float edgeN = SobelNormal(sampleUV, ts);

                float depthEdge  = saturate((edgeD - _DepthThreshold * 0.001) * 80.0);
                float normalEdge = saturate((edgeN - _NormalThreshold) * 5.0);
                float edge       = max(depthEdge, normalEdge);

                // ── 4) fBM dropout — line breaks into chunks like real charcoal.
                float dropout = Fbm3(uv * _DropoutScale);
                edge *= smoothstep(_DropoutThreshold, _DropoutThreshold + 0.15, dropout);

                edge *= _OutlineOpacity;

                half4 src = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                return lerp(src, _OutlineColor, edge);
            }
            ENDHLSL
        }
    }
}
