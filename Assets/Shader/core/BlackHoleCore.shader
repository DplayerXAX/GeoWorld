Shader "Custom/GlassySupernova_VolumeCube"
{
    Properties
    {
        [Header(Colors And Core)]
        [HDR] _CoreColor       ("Core Blast (blinding gold)", Color) = (1.5, 1.2, 0.8, 1)
        _ColorA                ("Streak A (muted gold)",     Color) = (0.92, 0.72, 0.32, 1)
        _ColorB                ("Streak B (rust orange)",    Color) = (0.78, 0.38, 0.20, 1)
        
        _CoreRadius            ("Core Gateway Radius",       Range(0.01, 0.5))  = 0.15
        _OuterFade             ("Outer Fade Start",          Range(0.5, 2.0))   = 1.1   // 增大以允许光芒到达边角
        _EruptionSpeed         ("Outward Eruption Speed",    Range(0.5, 5.0))   = 2.5
        _EffectSpread          ("Halo Spread Size",          Range(0.5, 3.0))   = 1.35  // 控制整体在 Mesh 内的缩放比例

        [Header(Manifold Stylization)]
        _GlassSharpness        ("Glass Quantization",        Range(0.0, 1.0))   = 0.85
        _ShardSteps            ("Shard Detail Levels",       Range(2.0, 10.0))  = 5.0
        
        [Header(Crystalline Rays)]
        _SwirlArms             ("Ray Count",                 Range(1, 16))      = 9.0
        _SwirlTwist            ("Spiral Twist",              Range(0, 5))       = 0.8
        _SwirlWarp             ("Streak Warp Amount",        Range(0, 6))       = 2.5
        _StreakSharp           ("Streak Sharpness",          Range(0.3, 10))    = 4.5
        _NoiseScale            ("Noise Scale",               Range(0.5, 12))    = 4.0

        [Header(Blend And Alpha)]
        _Intensity             ("Overall Intensity",         Range(0.2, 3.0))   = 1.2
        _AlphaBoost            ("Core Alpha Boost",          Range(0.0, 2.0))   = 1.0
        _GrainAmount           ("Paper Grain",               Range(0, 0.1))     = 0.035
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back 

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 objPos     : TEXCOORD0;
            };

            float4 _CoreColor, _ColorA, _ColorB;
            float  _CoreRadius, _OuterFade, _EruptionSpeed, _EffectSpread;
            float  _GlassSharpness, _ShardSteps;
            float  _SwirlArms, _SwirlTwist, _SwirlWarp, _StreakSharp, _NoiseScale;
            float  _Intensity, _AlphaBoost, _GrainAmount;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.objPos = v.positionOS.xyz;
                return o;
            }

            // 基础 2D Hash & Noise 
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise2(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float a = Hash21(i);
                float b = Hash21(i + float2(1, 0));
                float c = Hash21(i + float2(0, 1));
                float d = Hash21(i + float2(1, 1));
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 晶体切割噪声
            float GlassyNoise2(float2 p, float sharpness, float steps)
            {
                float n = Noise2(p);
                float quantized = floor(n * steps) / steps;
                return lerp(n, quantized, sharpness);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // ==========================================
                // 1. 体积射线与无限大平面的交点 (Ray-Plane)
                // 解除了之前 0.5 半径内切球的限制，光束可以到达立方体边角
                // ==========================================
                float3 rayOrigin = TransformWorldToObject(GetCameraPositionWS());
                float3 rayDir = normalize(i.objPos - rayOrigin);
                
                // 建立一个永远朝向摄像机的虚拟平面，法线直接指向摄像机
                float3 planeNormal = normalize(rayOrigin); 
                float denom = dot(planeNormal, rayDir);
                
                // 避免射线与平面平行（或者背面剔除）
                if (abs(denom) < 0.0001) discard; 
                
                // 计算相交距离 t
                float tDist = -dot(planeNormal, rayOrigin) / denom;
                if (tDist < 0.0) discard;
                
                // 获取三维物理击中点
                float3 hitPos = rayOrigin + rayDir * tDist;

                // ==========================================
                // 2. 构建纯平面的局部 2D 坐标系 (真正释放 Halo 潜力)
                // ==========================================
                float3 upOS = float3(0, 1, 0);
                float3 rightOS = normalize(cross(upOS, planeNormal) + float3(1e-4, 0, 0));
                upOS = normalize(cross(planeNormal, rightOS));
                
                // 投射到 2D 并加入 _EffectSpread 控制整体弥漫大小
                float2 planar = float2(dot(hitPos, rightOS), dot(hitPos, upOS)) / max(0.01, _EffectSpread); 
                
                float r = length(planar);
                float theta = atan2(planar.y, planar.x);

                // ==========================================
                // 3. 琉璃超新星几何核心逻辑 (Halo Style)
                // ==========================================
                float tTime = _Time.y * _EruptionSpeed;
                float lr = log(max(r, 0.01));
                
                // 极坐标螺旋
                float spin = theta * _SwirlArms - lr * _SwirlTwist;
                float2 warpUV = float2(spin * 0.2, r * _NoiseScale - tTime);
                float warp = GlassyNoise2(warpUV, _GlassSharpness, _ShardSteps);
                float spinW = spin + (warp - 0.5) * _SwirlWarp * 6.28;

                // 锋利的光束边缘
                float smoothStreak = pow(abs(sin(spinW * 0.5)), _StreakSharp);
                float sharpStreak = step(0.85, smoothStreak) * smoothStreak;
                float streak = lerp(smoothStreak, sharpStreak, _GlassSharpness);

                // 色彩混合
                float colorN = GlassyNoise2(warpUV * 2.0 + float2(17.3, 5.1), _GlassSharpness, 3.0);
                float mixT   = saturate((colorN - 0.5) * 2.0 + 0.5);
                half3 streakCol = lerp(_ColorA.rgb, _ColorB.rgb, mixT);

                // 平滑的边缘衰减，没有物理球体的切割感
                // _OuterFade 默认 1.1，会让光芒自然填满并消失在立方体边缘
                float outerFadeMask = 1.0 - smoothstep(_OuterFade, _OuterFade + 0.5, r);
                float radialTaper = (1.0 - smoothstep(_CoreRadius, _OuterFade, r)) * outerFadeMask;

                // 超新星核心 (Blinding Core)
                float coreNoise = GlassyNoise2(float2(theta * 5.0, -tTime * 2.0), 1.0, 4.0);
                float jaggedRadius = _CoreRadius + (coreNoise - 0.5) * 0.08;
                float coreBlast = step(r, jaggedRadius) + smoothstep(jaggedRadius * 1.5, jaggedRadius, r) * 0.5;

                // 结晶碎片
                float shards = step(0.95, GlassyNoise2(warpUV * 4.0 + tTime, 1.0, 5.0)) * radialTaper;

                // ==========================================
                // 4. 最终合成
                // ==========================================
                half3 col = float3(0, 0, 0);
                
                col += streakCol * streak * radialTaper * 1.8;
                col += _ColorA.rgb * shards * 2.5;
                col += _CoreColor.rgb * coreBlast * 1.5;

                // 纸张杂色
                float grain = Hash21(planar * 240.0 + _Time.y * 0.05) - 0.5;
                col += grain * _GrainAmount;

                col = saturate(col) * _Intensity;

                // 提取亮度作为 Alpha
                float luma = dot(col, float3(0.299, 0.587, 0.114));
                float alpha = saturate(luma + coreBlast * _AlphaBoost);
                alpha *= outerFadeMask;

                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    Fallback "Transparent/VertexLit"
}