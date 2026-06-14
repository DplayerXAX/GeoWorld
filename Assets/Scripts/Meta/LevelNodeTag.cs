using UnityEngine;

// Authoring marker placed on a block while you build a map with the real
// PlacementController. Records which level the block launches (and whether it's
// the pawn's start). LevelMapIO reads these on save; not used in LevelSelect.
[DisallowMultipleComponent]
public class LevelNodeTag : MonoBehaviour
{
    public LevelDefinition level;   // null = path stepping-stone
    public bool isStart;
}
