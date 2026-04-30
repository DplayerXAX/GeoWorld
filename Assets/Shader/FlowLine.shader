Shader "Custom/EnergyFlow"
{
    Properties
    {
        _BaseMap ("Texture", 2D) = "white" {}
        _Color ("Color", Color) = (0.3,0.8,1,1)
        _Intensity ("Intensity", Float) = 2
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            sampler2D _BaseMap;
            float4 _Color;
            float _Intensity;

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.positionHCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float tex = tex2D(_BaseMap, i.uv).r;

                float glow = pow(tex, 2) * _Intensity;

                return float4(_Color.rgb * glow, glow);
            }

            ENDHLSL
        }
    }
}