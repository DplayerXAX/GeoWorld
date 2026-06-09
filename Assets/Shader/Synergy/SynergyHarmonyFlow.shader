Shader "GeoWorld/Synergy/HarmonyCore_GlassyVolume_Scaleable"
{
    Properties
    {
        [Header(Emerald Radiance)]
        [HDR] _CoreColor       ("Core Blast (Blinding Green)", Color) = (1.2, 2.0, 0.8, 1.0)
        _MidColor              ("Crystal Body (Emerald)", Color) = (0.2, 0.8, 0.4, 1.0)
        _DarkColor             ("Deep Shadow (Forest)", Color) = (0.05, 0.15, 0.08, 1.0)
        _Absorption            ("Absorption Density", Range(0.1, 10.0)) = 3.5
        
        [Header(Crystalline Stylization)]
        _GlassSharpness        ("Glass Quantization", Range(0.0, 1.0)) = 0.85
        _ShardSteps            ("Shard Detail Levels", Range(2.0, 10.0)) = 4.0
        _TwistSpeed            ("Crystal Twist Speed", Range(0.0, 3.0)) = 1.0
        
        [Header(Surface Properties)]
        _Glossiness            ("Surface Smoothness", Range(0.1, 1.0)) = 0.9
        _Specular              ("Specular Intensity", Range(0.0, 5.0)) = 3.5
        _GrainAmount           ("Paper Grain", Range(0, 0.08)) = 0.035
        
        [Header(Raymarching Settings)]
        _MaxSteps              ("Max Ray Steps", Integer) = 96
        _StepSize              ("Ray Step Size", Range(0.01, 0.1)) = 0.02
        
        _VolumeScale           ("Volume Scale", Range(0.5, 5.0)) = 1.0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VolumeRaymarching"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha 
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _MidColor, _DarkColor;
                float  _Absorption, _GlassSharpness, _ShardSteps, _TwistSpeed;
                float  _Glossiness, _Specular, _GrainAmount;
                int    _MaxSteps;
                float  _StepSize;
                float  _VolumeScale;      
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                return OUT;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise(float3 p)
            {
                float3 i = floor(p); float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash(i); float n100 = Hash(i + float3(1,0,0));
                float n010 = Hash(i + float3(0,1,0)); float n110 = Hash(i + float3(1,1,0));
                float n001 = Hash(i + float3(0,0,1)); float n101 = Hash(i + float3(1,0,1));
                float n011 = Hash(i + float3(0,1,1)); float n111 = Hash(i + float3(1,1,1));
                float x00 = lerp(n000,n100,f.x); float x10 = lerp(n010,n110,f.x);
                float x01 = lerp(n001,n101,f.x); float x11 = lerp(n011,n111,f.x);
                float y0 = lerp(x00,x10,f.y); float y1 = lerp(x01,x11,f.y);
                return lerp(y0,y1,f.z);
            }

            float Fbm(float3 p)
            {
                float v = 0; float a = 0.5;
                [unroll] for(int i=0; i<4; i++) { v += Noise(p) * a; p *= 2.0; a *= 0.5; }
                return v;
            }

            float GlassyFBM(float3 p, float sharpness, float steps)
            {
                float n = Fbm(p);
                float quantized = floor(n * steps) / steps;
                return lerp(n, quantized, sharpness);
            }

            float sdOctahedron(float3 p, float s)
            {
                p = abs(p);
                return (p.x + p.y + p.z - s) * 0.57735027;
            }

            float MapInternalVolume(float3 p)
            {
                float3 scaledP = p / _VolumeScale;
                
                float t = _Time.y * _TwistSpeed;
                float theta = scaledP.y * 3.0 - t;
                float c = cos(theta); float s = sin(theta);
                float2x2 rot = float2x2(c, -s, s, c);
                
                float3 q = scaledP;
                q.xz = mul(rot, q.xz);

                float crystal = sdOctahedron(q, 0.40);
                float noiseMask = GlassyFBM(q * 5.0 + float3(0, t * 0.5, 0), _GlassSharpness, _ShardSteps);
                
                return crystal + (noiseMask - 0.5) * 0.3;
            }

            float3 CalcInternalNormal(float3 p)
            {
                float2 e = float2(0.01, 0.0);
                return normalize(float3(
                    MapInternalVolume(p + e.xyy) - MapInternalVolume(p - e.xyy),
                    MapInternalVolume(p + e.yxy) - MapInternalVolume(p - e.yxy),
                    MapInternalVolume(p + e.yyx) - MapInternalVolume(p - e.yyx)
                ));
            }

            float2 IntersectAABB(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
            {
                float3 tMin = (boxMin - rayOrigin) / rayDir;
                float3 tMax = (boxMax - rayOrigin) / rayDir;
                float3 t1 = min(tMin, tMax); float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z); float tFar = min(min(t2.x, t2.y), t2.z);
                return float2(tNear, tFar);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 viewPosWS = GetCameraPositionWS();
                float3 viewDirWS = normalize(IN.positionWS - viewPosWS);
                
                float3 rayOriginOS = TransformWorldToObject(viewPosWS);
                float3 rayDirOS    = normalize(TransformWorldToObjectDir(viewDirWS));

                float3 boxMin = float3(-0.5, -0.5, -0.5) * _VolumeScale;
                float3 boxMax = float3( 0.5,  0.5,  0.5) * _VolumeScale;

                float2 hitAABB = IntersectAABB(rayOriginOS, rayDirOS, boxMin, boxMax);
                if(hitAABB.x > hitAABB.y || hitAABB.y < 0.0) return half4(0,0,0,0);

                float tStart = max(hitAABB.x, 0.0);
                float tEnd   = hitAABB.y;
                
                float currentT = tStart;
                float3 currentPos = rayOriginOS + rayDirOS * currentT;
                
                float accumulatedThickness = 0.0;
                float3 crystalNormalOS = float3(0, 1, 0);
                float hitCrystal = 0.0;

                float adjustedStepSize = _StepSize * _VolumeScale;

                [loop]
                for(int i = 0; i < _MaxSteps; i++)
                {
                    if(currentT > tEnd) break;

                    float dist = MapInternalVolume(currentPos);
                    
                    if(dist < 0.0)
                    {
                        if (hitCrystal == 0.0)
                        {
                            crystalNormalOS = CalcInternalNormal(currentPos);
                            hitCrystal = 1.0;
                        }
                        accumulatedThickness += adjustedStepSize;
                    }
                    
                    currentT += adjustedStepSize;
                    currentPos = rayOriginOS + rayDirOS * currentT;
                }

                if (hitCrystal == 0.0) return half4(0, 0, 0, 0);

                float effectiveAbsorption = _Absorption / _VolumeScale;
                float thickness = saturate(accumulatedThickness * effectiveAbsorption);
                float sharpThickness = lerp(thickness, floor(thickness * _ShardSteps) / _ShardSteps, _GlassSharpness * 0.5);

                half3 finalColor = lerp(_DarkColor.rgb, _MidColor.rgb, sharpThickness);
                finalColor = lerp(finalColor, _CoreColor.rgb, pow(sharpThickness, 3.0));

                float3 normalWS_Crystal = normalize(TransformObjectToWorldNormal(crystalNormalOS));

                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);
                float3 halfDir = normalize(lightDirWS - viewDirWS); 
                
                float specPower = exp2(10.0 * _Glossiness + 1.0);
                float spec = pow(saturate(dot(normalWS_Crystal, halfDir)), specPower);
                finalColor += mainLight.color * spec * _Specular;

                float fresnel = pow(1.0 - saturate(dot(normalWS_Crystal, -viewDirWS)), 5.0);
                finalColor += fresnel * _CoreColor.rgb * 1.5;

                float grain = Hash21(IN.positionOS.xy + IN.positionOS.zz * 240.0 + _Time.y * 0.05) - 0.5;
                finalColor += grain * _GrainAmount;

                float alpha = saturate(accumulatedThickness * 5.0 / _VolumeScale);  

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}