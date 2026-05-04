using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LoopManager : MonoBehaviour
{
    public static LoopManager Instance;

    [Range(0.05f, 0.4f)] public float loopVelocity = 0.18f;
    [Range(0.3f, 1.2f)] public float tempoScale = 0.75f;
    [Range(0f, 1f)] public float flashBrightness = 0.55f;

    class LoopEntry
    {
        public List<ArpeggiatorManager.NoteEvent> notes;
        public List<GameObject>    visuals;
        public HashSet<Vector3Int> cells;   // grid cells this loop's path covers
        public float     bpm;
        public Coroutine coroutine;
    }

    readonly List<LoopEntry> _loops = new();
    readonly HashSet<GameObject> _flashing = new();

    void Awake() => Instance = this;

    public void AddLoop(List<ArpeggiatorManager.NoteEvent> notes, List<GameObject> visuals,
                        float bpm, IEnumerable<Vector3Int> cells = null)
    {
        if (notes == null || notes.Count == 0) return;

        var cellSet = new HashSet<Vector3Int>();
        if (cells != null)
            foreach (var c in cells) cellSet.Add(c);

        var entry = new LoopEntry
        {
            notes   = notes,
            visuals = visuals ?? new List<GameObject>(),
            cells   = cellSet,
            bpm     = bpm
        };

        entry.coroutine = StartCoroutine(PlayLoop(entry));
        _loops.Add(entry);
    }

    // Stops and removes every loop whose path overlaps the given cells.
    // Called by PlacementController when a block is lifted from the grid.
    public void RemoveLoopsOverlapping(IEnumerable<Vector3Int> cells)
    {
        var check = new HashSet<Vector3Int>(cells);
        for (int i = _loops.Count - 1; i >= 0; i--)
        {
            if (!_loops[i].cells.Overlaps(check)) continue;

            if (_loops[i].coroutine != null) StopCoroutine(_loops[i].coroutine);
            _loops.RemoveAt(i);
        }
    }

    public void ClearAllLoops()
    {
        foreach (var e in _loops)
            if (e.coroutine != null) StopCoroutine(e.coroutine);

        _loops.Clear();
        _flashing.Clear();
    }

    IEnumerator PlayLoop(LoopEntry entry)
    {
        float secPerBeat = (60f / entry.bpm) / tempoScale;

        yield return new WaitForSeconds(
            Random.Range(0f, secPerBeat * entry.notes.Count)
        );

        int tick = 0;

        while (true)
        {
            int i = tick % entry.notes.Count;
            var note = entry.notes[i];

            // degree == 0 = rest
            if (note.degree > 0)
            {
                // Resolve the visual block for this note — used for both
                // 3-D audio emission (attenuation) and the flash effect.
                GameObject noteEmitter = null;
                if (entry.visuals.Count > 0)
                {
                    int vi = entry.visuals.Count == 1 ? 0
                        : Mathf.Clamp(
                            Mathf.FloorToInt(
                                (float)i / entry.notes.Count * entry.visuals.Count),
                            0, entry.visuals.Count - 1);

                    noteEmitter = entry.visuals[vi];
                    if (noteEmitter != null)
                        StartCoroutine(FlashBlock(noteEmitter, secPerBeat * 0.55f));
                }

                // Shift loops down 2 octaves relative to how they were recorded.
                // Recorded notes can reach oct +2 (arc at path midpoint); -2 brings
                // those back to oct 0 at most.  Clamp to [-1, 0] so the ambient
                // layer always stays in the low register.
                int loopOct = Mathf.Clamp(note.octave - 2, -1, 0);
                ArpeggiatorManager.Instance.PlayAmbientNote(
                    note.degree, loopOct, loopVelocity, noteEmitter);
            }

            tick++;
            yield return new WaitForSeconds(secPerBeat);
        }
    }

    IEnumerator FlashBlock(GameObject obj, float duration)
    {
        if (obj == null || !_flashing.Add(obj)) yield break;

        var rends = obj.GetComponentsInChildren<Renderer>();
        int n = rends.Length;
        var orig = new Color[n];
        var bright = new Color[n];

        for (int i = 0; i < n; i++)
        {
            orig[i] = rends[i].material.color;
            bright[i] = Color.Lerp(orig[i], Color.white, flashBrightness);
            rends[i].material.color = bright[i];
        }

        float t = 0f;

        while (t < duration)
        {
            t += Time.deltaTime;
            float s = Mathf.Clamp01(t / duration);
            s = s * s * (3f - 2f * s);

            for (int i = 0; i < n; i++)
            {
                if (rends[i])
                    rends[i].material.color = Color.Lerp(bright[i], orig[i], s);
            }

            yield return null;
        }

        for (int i = 0; i < n; i++)
        {
            if (rends[i])
                rends[i].material.color = orig[i];
        }

        _flashing.Remove(obj);
    }
}