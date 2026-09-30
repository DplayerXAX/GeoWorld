Shader "GeoWorld/RainMist"
{
    Properties
    {
        _BaseColor ("Mist color and opacity", Color) = (0.65, 0.81, 0.9, 0.55)
        _MistCenter ("World center", Vector) = (0,0,0,0)
        _MistRadius ("World radius", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" "RenderType"="Transparent" }
        Pass
        {
            Cull Front
            ZTest Always
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _MistCenter;
                float _MistRadius;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 direction = -normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 origin = _WorldSpaceCameraPos;
                // Parallel rays for the level's orthographic camera.
                if (unity_OrthoParams.w > 0.5)
                    origin = input.positionWS - direction * dot(input.positionWS - origin, direction);
                float3 offset = origin - _MistCenter.xyz;
                float b = dot(offset, direction);
                float radius = max(_MistRadius, 0.001);
                float discriminant = b * b - dot(offset, offset) + radius * radius;
                if (discriminant <= 0) return 0;
                float root = sqrt(discriminant);
                float entry = max(0, -b - root);
                float exit = -b + root;
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float depth = SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                    depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
                #endif
                float3 scene = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                exit = min(exit, dot(scene - origin, direction));
                if (exit <= entry) return 0;
                float stepLength = (exit - entry) / 12.0;
                float density = 0;
                [unroll] for (int i = 0; i < 12; i++)
                {
                    float3 p = (origin + direction * (entry + (i + 0.5) * stepLength) - _MistCenter.xyz) / radius;
                    float edge = saturate((1 - length(p)) * 3);
                    float3 wind = p * 5 + float3(_Time.y * 0.22, -_Time.y * 0.08, _Time.y * 0.13);
                    float clumps = 0.65 + 0.35 * sin(wind.x + sin(wind.z)) * sin(wind.y - wind.z);
                    density += edge * clumps * stepLength / radius;
                }
                // Cap opacity so towers and enemies remain readable through overlapping banks.
                half alpha = _BaseColor.a * (1 - exp(-density * 2.4));
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
