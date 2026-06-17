// Dead-simple flat-color skybox. Use it for the "white" skybox state.
// Assign to a Material, set _Tint (default near-paper white), drop into TitleShaderSwap.skyboxSwapMat
// (or Window ▸ Rendering ▸ Lighting ▸ Environment ▸ Skybox Material).
Shader "Custom/FlatColorSkybox"
{
    Properties
    {
        _Tint ("Tint", Color) = (0.949, 0.937, 0.902, 1)   // GeoPalette paper
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            half4 _Tint;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target { return _Tint; }
            ENDHLSL
        }
    }
}
