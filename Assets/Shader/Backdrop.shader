Shader "GeoWorld/Backdrop"
{
    // Far scenery around the play area (EnvironmentBackdrop): cliffs, ruins,
    // floating islands, read as SILHOUETTES in the air, not as objects to look at.
    // Unlit: a flat colour, a touch lighter on faces turned up toward the sky, sinking
    // into _HazeColor with distance from the camera and toward its own base.
    //
    // Three optional lights (black = off):
    //   _SunGlow — the haze warms toward this colour where you look toward the main
    //              light, so a low sun smears across the air behind the scenery.
    //   _Rim     — edges turned toward the main light catch it.
    //   _Accent  — luminous flecks on up-facing surfaces (moss, lichen), barely
    //              hazed, so they still read far off — the bright-on-dark contrast.
    //
    // _GreenAmount (0..1, per renderer): a grass skin grows over the surface — first
    // on the gentlest, sunniest ground and in patches, then across the slopes until
    // only the steepest rock shows — two greens mottled together, with flowers
    // scattered through it (_Flower / _Flower2).
    //
    // Ripening (vertex-coloured land, _VertexColor on): the vertex colour is the
    // dry colour. TEXCOORD2 carries the ripe colour (rgb) and when that vertex
    // turns, in seconds after the bloom (w). _RipenT is the bloom's clock, and -1
    // means not yet. Each vertex eases across over 1.4 s once the clock passes it,
    // so a whole landscape ripens for the cost of one float a frame.
    Properties
    {
        _Color      ("Colour",         Color) = (0.35, 0.38, 0.42, 1)
        _HazeColor  ("Haze colour",    Color) = (0.7, 0.74, 0.78, 1)
        _HazeStart  ("Haze start",     Float) = 30
        _HazeRange  ("Haze range",     Float) = 120
        _HazeMax    ("Haze max",       Range(0, 1)) = 0.85
        _BaseY      ("Base fade from (world Y)", Float) = -20
        _BaseRange  ("Base fade over", Float) = 25
        [HDR] _SunGlow ("Sun glow in the haze", Color) = (0, 0, 0, 1)
        [HDR] _Rim     ("Sun-side rim", Color) = (0, 0, 0, 1)
        [HDR] _Accent  ("Luminous accents", Color) = (0, 0, 0, 1)
        _AccentAmount ("Accent amount", Range(0, 1)) = 0
        _Grass    ("Grass",        Color) = (0.30, 0.52, 0.22, 1)
        _Grass2   ("Grass, light", Color) = (0.58, 0.74, 0.30, 1)
        _Flower   ("Flowers",      Color) = (1, 0.6, 0.25, 1)
        _Flower2  ("Flowers, 2",   Color) = (1, 0.9, 0.6, 1)
        _GreenAmount ("Grass cover", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle] _VertexColor ("Colour from vertices", Float) = 0
        _RipenT ("Ripen clock (s since the bloom, -1 = dry)", Float) = -1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZWrite On
            Cull [_Cull]

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
                float4 _SunGlow;
                float4 _Rim;
                float4 _Accent;
                float  _AccentAmount;
                float4 _Grass;
                float4 _Grass2;
                float4 _Flower;
                float4 _Flower2;
                float  _GreenAmount;
                float  _VertexColor;
                float  _RipenT;
            CBUFFER_END

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 19.19);
                return frac((p.x + p.y) * p.z);
            }
            float vnoise(float3 x)
            {
                float3 i = floor(x), f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(hash13(i),               hash13(i + float3(1,0,0)), f.x),
                                 lerp(hash13(i + float3(0,1,0)), hash13(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(hash13(i + float3(0,0,1)), hash13(i + float3(1,0,1)), f.x),
                                 lerp(hash13(i + float3(0,1,1)), hash13(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; float4 ripe : TEXCOORD2; };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float4 color : COLOR; float4 ripe : TEXCOORD2; };

            Varyings vert(Attributes IN)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS   = TransformObjectToWorldNormal(IN.normalOS);
                o.color      = IN.color;
                o.ripe       = IN.ripe;
                return o;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Guarded: a mesh squashed flat has a degenerate normal, and a NaN here
                // becomes a white bloom flash on screen.
                float3 N = IN.normalWS;
                N = dot(N, N) > 1e-10 ? normalize(N) : float3(0, 1, 0);
                float3 baseCol = _VertexColor > 0.5 ? IN.color.rgb : _Color.rgb;
                if (_VertexColor > 0.5 && _RipenT > 0.0)
                {
                    float k = saturate((_RipenT - IN.ripe.w) / 1.4);
                    baseCol = lerp(baseCol, IN.ripe.rgb, k * k * (3.0 - 2.0 * k));
                }
                float3 c = baseCol * (0.85 + 0.25 * saturate(N.y) + 0.06 * N.x);

                // Grass skin. Score = how grass-friendly this spot is: flat and facing
                // up, plus patchy noise; the cover threshold drops as _GreenAmount grows.
                if (_GreenAmount > 0.001)
                {
                    float3 wp  = IN.positionWS;
                    float  up  = saturate(N.y * 1.25 + 0.15);
                    float  pat = vnoise(wp * 0.16) * 0.6 + vnoise(wp * 0.55) * 0.4;
                    float  score = up * 0.6 + pat * 0.5;
                    float  th    = 1.05 - _GreenAmount * 0.95;
                    float  m     = smoothstep(th, th + 0.1, score);

                    float  mott  = vnoise(wp * 1.1 + 7.3);
                    float3 grass = lerp(_Grass.rgb, _Grass2.rgb, smoothstep(0.3, 0.75, mott))
                                 * (0.8 + 0.3 * saturate(N.y));
                    c = lerp(c, grass, m);

                    // Flowers through the grass, thicker where it's lushest.
                    float fl = vnoise(wp * 2.4 + 3.1);
                    float bloom = smoothstep(0.8, 0.86, fl) * m * saturate(_GreenAmount * 1.4 - 0.3);
                    float3 fc = lerp(_Flower.rgb, _Flower2.rgb, step(0.5, vnoise(wp * 0.9 + 11.0)));
                    c = lerp(c, fc, bloom);
                }

                float3 toCam = _WorldSpaceCameraPos - IN.positionWS;
                float  dist  = length(toCam);
                float3 V     = toCam / max(dist, 1e-3);
                float3 L     = normalize(_MainLightPosition.xyz);   // toward the light

                // Rim: grazing edges on the side the light comes from.
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0) * saturate(dot(N, L) * 0.6 + 0.4);
                c += _Rim.rgb * rim;

                // Haze, warmed where the view looks into the light.
                float toSun = saturate(dot(-V, L));
                float3 hazeCol = _HazeColor.rgb + _SunGlow.rgb * (pow(toSun, 5.0) * 0.8 + pow(toSun, 24.0) * 0.6);

                float haze = saturate((dist - _HazeStart) / max(_HazeRange, 1e-3)) * _HazeMax;
                float base = 1.0 - saturate((IN.positionWS.y - _BaseY) / max(_BaseRange, 1e-3));
                haze = saturate(max(haze, base * 0.95));
                float3 col = lerp(c, hazeCol, haze);

                // Luminous flecks on top faces, only lightly hazed.
                if (_AccentAmount > 0.001)
                {
                    float n = vnoise(IN.positionWS * 0.45) * 0.65 + vnoise(IN.positionWS * 1.7) * 0.35;
                    float up = saturate(N.y * 1.6 - 0.3) + 0.25 * saturate(1.0 - abs(N.y));   // tops, a little on the sides
                    float fleck = smoothstep(1.0 - _AccentAmount * 0.55, 1.02 - _AccentAmount * 0.5, n) * up;
                    col += _Accent.rgb * fleck * (1.0 - haze * 0.55) * (1.0 - base);
                }

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
