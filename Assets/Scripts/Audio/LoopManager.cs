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
        public List<GameObject> visuals;
        public float bpm;
        public Coroutine coroutine;
    }

    readonly List<LoopEntry> _loops = new();
    readonly HashSet<GameObject> _flashing = new();

    void Awake() => Instance = this;

    public void AddLoop(List<ArpeggiatorManager.NoteEvent> notes, List<GameObject> visuals, float bpm)
    {
        if (notes == null || notes.Count == 0) return;

        var entry = new LoopEntry
        {
            notes = notes,
            visuals = visuals ?? new List<GameObject>(),
            bpm = bpm
        };

        entry.coroutine = StartCoroutine(PlayLoop(entry));
        _loops.Add(entry);
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

            // degree == 0 ¡ú rest
            if (note.degree > 0)
            {
                ArpeggiatorManager.Instance.PlayAmbientNote(
                    note.degree,
                    note.octave,
                    loopVelocity
                );

                if (entry.visuals.Count > 0)
                {
                    int vi = entry.visuals.Count == 1 ? 0
                        : Mathf.FloorToInt(
                            (float)i / entry.notes.Count * entry.visuals.Count
                        );

                    vi = Mathf.Clamp(vi, 0, entry.visuals.Count - 1);

                    if (entry.visuals[vi] != null)
                    {
                        StartCoroutine(
                            FlashBlock(entry.visuals[vi], secPerBeat * 0.55f)
                        );
                    }
                }
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