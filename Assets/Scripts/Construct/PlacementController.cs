using System.Collections.Generic;
using UnityEngine;

public enum PlacementMode { Edit, Select }

[RequireComponent(typeof(GridSystem))]
public class PlacementController : MonoBehaviour
{
    public PlacementMode mode = PlacementMode.Select;
    public BlockData[] blocks;
    public GridSystem grid;
    public BlockData currentBlock;
    public GameObject cubePrefab;
    public Transform previewParent;
    public OrbitCamera cam;
    public Vector3Int SnappedGridPos => baseGridPos;
    [Range(0.5f, 4f)] public float snapGridRadius = 1.5f;
    public float minDepth = 2f, maxDepth = 40f, scrollSpeed = 3f, rotateSpeed = 10f;
    public float panSpeed = 8f;

    [Header("Tray")]
    // If assigned, tray blocks spawn parented to this transform (use a scene
    // pivot so the tray sits in a fixed world location). If null, the tray
    // is parented to the camera so it always stays on-screen.
    public Transform trayAnchor;
    // Default lands in the bottom-right of the camera view at a comfortable
    // distance. +X is camera-right, -Y is camera-down.
    public Vector3 trayLocalOffset = new Vector3(4.5f, -2.5f, 7f);
    public float traySpacing = 2.0f;
    [Tooltip("Visual scale of tray tokens. Grid cellSize used for placement is unchanged.")]
    public float trayBlockScale = 0.6f;

    private Vector3Int baseGridPos, currentGridPos, manualOffset;
    private float _depth = 10f;
    private Quaternion _currentRotation = Quaternion.identity, _targetRotation = Quaternion.identity;
    private List<GameObject> previewCubes = new();

    private PlacedBlockInstance selectedInstance;
    private GameObject activePhysicsObject;
    private Transform editFocusAnchor;

    private GameObject lastHighlightedObject;
    private Color currentColor;

    private bool isPickingUpObject = false;
    private Vector3 lastObjectPos;
    private Quaternion lastObjectRot;
    private Vector3Int[] lastObjectCells;

    // Tray tracking — kept so we can show/hide tokens on edit mode enter/exit.
    private List<GameObject> trayBlocks = new();

    // Double-click detection for placed-block and endpoint focus.
    private float _lastClickTime;
    private GameObject _lastClickTarget;
    private const float DoubleClickInterval = 0.3f;

    void Awake()
    {
        editFocusAnchor = new GameObject("EditFocusAnchor").transform;
    }

    void Start()
    {
        // currentBlock starts null — the tray is the source of truth for what's
        // available to place. GameFlowManager.StartTurn populates the tray.
        currentColor = GetRandomColor();
    }

    // Spawns `count` block tokens laid out as a row in the tray (no physics).
    // Each token is a SelectableBlock the player clicks to grab; clicking
    // immediately enters Edit mode, and a successful TryPlace destroys the
    // token (consuming the slot).
    public void SpawnRoundBlocks(int count)
    {
        if (cubePrefab == null || blocks == null || blocks.Length == 0)
            return;

        Transform parent     = trayAnchor != null ? trayAnchor : cam.transform;
        Vector3   originLocal = trayAnchor != null ? Vector3.zero : trayLocalOffset;

        for (int i = 0; i < count; i++)
        {
            BlockData data = blocks[Random.Range(0, blocks.Length)];
            if (data == null || data.cells == null) continue;

            float   offset   = (i - (count - 1) * 0.5f) * traySpacing;
            Vector3 localPos = originLocal + Vector3.right * offset;

            GameObject obj = new GameObject("TrayBlock");
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            obj.transform.localRotation = Quaternion.identity;
            // Scale shrinks the whole token (cubes + their relative offsets)
            // so multi-cell blocks fit inside a small visual footprint.
            obj.transform.localScale    = Vector3.one * trayBlockScale;

            Color co = GetRandomColor();
            foreach (var cell in data.cells)
            {
                GameObject c = Instantiate(cubePrefab, obj.transform);
                c.transform.localPosition = (Vector3)cell * grid.cellSize;
                c.GetComponent<Renderer>().material.color = co;
            }

            obj.AddComponent<SelectableBlock>().data = data;
            trayBlocks.Add(obj);
        }
    }

    // Hides or shows all tray tokens that haven't been consumed yet.
    void SetTrayVisible(bool visible)
    {
        trayBlocks.RemoveAll(b => b == null);
        foreach (var b in trayBlocks) b.SetActive(visible);
    }

    void Update()
    {
        _currentRotation = Quaternion.Slerp(
            _currentRotation,
            _targetRotation,
            1f - Mathf.Exp(-rotateSpeed * Time.deltaTime)
        );

        HandleScroll();
        HandleMouseMove();
        HandleModeSwitch();

        if (mode == PlacementMode.Edit)
        {
            HandleKeyboardOffset();
            HandleRotate();
        }
        else
        {
            HandleSelectModePan();
        }

        UpdatePreview();

        if (Input.GetMouseButtonDown(0))
        {
            if (mode == PlacementMode.Edit)
            {
                if (currentBlock != null) TryPlace();
            }
            else
            {
                TrySelectObject();
            }
        }

        currentGridPos = baseGridPos + manualOffset;
    }

    // =========================
    // INPUT
    // =========================

    void HandleScroll()
    {
        float s = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(s) < 0.001f) return;

        if (mode == PlacementMode.Select)
            cam.AddDistance(-s * scrollSpeed * 10f);
        else
            _depth = Mathf.Clamp(_depth - s * scrollSpeed * _depth, minDepth, maxDepth);
    }

    void HandleMouseMove()
    {
        Ray r = cam.myCam.ScreenPointToRay(Input.mousePosition);
        Vector3 world = r.origin + r.direction * _depth;
        baseGridPos = grid.WorldToGrid(world);
    }

    // Edit mode only.
    // WASD  → move block relative to camera's horizontal facing (snapped to grid axes)
    // Q / E → move block down / up in world Y
    void HandleKeyboardOffset()
    {
        Vector3Int right   = SnapToHorizontalAxis(cam.transform.right);
        Vector3Int forward = SnapToHorizontalAxis(cam.transform.forward);

        if (Input.GetKeyDown(KeyCode.A)) manualOffset -= right;
        if (Input.GetKeyDown(KeyCode.D)) manualOffset += right;
        if (Input.GetKeyDown(KeyCode.W)) manualOffset += forward;
        if (Input.GetKeyDown(KeyCode.S)) manualOffset -= forward;
        if (Input.GetKeyDown(KeyCode.Q)) manualOffset += Vector3Int.down;
        if (Input.GetKeyDown(KeyCode.E)) manualOffset += Vector3Int.up;
    }

    // Select mode: WASD pans the camera continuously along its horizontal facing,
    // Q/E moves it down/up. Selecting an object cancels the pan and snaps focus
    // back to that object (handled in OrbitCamera.SetFocus).
    void HandleSelectModePan()
    {
        Vector3 right = cam.transform.right;   right.y = 0; right.Normalize();
        Vector3 fwd   = cam.transform.forward; fwd.y   = 0; fwd.Normalize();

        Vector3 delta = Vector3.zero;
        if (Input.GetKey(KeyCode.D)) delta += right;
        if (Input.GetKey(KeyCode.A)) delta -= right;
        if (Input.GetKey(KeyCode.W)) delta += fwd;
        if (Input.GetKey(KeyCode.S)) delta -= fwd;
        if (Input.GetKey(KeyCode.E)) delta += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) delta -= Vector3.up;

        if (delta.sqrMagnitude > 0.0001f)
            cam.Pan(delta.normalized * panSpeed * Time.deltaTime);
    }

    // Projects a world-space direction onto the XZ plane and snaps to the
    // nearest cardinal axis (+X, -X, +Z, -Z).
    Vector3Int SnapToHorizontalAxis(Vector3 dir)
    {
        dir.y = 0;
        dir = dir.normalized;
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z))
            return dir.x >= 0 ? Vector3Int.right : Vector3Int.left;
        else
            return dir.z >= 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
    }

    void HandleRotate()
    {
        bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        if (Input.GetKeyDown(KeyCode.Alpha1)) 
        {
            AudioManager.Instance.PlayRotate();
            _targetRotation *= Quaternion.Euler(90, 0, 0);
        }

        if (Input.GetKeyDown(KeyCode.Alpha2)) 
        {
            AudioManager.Instance.PlayRotate();
            _targetRotation *= Quaternion.Euler(0, 90, 0);
        }

        if (Input.GetKeyDown(KeyCode.Alpha3)) 
        {
            AudioManager.Instance.PlayRotate();
            _targetRotation *= Quaternion.Euler(0, 0, 90);
        }
    }

    void HandleModeSwitch()
    {
        if (!Input.GetKeyDown(KeyCode.Tab)) return;

        if (mode == PlacementMode.Select)
        {
            if (selectedInstance != null)
            {
                isPickingUpObject = true;
                lastObjectPos   = selectedInstance.visualObject.transform.position;
                lastObjectRot   = selectedInstance.visualObject.transform.rotation;
                lastObjectCells = selectedInstance.occupiedCells.ToArray();

                // Snap depth so the preview block materialises where the picked-up block was.
                // This also prevents the camera from flying when editFocusAnchor is set below.
                SnapDepthToWorldPos(lastObjectPos);

                grid.RemoveInstance(selectedInstance);
                NotifyBlockLifted(lastObjectCells);
                selectedInstance = null;
                EnterEditMode(lastObjectPos);
            }
            else
            {
                EnterEditMode(null);
            }
        }
        else
        {
            if (isPickingUpObject)
                CancelAndReturnObject();

            mode = PlacementMode.Select;
            previewParent.gameObject.SetActive(false);
            SetTrayVisible(true);
        }
    }

    // =========================
    // SELECT
    // =========================

    void TrySelectObject()
    {
        Ray ray = cam.myCam.ScreenPointToRay(Input.mousePosition);

        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            UpdateHighlight(null);
            _lastClickTarget = null;
            return;
        }

        // --- Tray token: single click → immediately grab and enter Edit ---
        var sb = hit.transform.GetComponentInParent<SelectableBlock>();
        if (sb != null)
        {
            currentBlock        = sb.data;
            currentColor        = sb.GetComponentInChildren<Renderer>().material.color;
            activePhysicsObject = sb.gameObject;
            selectedInstance    = null;

            UpdateHighlight(activePhysicsObject);
            EnterEditMode(null);
            return;
        }

        // --- Endpoint markers (start / end block): highlight, double-click focus ---
        var ep = hit.transform.GetComponentInParent<GridEndpoint>();
        if (ep != null)
        {
            UpdateHighlight(ep.gameObject);
            selectedInstance    = null;
            activePhysicsObject = null;

            bool isDouble = ep.gameObject == _lastClickTarget
                         && Time.time - _lastClickTime < DoubleClickInterval;
            _lastClickTime   = Time.time;
            _lastClickTarget = ep.gameObject;

            if (isDouble) cam.SetFocus(ep.transform);
            return;
        }

        // --- Placed blocks: single-click selects, double-click picks up for re-edit ---
        // Step slightly inward along the surface normal before snapping to grid so
        // a hit exactly on a face boundary doesn't round into the adjacent empty cell.
        Vector3Int gPos    = grid.WorldToGrid(hit.point - hit.normal * (grid.cellSize * 0.1f));
        var        instance = grid.GetInstanceAt(gPos);

        if (instance != null)
        {
            selectedInstance    = instance;
            currentBlock        = instance.data;
            currentColor        = instance.visualObject.GetComponentInChildren<Renderer>().material.color;
            activePhysicsObject = null;

            UpdateHighlight(instance.visualObject);

            bool isDouble = instance.visualObject == _lastClickTarget
                         && Time.time - _lastClickTime < DoubleClickInterval;
            _lastClickTime   = Time.time;
            _lastClickTarget = instance.visualObject;

            if (isDouble)
            {
                // Pick the block back up, same as Tab — remove from grid and re-enter edit mode.
                isPickingUpObject = true;
                lastObjectPos     = instance.visualObject.transform.position;
                lastObjectRot     = instance.visualObject.transform.rotation;
                lastObjectCells   = instance.occupiedCells.ToArray();

                SnapDepthToWorldPos(lastObjectPos);
                grid.RemoveInstance(instance);   // destroys visualObject
                NotifyBlockLifted(lastObjectCells);
                selectedInstance = null;
                EnterEditMode(lastObjectPos);
            }
        }
    }

    void UpdateHighlight(GameObject target)
    {
        if (lastHighlightedObject != null && lastHighlightedObject != target)
        {
            var old = lastHighlightedObject.GetComponent<Outline>();
            if (old != null)
            {
                old.enabled = false; // OnDisable fires immediately → materials restored this frame
                Destroy(old);
            }
        }

        if (target != null && target != lastHighlightedObject)
        {
            var ol = target.AddComponent<Outline>();
            ol.OutlineColor = Color.yellow;
        }

        lastHighlightedObject = target;
    }

    // =========================
    // EDIT
    // =========================

    // Adjusts _depth so the mouse ray lands at worldPos, then sets manualOffset
    // to shift the preview to that exact grid cell.
    void SnapDepthToWorldPos(Vector3 worldPos)
    {
        Ray r    = cam.myCam.ScreenPointToRay(Input.mousePosition);
        _depth   = Mathf.Clamp(Vector3.Dot(worldPos - r.origin, r.direction), minDepth, maxDepth);
        baseGridPos  = grid.WorldToGrid(r.origin + r.direction * _depth);
        manualOffset = grid.WorldToGrid(worldPos) - baseGridPos;
    }

    // focusPos: if provided, camera pivots there once. Pass null to leave camera in place.
    void EnterEditMode(Vector3? focusPos)
    {
        mode = PlacementMode.Edit;
        SetTrayVisible(false);  // hide tokens while placing so they don't clutter the view
        previewParent.gameObject.SetActive(currentBlock != null);
        if (focusPos == null) manualOffset = Vector3Int.zero;
        UpdateHighlight(null);

        if (focusPos.HasValue)
        {
            editFocusAnchor.position = focusPos.Value;
            cam.SetFocus(editFocusAnchor);
        }
    }

    void TryPlace()
    {
        if (currentBlock == null) return;

        var cells = GetRotatedCells();
        if (cells.Length == 0) return;
        if (!CanPlace(currentGridPos, cells)) return;

        Vector3 center = Vector3.zero;
        foreach (var c in cells)
            center += grid.GridToWorld(currentGridPos + c);
        center /= cells.Length;

        GameObject obj = new GameObject("PlacedBlock");
        obj.transform.position = center;
        obj.transform.rotation = _currentRotation;

        var br = obj.AddComponent<BlockRenderer>();
        br.cubePrefab = cubePrefab;
        br.Render(currentGridPos, cells, grid.cellSize, grid);

        foreach (var r in obj.GetComponentsInChildren<Renderer>())
            r.material.color = currentColor;

        PlacedBlockInstance ins = new()
        {
            data         = currentBlock,
            visualObject = obj
        };

        foreach (var c in cells)
            ins.occupiedCells.Add(currentGridPos + c);

        grid.RegisterInstance(ins);

        // Auto-check path after every block placement — updates live preview line.
        GameFlowManager.Instance?.EvaluateGrid();

        ArpeggiatorManager.Instance?.PlayAmbientNote(
    PlacementDegree(ins.data.blockType),
    PlacementOctave(ins.data.blockType),
    0.45f
);
        // Consume the tray token we picked up. activePhysicsObject is set
        // when TrySelectObject hits a SelectableBlock; destroying it frees
        // the slot and (when the tray empties) GameFlowManager spawns a
        // fresh round.
        if (activePhysicsObject != null)
        {
            trayBlocks.Remove(activePhysicsObject); // remove before Destroy so SetTrayVisible skips it
            Destroy(activePhysicsObject);
            activePhysicsObject = null;
        }

        isPickingUpObject = false;
        currentBlock = null;
        currentColor = GetRandomColor();

        mode = PlacementMode.Select;
        previewParent.gameObject.SetActive(false);
        SetTrayVisible(true);
    }

    void UpdatePreview()
    {
        if (currentBlock == null || mode != PlacementMode.Edit)
        {
            previewParent.gameObject.SetActive(false);
            return;
        }

        var cells = GetRotatedCells();
        if (cells.Length == 0) return;

        bool valid = CanPlace(currentGridPos, cells);

        while (previewCubes.Count < cells.Length)
        {
            var c = Instantiate(cubePrefab, previewParent);
            foreach (var col in c.GetComponentsInChildren<Collider>())
                col.enabled = false;
            previewCubes.Add(c);
        }

        for (int i = 0; i < previewCubes.Count; i++)
            previewCubes[i].SetActive(i < cells.Length);

        // Valid → green, invalid → red. Preview always reads as a placement hint;
        // the random per-block color is applied only on successful placement.
        Color tint = valid
            ? new Color(0.25f, 1.00f, 0.35f, 0.55f)
            : new Color(1.00f, 0.20f, 0.20f, 0.45f);

        for (int i = 0; i < cells.Length; i++)
        {
            previewCubes[i].transform.position =
                grid.GridToWorld(currentGridPos + cells[i]);
            previewCubes[i].GetComponent<Renderer>().material.color = tint;
        }
    }

    Vector3Int[] GetRotatedCells()
    {
        if (currentBlock == null || currentBlock.cells == null)
            return System.Array.Empty<Vector3Int>();

        Vector3Int[] res = new Vector3Int[currentBlock.cells.Length];
        for (int i = 0; i < res.Length; i++)
            res[i] = Vector3Int.RoundToInt(_currentRotation * (Vector3)currentBlock.cells[i]);
        return res;
    }

    bool CanPlace(Vector3Int bp, Vector3Int[] cs)
    {
        foreach (var c in cs)
        {
            var p = bp + c;
            if (grid.IsOccupied(p) || p.y < 0) return false;
        }
        return true;
    }

    // =========================
    // PICKUP RETURN
    // =========================

    // Called whenever a placed block is lifted from the grid.
    // Removes any laser lines and audio loops whose path ran through those cells.
    void NotifyBlockLifted(Vector3Int[] cells)
    {
        if (cells == null || cells.Length == 0) return;
        PathFlowManager.Instance?.RemoveFlowsOverlapping(cells);   // remove affected loop lines
        LoopManager.Instance?.RemoveLoopsOverlapping(cells);        // stop affected loop audio
        GameFlowManager.Instance?.EvaluateGrid();                   // stop unit / refresh live line
    }

    void CancelAndReturnObject()
    {
        if (lastObjectCells != null)
        {
            GameObject obj = new GameObject("ReturnedBlock");
            obj.transform.position = lastObjectPos;
            obj.transform.rotation = lastObjectRot;

            var br = obj.AddComponent<BlockRenderer>();
            br.cubePrefab = cubePrefab;

            Vector3Int origin = grid.WorldToGrid(lastObjectPos);
            Vector3Int[] rel  = new Vector3Int[lastObjectCells.Length];
            for (int i = 0; i < rel.Length; i++)
                rel[i] = lastObjectCells[i] - origin;

            br.Render(origin, rel, grid.cellSize, grid);

            PlacedBlockInstance ins = new()
            {
                data         = currentBlock,
                visualObject = obj
            };

            foreach (var c in lastObjectCells)
                ins.occupiedCells.Add(c);

            grid.RegisterInstance(ins);
        }
        else
        {
            SpawnPhysicsBlockAt(lastObjectPos, lastObjectRot);
        }

        isPickingUpObject = false;
    }

    void SpawnPhysicsBlockAt(Vector3 pos, Quaternion rot)
    {
        if (currentBlock == null) return;

        GameObject obj = new GameObject("PhysicsBlock");
        obj.transform.position = pos;
        obj.transform.rotation = rot;

        foreach (var cell in currentBlock.cells)
        {
            GameObject c = Instantiate(cubePrefab, obj.transform);
            c.transform.localPosition = (Vector3)cell * grid.cellSize;
            c.GetComponent<Renderer>().material.color = currentColor;
        }

        obj.AddComponent<Rigidbody>();
        obj.AddComponent<SelectableBlock>().data = currentBlock;
        obj.AddComponent<BoxCollider>().size = Vector3.one * 2.5f;
    }

    // =========================
    // UTIL
    // =========================

    static int PlacementDegree(BlockType t) => t switch
    {
        BlockType.Home => 1,
        BlockType.Lift => 4,
        BlockType.Pull => 5,
        BlockType.Shadow => 7,
        _ => 1,
    };

    static int PlacementOctave(BlockType t) => t switch
    {
        BlockType.Home => 0,
        BlockType.Lift => 1,
        BlockType.Pull => 0,
        BlockType.Shadow => -1,
        _ => 0,
    };

    Color GetRandomColor() =>
        Random.ColorHSV(0f, 1f, 0.6f, 1f, 0.7f, 1f);
}
