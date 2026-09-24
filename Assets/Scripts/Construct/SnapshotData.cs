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

    // ── v2 ───────────────────────────────────────────────────────────────────
    // A snapshot is no longer only a keepsake: the next level in a chapter is
    // BUILT from it (LevelDefinition.inheritFrom). That turns three omissions from
    // cosmetic into real bugs, so they are stored now.

    // The exact BlockData asset. v1 resolved blocks by BlockType alone, and when two
    // assets share a type that silently returns whichever comes first — right
    // shape (it comes from the cells), wrong block behind it.
    public string blockAssetName;

    // The synergy tag. v1 kept only the DISPLAY colour, so a restored board came
    // back with every piece untagged and not one synergy able to form.
    public BlockColor synergyColor;

    // Turret upgrade levels, so an inherited turret is the turret you left.
    public int upBasicPower, upBasicBurst, upAoeFire, upAoeGravity;
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
    public int    version = 2;   // 2: BlockSnapshot gained asset name, synergy tag, upgrades
    public string name;
    public string timestamp;     // ISO-ish: "2026-05-19_14-30-22"
    public int    roundIndex;
    public int    runsSinceLastEndpoint;

    public List<BlockSnapshot>    blocks    = new();
    public List<EndpointSnapshot> endpoints = new();
    public CameraSnapshot         camera    = new();
}
