#ifndef GEOWORLD_MAP_OCCLUSION_INCLUDED
#define GEOWORLD_MAP_OCCLUSION_INCLUDED

// See-through round the level-select pawn (LevelMapController.Occlusion).
//
// Anything standing between the camera and the pawn is cut away inside a cone
// from the camera to the pawn: the same circle on screen at every depth, fully
// clear in the middle and dithered back in toward its rim. Only what is
// (a) nearer the camera than the pawn, by a margin, and
// (b) higher than the pawn's floor
// is cut — so the pawn, the path it stands on and the ground around it all
// stay, and it is the tree crowns, roofs and ledges in front that open up.
//
// Globals (never in a material CBUFFER, so the SRP Batcher is untouched):
//   _MapOcclude      xyz: the pawn's body (world)   w: strength, 0 = off
//   _MapOccludeShape x: clear radius  y: outer radius (world units, measured at
//                    the pawn's distance)  z: floor height (world y)
//                    w: margin — how far in front of the pawn the cut starts,
//                       and the height over which it fades in above the floor
// Everything else (gameplay, other scenes) sees w = 0 and returns at once.
float4 _MapOcclude;
float4 _MapOccludeShape;

void MapOccludeClip(float4 positionCS)
{
    if (_MapOcclude.w <= 0.001) return;

    // World position of this very fragment, from its own screen position and
    // depth — works the same in the colour, depth and depth-normals passes, so
    // all of them cut the same pixels (a depth prepass that disagreed would
    // leave a hole in the depth buffer, or a phantom in front of the pawn).
    float2 uv = positionCS.xy / _ScaledScreenParams.xy;
    float3 ws = ComputeWorldSpacePosition(uv, positionCS.z, UNITY_MATRIX_I_VP);

    float3 cam = _WorldSpaceCameraPos;
    float3 toP = _MapOcclude.xyz - cam;
    float  L   = length(toP);
    if (L < 1e-3) return;
    float3 dir = toP / L;
    float3 toW = ws - cam;
    float  t   = dot(toW, dir);
    float  front = L - _MapOccludeShape.w;
    if (t <= 0.0 || t >= front) return;

    // Distance from the camera→pawn line, scaled back to the pawn's distance:
    // a cone, i.e. one steady circle on screen.
    float perp = length(toW - dir * t);
    float r    = perp * L / max(t, 1e-3);

    float soft = max(_MapOccludeShape.w, 1e-3);
    float fade = 1.0 - smoothstep(_MapOccludeShape.x, _MapOccludeShape.y, r);
    fade *= smoothstep(_MapOccludeShape.z, _MapOccludeShape.z + soft, ws.y);
    fade *= saturate((front - t) / soft);
    fade *= _MapOcclude.w;
    if (fade <= 0.0) return;

    // Screen-door: interleaved gradient noise, stable per pixel.
    float ign = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
    clip(fade >= 0.999 ? -1.0 : ign - fade);
}

#endif
