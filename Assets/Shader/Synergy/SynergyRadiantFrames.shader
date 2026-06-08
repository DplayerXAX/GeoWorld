Shader "GeoWorld/Synergy/RadiantFrames"
{
    Properties
    {
        [Header(Color Palette)]
        [HDR] _CoreColor     ("Core Glow (Blinding White)", Color) = (1.5, 2.2, 2.5, 1.0)
        _InnerColor          ("Vortex Inner (Electric Cyan)", Color) = (0.2, 0.7, 1.0, 1.0)
        _OuterColor          ("Vortex Outer (Deep Sapphire)", Color) = (0.04, 0.12, 0.35, 1.0)
        
        [Header(3D Vortex Geometry)]
        _Layers              ("Vortex Density (Layers)", Range(4, 16)) = 10
        _ScaleMultiplier     ("Exponential Scale Rate", Range(1.1, 1.6)) = 1.3
        _RotationPerLayer    ("3D Twist per Layer", Range(0.05, 0.8)) = 0.25
        _FrameThickness      ("Solid Frame Thickness", Range(0.01, 0.2)) = 0.05
        
        [Header(Animation Settings)]
        _ExpandSpeed         ("3D Core Zoom Speed", Range(-3, 3)) = 0.8
        _RotateSpeed         ("3D Tumble Speed", Range(-2, 2)) = 0.4
        
        [Header(3D Crystal Shading)]
        _LightDirection      ("Fake Light Vector (XYZ)", Vector) = (0.5, 0.8, -0.3, 0)
        _BevelIntensity      ("Crystal Edge Highlight", Range(0, 5)) = 3.0
        _CrystalGloss        ("Chisel Contrast", Range(0.2, 5.0)) = 2.0
        _GlowIntensity       ("Volumetric Aura", Range(0.0, 2.0)) = 1.0

        [Header(Raymarching Settings)]
        _MaxSteps            ("Max Ray Steps", Integer) = 64
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
                float4 _CoreColor;
                float4 _InnerColor;
                float4 _OuterColor;
                float  _Layers;
                float  _ScaleMultiplier;
                float  _RotationPerLayer;
                float  _FrameThickness;
                float  _ExpandSpeed;
                float  _RotateSpeed;
                float4 _LightDirection;
                float  _BevelIntensity;
                float  _CrystalGloss;
                float  _GlowIntensity;
                int    _MaxSteps;
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

            float3 Rotate3D(float3 p, float angle)
            {
                float cy = cos(angle); float sy = sin(angle);
                p = float3(cy * p.x + sy * p.z, p.y, -sy * p.x + cy * p.z);
                float cx = cos(angle * 0.7); float sx = sin(angle * 0.7);
                p = float3(p.x, cx * p.y - sx * p.z, sx * p.y + cx * p.z);
                return p;
            }

            float sdBoxFrame(float3 p, float3 b, float e)
            {
                p = abs(p) - b;
                float3 q = abs(p + e) - e;
                return min(min(
                    length(max(float3(p.x, q.y, q.z), 0.0)) + min(max(p.x, max(q.y, q.z)), 0.0),
                    length(max(float3(q.x, p.y, q.z), 0.0)) + min(max(q.x, max(p.y, q.z)), 0.0)),
                    length(max(float3(q.x, q.y, p.z), 0.0)) + min(max(q.x, max(q.y, p.z)), 0.0));
            }

            float2 MapInternalVolume(float3 p)
            {
                float minDist = 1000.0;
                float layerZ = 0.0;

                float time = _Time.y;
                float progress = frac(time * _ExpandSpeed);
                float t = time * _RotateSpeed;

                int maxLayers = (int)_Layers;

                [unroll(16)]
                for(int i = 0; i < 16; i++)
                {
                    if (i >= maxLayers) break;

                    float z = float(i) - progress;
                    if (z < -0.5) continue; 

                    float scale = 0.1 * pow(_ScaleMultiplier, z);
                    float angle = z * _RotationPerLayer + t;

                    float3 q = Rotate3D(p, angle);
                    
                    float thickness = scale * _FrameThickness;
                    float dist = sdBoxFrame(q, float3(scale, scale, scale), thickness);

                    if (dist < minDist)
                    {
                        minDist = dist;
                        layerZ = z;
                    }
                }
                return float2(minDist, layerZ);
            }

            float3 CalcNormal(float3 p)
            {
                float2 e = float2(0.002, 0.0);
                return normalize(float3(
                    MapInternalVolume(p + e.xyy).x - MapInternalVolume(p - e.xyy).x,
                    MapInternalVolume(p + e.yxy).x - MapInternalVolume(p - e.yxy).x,
                    MapInternalVolume(p + e.yyx).x - MapInternalVolume(p - e.yyx).x
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

                float3 boxMin = float3(-0.5, -0.5, -0.5);
                float3 boxMax = float3( 0.5,  0.5,  0.5);

                float2 hitAABB = IntersectAABB(rayOriginOS, rayDirOS, boxMin, boxMax);
                if(hitAABB.x > hitAABB.y || hitAABB.y < 0.0) return half4(0,0,0,0);

                float tStart = max(hitAABB.x, 0.0);
                float tEnd   = hitAABB.y;
                
                float currentT = tStart;
                float hitCrystal = 0.0;
                float2 finalMapData = float2(0, 0);
                float3 hitPosOS = float3(0,0,0);
                
                float accumulatedGlow = 0.0;

                [loop]
                for(int i = 0; i < _MaxSteps; i++)
                {
                    float3 currentPos = rayOriginOS + rayDirOS * currentT;
                    float2 mapData = MapInternalVolume(currentPos);
                    float dist = mapData.x;

                    accumulatedGlow += exp(-dist * 25.0) * 0.02 * _GlowIntensity;

                    if (dist < 0.001)
                    {
                        hitCrystal = 1.0;
                        finalMapData = mapData;
                        hitPosOS = currentPos;
                        break;
                    }

                    currentT += dist;
                    if (currentT > tEnd) break;
                }

                half3 finalColor = float3(0,0,0);
                float alpha = accumulatedGlow;
                
                finalColor += _OuterColor.rgb * accumulatedGlow;

                if (hitCrystal > 0.0)
                {
                    float z = finalMapData.y;
                    float layerRatio = saturate(z / _Layers);

                    half3 crystalColor = lerp(_InnerColor.rgb, _OuterColor.rgb, layerRatio);
                    if (z < 2.0)
                    {
                        crystalColor = lerp(_CoreColor.rgb, _InnerColor.rgb, saturate(z / 2.0));
                    }

                    float3 normalOS = CalcNormal(hitPosOS);
                    float3 normalWS = normalize(TransformObjectToWorldNormal(normalOS));
                    float3 lightDirOS = normalize(_LightDirection.xyz);
                    
                    float diff = max(dot(normalOS, lightDirOS), 0.0) * 0.6 + 0.4;
                    
                    float3 halfDir = normalize(lightDirOS - rayDirOS); 
                    float specPower = exp2(5.0 * _CrystalGloss + 1.0);
                    float spec = pow(saturate(dot(normalOS, halfDir)), specPower) * _BevelIntensity;

                    float fresnel = pow(1.0 - saturate(dot(normalWS, -viewDirWS)), 4.0);

                    finalColor += crystalColor * diff;
                    finalColor += _CoreColor.rgb * spec;
                    finalColor += _CoreColor.rgb * fresnel * 1.5 * (1.0 - layerRatio); 
                    
                    alpha = 1.0;
                }

                float edgeFade = smoothstep(0.0, 0.05, tEnd - currentT) * smoothstep(0.0, 0.05, currentT - tStart);
                alpha *= edgeFade;

                return half4(finalColor, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
