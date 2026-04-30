Shader "Custom/SacredMarker_SolidCore"
{
    Properties
    {
        _CoreColor ("Core Color", Color) = (0.3,0.7,1,1)
        _EdgeColor ("Edge Color", Color) = (0.6,0.9,1,1)
        _FresnelPower ("Fresnel", Float) = 3
        _EdgeSoftness ("Edge Softness", Float) = 1.5
        _PulseSpeed ("Pulse Speed", Float) = 2
        _PulseStrength ("Pulse Strength", Float) = 0.2
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            float4 _CoreColor;
            float4 _EdgeColor;
            float _FresnelPower;
            float _EdgeSoftness;
            float _PulseSpeed;
            float _PulseStrength;

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionHCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 viewDir = normalize(GetCameraPositionWS() - i.positionWS);
                float ndv = saturate(dot(normalize(i.normalWS), viewDir));

                // Fresnel 控制边缘
                float fresnel = pow(1 - ndv, _FresnelPower);

                // 中心 = 实体感（dim）
                float coreMask = 1 - fresnel;

                // pulse 只影响一点点（不要破坏稳定）
                float pulse = (sin(_Time.y * _PulseSpeed) * 0.5 + 0.5) * _PulseStrength;

                // 颜色
                float3 col = lerp(_CoreColor.rgb, _EdgeColor.rgb, fresnel);

                // 关键：alpha 从中心到边缘渐变
                float alpha = saturate(coreMask * 0.8 + pulse - fresnel * _EdgeSoftness);

                return float4(col, alpha);
            }

            ENDHLSL
        }
    }
}