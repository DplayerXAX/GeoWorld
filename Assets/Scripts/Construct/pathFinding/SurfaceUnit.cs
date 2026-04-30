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

    [HideInInspector] public GameFlowManager gameFlow;

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
    FaceNode prevNode;

    void Start()
    {
        secPerBeat = 60f / bpm;
        grid = GridSystem.instance;
    }

    public void SetPath(List<FaceNode> newPath)
    {
        path = newPath;
        index = 0;
        currentBlock = null;
        beatTimer = 0f;
        prevNode = null;

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
            else
            {
                OnPathFinished();
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

        var instance = GridSystem.instance.GetInstanceAt(node.cell);
        if (instance == null || instance.data == null) return;
        BlockData block = instance.data;

        float progress = path.Count > 1 ? (float)index / (path.Count - 1) : 0f;

        switch (block.blockType)
        {
            case BlockType.Home:
            case BlockType.Lift:
            case BlockType.Pull:
            case BlockType.Shadow:

                if (block != currentBlock)
                {
                    AudioManager.Instance.SetChord(block.blockType);
                    ArpeggiatorManager.Instance.PlayBassRoot(block.blockType);
                    currentBlock = block;
                }

                ArpeggiatorManager.Instance.PlayMelodyNote(
                    block.blockType,
                    node,
                    prevNode,
                    progress,
                    index
                );
                break;

            case BlockType.Turret:
                block.onStepEvent.Post(this.gameObject);
                break;
        }

        prevNode = node;

        AudioManager.Instance.SetIntensity(progress * 100f);
    }

    void OnPathFinished()
    {
        ArpeggiatorManager.Instance.StopArp();
        AkSoundEngine.PostEvent("Cadence_End", this.gameObject);
        AudioManager.Instance.SetIntensity(0f);
        path = null;
        gameFlow?.EndRunningPhase();
    }
}