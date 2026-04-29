using UnityEngine;

[CreateAssetMenu(menuName = "Music/Chord")]
public class ChordData : ScriptableObject
{
    public string chordName;

    public int[] notes; // e.g. C major = 60, 64, 67

    //public ChordFunction function;

    public AK.Wwise.Event myChord;
    public float tension;
}
