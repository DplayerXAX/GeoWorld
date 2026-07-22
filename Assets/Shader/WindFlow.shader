// 用于 LineRenderer 的"风丝"流动线 shader —— PathLaser 的姊妹版。
//
// PathLaser 是一条实心激光加上流动粒子：亮、整齐、适合 gameplay 里明确指示单位路径。
// 这个走的是完全不同的形态：**一束互相穿插的细丝**。每根丝是一根两头收尖的针，
// 中段最亮，沿线飘过后自然消失；丝与丝之间速度、长度、横向位置、编织相位全都错开，
// 所以整体读起来是"一股气流"而不是"一条线"。
//
// 关键在于形状而不是亮度 —— 之前那版是一条均匀的缎带，问题不在于太亮，
// 在于它只有一根、且粗细恒定，所以永远像"一条画出来的线"。
//
// UV.x = 沿线方向 0→1（必须配 LineTextureMode.Stretch，整条线一次 0→1）
// UV.y = 跨线方向 0→1（0 和 1 是边缘，0.5 是中心）
Shader "Custom/WindFlow"
{
    Properties
    {
        _Color        ("Wind Color",       Color) = (0.910, 0.698, 0.227, 1)   // GeoPalette.Gold
        // 芯部往白推能出"发光"感，但推太多整条丝会被洗成白色、丢掉色相。
        // 0.22 是让芯略微提亮、同时金色仍然读得出来的量。
        _WhiteHot     ("White-hot Core",   Range(0,1)) = 0.22
        _Intensity    ("Intensity",        Float) = 1.15
        _FlowSpeed    ("Flow Speed",       Float) = 0.32

        _StrandCount  ("Strands",          Range(1,10)) = 7
        _StrandLength ("Strand Length",    Range(0.05,0.9)) = 0.30  // 每根丝占 UV.x 的半长
        _Taper        ("Taper Sharpness",  Range(0.5,6)) = 2.2      // 越大两头收得越尖

        _CoreWidth    ("Core Width",       Range(0.005,0.4)) = 0.045 // 芯的粗细（UV.y 单位）
        _HaloWidth    ("Halo Spread",      Range(1,16)) = 7.0        // 外晕相对芯的倍数
        _HaloStrength ("Halo Strength",    Range(0,1)) = 0.22

        _Spread       ("Cross Spread",     Range(0,0.5)) = 0.30  // 丝在线宽内分散的范围
        _Wave         ("Weave Amount",     Range(0,0.4)) = 0.11  // 丝的横向编织幅度
        _WaveFreq     ("Weave Frequency",  Float) = 1.7

        _EndFade      ("End Fade",         Range(0.001,0.5)) = 0.10
    }

    SubShader
    {
        // Transparent+1：压在 PathLaser（+2）下面，风永远不会盖住路径激光。
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" "IgnoreProjector"="True" }

        Pass
        {
            Blend SrcAlpha One   // 加色 —— 细丝要的是"发光"，不是"半透明涂层"
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            float4 _Color;
            float  _WhiteHot, _Intensity, _FlowSpeed;
            float  _StrandCount, _StrandLength, _Taper;
            float  _CoreWidth, _HaloWidth, _HaloStrength;
            float  _Spread, _Wave, _WaveFreq, _EndFade;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv         = v.uv;
                o.color      = v.color;   // LineRenderer 的 startColor/endColor 照常生效（用来做淡入淡出）
                return o;
            }

            // 每根丝取一组互不相关的随机参数，避免它们同步移动成一片。
            float Hash(float n) { return frac(sin(n * 127.1) * 43758.5453); }

            half4 frag(Varyings i) : SV_Target
            {
                float t   = _Time.y * _FlowSpeed;
                float sum = 0.0;

                [loop]
                for (int k = 0; k < (int)_StrandCount; k++)
                {
                    float fk = (float)k;
                    float h1 = Hash(fk + 1.0);    // 速度
                    float h2 = Hash(fk + 17.3);   // 相位
                    float h3 = Hash(fk + 41.7);   // 长度
                    float h4 = Hash(fk + 73.1);   // 横向落位

                    // ── 沿线：一段会滚动的窗口，两头收尖 ────────────────────
                    float center = frac(t * (0.65 + 0.7 * h1) + h2);
                    float along  = i.uv.x - center;
                    along -= round(along);                       // 环绕到 [-0.5, 0.5]
                    float len    = _StrandLength * (0.45 + 1.0 * h3);
                    float env    = saturate(1.0 - abs(along) / max(len, 1e-4));
                    env          = env * env * (3.0 - 2.0 * env); // 平滑
                    float taper  = pow(env, _Taper);              // 针形：中段粗，两头几乎为零

                    if (env <= 0.001) continue;

                    // ── 跨线：每根丝有自己的落位，还会缓慢编织 ────────────────
                    // 编织相位带 h2，所以丝之间会互相穿插、交叉，而不是平行排列。
                    float y = 0.5
                            + _Spread * (h4 - 0.5) * 2.0
                            + _Wave * sin((i.uv.x * _WaveFreq + h2) * 6.2831853 + t * 2.2);

                    float d = i.uv.y - y;
                    float w = _CoreWidth * max(taper, 0.03);      // 收尖时芯也跟着变细
                    float core = exp(-(d * d) / (w * w));
                    float halo = exp(-(d * d) / (w * w * _HaloWidth * _HaloWidth)) * _HaloStrength;

                    sum += (core + halo) * env;
                }

                // ── 两端淡出，整束风不会凭空断掉 ────────────────────────────
                float ends = smoothstep(0.0, _EndFade, i.uv.x)
                           * smoothstep(0.0, _EndFade, 1.0 - i.uv.x);

                float b = sum * _Intensity * ends * i.color.a * _Color.a;

                // 芯越亮越往白推 —— 参考图里丝的中心是近白的，边缘才是彩色。
                float3 rgb = lerp(_Color.rgb * i.color.rgb, float3(1, 1, 1), saturate(sum * _WhiteHot));

                return half4(rgb * b, saturate(b));
            }

            ENDHLSL
        }
    }

    Fallback Off
}
