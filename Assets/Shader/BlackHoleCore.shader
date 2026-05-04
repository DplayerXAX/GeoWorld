// Glowing plasma sphere: coloured inner core + accretion disk ring + flowing streams.
//
// Single additive pass — no dark centre.  The sphere adds light everywhere:
//   • Centre : faint _DiskColor inner glow (_CoreGlow controls brightness)
//   • Mid    : sharp bright ring (_DiskColor) + flowing plasma streams (_StreamColor)
//   • Edge   : soft nebula haze (_HazeColor)
//
// Streams use three overlapping directional sine waves on the world-space normal
// — seamless (no UV seam) and continuously animated.
Shader "Custom/BlackHoleCore"
{
    Properties
    {
        [Header(Colors)]
        _DiskColor    ("Disk Ring Color",      Color)  = (1.0, 0.55, 0.12, 1)
        _StreamColor  ("Flow Stream Color",    Color)  = (1.0, 0.85, 0.30, 1)
        _HazeColor    ("Outer Haze Color",     Color)  = (0.8, 0.25, 1.00, 1)

        [Header(Inner Core Glow)]
        _CoreGlow     ("Core Brightness",      Float)  = 0.35
        _CorePower    ("Core Falloff",         Float)  = 1.6

        [Header(Disk Ring)]
        _DiskRadius   ("Ring Position",        Float)  = 0.60
        _DiskWidth    ("Ring Half-Width",      Float)  = 0.15
        _DiskSharp    ("Ring Sharpness",       Float)  = 5.0

        [Header(Flowing Streams)]
        _FlowSpeed    ("Flow Speed",           Float)  = 0.85
        _FlowScale    ("Flow Frequency",       Float)  = 4.2
        _FlowStrength ("Stream Brightness",    Float)  = 0.55
        _FlowContrast ("Stream Contrast",      Float)  = 2.8

        [Header(Outer Haze)]
        _HazePower    ("Haze Falloff",         Float)  = 1.5
        _HazeScale    ("Haze Brightness",      Float)  = 0.40

        [Header(Global)]
        _Intensity    ("Glow Intensity",       Float)  = 6.5
        _PulseSpeed   ("Pulse Speed",          Float)  = 2.2
        _PulseDepth   ("Pulse Depth",          Float)  = 0.28
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+1" }

        Pass
        {
            Name "PlasmaGlow"
            Blend SrcAlpha One      // purely additive — sphere adds light, never blacks out
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct A { float4 pos : POSITION; float3 nrm : NORMAL; };
            struct V { float4 cs : SV_POSITION; float3 nW : TEXCOORD0; float3 pW : TEXCOORD1; };

            float4 _DiskColor, _StreamColor, _HazeColor;
            float  _CoreGlow, _CorePower;
            float  _DiskRadius, _DiskWidth, _DiskSharp;
            float  _FlowSpeed, _FlowScale, _FlowStrength, _FlowContrast;
            float  _HazePower, _HazeScale;
            float  _Intensity, _PulseSpeed, _PulseDepth;

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
                float  ndv = abs(dot(N, Vd));   // 1 = centre of sphere, 0 = silhouette
                float  rim = 1.0 - ndv;

                // ── Inner core glow (centre) ──────────────────────────────────
                // Warm/cool colour at the centre, falls off toward the edge.
                float core = pow(ndv, _CorePower) * _CoreGlow;

                // ── Disk ring ─────────────────────────────────────────────────
                float d    = abs(rim - _DiskRadius) / max(_DiskWidth * 0.5, 0.001);
                float disk = pow(saturate(1.0 - d), _DiskSharp);

                // ── Flowing plasma streams ────────────────────────────────────
                // Three overlapping directional waves on world-space N — no UV seam.
                float t  = _Time.y * _FlowSpeed;
                float w1 = sin(dot(N, float3( 2.831,  1.713,  3.141)) * _FlowScale + t * 1.00);
                float w2 = sin(dot(N, float3(-1.907,  3.284, -2.376)) * _FlowScale + t * 0.71);
                float w3 = sin(dot(N, float3( 3.517, -2.063,  1.841)) * _FlowScale + t * 1.29);
                float flow = w1 * 0.50 + w2 * 0.33 + w3 * 0.17;           // [-1, 1]
                flow = pow(saturate(flow * 0.5 + 0.5), _FlowContrast);     // [0, 1], sharpened

                // Streams cover the whole sphere; fade only at the extreme silhouette
                // so they don't bleed into empty space around the ball.
                float edgeFade = pow(saturate(1.05 - rim), 0.6);
                float streams  = flow * edgeFade * _FlowStrength;

                // ── Outer nebula haze ─────────────────────────────────────────
                float haze = pow(saturate(rim - 0.02), _HazePower) * _HazeScale;

                // ── Heartbeat pulse ───────────────────────────────────────────
                float pulse = 1.0 - _PulseDepth + _PulseDepth * sin(_Time.y * _PulseSpeed);

                // ── Composite ────────────────────────────────────────────────
                float3 col = _DiskColor.rgb   * (core + disk)
                           + _StreamColor.rgb * streams
                           + _HazeColor.rgb   * haze;

                float a = saturate(core * 0.55 + disk * 0.90
                                 + streams * 0.70 + haze * 0.45) * pulse;

                return half4(col * _Intensity * pulse, a);
            }
            ENDHLSL
        }
    }
}
