// 用于 LineRenderer 的路径激光线 shader。
// UV.x = 沿线方向 0→1（LineTextureMode.Tile 时按世界长度平铺）
// UV.y = 跨线方向 0→1（0 和 1 是边缘，0.5 是中心）
Shader "Custom/PathLaser"
{
    Properties
    {
        _Color      ("Laser Color",   Color)  = (0.3, 0.9, 1.0, 1)
        _FlowSpeed  ("Flow Speed",    Float)  = 2.2
        _SparkCount ("Sparks/Unit",   Float)  = 2.5    // 每世界单位流动粒子数
        _Sharpness  ("Spark Shape",   Float)  = 7.0    // 越大粒子越细
        _CorePower  ("Core Tightness",Float)  = 2.2    // 线中心亮度收紧
        _Intensity  ("Intensity",     Float)  = 4.5
        _PulseSpeed ("Pulse Speed",   Float)  = 1.4
        _PulseDepth ("Pulse Depth",   Float)  = 0.22
        // 4 = LEqual (normal, occluded by geometry). Runtime-set to 8 (Always) for the
        // middle-mouse "x-ray all paths" mode — see PathFlowManager.
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+2" }

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _Color;
            float  _FlowSpeed, _SparkCount, _Sharpness;
            float  _CorePower, _Intensity, _PulseSpeed, _PulseDepth;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv         = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv;

                // ── 跨线渐变：中心最亮，边缘消失 ────────────────────────────
                float across = 1.0 - abs(uv.y * 2.0 - 1.0);       // 0 在边，1 在中
                float core   = pow(saturate(across), _CorePower);  // 收紧的亮核
                float halo   = pow(saturate(across * 0.7), 0.5);   // 更宽的晕

                // ── 沿线流动粒子 ─────────────────────────────────────────────
                float flow    = uv.x * _SparkCount - _Time.y * _FlowSpeed;
                float sparkD  = abs(frac(flow) - 0.5) * 2.0;       // 0 在粒子中心
                float spark   = pow(saturate(1.0 - sparkD), _Sharpness);

                // ── 轻微心跳脉冲 ─────────────────────────────────────────────
                float pulse = 1.0 - _PulseDepth + _PulseDepth * sin(_Time.y * _PulseSpeed);

                // ── 合成 ─────────────────────────────────────────────────────
                // core  = 中心稳定底光
                // spark = 流动高光，只在线宽内可见（乘 core 限制范围）
                // halo  = 外围柔光晕
                float brightness = (core * (0.5 + spark * 1.0)
                                  + halo * 0.12) * _Intensity * pulse;

                return half4(_Color.rgb * brightness, saturate(brightness) * _Color.a);
            }

            ENDHLSL
        }
    }
}
