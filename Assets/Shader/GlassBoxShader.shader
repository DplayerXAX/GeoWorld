Shader "URP/ColoredPrismGlass"
{
    Properties
    {
        _Tint ("Base Tint", Color) = (0.2,0.6,1,0.05)

        _Refraction ("Refraction", Range(0,0.2)) = 0.05
        _ChromaticAberration ("Chromatic Split", Range(0,0.02)) = 0.01

        _FresnelPower ("Fresnel Power", Range(1,8)) = 5
        _FresnelIntensity ("Fresnel Intensity", Range(0,3)) = 1.5

        _EdgeThickness ("Edge Glow Thickness", Range(0,2)) = 0.8

        _InternalGlow ("Internal Glow", Range(0,2)) = 0.6

        _RimColor ("Rim Color", Color) = (0.2,0.8,1,1)
        _DispersionColor ("Dispersion Tint", Color) = (1,0.6,0.2,1)

        _DistortionStrength ("Distortion Strength", Range(0,1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline"="UniversalPipeline"
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
            };

            TEXTURE2D(_CameraOpaqueTexture);
            SAMPLER(sampler_CameraOpaqueTexture);

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                float _Refraction;
                float _ChromaticAberration;
                float _FresnelPower;
                float _FresnelIntensity;
                float _EdgeThickness;
                float _InternalGlow;
                half4 _RimColor;
                half4 _DispersionColor;
                float _DistortionStrength;
            CBUFFER_END

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs pos = GetVertexPositionInputs(v.positionOS.xyz);

                o.positionCS = pos.positionCS;
                o.positionWS = pos.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.screenPos = ComputeScreenPos(o.positionCS);

                return o;
            }

            float3 SampleRGB(float2 uv, float2 offset)
            {
                float r = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv + offset).r;
                float g = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv).g;
                float b = SAMPLE_TEXTURE2D(_CameraOpaqueTexture, sampler_CameraOpaqueTexture, uv - offset).b;
                return float3(r,g,b);
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));

                float2 uv = i.screenPos.xy / i.screenPos.w;

                float fresnel = pow(1.0 - saturate(dot(N,V)), _FresnelPower);

                float2 distort = N.xy * _Refraction;

                float2 chroma = N.xy * _ChromaticAberration * fresnel;

                float3 scene = SampleRGB(uv + distort, chroma);

                float rim = fresnel * _FresnelIntensity;

                float3 rimColor = _RimColor.rgb * rim;

                float internalGlow = pow(fresnel, 2.0) * _InternalGlow;

                float3 col = scene * _Tint.rgb;

                col += rimColor;
                col += _DispersionColor.rgb * internalGlow;

                float alpha = saturate(0.2 + fresnel * 0.8);

                return half4(col, alpha);
            }

            ENDHLSL
        }
    }
}