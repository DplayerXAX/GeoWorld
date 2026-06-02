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

    [Header("Loop")]
    // Volume scale applied when this unit is in continuous-loop mode.
    [Range(0f, 1f)] public float loopVelocityScale = 0.35f;

    [Header("Audio Events")]
    // Assign the Wwise "Cadence_End" event here once it exists in your project.
    // Left unassigned → no error, the call is silently skipped.
    public AK.Wwise.Event cadenceEndEvent;

    [HideInInspector] public GameFlowManager gameFlow;

    List<FaceNode> path;
    public List<FaceNode> Path => path;   // read-only access for retirement logic
    int index;

    float secPerBeat;
    float beatTimer;

    bool isMoving;
    Vector3 moveFrom, moveTo, controlPoint;
    Quaternion rotFrom, rotTo;           
    float moveDuration, moveTimer;

    BlockData currentBlock;
    FaceNode  prevNode;

    // ── Loop state ─────────────────────────────────────────────────────
    bool _looping;

    public void SetLooping(bool on)
    {
        _looping = on;
        if (on)
        {
            // Discard any in-progress recording — the ambient loop is already
            // registered; we don't want a second copy from loop cycles.
            ArpeggiatorManager.Instance.StopRecording();
        }
    }

    // ──────────────────────────────────────────────────────────────────

    void Start()
    {
        secPerBeat = 60f / bpm;
        grid       = GridSystem.instance;
    }

    public void SetPath(List<FaceNode> newPath)
    {
        path         = newPath;
        index        = 0;
        currentBlock = null;
        beatTimer    = 0f;
        prevNode     = null;
        _looping     = false;

        ArpeggiatorManager.Instance.StartRecording();

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

            Vector3 m1 = Vector3.Lerp(moveFrom, controlPoint, t);
            Vector3 m2 = Vector3.Lerp(controlPoint, moveTo, t);
            transform.position = Vector3.Lerp(m1, m2, t);

            transform.rotation = Quaternion.Slerp(rotFrom, rotTo, t);

            if (moveTimer >= moveDuration) isMoving = false;
        }
    }

    // Returns the visual face-centre for a node, matching block render positions.
    // FaceNode.worldPos uses corner-based coords (cell*size); GridToWorld is
    // centre-based (cell*size + size/2), so the face sits at blockCentre + normal*(size/2).
    static Vector3 FaceCenter(FaceNode node)
    {
        var gs = GridSystem.instance;
        return gs.GridToWorld(node.cell) + node.normal * (gs.cellSize * 0.5f);
    }

    void StepToNode(FaceNode node)
    {
        moveFrom = transform.position;
        moveTo   = FaceCenter(node);
        moveDuration = secPerBeat * moveRatio;
        moveTimer = 0f;
        isMoving = true;

        if (prevNode != null && Vector3.Angle(prevNode.normal, node.normal) > 1f)
        {
            Vector3 center = (moveFrom + moveTo) / 2f;
            Vector3 outwardNormal = (prevNode.normal + node.normal).normalized;
            controlPoint = center + outwardNormal * (grid.cellSize * 0.4f);
        }
        else
        {
            controlPoint = (moveFrom + moveTo) / 2f;
        }

        rotFrom = transform.rotation;

        Vector3 moveDir = (moveTo - moveFrom).normalized;
        if (moveDir.sqrMagnitude < 0.001f)
        {
            moveDir = transform.forward;
        }

        Vector3 forwardOnSurface = Vector3.ProjectOnPlane(moveDir, node.normal).normalized;
        if (forwardOnSurface.sqrMagnitude < 0.001f)
        {
            forwardOnSurface = transform.forward; 
        }

        rotTo = Quaternion.LookRotation(forwardOnSurface, node.normal);

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
                // Looping units don't switch chord pads — avoids fighting
                // with the live unit's harmony.
                if (!_looping && block != currentBlock)
                {
                    AudioManager.Instance.SetChord(block.blockType);
                    ArpeggiatorManager.Instance.PlayBassRoot(block.blockType, this.gameObject);
                    BackgroundReactor.Instance?.OnChordChange(block.blockType);
                    currentBlock = block;
                }
                else if (_looping)
                {
                    currentBlock = block;
                }

                ArpeggiatorManager.Instance.PlayMelodyNote(
                    block.blockType, node, prevNode, progress, index,
                    _looping ? loopVelocityScale : 1f, _looping, this.gameObject);
                break;

            case BlockType.Turret:
            case BlockType.SlowTurret:
            case BlockType.AoeTurret:
                block.onStepEvent.Post(this.gameObject);
                break;
        }

        prevNode = node;

        // Only the live unit drives Intensity (looping units sit in the background).
        if (!_looping)
            AudioManager.Instance.SetIntensity(progress * 100f);
    }

    void OnPathFinished()
    {
        if (_looping)
        {
            // Loop back to the start of the path.
            index        = 0;
            beatTimer    = 0f;
            prevNode     = null;
            currentBlock = null;
            StepToNode(path[0]);
            index = 1;
            return;
        }

        // ── First traversal complete ──────────────────────────────────
        ArpeggiatorManager.Instance.StopArp();

        var rec     = ArpeggiatorManager.Instance.StopRecording();
        var visuals = new List<GameObject>();
        var cells   = new List<Vector3Int>();
        foreach (var node in path)
        {
            cells.Add(node.cell);
            var inst = GridSystem.instance?.GetInstanceAt(node.cell);
            if (inst?.visualObject != null) visuals.Add(inst.visualObject);
        }
        LoopManager.Instance?.AddLoop(rec, visuals, bpm, cells);
        // (Laser line is now drawn in GameFlowManager.Run() the moment the path is
        //  confirmed, so we don't need to draw it again here.)

        // cadenceEndEvent is assigned in Inspector — silently skip if not yet set.
        if (cadenceEndEvent != null && cadenceEndEvent.IsValid())
            cadenceEndEvent.Post(this.gameObject);
        AudioManager.Instance.SetIntensity(0f);

        // EndRunningPhase calls SetLooping(true) on us if the path succeeded.
        gameFlow?.EndRunningPhase();

        if (_looping)
        {
            // GameFlowManager promoted us to loop mode — restart traversal now.
            index        = 0;
            beatTimer    = 0f;
            prevNode     = null;
            currentBlock = null;
            StepToNode(path[0]);
            index = 1;
        }
        else
        {
            path = null;
        }
    }
}
