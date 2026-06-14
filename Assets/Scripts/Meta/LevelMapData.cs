using System;
using System.Collections.Generic;
using UnityEngine;

// Serializable layout of a level-select map: the placed blocks (geometry authored
// with the real placement tool) plus per-block level metadata. Saved/loaded as
// JSON by LevelMapIO. A node with an empty levelId is a path stepping-stone.
[Serializable]
public class LevelMapData
{
    public int    version = 1;
    public string name    = "map";
    public List<LevelMapNode> nodes = new();
}

[Serializable]
public class LevelMapNode
{
    public Vector3Int[] cells;            // absolute grid cells this block occupies
    public Color        color = Color.white;
    public string       blockTypeName;    // kept for future edit-reload; unused at runtime
    public string       levelId;          // empty = waypoint / path stone
    public bool         isStart;
}
