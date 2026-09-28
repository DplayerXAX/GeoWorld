Shader "GeoWorld/SoftDot"
{
    // A soft round particle — rain splashes, and the environment's drifting motes
    // (dust, pollen, snow, embers). Alpha-blended or additive via _SrcBlend/_DstBlend
    // (set by LevelEnvironmentDriver). Colour = _Color × the particle's vertex colour.
    Properties
    {
        _Color    ("Colour", Color) = (1, 1, 1, 1)
        _Softness ("Edge softness", Range(0.05, 1)) = 0.6
        [HideInInspector] _SrcBlend ("Src", Float) = 5    // SrcAlpha
        [HideInInspector] _DstBlend ("Dst", Float) = 10   // OneMinusSrcAlpha
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float  _Softness;
                float  _SrcBlend;
                float  _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                o.color = IN.color;
                o.uv = IN.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float r = length(IN.uv * 2.0 - 1.0);
                float a = 1.0 - smoothstep(1.0 - _Softness, 1.0, r);
                return half4(_Color.rgb * IN.color.rgb, _Color.a * IN.color.a * a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
