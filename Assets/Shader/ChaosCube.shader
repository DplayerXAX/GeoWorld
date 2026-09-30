Shader "GeoWorld/ChaosCube"
{
    // The Chaos Block: a cube that won't hold still. Its surface is a slow churn
    // of near-black violet, split by thin burning cracks that wander over it; the
    // edges glow where they turn from the camera; and every so often it glitches —
    // horizontal slices of it jump sideways for a frame or two, and the whole
    // thing shivers along its normals. Deliberately the opposite of the board's
    // calm flat-printed blocks, so it reads as something that does not belong.
    //
    // No texture, no lighting: it lights itself. The cracks are HDR, so with
    // bloom on they smoulder. The same displacement runs in the depth pass, so
    // fog and the depth texture see the shape that is actually drawn.
    Properties
    {
        _Dark     ("Body",           Color) = (0.035, 0.02, 0.06, 1)
        _Mid      ("Churn",          Color) = (0.28, 0.06, 0.42, 1)
        [HDR] _Crack ("Cracks",      Color) = (2.6, 0.45, 1.6, 1)
        [HDR] _Rim   ("Rim",         Color) = (0.9, 0.25, 1.4, 1)
        _Churn    ("Churn speed",    Float) = 0.6
        _CrackWidth ("Crack width",  Range(0.005, 0.12)) = 0.035
        _Glitch   ("Glitch amount",  Range(0, 1)) = 0.6
        _Shiver   ("Shiver (object units)", Range(0, 0.1)) = 0.025
        // ChaosBlockUnit pulses this (via MpbColor) from near-black toward red as
        // the block takes damage; read here as "how close to breaking".
        [HideInInspector] _BaseColor ("Damage pulse (MPB)", Color) = (0.03, 0.03, 0.05, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Dark, _Mid, _Crack, _Rim, _BaseColor;
            float _Churn, _CrackWidth, _Glitch, _Shiver;
        CBUFFER_END

        float ChaosHash(float3 p)
        {
            p = frac(p * 0.1031);
            p += dot(p, p.yzx + 19.19);
            return frac((p.x + p.y) * p.z);
        }
        float ChaosNoise(float3 x)
        {
            float3 i = floor(x), f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(ChaosHash(i),                  ChaosHash(i + float3(1, 0, 0)), f.x),
                             lerp(ChaosHash(i + float3(0, 1, 0)), ChaosHash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(ChaosHash(i + float3(0, 0, 1)), ChaosHash(i + float3(1, 0, 1)), f.x),
                             lerp(ChaosHash(i + float3(0, 1, 1)), ChaosHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        // Each block glitches on its own clock (seeded by where it stands).
        float3 ChaosDisplace(float3 posOS, float3 normalOS, out float glitchOut)
        {
            float3 seed = TransformObjectToWorld(float3(0, 0, 0));
            float  s    = ChaosHash(floor(seed * 3.1) + 7.0);
            float  t    = _Time.y;

            // A glitch burst: on for a short window every ~1–2.5 s.
            float tick  = floor(t * 7.0);
            float burst = step(0.86, ChaosHash(float3(tick, s * 91.0, 3.0))) * _Glitch;
            // Slices: bands across the height jump sideways, each its own way.
            float band  = floor((posOS.y + 0.5) * 7.0);
            float slip  = (ChaosHash(float3(band, tick, s * 13.0)) - 0.5) * 0.28 * burst
                        * step(0.45, ChaosHash(float3(band + 5.0, tick, s)));
            posOS.x += slip;
            posOS.z += slip * (ChaosHash(float3(band, tick + 1.0, s)) - 0.5);

            // A constant shiver along the normals.
            float sh = (ChaosNoise(posOS * 6.0 + t * 9.0 + s * 17.0) - 0.5) * 2.0;
            posOS += normalOS * sh * _Shiver * (1.0 + burst * 3.0);

            glitchOut = burst;
            return posOS;
        }
        ENDHLSL

        Pass
        {
            Name "Chaos"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS      : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
                float  glitch     : TEXCOORD3;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings o;
                float g;
                float3 p = ChaosDisplace(IN.positionOS.xyz, IN.normalOS, g);
                o.posOS      = IN.positionOS.xyz;
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                o.glitch     = g;
                return o;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float t = _Time.y * _Churn;
                float3 p = IN.posOS * 2.2;

                // Domain-warped churn: the surface seems to boil from inside.
                float3 w = float3(ChaosNoise(p + t), ChaosNoise(p + 11.3 - t), ChaosNoise(p + 23.7 + t * 0.7));
                float  n = ChaosNoise(p * 1.7 + w * 2.4 + t * 0.5);
                float  n2 = ChaosNoise(p * 4.1 - w * 1.3 - t * 0.8);

                // Damage: ChaosBlockUnit's red pulse turns the cracks to hot red and
                // bleeds a little red into the body.
                float  danger = saturate((_BaseColor.r - _BaseColor.b) * 2.0);
                float3 crackC = lerp(_Crack.rgb, float3(3.2, 0.35, 0.2), danger);

                float3 col = lerp(_Dark.rgb, _Mid.rgb, smoothstep(0.35, 0.85, n) * 0.85);
                col = lerp(col, float3(0.35, 0.02, 0.04), danger * 0.5);

                // Cracks: thin lines where the churn crosses its midpoint.
                float crack = 1.0 - smoothstep(0.0, _CrackWidth, abs(n2 - 0.5));
                crack *= smoothstep(0.3, 0.7, n);                       // not everywhere at once
                float flicker = 0.7 + 0.3 * sin(_Time.y * 23.0 + n * 40.0);
                col += crackC * crack * flicker * (1.0 + danger);

                // Rim: the edges turned from the camera glow.
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float  rim = pow(1.0 - saturate(dot(normalize(IN.normalWS), V)), 3.0);
                col += _Rim.rgb * rim;

                // During a glitch: colour splits toward magenta/cyan and it flares.
                col = lerp(col, col.bgr * 1.6 + crackC * 0.2, IN.glitch * 0.6);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex   DVert
            #pragma fragment DFrag
            struct DA { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            float4 DVert(DA IN) : SV_POSITION
            {
                float g;
                return TransformObjectToHClip(ChaosDisplace(IN.positionOS.xyz, IN.normalOS, g));
            }
            half DFrag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex   NVert
            #pragma fragment NFrag
            struct NA { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct NV { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            NV NVert(NA IN)
            {
                NV o; float g;
                o.positionCS = TransformObjectToHClip(ChaosDisplace(IN.positionOS.xyz, IN.normalOS, g));
                o.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return o;
            }
            half4 NFrag(NV IN) : SV_Target { return half4(NormalizeNormalPerPixel(IN.normalWS), 0.0); }
            ENDHLSL
        }
    }
    FallBack Off
}
