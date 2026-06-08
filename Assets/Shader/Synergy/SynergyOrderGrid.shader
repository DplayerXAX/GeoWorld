// OrderCore — Volumetric Architectural Blueprint Cube
Shader "GeoWorld/Synergy/OrderCore_ArchitecturalVolume"
{
    Properties
    {
        [Header(Blueprint Laser Colors)]
        [HDR] _CoreColor       ("Central Matrix Core (Neon Cyan)", Color) = (0.4, 2.3, 2.5, 1.0)
        _MidColor              ("Structural Frames (Teal)", Color)       = (0.15, 0.6, 0.65, 1.0)
        _DarkColor             ("Deep Volume Shadows (Dark Teal)", Color) = (0.02, 0.08, 0.1, 1.0)
        _Absorption            ("Volumetric Density", Range(0.1, 10.0))   = 4.0
        
        [Header(Architectural Geometry)]
        _FrameCount            ("Nested Frame Layers", Range(1.0, 4.0))   = 3.0
        _BarThickness          ("Beam/Column Thickness", Range(0.005, 0.04)) = 0.012
        
        [Header(Surface Properties)]
        _Glossiness            ("Acrylic Surface Smoothness", Range(0.1, 1.0)) = 0.95
        _Specular              ("Laser Flash Intensity", Range(0.0, 5.0))  = 4.0
        _GrainAmount           ("Technical Paper Grain", Range(0, 0.08))  = 0.025
        
        [Header(Raymarching Engine)]
        _MaxSteps              ("Max Ray Steps", Integer) = 96
        _StepSize              ("Ray Step Size", Range(0.01, 0.1)) = 0.015
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
                float  _Absorption, _FrameCount, _BarThickness;
                float  _Glossiness, _Specular, _GrainAmount;
                int    _MaxSteps;
                float  _StepSize;
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

            float sdBox(float3 p, float3 b)
            {
                float3 d = abs(p) - b;
                return min(max(d.x, max(d.y, d.z)), 0.0) + length(max(d, 0.0));
            }

            float sdBoxFrame(float3 p, float3 b, float e)
            {
                p = abs(p) - b;
                float3 q = abs(p + e) - e;
                return min(min(
                    max(max(p.x, q.y), q.z),
                    max(max(q.x, p.y), q.z)),
                    max(max(q.x, q.y), p.z));
            }

            float MapInternalVolume(float3 p)
            {
                float d = 1e5;
                
                float centerCore = sdBox(p, float3(0.06, 0.06, 0.06));
                d = min(d, centerCore);
                
                [unroll(4)]
                for(int i = 1; i <= 4; i++)
                {
                    if ((float)i > _FrameCount) break;
                    
                    float frameSize = 0.11 * (float)i; 
                    float subFrame = sdBoxFrame(p, float3(frameSize, frameSize, frameSize), _BarThickness);
                    d = min(d, subFrame);
                }
                
                float3 absP = abs(p);
                float rodX = max(absP.y, absP.z) - _BarThickness * 0.6;
                float rodY = max(absP.x, absP.z) - _BarThickness * 0.6;
                float rodZ = max(absP.x, absP.y) - _BarThickness * 0.6;
                
                float boundary = sdBox(p, float3(0.45, 0.45, 0.45));
                float internalRods = max(boundary, min(rodX, min(rodY, rodZ)));
                
                d = min(d, internalRods);
                return d;
            }

            float3 CalcInternalNormal(float3 p)
            {
                float2 e = float2(0.005, 0.0);
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

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 viewPosWS = GetCameraPositionWS();
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
                float3 crystalNormalOS = float3(0, 1, 0);
                float3 firstHitPosOS = float3(0, 0, 0);
                float hitCrystal = 0.0;

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
                            firstHitPosOS = currentPos;
                            hitCrystal = 1.0;
                        }
                        accumulatedThickness += _StepSize;
                    }
                    
                    currentT += _StepSize;
                    currentPos = rayOriginOS + rayDirOS * currentT;
                }

                if (hitCrystal == 0.0) return half4(0, 0, 0, 0);

                float3 absHitPos = abs(firstHitPosOS);
                float chebyshevRadius = max(absHitPos.x, max(absHitPos.y, absHitPos.z)) * 2.0;

                float thickness = saturate(accumulatedThickness * _Absorption);

                half3 finalColor = lerp(_DarkColor.rgb, _MidColor.rgb, thickness);
                
                float coreMask = smoothstep(0.25, 0.0, chebyshevRadius);
                finalColor = lerp(finalColor, _CoreColor.rgb, coreMask * thickness);

                float3 normalWS_Crystal = normalize(TransformObjectToWorldNormal(crystalNormalOS));
                Light mainLight = GetMainLight();
                float3 lightDirWS = normalize(mainLight.direction);
                float3 halfDir = normalize(lightDirWS - viewDirWS); 
                
                float specPower = exp2(10.0 * _Glossiness + 1.0);
                float spec = pow(saturate(dot(normalWS_Crystal, halfDir)), specPower);
                finalColor += mainLight.color * spec * _Specular;

                float fresnel = pow(1.0 - saturate(dot(normalWS_Crystal, -viewDirWS)), 4.0);
                finalColor += fresnel * _CoreColor.rgb * 1.2;

                float grain = Hash21(IN.positionOS.xy * 150.0 + IN.positionOS.zz) - 0.5;
                finalColor += grain * _GrainAmount;

                float alpha = saturate(accumulatedThickness * 6.0);

                return half4(finalColor, alpha);
            }
            ENDHLSL
        }
    }
}
