Shader "GeoWorld/LightShaft"
{
    // A beam of light: lit air, not a solid. Additive and soft all over. It is
    // brightest at the lamp and fades along its length (uv.y, 0 at the lamp,
    // 1 at the far end). It fades out toward its silhouette, where the view runs
    // along the surface, so the cone has no hard outline. Motes drift slowly in
    // it. Drawn both sides, so the middle of the beam, where both walls overlap,
    // is the brightest, as a real shaft is.
    Properties
    {
        [HDR] _Color ("Colour", Color) = (1.2, 1.1, 0.8, 1)
        _Falloff  ("Falloff along the beam", Range(0.3, 4)) = 1.6
        _EdgeSoft ("Edge softness", Range(0.5, 6)) = 2.2
        _Shimmer  ("Motes in the beam", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
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
                float  _Falloff;
                float  _EdgeSoft;
                float  _Shimmer;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float2 uv : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                o.uv         = IN.uv;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - IN.positionWS);
                float3 N = IN.normalWS;
                N = dot(N, N) > 1e-8 ? normalize(N) : V;
                float face  = pow(saturate(abs(dot(N, V))), _EdgeSoft);
                float along = saturate(IN.uv.y);
                float fade  = pow(1.0 - along, _Falloff) * smoothstep(0.0, 0.05, along);
                float3 p = IN.positionWS;
                float motes = 1.0 + _Shimmer * sin(p.x * 3.1 + _Time.y * 1.7) * sin(p.z * 2.7 - _Time.y * 1.3) * sin(p.y * 4.3 + _Time.y);
                return half4(_Color.rgb * (face * fade * motes), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
