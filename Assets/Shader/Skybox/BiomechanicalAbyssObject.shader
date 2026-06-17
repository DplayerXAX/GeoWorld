// Object-space version of BiomechanicalAbyssSkybox: identical look, but renders as a
// solid opaque mesh (Geometry queue / ZWrite On / Cull Back) so it can sit ON the cube.
// The pattern is sampled from object-space position, so it maps cleanly onto the cube.
Shader "Custom/BiomechanicalAbyssObject"
{
    Properties
    {
        _AbyssColor    ("Abyss Void Color", Color) = (0.04, 0.01, 0.06, 1)
        _FleshColor    ("Biolume Flesh Color", Color) = (0.1, 0.45, 0.4, 1)
        _VeinColor     ("Vein Glow Color", Color) = (0.3, 0.9, 0.7, 1)

        _Viscosity     ("Fluid Viscosity", Float) = 3.0
        _VeinDensity   ("Vein Density", Float) = 12.0
        _PulseSpeed    ("Throb Speed", Float) = 0.5

        [Toggle] _MenuMode ("Enable Main Menu Mode", Float) = 0.0
        _MenuBgDamp        ("Menu UI Contrast Damp", Range(0,1)) = 0.6

        _BeatPulse     ("Heartbeat Pulse", Range(0,1)) = 0
        _MusicIntensity("Music Intensity", Range(0,1)) = 0.3
        _CombatMode    ("Eldritch Mutation", Range(0,1)) = 0
        _DamageTint    ("Abyss Corrupted", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Cull Back ZWrite On

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _AbyssColor, _FleshColor, _VeinColor;
            float _Viscosity, _VeinDensity, _PulseSpeed;
            float _BeatPulse, _MusicIntensity, _CombatMode, _DamageTint;
            float _MenuMode, _MenuBgDamp;

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
                OUT.dir = IN.positionOS.xyz;   // object-space direction drives the pattern
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

            float DynamicDomainWarp(float3 p, float time, out float3 warpQ)
            {
                float3 q = float3(
                    Noise(p + float3(0.0, 0.0, time)),
                    Noise(p + float3(4.3, 1.2, time * 0.8)),
                    Noise(p + float3(1.7, 5.4, time * 1.2))
                );
                warpQ = q;

                float3 r = float3(
                    Noise(p + 3.0 * q + float3(2.8, 5.7, time * 0.5)),
                    Noise(p + 3.0 * q + float3(8.1, 1.3, time * 0.9)),
                    Noise(p + 3.0 * q + float3(4.5, 9.2, time * 0.3))
                );

                return Noise(p + _Viscosity * r);
            }

            half3 HsvRotation(half3 rgb, float shift)
            {
                float c = cos(shift * 6.2832), s = sin(shift * 6.2832);
                float3x3 m = float3x3(
                    0.299 + 0.701*c + 0.168*s,  0.587 - 0.587*c + 0.330*s,  0.114 - 0.114*c - 0.497*s,
                    0.299 - 0.299*c - 0.328*s,  0.587 + 0.413*c + 0.035*s,  0.114 - 0.114*c + 0.292*s,
                    0.299 - 0.300*c + 1.250*s,  0.587 - 0.588*c - 1.050*s,  0.114 + 0.886*c - 0.203*s
                );
                return saturate(mul(m, rgb));
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);

                float speedMult = (_MenuMode > 0.5) ? 0.4 : 1.0;
                float time = _Time.y * _PulseSpeed * speedMult;

                if (_CombatMode > 0.01)
                {
                    float3 cellDir = dir;
                    [unroll] for(int i=0; i<3; i++)
                    {
                        cellDir = abs(cellDir) - float3(0.2, 0.1, 0.3);
                        float s = sin(time * 0.5), c = cos(time * 0.5);
                        cellDir.xy = float2(c * cellDir.x - s * cellDir.y, s * cellDir.x + c * cellDir.y);
                    }
                    dir = normalize(lerp(dir, cellDir, _CombatMode * 0.7));
                }

                float pulseFactor = 1.0 - _BeatPulse * 0.25 * (0.5 + _MusicIntensity * 0.5);
                float3 samplePos = dir * 4.5 * pulseFactor;
                samplePos.y += time * 0.5;

                float3 warpQ = float3(0,0,0);
                float fluidField = DynamicDomainWarp(samplePos, time, warpQ);

                half3 baseAbyss = _AbyssColor.rgb;
                half3 fleshBio  = _FleshColor.rgb * (warpQ.x * 0.6 + 0.7);
                fleshBio *= (1.0 + _BeatPulse * 0.4) * (0.8 + _MusicIntensity * 0.4);

                half3 finalFlesh = lerp(baseAbyss, fleshBio, smoothstep(0.1, 0.7, fluidField));

                float veinDensity = _VeinDensity * (1.0 + _CombatMode * 0.5);
                float veinVal = frac(fluidField * veinDensity);

                float widthExtension = (_MenuMode > 0.5) ? 0.15 : 0.0;
                float veinLine = smoothstep(0.08 + widthExtension, 0.0, abs(veinVal - 0.5) / (fwidth(fluidField * veinDensity) + 0.001));

                veinLine *= smoothstep(0.2, 0.6, Noise(samplePos * 2.0 + 12.0)) * _VeinColor.a;
                half3 activeVeinCol = _VeinColor.rgb * (1.0 + _BeatPulse * 2.0);

                if (_CombatMode > 0.01)
                {
                    finalFlesh = lerp(finalFlesh, half3(0.02, 0.0, 0.03), _CombatMode * 0.8);
                    activeVeinCol = lerp(activeVeinCol, half3(0.95, 0.05, 0.1) * 2.5, _CombatMode);
                }

                half3 result = finalFlesh + (activeVeinCol * veinLine);

                float cellNoise = Noise(dir * 25.0 - float3(0, time * 0.2, 0));
                float cellMask = (_MenuMode > 0.5) ? 0.15 : 1.0;
                float cells = smoothstep(0.82, 0.85, cellNoise) * (1.0 - _CombatMode * 0.5) * cellMask;
                result += _VeinColor.rgb * cells * 0.4 * (warpQ.z * 0.5 + 0.5);

                result = HsvRotation(result, warpQ.y * 0.05 + _CombatMode * -0.08);

                float luma = dot(result, float3(0.299, 0.587, 0.114));
                half3 bloodPalette = half3(luma * 1.6 + 0.1, result.g * 0.01, result.b * 0.02);
                result = lerp(result, bloodPalette, _DamageTint);

                if (_MenuMode > 0.5)
                {
                    float verticalDim = smoothstep(-0.6, 0.5, dir.y);
                    result = lerp(result, baseAbyss * 0.6, _MenuBgDamp);
                    result *= lerp(0.4, 1.2, verticalDim);
                }

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }
}
