// Glassy Supernova Core — geometric portal / outward eruption.
// Matches the warm earthy silkscreen palette, but injected with 
// faceted, crystalline "stained glass" energy.
Shader "GeoWorld/HaloCore_GlassySupernova"
{
    Properties
    {
        [Header(Colors)]
        [HDR] _CoreColor       ("Core Blast (blinding gold)", Color) = (1.5, 1.2, 0.8, 1)
        _VoidColor             ("Outer Void",                Color) = (0.18, 0.10, 0.08, 1)
        _ColorA                ("Streak A (muted gold)",     Color) = (0.92, 0.72, 0.32, 1)
        _ColorB                ("Streak B (rust orange)",    Color) = (0.78, 0.38, 0.20, 1)

        [Header(Geometry And Flow)]
        _CoreRadius            ("Core Gateway Radius",       Range(0.05, 0.5))  = 0.15
        _OuterFade             ("Outer Fade Start",          Range(0.5, 1.0))   = 0.70
        _EruptionSpeed         ("Outward Eruption Speed",    Range(0.5, 5.0))   = 2.5

        [Header(Manifold Stylization)]
        _GlassSharpness        ("Glass Quantization",        Range(0.0, 1.0))   = 0.85
        _ShardSteps            ("Shard Detail Levels",       Range(2.0, 10.0))  = 5.0
        
        [Header(Crystalline Rays)]
        _SwirlArms             ("Ray Count",                 Range(1, 16))      = 9.0
        _SwirlTwist            ("Spiral Twist",              Range(0, 5))       = 0.8
        _SwirlWarp             ("Streak Warp Amount",        Range(0, 6))       = 2.5
        _StreakSharp           ("Streak Sharpness",          Range(0.3, 10))    = 4.5

        [Header(Noise Variation)]
        _NoiseScale            ("Noise Scale",               Range(0.5, 12))    = 4.0
        _ColorContrast         ("Color Contrast",            Range(0.5, 4))     = 2.0

        [Header(Brightness)]
        _StreakIntensity       ("Streak Intensity",          Range(0.3, 4))     = 1.8
        _CoreGlow              ("Core Blast Strength",       Range(0, 3))       = 1.5
        _Brightness            ("Overall Brightness",        Range(0.2, 2))     = 1.1

        [Header(Halo And Texture)]
        _FresnelPower          ("Fresnel Power",             Range(0.5, 8))     = 2.4
        _FresnelStrength       ("Halo Strength",             Range(0, 3))       = 1.0
        _GrainAmount           ("Paper Grain",               Range(0, 0.08))    = 0.035
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _VoidColor, _ColorA, _ColorB;
                float  _CoreRadius, _OuterFade, _EruptionSpeed;
                float  _GlassSharpness, _ShardSteps;
                float  _SwirlArms, _SwirlTwist, _SwirlWarp, _StreakSharp;
                float  _NoiseScale, _ColorContrast;
                float  _StreakIntensity, _CoreGlow, _Brightness;
                float  _FresnelPower, _FresnelStrength, _GrainAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : NORMAL_WS;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

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

            float GlassyNoise2(float2 p, float sharpness, float steps)
            {
                float n = Noise2(p);
                float quantized = floor(n * steps) / steps;
                return lerp(n, quantized, sharpness);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 nWS = normalize(IN.normalWS);
                float3 vWS = normalize(GetCameraPositionWS() - IN.positionWS);

                float3 worldUp = float3(0, 1, 0);
                float3 rightWS = normalize(cross(worldUp, vWS) + float3(1e-4, 0, 0));
                float3 upWS    = normalize(cross(vWS, rightWS));

                float3 centerWS = TransformObjectToWorld(float3(0, 0, 0));
                float3 offsetWS = IN.positionWS - centerWS;

                float2 planar = float2(dot(offsetWS, rightWS), dot(offsetWS, upWS));
                float worldRadius = length(TransformObjectToWorldDir(float3(0.5, 0, 0)));
                
                float r     = length(planar) / max(worldRadius, 1e-4);
                float theta = atan2(planar.y, planar.x);

                float t = _Time.y * _EruptionSpeed;
                float lr = log(max(r, 0.01));
                
                float spin = theta * _SwirlArms - lr * _SwirlTwist;

                float2 warpUV = float2(spin * 0.2, r * _NoiseScale - t);
                
                float warp = GlassyNoise2(warpUV, _GlassSharpness, _ShardSteps);
                float spinW = spin + (warp - 0.5) * _SwirlWarp * 6.28;

                float smoothStreak = pow(abs(sin(spinW * 0.5)), _StreakSharp);
                float sharpStreak = step(0.85, smoothStreak) * smoothStreak;
                float streak = lerp(smoothStreak, sharpStreak, _GlassSharpness);

                float colorN = GlassyNoise2(warpUV * 2.0 + float2(17.3, 5.1), _GlassSharpness, 3.0);
                float mixT   = saturate((colorN - 0.5) * _ColorContrast + 0.5);
                half3 streakCol = lerp(_ColorA.rgb, _ColorB.rgb, mixT);

                float outerFade = 1.0 - smoothstep(_OuterFade, 1.0, r);
                float radialTaper = (1.0 - smoothstep(_CoreRadius, _OuterFade, r)) * outerFade;

                float coreNoise = GlassyNoise2(float2(theta * 5.0, -t * 2.0), 1.0, 4.0);
                float jaggedRadius = _CoreRadius + (coreNoise - 0.5) * 0.08;
                float coreBlast = step(r, jaggedRadius) + smoothstep(jaggedRadius * 1.5, jaggedRadius, r) * 0.5;

                float shards = step(0.95, GlassyNoise2(warpUV * 4.0 + t, 1.0, 5.0)) * radialTaper;

                half3 col = _VoidColor.rgb;
                
                col += streakCol * streak * radialTaper * _StreakIntensity;
                col += _ColorA.rgb * shards * _StreakIntensity * 1.5;
                col += _CoreColor.rgb * coreBlast * _CoreGlow;

                float fres = pow(1.0 - saturate(dot(nWS, vWS)), _FresnelPower) * _FresnelStrength;
                col += _ColorA.rgb * fres;

                float grain = Hash21(planar * 240.0 + _Time.y * 0.05) - 0.5;
                col += grain * _GrainAmount;

                col *= _Brightness;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
