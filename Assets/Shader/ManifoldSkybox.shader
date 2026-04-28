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

        _HorizonSharp  ("Horizon Sharpness", Range(1, 20)) = 6.0
        _TimeSpeed     ("Time Speed", Float) = 0.2
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
            float _FogStart, _FogDensity, _HorizonSharp;
            float _TimeSpeed;

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

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);

                float n000 = Hash(i + float3(0,0,0));
                float n100 = Hash(i + float3(1,0,0));
                float n010 = Hash(i + float3(0,1,0));
                float n110 = Hash(i + float3(1,1,0));
                float n001 = Hash(i + float3(0,0,1));
                float n101 = Hash(i + float3(1,0,1));
                float n011 = Hash(i + float3(0,1,1));
                float n111 = Hash(i + float3(1,1,1));

                float3 u = f * f * (3.0 - 2.0 * f);

                return lerp(lerp(lerp(n000, n100, u.x),
                                 lerp(n010, n110, u.x), u.y),
                            lerp(lerp(n001, n101, u.x),
                                 lerp(n011, n111, u.x), u.y),
                            u.z);
            }

            float BlockNoise(float3 p)
            {
                float n = Noise(p);
                n = floor(n * 4.0) / 4.0;
                return n;
            }

            float GridLines(float3 dir)
            {
                float3 d = normalize(dir);

                float2 uvXY = d.xy * _GridScale;
                float2 uvXZ = d.xz * _GridScale;
                float2 uvYZ = d.yz * _GridScale;

                float2 gXY = abs(frac(uvXY - 0.5) - 0.5) / fwidth(uvXY);
                float2 gXZ = abs(frac(uvXZ - 0.5) - 0.5) / fwidth(uvXZ);
                float2 gYZ = abs(frac(uvYZ - 0.5) - 0.5) / fwidth(uvYZ);

                float lineXY = 1.0 - min(min(gXY.x, gXY.y), 1.0 / _GridThickness);
                float lineXZ = 1.0 - min(min(gXZ.x, gXZ.y), 1.0 / _GridThickness);
                float lineYZ = 1.0 - min(min(gYZ.x, gYZ.y), 1.0 / _GridThickness);

                float3 w = abs(d);
                w = pow(w, 6.0);
                w /= (w.x + w.y + w.z);

                float grid = lineXZ * w.y
                           + lineXY * w.z
                           + lineYZ * w.x;

                return saturate(grid);
            }

            float HorizonFog(float3 dir)
            {
                float elevation = normalize(dir).y;
                float t = abs(elevation);
                float fog = exp(-_FogDensity * max(0, t - _FogStart));
                return saturate(fog);
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                float up = dir.y * 0.5 + 0.5;

                float t = pow(saturate(up), 1.0 / _HorizonSharp);
                half4 sky = lerp(_HorizonColor, _ZenithColor, t);

                float grid = GridLines(dir);

                float3 p = dir * 8.0 + _Time.y * _TimeSpeed;

                float n1 = BlockNoise(p);
                float n2 = BlockNoise(p * 2.0) * 0.5;
                float n3 = BlockNoise(p * 4.0) * 0.25;

                float fogLayer = saturate(n1 + n2 + n3);

                float baseFog = HorizonFog(dir);
                float fog = saturate(baseFog * (0.6 + fogLayer));

                float height = dir.y * 0.5 + 0.5;
                fog *= smoothstep(0.0, 0.6, 1.0 - height);

                grid *= (1.0 - fog * 0.7);

                half4 fogCol = lerp(_FogColor, _ZenithColor, fogLayer);
                sky = lerp(fogCol, sky, 1.0 - fog);

                half4 result = lerp(sky, _GridColor, grid * 0.6);

                return result;
            }
            ENDHLSL
        }
    }
}