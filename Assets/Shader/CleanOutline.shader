Shader "GeoWorld/CleanOutline"
{
    // Clean Constructivism-style outline: pure Sobel + binary threshold.
    // No noise, no dropout, no thickness variation — a hard black line
    // that defines geometry as a graphic shape against the background.
    Properties
    {
        _Thickness       ("Thickness (px)",        Range(0.5, 6)) = 1.6
        _OutlineColor    ("Outline Color",         Color)         = (0.08, 0.07, 0.10, 1)
        _DepthThreshold  ("Depth Threshold",       Float)         = 0.5
        _NormalThreshold ("Normal Threshold",      Float)         = 0.95
        _OutlineOpacity  ("Outline Opacity",       Range(0, 1))   = 0.8
        // Anti-aliasing transition width — wider = softer line.
        _EdgeSoftness    ("Edge Softness",         Range(0.05, 1))= 0.25
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            Name "CleanOutlinePass"
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
            float  _EdgeSoftness;

            float SobelDepthOne(float2 uv, float2 ts)
            {
                float c00 = SampleSceneDepth(uv + ts * float2(-1, -1));
                float c10 = SampleSceneDepth(uv + ts * float2( 0, -1));
                float c20 = SampleSceneDepth(uv + ts * float2( 1, -1));
                float c01 = SampleSceneDepth(uv + ts * float2(-1,  0));
                float c21 = SampleSceneDepth(uv + ts * float2( 1,  0));
                float c02 = SampleSceneDepth(uv + ts * float2(-1,  1));
                float c12 = SampleSceneDepth(uv + ts * float2( 0,  1));
                float c22 = SampleSceneDepth(uv + ts * float2( 1,  1));

                float gx = -c00 - 2*c01 - c02 + c20 + 2*c21 + c22;
                float gy = -c00 - 2*c10 - c20 + c02 + 2*c12 + c22;
                return sqrt(gx*gx + gy*gy);
            }

            float SobelNormalOne(float2 uv, float2 ts)
            {
                float3 n00 = SampleSceneNormals(uv + ts * float2(-1, -1));
                float3 n22 = SampleSceneNormals(uv + ts * float2( 1,  1));
                float3 n02 = SampleSceneNormals(uv + ts * float2(-1,  1));
                float3 n20 = SampleSceneNormals(uv + ts * float2( 1, -1));
                return length(n22 - n00) + length(n20 - n02);
            }

            // 4× sub-pixel supersampling: take Sobel at four jittered offsets
            // within the current texel and average. Each pixel's edge "score"
            // becomes fractional coverage — diagonals get smooth grey instead
            // of binary on/off staircase.
            float SobelDepthMS(float2 uv, float2 ts, float2 texel)
            {
                float s = 0;
                s += SobelDepthOne(uv + texel * float2(-0.25, -0.25), ts);
                s += SobelDepthOne(uv + texel * float2( 0.25, -0.25), ts);
                s += SobelDepthOne(uv + texel * float2(-0.25,  0.25), ts);
                s += SobelDepthOne(uv + texel * float2( 0.25,  0.25), ts);
                return s * 0.25;
            }

            float SobelNormalMS(float2 uv, float2 ts, float2 texel)
            {
                float s = 0;
                s += SobelNormalOne(uv + texel * float2(-0.25, -0.25), ts);
                s += SobelNormalOne(uv + texel * float2( 0.25, -0.25), ts);
                s += SobelNormalOne(uv + texel * float2(-0.25,  0.25), ts);
                s += SobelNormalOne(uv + texel * float2( 0.25,  0.25), ts);
                return s * 0.25;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 uv    = IN.texcoord;
                float2 texel = _BlitTexture_TexelSize.xy;
                float2 ts    = texel * _Thickness;

                float edgeD = SobelDepthMS (uv, ts, texel);
                float edgeN = SobelNormalMS(uv, ts, texel);

                // Wider transition window — combined with 4× supersample
                // gives diagonals real sub-pixel anti-aliasing.
                float dT = _DepthThreshold * 0.001;
                float depthEdge  = smoothstep(dT, dT + _EdgeSoftness * 0.05,        edgeD);
                float normalEdge = smoothstep(_NormalThreshold,
                                              _NormalThreshold + _EdgeSoftness * 2, edgeN);
                float edge       = max(depthEdge, normalEdge) * _OutlineOpacity;

                half4 src = SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, uv);
                return lerp(src, _OutlineColor, edge);
            }
            ENDHLSL
        }
    }
}
