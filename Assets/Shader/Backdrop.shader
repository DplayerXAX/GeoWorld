Shader "GeoWorld/Backdrop"
{
    // Far scenery around the play area (EnvironmentBackdrop): cliffs, ruins,
    // floating islands, read as SILHOUETTES in the air, not as objects to look at.
    // Unlit: a flat colour, a touch lighter on faces turned up toward the sky, sinking
    // into _HazeColor with distance from the camera and toward its own base.
    Properties
    {
        _Color      ("Colour",         Color) = (0.35, 0.38, 0.42, 1)
        _HazeColor  ("Haze colour",    Color) = (0.7, 0.74, 0.78, 1)
        _HazeStart  ("Haze start",     Float) = 30
        _HazeRange  ("Haze range",     Float) = 120
        _HazeMax    ("Haze max",       Range(0, 1)) = 0.85
        _BaseY      ("Base fade from (world Y)", Float) = -20
        _BaseRange  ("Base fade over", Float) = 25
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _HazeColor;
                float  _HazeStart;
                float  _HazeRange;
                float  _HazeMax;
                float  _BaseY;
                float  _BaseRange;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 c = _Color.rgb * (0.85 + 0.25 * saturate(N.y) + 0.06 * N.x);

                float dist = distance(IN.positionWS, _WorldSpaceCameraPos);
                float haze = saturate((dist - _HazeStart) / max(_HazeRange, 1e-3)) * _HazeMax;
                float base = 1.0 - saturate((IN.positionWS.y - _BaseY) / max(_BaseRange, 1e-3));
                haze = saturate(max(haze, base * 0.95));

                return half4(lerp(c, _HazeColor.rgb, haze), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
