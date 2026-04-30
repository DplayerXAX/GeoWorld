Shader "Custom/ManifoldSkybox"
{
    Properties
    {
        _ZenithColor   ("Zenith Color",   Color) = (0.02, 0.02, 0.06, 1)
        _HorizonColor  ("Horizon Color",  Color) = (0.06, 0.08, 0.15, 1)

        _GridColor     ("Grid Line Color",Color) = (0.25, 0.35, 0.65, 1)
        _GridScale     ("Grid Scale",     Float) = 12.0
        _GridThickness ("Grid Thickness", Range(0.01, 0.1)) = 0.03

        _StarDensity   ("Star Density",   Range(0,1)) = 0.6
        _HorizonSharp  ("Horizon Sharpness", Range(1, 20)) = 6.0
        _TimeSpeed     ("Time Speed", Float) = 0.2

        // Music reactivity (set from BackgroundReactor.cs).
        // Effects are tuned to be VISIBLE — pulse multipliers are large.
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

            half4 _ZenithColor, _HorizonColor, _GridColor;
            float _GridScale, _GridThickness;
            float _StarDensity, _HorizonSharp, _TimeSpeed;
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

            // ── Hash ──────────────────────────────────────────────────────
            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            // ── HSV-ish hue rotation (Rec.709 luma-preserving) ────────────
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

            // ── Spherical 3-axis grid ─────────────────────────────────────
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

            // ── Perspective floor grid ────────────────────────────────────
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

            // ── Sharp star/point field ────────────────────────────────────
            // Replaces the old block-noise fog. Stars are tiny dots in a 3D
            // lattice — clean geometric look, not a smear.
            float Stars(float3 dir)
            {
                float3 d = normalize(dir) * 60.0;
                float3 cellId  = floor(d);
                float3 cellPos = frac(d) - 0.5;

                float rnd = Hash3(cellId);
                // _StarDensity 1 → many stars, 0 → almost none
                float visible = step(1.0 - _StarDensity * 0.04, rnd);

                float dist = length(cellPos);
                float core = saturate(1.0 - dist * 18.0);
                core = pow(core, 4.0);

                // Subtle twinkle keyed off time + cell id
                float twinkle = 0.6 + 0.4 * sin(_Time.y * 2.3 + rnd * 31.7);

                return core * visible * twinkle;
            }

            // ── Floating block silhouettes ────────────────────────────────
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
                    result = max(result, edge * weight * 0.55);
                }
                return result;
            }

            // ── Beat shockwave ────────────────────────────────────────────
            // A radial pulse expanding outward each beat — clearly visible
            // confirmation that music is driving the visuals.
            float BeatRipple(float3 dir, float pulse)
            {
                if (pulse < 0.01) return 0.0;
                // distance from horizon line (pulse rises from horizon)
                float horiz = abs(dir.y);
                float wave  = sin(horiz * 24.0 - _Time.y * 6.0) * 0.5 + 0.5;
                float band  = smoothstep(0.0, 0.5, horiz) * (1.0 - smoothstep(0.4, 0.9, horiz));
                return wave * band * pulse;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);

                // ── Music-driven boosts ─────────────────────────────────
                float beat       = _BeatPulse;             // [0,1] decays each frame
                float intensity  = 0.4 + _MusicIntensity * 0.6;
                float beatBoost  = 1.0 + beat * 2.5;       // grids brighten dramatically
                float gridScaleM = 1.0 + beat * 0.05;      // tiny grid breathing on hit

                // ── Base sky gradient ───────────────────────────────────
                float up    = dir.y * 0.5 + 0.5;
                float t     = pow(saturate(up), 1.0 / _HorizonSharp);
                half3 sky   = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);

                // ── Geometry layers ─────────────────────────────────────
                float sphereGrid = SphericalGrid(dir, gridScaleM);
                float floorGrid  = PerspectiveFloor(dir);
                float cubes      = FloatingCubes(dir);
                float stars      = Stars(dir);
                float ripple     = BeatRipple(dir, beat);

                half3 gridCol    = _GridColor.rgb * beatBoost * intensity;
                half3 starCol    = lerp(half3(0.9,0.95,1.1), gridCol, 0.3);
                half3 floorCol   = gridCol * half3(1.10, 1.00, 0.85);

                // ── Compose ─────────────────────────────────────────────
                half3 result = sky;
                result += starCol * stars * 0.9;
                result  = lerp(result, gridCol,    sphereGrid * 0.55);
                result  = lerp(result, floorCol,   floorGrid  * 0.85);
                result  = lerp(result, gridCol*1.4, cubes     * 0.50);
                result += gridCol * ripple * 0.6;

                // ── Music color shift (continuous hue rotation) ────────
                result = HsvShift(result, _ColorShift);

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
