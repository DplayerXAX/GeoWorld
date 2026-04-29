using System.Collections.Generic;
using UnityEngine;

public class SurfaceUnit : MonoBehaviour
{
    [Header("Movement")]
    public float bpm = 120f;
    [Range(0.5f, 0.95f)]
    public float moveRatio = 0.8f; 

    [Header("Music")]
    public GridSystem grid;

    List<FaceNode> path;
    int index;

    float secPerBeat;
    float beatTimer;
    bool isMoving;

    Vector3 moveFrom;
    Vector3 moveTo;
    float moveDuration;
    float moveTimer;

    BlockData currentBlock;
    ChordData currentChord;

    void Start()
    {
        secPerBeat = 60f / bpm;
        //getBlockFromCell = GridSystem.instance.GetBlock;
        grid = GridSystem.instance;
    }

    public void SetPath(List<FaceNode> newPath)
    {
        path = newPath;
        index = 0;
        currentBlock = null;
        currentChord = null;
        beatTimer = 0f;

        StepToNode(path[0]);
        index = 1;
    }

    void Update()
    {
        if (path == null) return;

        beatTimer += Time.deltaTime;
        if (beatTimer >= secPerBeat)
        {
            beatTimer -= secPerBeat;

            if (index < path.Count)
            {
                StepToNode(path[index]);
                index++;
            }
        }

        if (isMoving)
        {
            moveTimer += Time.deltaTime;
            float t = Mathf.Clamp01(moveTimer / moveDuration);
            t = t * t * (3f - 2f * t);
            transform.position = Vector3.Lerp(moveFrom, moveTo, t);

            if (moveTimer >= moveDuration)
                isMoving = false;
        }
    }

    void StepToNode(FaceNode node)
    {
        moveFrom = transform.position;
        moveTo = node.worldPos;
        moveDuration = secPerBeat * moveRatio;
        moveTimer = 0f;
        isMoving = true;

        BlockData block = GridSystem.instance.GetBlock(node.cell);
        if (block != null && block != currentBlock)
        {
            currentBlock = block;
            TriggerChord(block, node);
        }
    }

    void TriggerChord(BlockData block, FaceNode node)
    {
        ChordData chord = PickChord(block);
        if (chord == null) return;

        chord.myChord.Post(this.gameObject);
        currentChord = chord;

        float progress = (float)index / path.Count;
        AudioManager.Instance.SetIntensity(progress);
    }

    ChordData PickChord(BlockData block)
    {
        if (block.chords == null || block.chords.Count == 0) return null;
        if (currentChord?.notes == null)
            return block.chords[Random.Range(0, block.chords.Count)];

        ChordData best = block.chords[0];
        int bestShared = -1;

        foreach (var c in block.chords)
        {
            if (c.notes == null) continue;
            int shared = 0;
            foreach (int n in c.notes)
                foreach (int m in currentChord.notes)
                    if ((n % 12) == (m % 12)) shared++;

            if (shared > bestShared) { bestShared = shared; best = c; }
        }

        return best;
    }
}