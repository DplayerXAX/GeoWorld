using UnityEngine;

public class ArpeggiatorManager : MonoBehaviour
{
    public static ArpeggiatorManager Instance;

    // Chord pools for each block type (semitones from C4).
    // Each chord has multiple tones so different cells of a block map to
    // different notes — the block's shape becomes a melodic phrase rather
    // than a fixed 4-note burst.
    //
    //   Home   = I   (stable, resolves)
    //   Lift   = IV  (rising)
    //   Pull   = V   (tension)
    //   Shadow = vi  (color / minor)
    static readonly int[] Chord_Home   = { 0,  4,  7, 12, 16, 19 }; // C  E  G  C5 E5 G5
    static readonly int[] Chord_Lift   = { 5,  9, 12, 17, 21, 24 }; // F  A  C5 F5 A5 C6
    static readonly int[] Chord_Pull   = { 7, 11, 14, 19, 23, 26 }; // G  B  D5 G5 B5 D6
    static readonly int[] Chord_Shadow = { -3, 0,  4,  9, 12, 16 }; // A3 C  E  A  C5 E5

    void Awake() => Instance = this;

    // Called once per cell-step from SurfaceUnit. One note per beat → a
    // continuous melodic flow synced to the unit's walk.
    //
    //   local: cell coordinates relative to the block's anchor (min) cell.
    //          For an L-shape spanning (0,0,0)→(2,0,0)→(2,1,0) the local
    //          values [0, 1, 2, (2,1,0)] pick different chord tones in turn.
    //
    // Mapping (kept entirely inside the chord pool — no octave jumps that
    // sound dissonant when Wwise pitch-shifts beyond the sample's natural range):
    //   step index = |x| + |z| + y*2 → chord-tone slot
    //   higher y still reaches higher tones because the pool spans ~2 octaves,
    //   but the pitch shift stays within musically safe bounds.
    public void PlayCellNote(BlockType type, Vector3Int local)
    {
        int[] chord = GetChord(type);
        if (chord == null || chord.Length == 0) return;

        int step     = Mathf.Abs(local.x) + Mathf.Abs(local.z) + Mathf.Abs(local.y) * 2;
        int idx      = step % chord.Length;
        int semitone = chord[idx];

        AudioManager.Instance.PlayArpNote(semitone);
        BackgroundReactor.Instance?.OnNote(0.7f);
    }

    // Kept so SurfaceUnit.OnPathFinished still compiles. No looping coroutines anymore.
    public void StopArp() { }

    static int[] GetChord(BlockType t) => t switch
    {
        BlockType.Home   => Chord_Home,
        BlockType.Lift   => Chord_Lift,
        BlockType.Pull   => Chord_Pull,
        BlockType.Shadow => Chord_Shadow,
        _                => Chord_Home,
    };
}
