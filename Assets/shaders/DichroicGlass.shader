Shader "Custom/URP_PerFace_Dichroic"
{
    Properties
    {
        [HDR] _BaseColor("Base Tint (Transparent)", Color) = (1, 1, 1, 0.2)
        _Smoothness("Smoothness", Range(0, 1)) = 0.98

        [Header(Iridescence Per Face)]
        _ColorSplitScale("Face Color Differentiator", Range(0.1, 10)) = 3.0
        [HDR] _GlowIntensity("Iridescence Intensity (HDR)", Range(0, 10)) = 4.0

        [Header(Refraction)]
        _RefractionDistortion("Refraction Distortion", Range(0, 0.2)) = 0.05
    }

        SubShader
        {
            Tags
            {
                "RenderPipeline" = "UniversalPipeline"
                "Queue" = "Transparent+1" // 确保在普通透明物体后渲染，利于抓取背景
                "RenderType" = "Transparent"
            }

            LOD 100
            Cull Off // 双面渲染
            Blend SrcAlpha OneMinusSrcAlpha // 透明混合
            ZWrite Off

            Pass
            {
                Name "ForwardLit"
                Tags { "LightMode" = "UniversalForward" }

                HLSLPROGRAM
                #pragma vertex vert
                #pragma fragment frag

                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
                #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

                struct Attributes
                {
                    float4 positionOS   : POSITION;
                    float3 normalOS     : NORMAL;
                };

                struct Varyings
                {
                    float4 positionCS   : SV_POSITION;
                    float3 normalWS     : TEXCOORD0;
                    float3 viewDirWS    : TEXCOORD1;
                    float4 screenPos    : TEXCOORD2;
                };

                CBUFFER_START(UnityPerMaterial)
                    float4 _BaseColor;
                    float _Smoothness;
                    float _ColorSplitScale;
                    float _GlowIntensity;
                    float _RefractionDistortion;
                CBUFFER_END

                    // 声明背景纹理
                    TEXTURE2D(_CameraOpaqueTexture);
                    SAMPLER(sampler_CameraOpaqueTexture);

                    // 伪彩虹函数
                    float3 SpectralSpectrum(float value)
                    {
                        float3 color;
                        color.r = sin(value * 6.28318 + 0.0) * 0.5 + 0.5;
                        color.g = sin(value * 6.28318 + 2.0) * 0.5 + 0.5;
                        color.b = sin(value * 6.28318 + 4.0) * 0.5 + 0.5;
                        return color;
                    }

                    Varyings vert(Attributes input)
                    {
                        Varyings output;
                        VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                        output.positionCS = vertexInput.positionCS;
                        output.screenPos = ComputeScreenPos(vertexInput.positionCS);
                        output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                        output.viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
                        return output;
                    }

                    half4 frag(Varyings input) : SV_Target
                    {
                        // 初始化基础参数
                        float3 normalWS = normalize(input.normalWS);
                        float3 viewDirWS = normalize(input.viewDirWS);
                        float nv = saturate(dot(normalWS, viewDirWS));

                        // ========================================================
                        // 核心修改：基于面法线的颜色差异化
                        // 1. 获取世界坐标法线的绝对值，得到主要朝向（X, Y, Z）
                        float3 absNormal = abs(normalWS);

                        // 2. 将 X,Y,Z 朝向转换为一个彩虹色谱上的采样值
                        // 这里我们将 XYZ 映射到 0.0, 0.33, 0.66 左右，强制拉开色差
                        float faceHue = absNormal.x * 0.0 + absNormal.y * 0.33 + absNormal.z * 0.66;

                        // 3. 融合轻微的视线变化（让面内仍有渐变，但面的主色调由面决定）
                        // _ColorSplitScale 决定面之间颜色差多大，菲涅尔(1-nv)保证边缘有过渡
                        float finalHueSample = (faceHue * _ColorSplitScale) + (1.0 - nv);

                        // 4. 生成彩虹色
                        float3 iridescenceColor = SpectralSpectrum(finalHueSample) * _GlowIntensity;
                        // ========================================================

                        // 2. 简易幕后折射
                        float4 screenPos = input.screenPos / input.screenPos.w;
                        float2 refractionOffset = normalWS.xy * _RefractionDistortion * (1.0 - nv);
                        float4 refractedSceneColor = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, screenPos.xy + refractionOffset);

                        // 3. 最终颜色组合
                        float3 finalColor = lerp(refractedSceneColor.rgb, _BaseColor.rgb, _BaseColor.a);
                        finalColor += iridescenceColor;

                        // 4. 模拟锋利高光
                        Light mainLight = GetMainLight();
                        float3 lightDirWS = normalize(mainLight.direction);
                        float3 specular = mainLight.color * pow(saturate(dot(reflect(-lightDirWS, normalWS), viewDirWS)), _Smoothness * 256.0);
                        finalColor += specular * _Smoothness;

                        return half4(finalColor, _BaseColor.a);
                    }
                    ENDHLSL
                }
        }
            FallBack "Transparent/VertexLit"
}