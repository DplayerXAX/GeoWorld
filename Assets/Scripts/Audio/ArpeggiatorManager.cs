using System.Collections.Generic;
using UnityEngine;

public class ArpeggiatorManager : MonoBehaviour
{
    public static ArpeggiatorManager Instance;

    // NoteEvent: degree 1–7 = C Dorian scale (C D Eb F G A Bb)
    //            degree  0  = rest / silence
    //            octave  0  = middle register; -1 lower; +1 higher
    public struct NoteEvent
    {
        public int   degree;   // 1–7; 0 = rest
        public int   octave;
        public float tension;  // 0–1, used by BackgroundReactor
    }

    // ── Melody state (reset each new path traversal) ─────────────────────
    class MelodyState
    {
        public int   lastDeg = 5;    // G (degree 5), comfortable mid-range start
        public int   lastOct = 0;
        public int   hDir    = 1;
        public float zigzag  = 0f;
        public float vertDir = 0f;

        // Linear scalar across octaves: (degree-1) + octave*7
        public int Scalar => (lastDeg - 1) + lastOct * 7;
    }

    MelodyState state = new();

    // C Dorian — one octave, semitone offsets (used only for reference;
    // PlayArpNote receives degree+octave directly, not semitones).
    // Degree:   1   2   3   4   5   6   7
    //           C   D   Eb  F   G   A   Bb
    public static readonly int[] Scale = { 0, 2, 3, 5, 7, 9, 10 };

    [Header("Feel")]
    [Range(0f, 1f)] public float noteDensity = 0.75f;
    [Range(0f, 1f)] public float repetition  = 0.25f;

    readonly List<NoteEvent> _rec = new();
    bool _recording;

    void Awake() => Instance = this;

    // ─────────────────────────────────────────────────────────────────────
    // Recording API
    // ─────────────────────────────────────────────────────────────────────
    public void StartRecording()
    {
        _rec.Clear();
        state = new MelodyState();
        _recording = true;
    }

    public List<NoteEvent> StopRecording()
    {
        _recording = false;
        var copy = new List<NoteEvent>(_rec);
        _rec.Clear();
        return copy;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Melody note (one per path step)
    // velocityScale < 1 → quieter (used when unit is looping)
    // ─────────────────────────────────────────────────────────────────────
    // isLoop = true when called from a looping SurfaceUnit — plays audio but
    // does NOT write into the recording (prevents contaminating the live run).
    public void PlayMelodyNote(
        BlockType type, FaceNode node, FaceNode prevNode,
        float progress, int pathIndex, float velocityScale = 1f, bool isLoop = false)
    {
        float density = noteDensity * (0.45f + 0.55f * Mathf.Sin(progress * Mathf.PI));
        if (Random.value > density)
        {
            if (_recording && !isLoop) _rec.Add(new NoteEvent { degree = 0 });
            return;
        }

        UpdateState(node, prevNode);
        NoteEvent ne = PickEvent(type);

        // Arc: push melody up/down by scale degrees at the path's midpoint.
        int arcDeg = Mathf.RoundToInt(Mathf.Sin(progress * Mathf.PI) * 2f);
        int sc     = (ne.degree - 1) + ne.octave * 7 + arcDeg;
        sc         = Mathf.Clamp(sc, -7, 20);          // oct -1 to +2
        ne.octave  = Mathf.FloorToInt((float)sc / 7);
        ne.degree  = sc - ne.octave * 7 + 1;           // back to 1-indexed

        // Tonic resolution on the final step — clean cadence every time.
        if (progress >= 0.97f) { ne.degree = 1; ne.octave = 0; }

        float vel = Mathf.Lerp(0.38f, 0.82f, progress) * velocityScale;

        if (_recording && !isLoop) _rec.Add(ne);

        AudioManager.Instance.PlayArpNote(ne.degree, ne.octave, vel);
        BackgroundReactor.Instance?.OnNoteDetailed(ne.degree, ne.octave, vel, ne.tension, type);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Bass root (triggered once per block-type change)
    // ─────────────────────────────────────────────────────────────────────
    public void PlayBassRoot(BlockType type)
    {
        int deg = type switch
        {
            BlockType.Home   => 1,  // C
            BlockType.Lift   => 4,  // F
            BlockType.Pull   => 5,  // G
            BlockType.Shadow => 7,  // Bb
            _                => 1,
        };
        AudioManager.Instance.PlayArpNote(deg, -1, 0.4f);
        BackgroundReactor.Instance?.OnNote(0.4f);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Ambient note (used by LoopManager and PathPulseScan)
    // ─────────────────────────────────────────────────────────────────────
    public void PlayAmbientNote(int degree, int octave, float velocity)
        => AudioManager.Instance.PlayArpNote(degree, octave, velocity);

    public void StopArp() { }

    // ─────────────────────────────────────────────────────────────────────
    // State update — reads path geometry to shape the melody
    // ─────────────────────────────────────────────────────────────────────
    void UpdateState(FaceNode cur, FaceNode prev)
    {
        if (prev == null) return;

        Vector3 d = cur.worldPos - prev.worldPos;

        int newH = 0;
        if (Mathf.Abs(d.x) > Mathf.Abs(d.z))
            newH = d.x > 0 ? 1 : -1;
        else if (Mathf.Abs(d.z) > 0.05f)
            newH = d.z > 0 ? 1 : -1;

        if (newH != 0 && newH != state.hDir)
            state.zigzag = Mathf.Min(state.zigzag + 1f, 6f);
        else
            state.zigzag = Mathf.Max(state.zigzag - 0.3f, 0f);

        if (newH != 0) state.hDir = newH;

        state.vertDir = Mathf.Sign(d.y);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Note picker — evaluates all (degree, octave) combinations and scores
    // ─────────────────────────────────────────────────────────────────────
    NoteEvent PickEvent(BlockType type)
    {
        int   bestDeg = 1, bestOct = 0;
        float bScore  = float.MinValue;

        int[] preferred = ChordDegrees(type);
        int   lastSc    = state.Scalar;

        // Range: oct -1 to +2 (four octaves, 28 candidates total)
        for (int oct = -1; oct <= 2; oct++)
        for (int deg = 1;  deg <= 7; deg++)
        {
            int   sc   = (deg - 1) + oct * 7;
            int   dist = sc - lastSc;
            float score = 0f;

            // Jump penalty — relaxes as the path twists (zigzag)
            float pen = Mathf.Lerp(2.2f, 0.6f, state.zigzag / 6f);
            score -= Mathf.Abs(dist) * pen;

            // Reward stepwise motion (≤ 2 scale degrees)
            if (Mathf.Abs(dist) <= 2) score += 2.8f;

            // Horizontal direction nudge
            if (dist != 0 && Mathf.Sign(dist) == state.hDir) score += 0.7f;

            // Vertical contour
            if (state.vertDir > 0.05f  && dist > 0) score += 1.8f;
            if (state.vertDir < -0.05f && dist < 0) score += 1.8f;

            // Chord-tone bonus
            if (System.Array.IndexOf(preferred, deg) >= 0) score += 2.2f;

            // Center-of-range bias: prefer scalar 4–7 (G–C area in middle octave)
            score -= Mathf.Abs(sc - 5) * 0.10f;

            if (score > bScore) { bScore = score; bestDeg = deg; bestOct = oct; }
        }

        if (Random.value < repetition) { bestDeg = state.lastDeg; bestOct = state.lastOct; }

        float tension = Mathf.Clamp01(
            Mathf.Abs((bestDeg - 1 + bestOct * 7) - lastSc) / 6f);

        state.lastDeg = bestDeg;
        state.lastOct = bestOct;

        return new NoteEvent { degree = bestDeg, octave = bestOct, tension = tension };
    }

    // Chord-tone degrees for each block type (C Dorian, 1-indexed)
    // Home  Cm : 1(C)  3(Eb) 5(G)
    // Lift  F  : 4(F)  6(A)  1(C)   ← natural 6th, Dorian colour
    // Pull  Gm : 5(G)  7(Bb) 2(D)
    // Shadow Bb: 7(Bb) 2(D)  4(F)
    static int[] ChordDegrees(BlockType t) => t switch
    {
        BlockType.Home   => new[] { 1, 3, 5 },
        BlockType.Lift   => new[] { 4, 6, 1 },
        BlockType.Pull   => new[] { 5, 7, 2 },
        BlockType.Shadow => new[] { 7, 2, 4 },
        _                => new[] { 1, 5 },
    };
}
