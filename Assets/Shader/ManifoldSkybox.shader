Shader "Custom/ManifoldSkybox"
{
    Properties
    {
        _ZenithColor   ("Zenith Color",   Color) = (0.02, 0.02, 0.06, 1)
        _HorizonColor  ("Horizon Color",  Color) = (0.06, 0.08, 0.15, 1)
        _FogColor      ("Fog/Haze Color", Color) = (0.10, 0.12, 0.22, 1)

        _GridColor     ("Grid Line Color",Color) = (0.25, 0.35, 0.65, 1)
        _GridScale     ("Grid Scale",     Float) = 12.0
        _GridThickness ("Grid Thickness", Range(0.01, 0.1)) = 0.03

        _FogStart      ("Fog Start (horizon band)", Range(0, 1)) = 0.08
        _FogDensity    ("Fog Density",    Range(0, 8)) = 3.5
        _FogStrength   ("Fog Strength",   Range(0, 1)) = 0.85

        _HorizonSharp  ("Horizon Sharpness", Range(1, 20)) = 6.0
        _TimeSpeed     ("Time Speed", Float) = 0.2

        // Music reactivity (driven from BackgroundReactor.cs)
        _BeatPulse     ("Beat Pulse",     Range(0,1)) = 0
        _MusicIntensity("Music Intensity",Range(0,1)) = 0.5
        _ColorShift    ("Color Shift",    Range(0,1)) = 0
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _ZenithColor, _HorizonColor, _FogColor, _GridColor;
            float _GridScale, _GridThickness;
            float _FogStart, _FogDensity, _FogStrength;
            float _HorizonSharp, _TimeSpeed;
            float _BeatPulse, _MusicIntensity, _ColorShift;

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

            // ── Hash & noise (for the stained-glass fog cells) ───────────
            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float n000 = Hash3(i + float3(0,0,0)); float n100 = Hash3(i + float3(1,0,0));
                float n010 = Hash3(i + float3(0,1,0)); float n110 = Hash3(i + float3(1,1,0));
                float n001 = Hash3(i + float3(0,0,1)); float n101 = Hash3(i + float3(1,0,1));
                float n011 = Hash3(i + float3(0,1,1)); float n111 = Hash3(i + float3(1,1,1));
                float3 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(n000,n100,u.x), lerp(n010,n110,u.x), u.y),
                            lerp(lerp(n001,n101,u.x), lerp(n011,n111,u.x), u.y), u.z);
            }

            // Quantised noise → blocky stained-glass cells (the look the user liked)
            float StainedGlassFog(float3 p)
            {
                float n1 = floor(Noise(p)       * 4.0) / 4.0;
                float n2 = floor(Noise(p * 2.0) * 4.0) / 4.0 * 0.5;
                float n3 = floor(Noise(p * 4.0) * 4.0) / 4.0 * 0.25;
                return saturate(n1 + n2 + n3);
            }

            // Per-cell hue offset so adjacent cells read as separate glass panels
            // (deep blues next to violets next to teals — chapel-window vibe)
            // rather than a single muddy color.
            float StainedGlassPanelHue(float3 p)
            {
                // Same cell scale as the dominant fog layer
                float3 cellId = floor(p);
                float h = Hash3(cellId);
                // Spread around 0 with limited range so the palette stays cohesive
                return (h - 0.5) * 0.18;
            }

            // ── HSV-style hue rotation for the music color shift ─────────
            float3 HsvShift(float3 rgb, float shift)
            {
                float c = cos(shift * 6.2832);
                float s = sin(shift * 6.2832);
                float3x3 m = float3x3(
                    0.299 + 0.701*c + 0.168*s,  0.587 - 0.587*c + 0.330*s,  0.114 - 0.114*c - 0.497*s,
                    0.299 - 0.299*c - 0.328*s,  0.587 + 0.413*c + 0.035*s,  0.114 - 0.114*c + 0.292*s,
                    0.299 - 0.300*c + 1.250*s,  0.587 - 0.588*c - 1.050*s,  0.114 + 0.886*c - 0.203*s
                );
                return saturate(mul(m, rgb));
            }

            // ── 3-axis spherical grid ────────────────────────────────────
            float SphericalGrid(float3 dir, float scaleMul)
            {
                float3 d = normalize(dir);
                float scale = _GridScale * scaleMul;

                float2 uvXY = d.xy * scale;
                float2 uvXZ = d.xz * scale;
                float2 uvYZ = d.yz * scale;

                float2 gXY = abs(frac(uvXY - 0.5) - 0.5) / fwidth(uvXY);
                float2 gXZ = abs(frac(uvXZ - 0.5) - 0.5) / fwidth(uvXZ);
                float2 gYZ = abs(frac(uvYZ - 0.5) - 0.5) / fwidth(uvYZ);

                float lXY = 1.0 - min(min(gXY.x, gXY.y), 1.0 / _GridThickness);
                float lXZ = 1.0 - min(min(gXZ.x, gXZ.y), 1.0 / _GridThickness);
                float lYZ = 1.0 - min(min(gYZ.x, gYZ.y), 1.0 / _GridThickness);

                float3 w = abs(d);
                w = pow(w, 6.0);
                w /= (w.x + w.y + w.z + 1e-5);

                return saturate(lXZ * w.y + lXY * w.z + lYZ * w.x);
            }

            // ── Perspective floor grid (depth cue) ───────────────────────
            float PerspectiveFloor(float3 dir)
            {
                if (dir.y >= -0.005) return 0.0;

                float t  = -1.0 / dir.y;
                float2 xz = dir.xz * t;
                float scale = _GridScale * 0.25;
                float2 uv   = xz * scale;
                float2 g    = abs(frac(uv - 0.5) - 0.5) / fwidth(uv);
                float  ln   = 1.0 - min(min(g.x, g.y), 1.0 / _GridThickness);

                float elevFade = saturate(-dir.y * 4.0);
                float distFade = saturate(1.0 - exp(-t * 0.15));
                float distFar  = exp(-t * 0.04);
                return saturate(ln) * elevFade * distFade * distFar;
            }

            // ── Floating cube silhouettes (3D feel through the fog) ──────
            float FloatingCubes(float3 dir)
            {
                float3 d = normalize(dir);
                float result = 0.0;

                [unroll] for (int shell = 0; shell < 3; shell++)
                {
                    float depth = 2.5 + float(shell) * 2.0;
                    float3 p = d * depth + float3(0.3, 0.1, 0.7) * _Time.y * _TimeSpeed * 0.15;
                    float3 cellId  = floor(p);
                    float3 cellPos = frac(p) - 0.5;

                    float rnd = Hash3(cellId + float3(float(shell), 0, 0));
                    if (rnd < 0.74) continue;

                    float3 absP = abs(cellPos);
                    float cubeSize = 0.18 + rnd * 0.10;
                    float3 d3 = absP - cubeSize;
                    float sdf = length(max(d3, 0.0)) + min(max(d3.x, max(d3.y, d3.z)), 0.0);

                    float edge = saturate(1.0 - abs(sdf) * 22.0);
                    float weight = 1.0 - float(shell) * 0.28;
                    result = max(result, edge * weight * 0.5);
                }
                return result;
            }

            // ── Beat shockwave (radial pulse from horizon on each beat) ──
            float BeatRipple(float3 dir, float pulse)
            {
                if (pulse < 0.01) return 0.0;
                float horiz = abs(dir.y);
                float wave  = sin(horiz * 24.0 - _Time.y * 6.0) * 0.5 + 0.5;
                float band  = smoothstep(0.0, 0.5, horiz) * (1.0 - smoothstep(0.4, 0.9, horiz));
                return wave * band * pulse;
            }

            // ── Horizon fog gradient ─────────────────────────────────────
            float HorizonFog(float3 dir)
            {
                float t = abs(normalize(dir).y);
                return saturate(exp(-_FogDensity * max(0, t - _FogStart)));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);

                // ── Music-driven boosts ─────────────────────────────────
                float beat       = _BeatPulse;
                float intensity  = 0.4 + _MusicIntensity * 0.6;
                float beatBoost  = 1.0 + beat * 2.0;       // grids brighten on hit
                float gridScaleM = 1.0 + beat * 0.04;

                // ── Sky base gradient ───────────────────────────────────
                float up = dir.y * 0.5 + 0.5;
                float t  = pow(saturate(up), 1.0 / _HorizonSharp);
                half3 sky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);

                // ── Stained-glass fog (the foggy / chapel-glass feel) ──
                float3 p = dir * 8.0 + _Time.y * _TimeSpeed;
                float fogLayer = StainedGlassFog(p);

                float baseFog = HorizonFog(dir);
                float height  = dir.y * 0.5 + 0.5;
                float fog = saturate(baseFog * (0.6 + fogLayer))
                          * smoothstep(0.0, 0.6, 1.0 - height)
                          * _FogStrength;

                // Fog color tinted by the cell value, with a per-panel hue
                // offset so adjacent cells read as distinct stained-glass panels.
                half3 fogCol = lerp(_FogColor.rgb, _ZenithColor.rgb, fogLayer);
                float panelHue = StainedGlassPanelHue(p);
                fogCol = HsvShift(fogCol, _ColorShift + panelHue);
                sky    = lerp(fogCol, sky, 1.0 - fog);

                // ── Geometry layered through the fog ────────────────────
                float sphereGrid = SphericalGrid(dir, gridScaleM) * (1.0 - fog * 0.55);
                float floorGrid  = PerspectiveFloor(dir);
                float cubes      = FloatingCubes(dir) * (1.0 - fog * 0.4);
                float ripple     = BeatRipple(dir, beat);

                half3 gridCol  = _GridColor.rgb * beatBoost * intensity;
                half3 floorCol = gridCol * half3(1.10, 1.00, 0.85);

                half3 result = sky;
                result = lerp(result, gridCol,    sphereGrid * 0.55);
                result = lerp(result, floorCol,   floorGrid  * 0.85);
                result = lerp(result, gridCol*1.3, cubes     * 0.40);
                result += gridCol * ripple * 0.5;

                // Final hue rotation — keeps the chapel-glass palette evolving
                result = HsvShift(result, _ColorShift);

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
