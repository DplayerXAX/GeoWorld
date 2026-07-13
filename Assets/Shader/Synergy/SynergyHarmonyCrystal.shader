Shader "GeoWorld/Synergy/HarmonyCore"
{
    Properties
    {
        [Header(Volume Absorption and Color)]
        _VolumeColor   ("Volume Base Color", Color) = (0.3, 0.9, 0.1, 1.0)
        _Absorption    ("Absorption Density", Range(0.1, 10.0)) = 2.2
        
        [Header(Surface Properties)]
        _IOR           ("Index of Refraction", Range(1.0, 2.5)) = 1.75
        _Glossiness    ("Surface Smoothness", Range(0.1, 1.0)) = 1.0
        _Specular      ("Specular Intensity", Range(0.0, 5.0)) = 3.5
        
        [Header(Raymarching Settings)]
        _MaxSteps      ("Max Ray Steps", Integer) = 96
        _StepSize      ("Ray Step Size", Range(0.01, 0.1)) = 0.03
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "VolumeRaymarching"
            Tags { "LightMode" = "UniversalForward" }

            Blend One Zero 
            ZWrite On
            Cull Front     

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5 

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _VolumeColor;
                float  _Absorption;
                float  _IOR;
                float  _Glossiness;
                float  _Specular;
                int    _MaxSteps;
                float  _StepSize;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            // 修复结构：正确传递法线
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 positionOS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                // 修复法线变换
                OUT.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.screenPos  = ComputeScreenPos(OUT.positionCS);
                return OUT;
            }

            // --- 3D Noise & FBM 库 ---
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

                f = f * f * (3.0 - 2.0 * f);

                float n000 = Hash(i);
                float n100 = Hash(i + float3(1,0,0));
                float n010 = Hash(i + float3(0,1,0));
                float n110 = Hash(i + float3(1,1,0));

                float n001 = Hash(i + float3(0,0,1));
                float n101 = Hash(i + float3(1,0,1));
                float n011 = Hash(i + float3(0,1,1));
                float n111 = Hash(i + float3(1,1,1));

                float x00 = lerp(n000,n100,f.x);
                float x10 = lerp(n010,n110,f.x);
                float x01 = lerp(n001,n101,f.x);
                float x11 = lerp(n011,n111,f.x);

                float y0 = lerp(x00,x10,f.y);
                float y1 = lerp(x01,x11,f.y);

                return lerp(y0,y1,f.z);
            }

            float Fbm(float3 p)
            {
                float v = 0;
                float a = 0.5;

                [unroll]
                for(int i=0;i<5;i++)
                {
                    v += Noise(p) * a;
                    p *= 2.0;
                    a *= 0.5;
                }

                return v;
            }

            // --- SDF 几何库 ---
            float sdSphere(float3 p, float radius)
            {
                return length(p) - radius;
            }

            // 有机能量核心映射
            float MapInternalVolume(float3 p)
            {
                float core = length(p) - 0.42;

                float3 q = p * 3.5;

                q += float3(
                    Fbm(q.yzx + _Time.y * 0.15),
                    Fbm(q.zxy + _Time.y * 0.12),
                    Fbm(q.xyz + _Time.y * 0.10)
                ) * 0.45;

                float harmony = Fbm(q);
                float organic = harmony - 0.55;
                float shape = max(core, organic);

                float bubble1 = sdSphere(p - float3( 0.18,  0.06,  0.12), 0.09);
                float bubble2 = sdSphere(p - float3(-0.16,  0.14, -0.08), 0.07);
                float bubble3 = sdSphere(p - float3( 0.10, -0.18, -0.10), 0.06);

                shape = max(shape, -bubble1);
                shape = max(shape, -bubble2);
                shape = max(shape, -bubble3);

                return shape;
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
                float3 t1 = min(tMin, tMax);
                float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z);
                float tFar = min(min(t2.x, t2.y), t2.z);
                return float2(tNear, tFar);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // 正确获取世界空间法线
                float3 normalWS = normalize(IN.normalWS);
                
                float3 viewPosWS = GetCameraPositionWS();
                // 射线方向 (相机 -> 像素)
                float3 viewDirWS = normalize(IN.positionWS - viewPosWS); 
                
                float3 rayOriginOS = TransformWorldToObject(viewPosWS);
                float3 rayDirOS    = normalize(TransformWorldToObjectDir(viewDirWS));

                float3 boxMin = float3(-0.5, -0.5, -0.5);
                float3 boxMax = float3( 0.5,  0.5,  0.5);

                float2 hitAABB = IntersectAABB(rayOriginOS, rayDirOS, boxMin, boxMax);
                
                if(hitAABB.x > hitAABB.y || hitAABB.y < 0.0) return half4(0,0,0,0);

                float tStart = max(hitAABB.x, 0.0);
                float tEnd   = hitAABB.y;
                
                float currentT = tStart;
                float3 currentPos = rayOriginOS + rayDirOS * currentT;
                
                float accumulatedThickness = 0.0;
                float2 internalRefractionOffset = float2(0, 0);

                // 体积步进
                [loop]
                for(int i = 0; i < _MaxSteps; i++)
                {
                    if(currentT > tEnd) break;

                    float dist = MapInternalVolume(currentPos);
                    
                    if(dist < 0.0)
                    {
                        accumulatedThickness += _StepSize;
                    }
                    else if(dist < 0.05) 
                    {
                        float3 internalNormal = CalcInternalNormal(currentPos);
                        internalRefractionOffset += internalNormal.xy * 0.1; 
                        currentT += 0.05; 
                    }
                    
                    currentT += _StepSize;
                    currentPos = rayOriginOS + rayDirOS * currentT;
                }

                // 光学吸收
                half3 transmission = exp(-_VolumeColor.rgb * _Absorption * accumulatedThickness);
                
                // 核心颜色多级映射
                float thickness = saturate(accumulatedThickness * 2.5);
                half3 darkColor = half3(0.08, 0.18, 0.05);
                half3 midColor  = half3(0.35, 0.75, 0.15);
                half3 lightColor= half3(0.95, 1.0, 0.55);

                half3 harmonyColor = lerp(darkColor, midColor, thickness);
                harmonyColor = lerp(harmonyColor, lightColor, pow(thickness, 3.0));

                half3 volumeColor = harmonyColor * (1.0 - transmission);

                // 折射背景采样
                float2 screenUV = IN.screenPos.xy / IN.screenPos.w;
                float2 surfaceRefraction = normalWS.xy * (_IOR - 1.0) * 0.05;
                float2 finalUV = screenUV + surfaceRefraction + internalRefractionOffset;
                half3 bgRefraction = SampleSceneColor(finalUV);

                half3 finalRGB = bgRefraction * transmission + volumeColor;

                // 物理高光
                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);
                float3 halfDir = normalize(lightDirWS - viewDirWS); // 注意这里 viewDirWS 的方向
                
                float specPower = exp2(10.0 * _Glossiness + 1.0);
                float spec = pow(saturate(dot(normalWS, halfDir)), specPower);
                
                finalRGB += mainLight.color * spec * _Specular;

                // 边缘强化菲涅尔
                float fresnel = pow(1.0 - saturate(dot(normalWS, -viewDirWS)), 5.0);
                finalRGB += fresnel * float3(1.0, 1.0, 1.0) * 1.25;

                finalRGB = min(finalRGB, half3(5.0, 5.0, 5.0));

                return half4(finalRGB, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}