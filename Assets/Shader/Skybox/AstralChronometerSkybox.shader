Shader "Custom/LevelSelectSky"
{
    // Serene, misty level-select sky in the same spirit as the abyss / manifold
    // skyboxes: a pale luminous vertical gradient, slow domain-warped fbm read as
    // soft drifting MIST (not bright ribbons), a gentle sacred horizon glow, a very
    // sparse star field, and subtle HSV breathing. Calm and hazy — "雾茫茫神圣静谧".
    Properties
    {
        [Header(Backdrop)]
        _SkyTop       ("Zenith",  Color) = (0.62, 0.70, 0.80, 1)
        _SkyBottom    ("Horizon", Color) = (0.90, 0.90, 0.86, 1)
        _HorizonSharp ("Gradient Curve", Range(1, 8)) = 2.0

        [Header(Drifting Mist)]
        _NebulaA   ("Mist Cool", Color) = (0.80, 0.84, 0.90, 1)  // pale blue
        _NebulaB   ("Mist Warm / Glow", Color) = (0.95, 0.90, 0.78, 1)  // soft gold
        _NebulaC   ("Mist Accent", Color) = (0.95, 0.88, 0.78, 1)
        _Scale     ("Mist Scale", Float) = 2.0
        _Intensity ("Mist Intensity", Range(0, 3)) = 0.8
        _FlowSpeed ("Flow Speed", Float) = 0.06
        _HueDrift  ("Hue Drift", Range(0, 0.5)) = 0.05

        [Header(Stars)]
        _StarColor ("Star Color", Color) = (1.0, 0.97, 0.9, 1)
        _StarDensity ("Star Density", Range(0, 1)) = 0.2
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _SkyTop, _SkyBottom, _NebulaA, _NebulaB, _NebulaC, _StarColor;
            float _HorizonSharp, _Scale, _Intensity, _FlowSpeed, _HueDrift;
            float _StarDensity;

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
                OUT.dir        = IN.positionOS.xyz;
                return OUT;
            }

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p), f = frac(p);
                float n000 = Hash3(i + float3(0,0,0)); float n100 = Hash3(i + float3(1,0,0));
                float n010 = Hash3(i + float3(0,1,0)); float n110 = Hash3(i + float3(1,1,0));
                float n001 = Hash3(i + float3(0,0,1)); float n101 = Hash3(i + float3(1,0,1));
                float n011 = Hash3(i + float3(0,1,1)); float n111 = Hash3(i + float3(1,1,1));
                float3 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(n000,n100,u.x), lerp(n010,n110,u.x), u.y),
                            lerp(lerp(n001,n101,u.x), lerp(n011,n111,u.x), u.y), u.z);
            }

            float Fbm(float3 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int i = 0; i < 5; i++) { s += a * Noise(p); p *= 2.02; a *= 0.5; }
                return s;
            }

            // Domain-warped fbm: layered self-advection → flowing, painterly ribbons.
            float DomainWarp(float3 p, float time, out float3 warpQ)
            {
                float3 q = float3(Fbm(p), Fbm(p + 5.2), Fbm(p + 9.1));
                warpQ = q;
                float3 r = float3(Fbm(p + 4.0 * q + float3(1.7, 9.2, time)),
                                  Fbm(p + 4.0 * q + float3(8.3, 2.8, time * 0.8)),
                                  Fbm(p + 4.0 * q + float3(4.5, 1.3, time * 1.2)));
                return Fbm(p + 4.0 * r);
            }

            half3 HsvShift(half3 rgb, float shift)
            {
                float c = cos(shift * 6.2832), s = sin(shift * 6.2832);
                float3x3 m = float3x3(
                    0.299 + 0.701*c + 0.168*s,  0.587 - 0.587*c + 0.330*s,  0.114 - 0.114*c - 0.497*s,
                    0.299 - 0.299*c - 0.328*s,  0.587 + 0.413*c + 0.035*s,  0.114 - 0.114*c + 0.292*s,
                    0.299 - 0.300*c + 1.250*s,  0.587 - 0.588*c - 1.050*s,  0.114 + 0.886*c - 0.203*s);
                return saturate(mul(m, rgb));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                float  t   = _Time.y * _FlowSpeed;

                // ── Backdrop gradient (pale, luminous, serene) ──────────────────
                float up   = saturate(dir.y * 0.5 + 0.5);
                half3 col  = lerp(_SkyBottom.rgb, _SkyTop.rgb, pow(up, _HorizonSharp));

                // ── Drifting mist (domain-warped fbm, soft & low-contrast) ──────
                float3 p = dir * _Scale;
                p.y -= t * 0.3;                         // slow drift
                float3 warpQ;
                float field = DomainWarp(p, t, warpQ);

                // Pale mist veil — cool→warm by the field, thicker low (toward the
                // horizon), clearing toward the zenith. Blend TOWARD the mist colour
                // (a participating veil) rather than adding bright ribbons.
                half3 mistCol = lerp(_NebulaA.rgb, _NebulaB.rgb, smoothstep(0.40, 0.72, field));
                float mist    = smoothstep(0.35, 0.85, field) * (0.6 + 0.4 * warpQ.y);
                mist         *= (1.0 - up * 0.5);
                col = lerp(col, mistCol, saturate(mist * _Intensity * 0.5));

                // ── Sacred horizon glow (soft bright band at the horizon) ───────
                float horizonGlow = pow(1.0 - abs(dir.y), 6.0);
                col += _NebulaB.rgb * horizonGlow * 0.25;

                // ── Very sparse star field (barely there against the bright mist) ─
                float3 sp = floor(dir * 220.0);
                float  sh = Hash3(sp);
                float  star = smoothstep(0.999 - _StarDensity * 0.01, 1.0, sh);
                float  tw   = 0.6 + 0.4 * sin(t * 6.0 + sh * 40.0);     // twinkle
                col += _StarColor.rgb * star * tw * 0.4;

                // ── Subtle hue breathing ────────────────────────────────────────
                col = lerp(col, HsvShift(col, sin(t * 0.4) * _HueDrift), 0.3);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
