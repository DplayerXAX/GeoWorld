Shader "Custom/ScreenSpaceThickOutline"
{
    Properties
    {
        [HDR] _OutlineColor("Outline Color", Color) = (0, 0.8, 1, 1)
        _OutlineWidth("Outline Pixels Width", Range(0, 30)) = 5.0
    }
        SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        LOD 100

        // ------------------------------------------------------------
        // PASS 0: 纯白方块剪影
        // ------------------------------------------------------------
        Pass
        {
            Name "DrawSilhouette"
            Cull Off
            ZWrite Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                return float4(1, 1, 1, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------
        // PASS 1: 全屏 8方向采样膨胀（无视天空盒深度干扰）
        // ------------------------------------------------------------
        Pass
        {
            Name "ExpandAndOutline"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest Always // 强制关闭任何几何深度测试

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { uint vertexID : SV_VertexID; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Texture2D _SilhouetteTex;
            SamplerState sampler_SilhouetteTex;

            float4 _OutlineColor;
            float _OutlineWidth;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.uv = float2((input.vertexID << 1) & 2, input.vertexID & 2);
                output.positionCS = float4(output.uv * 2.0 - 1.0, 0.0, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                output.uv.y = 1.0 - output.uv.y;
                #endif
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float2 texelSize = _ScreenSize.zw * _OutlineWidth;

                float center = _SilhouetteTex.Sample(sampler_SilhouetteTex, uv).r;

                // 如果在物体内部，保持完全透明，完美暴露出你本来的网格
                if (center > 0.5) return float4(0, 0, 0, 0);

                // 8方向采样边缘
                float edge = 0.0;
                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv + float2(texelSize.x, 0)).r;
                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv - float2(texelSize.x, 0)).r;
                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv + float2(0, texelSize.y)).r;
                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv - float2(0, texelSize.y)).r;

                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv + float2(texelSize.x, texelSize.y)).r;
                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv + float2(-texelSize.x, texelSize.y)).r;
                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv + float2(texelSize.x, -texelSize.y)).r;
                edge += _SilhouetteTex.Sample(sampler_SilhouetteTex, uv + float2(-texelSize.x, -texelSize.y)).r;

                if (edge > 0.1)
                {
                    return _OutlineColor;
                }

                return float4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}