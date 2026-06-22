Shader "Custom/LevelSelectSky"
{
    // Painterly level-select sky in the same spirit as the abyss / manifold skyboxes:
    // a slowly drifting "astral aurora" built from domain-warped fbm (flowing colour
    // ribbons), soft glowing concentric arcs for the constructivist signature, a sparse
    // star field, and gentle HSV breathing. Calm — but with real depth, not a flat poster.
    Properties
    {
        [Header(Backdrop)]
        _SkyTop       ("Zenith",  Color) = (0.02, 0.02, 0.06, 1)
        _SkyBottom    ("Horizon", Color) = (0.05, 0.06, 0.14, 1)
        _HorizonSharp ("Gradient Curve", Range(1, 8)) = 3.0

        [Header(Astral Aurora)]
        _NebulaA   ("Aurora Cool", Color) = (0.169, 0.424, 0.690, 1)  // blue
        _NebulaB   ("Aurora Warm", Color) = (0.910, 0.698, 0.227, 1)  // gold
        _NebulaC   ("Aurora Flare", Color) = (0.886, 0.141, 0.106, 1) // signal
        _Scale     ("Aurora Scale", Float) = 3.0
        _Intensity ("Aurora Intensity", Range(0, 3)) = 1.2
        _FlowSpeed ("Flow Speed", Float) = 0.12
        _HueDrift  ("Hue Drift", Range(0, 0.5)) = 0.10

        [Header(Stars)]
        _StarColor ("Star Color", Color) = (1.0, 0.97, 0.9, 1)
        _StarDensity ("Star Density", Range(0, 1)) = 0.5
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

                // ── Backdrop gradient ───────────────────────────────────────────
                float up   = saturate(dir.y * 0.5 + 0.5);
                half3 col  = lerp(_SkyBottom.rgb, _SkyTop.rgb, pow(up, _HorizonSharp));

                // ── Astral aurora (domain-warped, flowing ribbons) ──────────────
                float3 p = dir * _Scale;
                p.y -= t * 0.6;                         // slow upward drift
                float3 warpQ;
                float field = DomainWarp(p, t, warpQ);

                // Colour the ribbons: cool→warm by the field, rare signal flares.
                half3 neb = lerp(_NebulaA.rgb, _NebulaB.rgb, smoothstep(0.30, 0.70, field));
                neb = lerp(neb, _NebulaC.rgb, smoothstep(0.70, 0.97, warpQ.x));
                neb *= (0.7 + warpQ.y * 0.8);          // internal brightness variation

                float ribbon = pow(smoothstep(0.35, 0.9, field), 1.6) * (0.5 + 0.5 * warpQ.z);
                col += neb * ribbon * _Intensity;          // full-sphere aurora (top & bottom)

                // ── Sparse star field (full sphere) ─────────────────────────────
                float3 sp = floor(dir * 220.0);
                float  sh = Hash3(sp);
                float  star = smoothstep(0.998 - _StarDensity * 0.01, 1.0, sh);
                float  tw   = 0.6 + 0.4 * sin(t * 6.0 + sh * 40.0);     // twinkle
                col += _StarColor.rgb * star * tw;

                // ── Slow hue breathing ──────────────────────────────────────────
                col = lerp(col, HsvShift(col, sin(t * 0.5) * _HueDrift), 0.6);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
