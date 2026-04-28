using UnityEngine;
using System.Collections.Generic;
using UnityEngine;


public enum ChordFunction
{
    Tonic,
    Subdominant,
    Dominant
}

[CreateAssetMenu(menuName = "Game/Block")]
public class BlockData : ScriptableObject
{
    [Header("Shape")]
    public Vector3Int[] cells;

    [Header("Placement")]
    public bool isSolid;

    [Header("Music Function")]
    public ChordFunction function;

    [Header("Chord Pool (scale-degree based)")]
    public List<ChordData> chords;

    public GameObject selfRef;

}