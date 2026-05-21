Shader "GeoWorld/SketchyOutline"
{
    // Fullscreen post-process: Sobel on depth + normals → black outline.
    // Per-pixel hash-noise jitters the sample UV so the outline "wobbles"
    // instead of being a clean line — reads as a brush / ink stroke.
    Properties
    {
        _Thickness        ("Thickness (px)",      Float)         = 1.5
        _OutlineColor     ("Outline Color",       Color)         = (0, 0, 0, 1)
        _DepthThreshold   ("Depth Threshold",     Float)         = 0.5
        _NormalThreshold  ("Normal Threshold",    Float)         = 0.4
        _NoiseStrength    ("Noise Jitter",        Range(0, 3))   = 0.6
        _NoiseFrequency   ("Noise Frequency",     Float)         = 480
        _OutlineOpacity   ("Outline Opacity",     Range(0, 1))   = 1
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
            float  _NoiseStrength;
            float  _NoiseFrequency;
            float  _OutlineOpacity;

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
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
                float2 uv = IN.texcoord;
                float2 ts = _BlitTexture_TexelSize.xy * _Thickness;

                // Hash-driven per-pixel UV jitter → "wobbly" outline.
                float h        = Hash21(uv * _NoiseFrequency);
                float2 jitter  = (float2(h, Hash21(uv * _NoiseFrequency + 17.31)) - 0.5) * _NoiseStrength * ts;
                float2 jittered = uv + jitter;

                float edgeD = SobelDepth (jittered, ts);
                float edgeN = SobelNormal(jittered, ts);

                // Two thresholds, max-combined.
                float depthEdge  = saturate((edgeD - _DepthThreshold  * 0.001) * 80.0);
                float normalEdge = saturate((edgeN - _NormalThreshold) * 5.0);
                float edge       = max(depthEdge, normalEdge);

                // Extra hash mask: drop ~25 % of edge pixels → ink-dropout feel.
                float dropout = Hash21(uv * 7000.0 + 0.137);
                edge *= step(0.20, dropout);

                edge *= _OutlineOpacity;

                half4 src = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, IN.texcoord);
                return lerp(src, _OutlineColor, edge);
            }
            ENDHLSL
        }
    }
}
