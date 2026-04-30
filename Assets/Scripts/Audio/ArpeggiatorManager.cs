using System.Collections.Generic;
using UnityEngine;

public class ArpeggiatorManager : MonoBehaviour
{
    public static ArpeggiatorManager Instance;

    class MelodyState
    {
        public int lastIndex = 7;
        public int hDir = 1;
        public float zigzag = 0f;
        public float vertDir = 0f;
    }

    MelodyState state = new();

    static readonly int[] Scale = { 0, 2, 3, 5, 7, 9, 10, 12, 14, 15, 17 };

    [Header("Feel")]
    [Range(0f, 1f)] public float noteDensity = 0.75f;
    [Range(0f, 1f)] public float repetition = 0.25f;

    readonly List<NoteEvent> _rec = new();
    bool _recording;

    void Awake() => Instance = this;

    public struct NoteEvent
    {
        public int degree;   // 0 = rest
        public int octave;
        public float tension;
    }

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

    public void PlayMelodyNote(
        BlockType type, FaceNode node, FaceNode prevNode,
        float progress, int pathIndex, float velocityScale = 1f)
    {
        float density = noteDensity * (0.45f + 0.55f * Mathf.Sin(progress * Mathf.PI));
        bool rest = Random.value > density;

        if (rest)
        {
            if (_recording)
                _rec.Add(new NoteEvent { degree = 0 });
            return;
        }

        UpdateState(node, prevNode);

        NoteEvent e = PickEvent(type);

        if (progress >= 0.97f)
        {
            e.degree = 1;
            e.octave = 0;
        }

        float vel = Mathf.Lerp(0.38f, 0.82f, progress) * velocityScale;

        if (_recording)
            _rec.Add(e);

        AudioManager.Instance.PlayArpNote(e.degree, e.octave, vel);
        BackgroundReactor.Instance?.OnNote(e.tension * velocityScale);
    }

    public void PlayBassRoot(BlockType type)
    {
        int degree = type switch
        {
            BlockType.Home => 1,
            BlockType.Lift => 4,
            BlockType.Pull => 5,
            BlockType.Shadow => 7,
            _ => 1
        };

        int octave = -1;

        AudioManager.Instance.PlayArpNote(degree, octave, 0.4f);
        BackgroundReactor.Instance?.OnNote(0.4f);
    }

    public void PlayAmbientNote(int degree, int octave, float velocity)
    {
        AudioManager.Instance.PlayArpNote(degree, octave, velocity);
    }

    public void StopArp() { }

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

    NoteEvent PickEvent(BlockType type)
    {
        int bestIndex = 0;
        float bestScore = float.MinValue;

        int[] preferred = ChordDegrees(type);

        for (int i = 0; i < Scale.Length; i++)
        {
            int dist = i - state.lastIndex;
            float score = 0f;

            float penalty = Mathf.Lerp(2.2f, 0.6f, state.zigzag / 6f);
            score -= Mathf.Abs(dist) * penalty;

            if (Mathf.Abs(dist) <= 1) score += 2.8f;

            if (dist != 0 && Mathf.Sign(dist) == state.hDir) score += 0.7f;

            if (state.vertDir > 0 && dist > 0) score += 1.8f;
            if (state.vertDir < 0 && dist < 0) score += 1.8f;

            int degree = (i % 7) + 1;

            if (System.Array.IndexOf(preferred, degree) >= 0)
                score += 2.2f;

            score -= Mathf.Abs(i - 5) * 0.15f;

            if (score > bestScore)
            {
                bestScore = score;
                bestIndex = i;
            }
        }

        if (Random.value < repetition)
            bestIndex = state.lastIndex;

        int finalDegree = (bestIndex % 7) + 1;
        int finalOctave = (bestIndex / 7) - 1;

        float tension = Mathf.Clamp01(Mathf.Abs(bestIndex - state.lastIndex) / 6f);

        state.lastIndex = bestIndex;

        return new NoteEvent
        {
            degree = finalDegree,
            octave = finalOctave,
            tension = tension
        };
    }

    static int[] ChordDegrees(BlockType t) => t switch
    {
        BlockType.Home => new[] { 1, 3, 5 },
        BlockType.Lift => new[] { 4, 6, 1 },
        BlockType.Pull => new[] { 5, 7, 2 },
        BlockType.Shadow => new[] { 7, 2, 4 },
        _ => new[] { 1, 5 }
    };
}