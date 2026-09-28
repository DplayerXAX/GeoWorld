Shader "GeoWorld/RainStreak"
{
    // Rain drops as stretched particle billboards (LevelEnvironmentDriver): a thin
    // streak, brightest at the head, fading along its tail. Alpha-blended in the
    // environment's rain colour × the particle's vertex colour.
    Properties
    {
        _Color ("Rain colour", Color) = (0.78, 0.84, 0.92, 0.35)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend SrcAlpha OneMinusSrcAlpha
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
                // Which UV axis runs ALONG the streak depends on how the stretched
                // billboard lays its quad out, so find it: the long axis changes
                // least per screen pixel.
                float gu = length(float2(ddx(IN.uv.x), ddy(IN.uv.x)));
                float gv = length(float2(ddx(IN.uv.y), ddy(IN.uv.y)));
                float2 t = gu < gv ? IN.uv.yx : IN.uv.xy;                // t.x across, t.y along
                float across = 1.0 - abs(t.x * 2.0 - 1.0);                 // thin in the middle
                float along  = sin(3.14159 * saturate(t.y));               // soft at both ends
                float a = _Color.a * IN.color.a * across * across * along;
                return half4(_Color.rgb * IN.color.rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
