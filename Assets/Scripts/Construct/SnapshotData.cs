using System;
using System.Collections.Generic;
using UnityEngine;

// Data classes for grid snapshots. Bump `version` whenever the shape changes
// so older saves can be migrated rather than silently misread.

[Serializable]
public class BlockSnapshot
{
    public string       blockTypeName;   // BlockType enum name, e.g. "Home"
    public Color        color;
    public Quaternion   rotation;
    public Vector3Int[] occupiedCells;
}

[Serializable]
public class EndpointSnapshot
{
    public Vector3Int cell;
    public bool       isStart;
}

[Serializable]
public class CameraSnapshot
{
    public Vector3 focusPoint;
    public float   distance;
    public float   yaw;
    public float   pitch;
}

[Serializable]
public class GridSnapshot
{
    public int    version = 1;
    public string name;
    public string timestamp;     // ISO-ish: "2026-05-19_14-30-22"
    public int    roundIndex;
    public int    runsSinceLastEndpoint;

    public List<BlockSnapshot>    blocks    = new();
    public List<EndpointSnapshot> endpoints = new();
    public CameraSnapshot         camera    = new();
}
