Shader "Custom/DeconstructiveGeometrySkybox"
{
    Properties
    {
        [Header(Void Canvas)]
        _SpaceColor    ("Deep Space Void", Color) = (0.03, 0.03, 0.06, 1)
        _AmbientGlow   ("Deconstructive Haze", Color) = (0.08, 0.05, 0.12, 1)
        
        [Header(Geometric Splinters)]
        _FacetColorA   ("Primary Fragment (Cold)", Color) = (0.2, 0.35, 0.6, 0.3)
        _FacetColorB   ("Secondary Fragment (Warm)", Color) = (0.5, 0.25, 0.4, 0.2)
        _LineColor     ("Structural Fracture Line", Color) = (0.7, 0.8, 1.0, 0.4)
        
        [Header(Control Parameters)]
        _Complexity    ("Deconstruction Scale", Float) = 3.0
        _FlowSpeed     ("Fluidic Speed", Float) = 0.15
        _FractureSharp ("Line Sharpness", Range(0.01, 0.1)) = 0.03
    }
    SubShader
    {
        // 确保是背景队列，不写入深度
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _SpaceColor, _AmbientGlow, _FacetColorA, _FacetColorB, _LineColor;
            float _Complexity, _FlowSpeed, _FractureSharp;

            struct Attributes 
            { 
                float4 positionOS : POSITION; 
            };

            struct Varyings 
            { 
                float4 positionCS : SV_POSITION; 
                float3 dir        : TEXCOORD0; 
            };

            // 矩阵旋转函数：用于制造多维度的错位剪切
            float2 Rotate2D(float2 p, float angle)
            {
                float s = sin(angle), c = cos(angle);
                return float2(c * p.x - s * p.y, s * p.x + c * p.y);
            }

            Varyings vert(Attributes IN) 
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            // 解构主义核心算法：非线性空间撕裂与伪随机断层
            float3 DeconstructSpace(float3 p, float time)
            {
                // 层级一：基础流动扭曲
                p.xy = Rotate2D(p.xy, time * 0.2);
                p.xz += sin(p.zy * 2.0 + time) * 0.3; // 空间剪切流

                // 层级二：绝对值折叠（IFS），制造解构主义的锐利碎片与非对称折痕
                for(int i = 0; i < 3; i++)
                {
                    p = abs(p) - float3(0.4, 0.2, 0.5);
                    p.yz = Rotate2D(p.yz, time * 0.1 + float(i) * 0.5);
                    p.xy += cos(p.zx * 1.5) * 0.2;
                }
                return p;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                float t = _Time.y * _FlowSpeed;

                // 1. 基础舞台氛围
                float horizon = saturate(1.0 - abs(dir.y));
                half3 finalColor = lerp(_SpaceColor.rgb, _AmbientGlow.rgb, pow(horizon, 3.0));

                // 2. 空间重构与撕裂
                float3 warpedSpace = DeconstructSpace(dir * _Complexity, t);

                // 3. 提取解构碎片的面（Facets）
                // 利用多维坐标的差值形成不规则的几何体色块
                float facetMaskA = saturate(sin(warpedSpace.x * 2.0) * cos(warpedSpace.y * 2.0));
                float facetMaskB = saturate(cos(warpedSpace.z * 1.5) * sin(warpedSpace.x * 1.5));

                // 混合解构块面，带来冷暖交织的半透明叠影
                finalColor += _FacetColorA.rgb * facetMaskA * _FacetColorA.a;
                finalColor += _FacetColorB.rgb * facetMaskB * _FacetColorB.a;

                // 4. 提取解构断层线（Fracture Lines）
                // 使用 fwidth(warpedSpace) 确保几何线条在任何视角、分辨率下都绝对精致、不发糊
                float3 fW = fwidth(warpedSpace);
                float3 edge = abs(frac(warpedSpace - 0.5) - 0.5) / (fW + 0.001);
                float lineVal = 1.0 - min(min(edge.x, edge.y), edge.z);
                float fractureLines = smoothstep(1.0 - _FractureSharp * 10.0, 1.0, lineVal);

                // 让断层线产生若隐若现的能量流动感
                float linePulse = sin(warpedSpace.x + warpedSpace.y + t * 2.0) * 0.3 + 0.7;
                finalColor = lerp(finalColor, finalColor + _LineColor.rgb * 2.0, fractureLines * _LineColor.a * linePulse);

                // 5. 边缘消融（边缘向深空平滑淡出，不显得杂乱）
                finalColor += _AmbientGlow.rgb * (fractureLines * 0.2);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
}