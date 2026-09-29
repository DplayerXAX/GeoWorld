Shader "GeoWorld/LandscapeSky"
{
    // A plain, natural sky for landscape levels (LevelEnvironment.useLandscapeSky):
    // zenith to horizon gradient, a bright band at the horizon, a sun disc and halo
    // where the main light comes from, and soft clouds lit from the sun's side.
    // Below the horizon it settles to _Ground, so scenery and fog melt into it.
    Properties
    {
        _Zenith      ("Zenith",   Color) = (0.45, 0.62, 0.85, 1)
        _Horizon     ("Horizon",  Color) = (1, 0.86, 0.66, 1)
        _Band        ("Horizon band", Color) = (1, 0.8, 0.55, 1)
        _Ground      ("Below horizon", Color) = (0.72, 0.64, 0.52, 1)
        [HDR] _Sun   ("Sun", Color) = (1.6, 1.2, 0.8, 1)
        _SunSize     ("Sun size (deg)", Range(0.5, 8)) = 2.4
        _Cloud       ("Cloud, lit",   Color) = (1, 0.95, 0.88, 1)
        _CloudShade  ("Cloud, shade", Color) = (0.72, 0.66, 0.66, 1)
        _Cover       ("Cloud cover", Range(0, 1)) = 0.45
        _CloudScale  ("Cloud scale", Float) = 1.3
        _CloudSpeed  ("Cloud drift", Float) = 0.004
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _Zenith, _Horizon, _Band, _Ground, _Sun, _Cloud, _CloudShade;
            float _SunSize, _Cover, _CloudScale, _CloudSpeed;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.dir = IN.positionOS.xyz;
                return o;
            }

            float hash12(float2 p) { float3 q = frac(float3(p.xyx) * 0.1031); q += dot(q, q.yzx + 33.33); return frac((q.x + q.y) * q.z); }
            float vnoise(float2 x)
            {
                float2 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(hash12(i), hash12(i + float2(1, 0)), f.x),
                            lerp(hash12(i + float2(0, 1)), hash12(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p)
            {
                float s = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { s += vnoise(p) * a; p = p * 2.03 + 17.1; a *= 0.5; }
                return s;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 d = normalize(IN.dir);
                float3 L = normalize(_MainLightPosition.xyz);
                float up = d.y;

                // Gradient: horizon up to zenith; a warm band hugging the horizon.
                float3 sky = lerp(_Horizon.rgb, _Zenith.rgb, pow(saturate(up), 0.5));
                sky = lerp(sky, _Band.rgb, exp(-max(up, 0.0) * 14.0) * 0.8);
                // Below: into the ground colour, softly.
                sky = up < 0.0 ? lerp(lerp(_Band.rgb, _Horizon.rgb, 0.5), _Ground.rgb, saturate(-up * 5.0)) : sky;

                // Sun: disc, close halo, and a broad glow that warms the whole side.
                float mu = dot(d, L);
                float cosSize = cos(radians(_SunSize));
                float disc = smoothstep(cosSize, lerp(cosSize, 1.0, 0.35), mu);
                float halo = pow(saturate(mu), 180.0) * 0.8 + pow(saturate(mu), 12.0) * 0.25;
                sky += _Sun.rgb * (disc * 3.0 + halo);

                // Clouds on a plane overhead, fading toward the horizon.
                if (up > 0.0)
                {
                    float2 uv = d.xz / (up + 0.15) * _CloudScale + _Time.y * _CloudSpeed * float2(1.0, 0.4);
                    float n = fbm(uv);
                    float c = smoothstep(1.0 - _Cover, 1.0 - _Cover + 0.25, n);
                    float thick = smoothstep(1.0 - _Cover + 0.1, 1.0, n);
                    // Lit on the sun's side, shadowed underneath where thick.
                    float lit = saturate(0.55 + 0.45 * dot(normalize(float3(d.x, 0.3, d.z)), L)) * (1.0 - thick * 0.4);
                    float3 cc = lerp(_CloudShade.rgb, _Cloud.rgb, lit);
                    cc += _Sun.rgb * pow(saturate(mu), 8.0) * 0.35;   // edges lit near the sun
                    c *= smoothstep(0.0, 0.18, up);
                    sky = lerp(sky, cc, c * 0.9);
                }
                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
