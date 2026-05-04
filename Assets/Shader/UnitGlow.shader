// Simple additive-blend emissive shader for geometric art pieces.
// Works on any mesh (sphere, cylinder, cube, etc.).
// Fresnel rim brightening makes silhouette edges glow more intensely,
// giving flat primitives apparent depth without complex lighting.
Shader "Custom/UnitGlow"
{
    Properties
    {
        _Color      ("Color",         Color)  = (0.35, 0.92, 1.0, 1)
        _Intensity  ("Intensity",     Float)  = 2.8
        _RimBoost   ("Rim Boost",     Float)  = 0.50   // extra glow at silhouette edges
        _RimPower   ("Rim Power",     Float)  = 1.8
        _PulseSpeed ("Pulse Speed",   Float)  = 1.6
        _PulseDepth ("Pulse Depth",   Float)  = 0.18
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+2" }

        Pass
        {
            Blend SrcAlpha One      // additive — always brightens, never darkens
            ZWrite Off
            Cull Off                // render both faces so thin rings look solid

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 pos : POSITION; float3 nrm : NORMAL; };
            struct V { float4 cs  : SV_POSITION; float3 nW : TEXCOORD0; float3 pW : TEXCOORD1; };

            float4 _Color;
            float  _Intensity, _RimBoost, _RimPower, _PulseSpeed, _PulseDepth;

            V vert(A v)
            {
                V o;
                o.pW = TransformObjectToWorld(v.pos.xyz);
                o.cs = TransformWorldToHClip(o.pW);
                o.nW = TransformObjectToWorldNormal(v.nrm);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 N   = normalize(i.nW);
                float3 Vd  = normalize(GetCameraPositionWS() - i.pW);
                float  ndv = saturate(abs(dot(N, Vd)));   // 1 = face-on, 0 = silhouette

                // Fresnel rim: silhouette edges are brighter
                float rim  = pow(1.0 - ndv, _RimPower) * _RimBoost + 1.0;

                float pulse = 1.0 - _PulseDepth + _PulseDepth * sin(_Time.y * _PulseSpeed);

                return half4(_Color.rgb * _Intensity * rim * pulse,
                             _Color.a * pulse);
            }
            ENDHLSL
        }
    }
}
