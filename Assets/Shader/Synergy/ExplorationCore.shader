Shader "GeoWorld/Synergy/Exploration"
{
    Properties
    {
        [Header(Silkscreen Palette)]
        [HDR] _CoreColor       ("Core Blast (Blinding Gold)", Color) = (2.2, 1.8, 1.0, 1.0)
        _ColorA                ("Inner Shards (Muted Gold)", Color) = (0.92, 0.72, 0.32, 1.0)
        _ColorB                ("Outer Shards (Rust Orange)", Color) = (0.78, 0.38, 0.20, 1.0)
        _VoidColor             ("Abyss Background", Color) = (0.05, 0.02, 0.03, 1.0)

        [Header(Crystalline Matrix)]
        _ShellDensity          ("Concentric Shell Density", Range(2.0, 15.0)) = 5.0
        _FacetCount            ("Stained Glass Facet Count", Range(2.0, 16.0)) = 8.0
        _GlassSharpness        ("Fracture Displacement", Range(0.0, 0.5)) = 0.15
        _EruptionSpeed         ("Supernova Eruption Speed", Range(0.0, 2.0)) = 0.4
        
        [Header(Holographic Rendering)]
        _LineThickness         ("Shell Wall Thickness", Range(0.002, 0.05)) = 0.015
        _ShellAlpha            ("Shell Opacity", Range(0.1, 1.0)) = 0.85
        _CoreRadius            ("Core Singularity Radius", Range(0.05, 0.3)) = 0.12
        _OuterFade             ("Boundary Containment", Range(0.3, 0.5)) = 0.46
        
        [Header(Lighting and Polish)]
        _Specular              ("Facet Specular Glint", Range(0.0, 5.0)) = 3.5
        _FresnelPower          ("Edge Glowing Power", Range(0.5, 5.0)) = 2.0
        _VolumeScale           ("Internal Scale Adaptor", Range(0.5, 3.0)) = 1.0
        
        [Header(Engine Integration)]
        _StepSize              ("Ray Step Precision", Range(0.005, 0.05)) = 0.015
        _MaxSteps              ("Max Ray Steps", Int) = 80
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "IsoSurfaceMatrix"
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

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor, _ColorA, _ColorB, _VoidColor;
                float  _ShellDensity, _FacetCount, _GlassSharpness, _EruptionSpeed;
                float  _LineThickness, _ShellAlpha, _CoreRadius, _OuterFade;
                float  _Specular, _FresnelPower, _VolumeScale;
                float  _StepSize;
                int    _MaxSteps;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 positionOS : TEXCOORD1; };

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

            float FracturedCrystalSDF(float3 posOS)
            {
                float3 sp = posOS / _VolumeScale;
                float r = length(sp);
                float3 n = sp / max(r, 0.0001);
                
                float theta = atan2(n.z, n.x);
                float phi = acos(n.y);
                
                float qTheta = floor(theta * _FacetCount) / _FacetCount;
                float qPhi   = floor(phi * _FacetCount) / _FacetCount;
                
                float chunkShift = Hash21(float2(qTheta, qPhi)) * _GlassSharpness * 0.3;
                
                float3 q = abs(sp);
                float d1 = max(q.x, max(q.y, q.z));
                float d2 = (q.x + q.y + q.z) * 0.57735;
                float baseGeometry = max(d1, d2);
                
                baseGeometry += chunkShift;
                
                float spacing = 1.0 / _ShellDensity;
                float phase = baseGeometry - _Time.y * _EruptionSpeed;
                
                float shellDist = abs(frac(phase / spacing) - 0.5) * spacing;
                
                return shellDist;
            }

            float3 CalcNormal(float3 p)
            {
                float2 e = float2(0.01, 0.0);
                return normalize(float3(
                    FracturedCrystalSDF(p + e.xyy) - FracturedCrystalSDF(p - e.xyy),
                    FracturedCrystalSDF(p + e.yxy) - FracturedCrystalSDF(p - e.yxy),
                    FracturedCrystalSDF(p + e.yyx) - FracturedCrystalSDF(p - e.yyx)
                ));
            }

            float2 IntersectAABB(float3 rayOrigin, float3 rayDir, float3 boxMin, float3 boxMax)
            {
                float3 tMin = (boxMin - rayOrigin) / rayDir;
                float3 tMax = (boxMax - rayOrigin) / rayDir;
                float3 t1 = min(tMin, tMax); float3 t2 = max(tMin, tMax);
                float tNear = max(max(t1.x, t1.y), t1.z); 
                float tFar  = min(min(t2.x, t2.y), t2.z);
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
                if(hitAABB.x > hitAABB.y || hitAABB.y < 0.0) return half4(_VoidColor.rgb, 1.0);

                float tStart = max(hitAABB.x, 0.0);
                float tEnd   = hitAABB.y;
                
                float grain = (Hash21(IN.positionCS.xy * 0.05 + _Time.y) - 0.5) * 0.04;
                half3 finalColor = _VoidColor.rgb + grain;
                
                half3 accColor = 0.0;
                float accAlpha = 0.0;
                float currentT = tStart;
                
                float adjustedStep = _StepSize * _VolumeScale;
                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);

                [loop]
                for(int i = 0; i < _MaxSteps; i++)
                {
                    if(currentT > tEnd || accAlpha > 0.98) break;

                    float3 posOS = rayOriginOS + rayDirOS * currentT;
                    float r = length(posOS / _VolumeScale);

                    if (r < _OuterFade)
                    {
                        if (r < _CoreRadius)
                        {
                            float coreIntensity = smoothstep(_CoreRadius, _CoreRadius * 0.7, r);
                            float coreAlpha = coreIntensity * 0.5;
                            accColor += _CoreColor.rgb * coreAlpha * (1.0 - accAlpha);
                            accAlpha += coreAlpha * (1.0 - accAlpha);
                        }
                        else
                        {
                            float shellDist = FracturedCrystalSDF(posOS);
                            float shellIntensity = smoothstep(_LineThickness, _LineThickness * 0.3, shellDist);
                            
                            if (shellIntensity > 0.01)
                            {
                                float3 normalOS = CalcNormal(posOS);
                                float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                                
                                float colorBlend = saturate((r - _CoreRadius) / (_OuterFade - _CoreRadius));
                                half3 baseLayerCol = lerp(_ColorA.rgb, _ColorB.rgb, colorBlend);
                                
                                float fresnel = pow(1.0 - abs(dot(normalWS, viewDirWS)), _FresnelPower);
                                
                                float3 halfDir = normalize(lightDirWS - viewDirWS);
                                float spec = pow(max(dot(normalWS, halfDir), 0.0), 32.0);
                                
                                half3 shellFinalCol = baseLayerCol * (fresnel + 0.3) + (mainLight.color * spec * _Specular);
                                float shellLayerAlpha = shellIntensity * _ShellAlpha * (fresnel * 0.5 + 0.5);
                                
                                accColor += shellFinalCol * shellLayerAlpha * (1.0 - accAlpha);
                                accAlpha += shellLayerAlpha * (1.0 - accAlpha);
                            }
                        }
                    }
                    
                    currentT += adjustedStep;
                }

                finalColor = accColor + finalColor * (1.0 - accAlpha);

                return half4(finalColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
