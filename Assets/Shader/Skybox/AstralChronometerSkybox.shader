Shader "Custom/NeuromancerProtocolSkybox"
{
    Properties
    {
        _MatrixBgColor ("Matrix Void Color", Color) = (0.0, 0.02, 0.01, 1)
        _CodeColor     ("Base Code Line Color", Color) = (0.0, 1.0, 0.33, 1)
        _GlitchColor   ("Glitch Shift Color", Color) = (1.0, 0.0, 0.4, 1)
        
        _GridDensity   ("Matrix Resolution", Float) = 32.0
        _StreamSpeed   ("Data Cascade Speed", Float) = 2.5
        
        // Dynamic Controls
        _BeatPulse     ("Beat Pulse", Range(0,1)) = 0
        _MusicIntensity("Music Intensity", Range(0,1)) = 0.5
        _CombatMode    ("Glitch Intensity", Range(0,1)) = 0
        _DamageTint    ("System Corrupted", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _MatrixBgColor, _CodeColor, _GlitchColor;
            float _GridDensity, _StreamSpeed;
            float _BeatPulse, _MusicIntensity, _CombatMode, _DamageTint;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;
                return OUT;
            }

            // 程序化伪随机哈希
            float Hash3D(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // 计算单通道二进制流
            float SampleDataStream(float3 sampleDir, float speedOffset)
            {
                // 量化球面坐标，形成方块矩阵点阵
                float3 gridId = floor(sampleDir * _GridDensity);
                
                // 基于每个垂直列的随机哈希计算流动偏移
                float rawHash = Hash3D(float3(gridId.x, 0.0, gridId.z));
                
                // 产生断续滚动的瀑布流
                float cascade = frac(gridId.y * 0.04 - _Time.y * _StreamSpeed * (0.4 + rawHash * 0.6) + speedOffset);
                
                // 过滤出亮点的头部信息和微弱的拖尾
                float leadPoint = step(0.94, cascade) * 2.0;
                float tailTrail = pow(cascade, 6.0) * 0.75;
                
                // 剔除部分列，使其疏密有致
                float columnMask = step(0.45, rawHash);
                
                return (leadPoint + tailTrail) * columnMask;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                
                // ── 战斗联动：画面高频切片断裂故障 (Glitch Slicing) ──
                if (_CombatMode > 0.01)
                {
                    float glitchTime = floor(_Time.y * 24.0); // 阶梯式高频时间
                    float sliceNoise = Hash3D(float3(0.0, floor(dir.y * 15.0), glitchTime));
                    
                    // 仅对特定水平切片层进行突发性横向撕裂
                    float sliceMask = step(1.0 - _CombatMode * 0.45, sliceNoise);
                    dir.x += sin(dir.y * 100.0 + _Time.y) * 0.15 * sliceMask * _CombatMode;
                    dir = normalize(dir);
                }

                // ── 赛博朋克色散分离采样 (Chromatic Aberration) ──
                // 通过让RGB采样通道产生坐标偏置，模拟硬件系统崩溃
                float aberration = _CombatMode * 0.05 + _BeatPulse * _MusicIntensity * 0.01;
                
                float streamR = SampleDataStream(normalize(dir + float3(aberration, 0, 0)), 0.0);
                float streamG = SampleDataStream(dir, 0.0);
                float streamB = SampleDataStream(normalize(dir - float3(aberration, 0, 0)), 0.03);

                // ── 拼装多通道颜色 ──
                half3 baseMatrixCol = _CodeColor.rgb * streamG;
                
                // 故障色散混色：R和B通道偏向赛博霓虹粉/蓝
                half3 glitchComponent = _GlitchColor.rgb * streamR + half3(0.0, 0.3, 1.0) * streamB;
                half3 finalCodeNet = lerp(baseMatrixCol, glitchComponent, saturate(_CombatMode * 1.2));

                // ── 数字化透视扫描网格 (Neuromancer Lattice) ──
                float3 gridLines = abs(frac(dir * _GridDensity * 0.5) - 0.5) / fwidth(dir * _GridDensity * 0.5);
                float lattice = 1.0 - min(min(gridLines.x, gridLines.y), gridLines.z);
                lattice = saturate(lattice * 0.08) * (1.0 - _CombatMode * 0.4); // 战斗时线框破碎

                // ── 音乐超载脉冲 (Core Overload) ──
                float intensity = 0.5 + _MusicIntensity * 0.5;
                half3 bgCol = _MatrixBgColor.rgb * (1.0 + _BeatPulse * 2.0 * intensity);
                
                // 激烈战斗时背景反转为刺眼的死机灰白，随后被数据吞噬
                bgCol = lerp(bgCol, half3(0.08, 0.1, 0.15), _CombatMode);

                half3 result = bgCol + finalCodeNet * (1.0 + _BeatPulse * 1.2) + _CodeColor.rgb * lattice;

                // ── 全局系统损坏滤镜 (System Corrupted/Damage Pass) ──
                // 区别于传统的血红，这里将画面转为极具数码感的“硬件过载致命红”
                float finalLuma = dot(result, float3(0.299, 0.587, 0.114));
                half3 corruptedPalette = half3(finalLuma * 1.8, result.g * 0.02, result.b * 0.05);
                result = lerp(result, corruptedPalette, _DamageTint);

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}