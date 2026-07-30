Shader "Custom/GalleryAtelierSky"
{
    // The Gallery's sky: an infinite museum atelier. Same family as the other
    // skyboxes (pale luminous gradient + fbm mist + hue breathing) but speaking
    // the Title's constructivist paper/ink/gold/signal language — and with its
    // own signature element none of the others have: hollow "picture frames"
    // drifting slowly in the haze, as if the hall itself were hanging works.
    Properties
    {
        [Header(Backdrop)]
        _SkyTop       ("Zenith",  Color) = (0.86, 0.84, 0.79, 1)   // warm paper, dimmed
        _SkyBottom    ("Horizon", Color) = (0.949, 0.937, 0.902, 1) // paper
        _HorizonSharp ("Gradient Curve", Range(1, 8)) = 2.5

        [Header(Mist)]
        _MistCool  ("Mist Cool", Color) = (0.80, 0.82, 0.86, 1)
        _MistWarm  ("Mist Warm", Color) = (0.93, 0.87, 0.74, 1)   // gold-tinged
        _Scale     ("Mist Scale", Float) = 2.2
        _Intensity ("Mist Intensity", Range(0, 3)) = 0.65
        _FlowSpeed ("Flow Speed", Float) = 0.05
        _HueDrift  ("Hue Drift", Range(0, 0.5)) = 0.04

        [Header(Floating Frames)]
        _Ink        ("Frame Ink", Color) = (0.086, 0.086, 0.086, 1)
        _Gold       ("Frame Gold", Color) = (0.910, 0.698, 0.227, 1)
        _FrameScale ("Frame Density", Float) = 3.0
        _FrameDrift ("Frame Drift Speed", Float) = 0.015
        _FrameAlpha ("Frame Strength", Range(0, 1)) = 0.5

        [Header(Signal Band)]
        _Signal      ("Signal", Color) = (0.886, 0.141, 0.106, 1)
        _BandStrength("Band Strength", Range(0, 1)) = 0.18

        [Header(Dust)]
        _DustColor   ("Dust Color", Color) = (0.910, 0.698, 0.227, 1)
        _DustDensity ("Dust Density", Range(0, 1)) = 0.25
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

            half4 _SkyTop, _SkyBottom, _MistCool, _MistWarm, _Ink, _Gold, _Signal, _DustColor;
            float _HorizonSharp, _Scale, _Intensity, _FlowSpeed, _HueDrift;
            float _FrameScale, _FrameDrift, _FrameAlpha, _BandStrength, _DustDensity;

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

            float Hash2(float2 p) { return frac(sin(dot(p, float2(41.3, 289.1))) * 43758.5453); }

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
                [unroll] for (int i = 0; i < 4; i++) { s += a * Noise(p); p *= 2.02; a *= 0.5; }
                return s;
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

            // SOLID drifting squares (stained glass) on an abstract wall plane.
            // Equal margins on both axes → squares (no landscape rectangles). uv =
            // planar coords; returns 0..1 filled mask + the pane's glass colour.
            float Frames(float2 uv, float t, out half3 tint)
            {
                uv   += float2(t, t * 0.4);            // whole wall drifts slowly (flow)
                float2 cell = floor(uv);
                float2 f    = frac(uv);
                float  h    = Hash2(cell);

                // Per-cell stained-glass colour from the palette.
                float pick = Hash2(cell + 5.1);
                tint = pick < 0.33 ? _Gold.rgb : (pick < 0.66 ? _Signal.rgb : _MistCool.rgb);

                // Uniform per-cell margin → always a square, size varies by hash.
                float  m    = 0.16 + h * 0.12;
                float2 d    = min(f - m, (1.0 - m) - f);   // signed dist inside the square
                float  fill = smoothstep(0.0, 0.03, min(d.x, d.y));

                // Only some cells hold a square at all (~55%) — sparse, not a grid.
                return fill * step(0.45, Hash2(cell + 3.7));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                float  t   = _Time.y * _FlowSpeed;

                // ── Paper gradient, brighter at the horizon like a lit hall ─────
                float up   = saturate(dir.y * 0.5 + 0.5);
                half3 col  = lerp(_SkyBottom.rgb, _SkyTop.rgb, pow(up, _HorizonSharp));

                // ── Soft mist veil (family DNA, fewer octaves — calmer) ─────────
                float3 p = dir * _Scale;
                p.xz += t * 0.4;
                float field = Fbm(p + Fbm(p + t) * 1.5);
                half3 mistCol = lerp(_MistCool.rgb, _MistWarm.rgb, smoothstep(0.40, 0.70, field));
                float mist = smoothstep(0.35, 0.85, field) * (1.0 - up * 0.45);
                col = lerp(col, mistCol, saturate(mist * _Intensity * 0.5));

                // ── Drifting stained-glass squares on the ±X wall only ──────────
                // (The ±Z wall was removed — one wall of works, not a box.)
                float tF = _Time.y * _FrameDrift;
                half3 glassTint;
                float2 uvB = float2(dir.z, dir.y) / max(0.35, abs(dir.x)) * _FrameScale;
                float frameB = Frames(uvB, tF, glassTint) * smoothstep(0.25, 0.6, abs(dir.x));

                // Squares thin out toward the zenith/nadir — hung at eye height.
                float belt = 1.0 - smoothstep(0.35, 0.8, abs(dir.y));
                frameB *= belt;

                // Translucent blend so the sky reads through, like coloured glass.
                col = lerp(col, glassTint, frameB * _FrameAlpha * 0.6);

                // ── One quiet signal-red diagonal band (Title's sweep, stilled) ─
                float band = sin(dir.x * 2.0 + dir.y * 4.5 + t * 1.5);
                col = lerp(col, _Signal.rgb, smoothstep(0.90, 0.97, band) * _BandStrength * belt);

                // ── Gold dust motes (this hall's "stars") ───────────────────────
                float3 sp = floor(dir * 160.0);
                float  sh = Hash3(sp);
                float  mote = smoothstep(0.999 - _DustDensity * 0.008, 1.0, sh);
                float  tw   = 0.5 + 0.5 * sin(_Time.y * 1.2 + sh * 40.0);   // slow glint
                col += _DustColor.rgb * mote * tw * 0.35;

                // ── Subtle hue breathing (family DNA) ───────────────────────────
                col = lerp(col, HsvShift(col, sin(t * 0.4) * _HueDrift), 0.3);

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
}
