Shader "Custom/EnergyFlow"
{
    Properties
    {
        _Color          ("Flow Color",      Color)  = (0.3, 0.85, 1.0, 1)
        _FlowSpeed      ("Flow Speed",      Float)  = 1.3
        _LineScale      ("Line Density",    Float)  = 2.0
        _Sharpness      ("Line Sharpness",  Float)  = 9.0
        _SecondStr      ("Secondary Lines", Float)  = 0.45
        _EdgeGlow       ("Edge Glow",       Float)  = 0.55
        _Intensity      ("Glow Intensity",  Float)  = 2.8
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }

        Pass
        {
            Blend SrcAlpha One   // additive — glows on top of any block colour
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float3 positionWS  : TEXCOORD1;
                float3 normalWS    : TEXCOORD2;
            };

            float4 _Color;
            float  _FlowSpeed, _LineScale, _Sharpness, _SecondStr, _EdgeGlow, _Intensity;

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS  = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS    = TransformObjectToWorldNormal(v.normalOS);
                o.uv          = v.uv;
                return o;
            }

            // Sharp glowing stripe at phase = 0, dark elsewhere.
            float GlowLine(float phase, float sharpness)
            {
                float d = abs(frac(phase + 0.5) - 0.5) * 2.0; // 0 at centre, 1 at edges
                return pow(saturate(1.0 - d), sharpness);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t  = _Time.y * _FlowSpeed;
                float3 p = i.positionWS * _LineScale;
                float3 n = abs(normalize(i.normalWS));

                // ── Stripe axes depend on face orientation ────────────────
                // Each face type shows two diagonal stripe sets that lie
                // within that face's tangent plane.
                //
                //  Y-face (top/bottom) → X+Z  and  X-Z  diagonals
                //  X-face (left/right) → Y+Z  and  Y-Z  diagonals
                //  Z-face (front/back) → X+Y  and  X-Y  diagonals
                //
                // n component weighting blends them without branching.

                float fA = GlowLine(p.x + p.z + t,        _Sharpness) * n.y   // Y-face primary
                         + GlowLine(p.y + p.z + t,        _Sharpness) * n.x   // X-face primary
                         + GlowLine(p.x + p.y + t,        _Sharpness) * n.z;  // Z-face primary

                float fB = GlowLine(p.x - p.z + t * 0.6,  _Sharpness * 0.7) * n.y
                         + GlowLine(p.y - p.z + t * 0.6,  _Sharpness * 0.7) * n.x
                         + GlowLine(p.x - p.y + t * 0.6,  _Sharpness * 0.7) * n.z;

                float flow = saturate(fA + fB * _SecondStr);

                // ── Edge highlight — brighten block boundaries ────────────
                // UV 0→1 on each face; min distance to edge gives border width.
                float2 uv = i.uv;
                float border = 1.0 - min(
                    min(uv.x, 1.0 - uv.x),
                    min(uv.y, 1.0 - uv.y)) * 12.0;
                float edge = saturate(border) * _EdgeGlow;

                // ── Slow heartbeat so the path feels alive ────────────────
                float pulse = 0.78 + 0.22 * sin(t * 1.8);

                // ── Combine ───────────────────────────────────────────────
                float brightness = (flow + edge) * _Intensity * pulse;
                return half4(_Color.rgb * brightness, saturate(brightness) * _Color.a);
            }

            ENDHLSL
        }
    }
}
