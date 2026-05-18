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
    private int  _pendingShopPrice = 0;    // price of the current shop item being held
    private Vector3 lastObjectPos;
    private Quaternion lastObjectRot;
    private Vector3Int[] lastObjectCells;

    // Tray tracking — kept so we can show/hide tokens on edit mode enter/exit.
    private List<GameObject> trayBlocks = new();

    // Double-click detection for placed-block and endpoint focus.
    private float _lastClickTime;
    private GameObject _lastClickTarget;
    private const float DoubleClickInterval = 0.3f;

    // ── Undo history ──────────────────────────────────────────────────────────
    const int MaxUndoDepth = 20;
    readonly List<UndoRecord> _undoStack = new();

    enum UndoType { NewPlace, Reposition, Delete }

    class UndoRecord
    {
        public UndoType     actionType;
        public BlockData    data;
        public Color        color;
        public Quaternion   rotation;
        public Vector3Int[] cells;      // world-grid cells after the action
        public Vector3      worldCenter;
        public int          pricePaid;  // > 0 only for NewPlace — refunded on undo
        // Reposition only: state before the move
        public Vector3Int[] prevCells;
        public Vector3      prevCenter;
        public Quaternion   prevRotation;
    }

    public static PlacementController Instance;

    void Awake()
    {
        Instance        = this;
        editFocusAnchor = new GameObject("EditFocusAnchor").transform;
    }

    void Start()
    {
        // currentBlock starts null — the tray is the source of truth for what's
        // available to place. GameFlowManager.StartTurn populates the tray.
        currentColor = GetRandomColor();
    }

    // Clears all shop items for the new round.
    public void ClearTray()
    {
        ShopController.Instance?.ClearItems();
        // Also clear any legacy trayBlocks (fallback path).
        foreach (var b in trayBlocks) if (b != null) Destroy(b);
        trayBlocks.Clear();
        ClearUndoHistory();   // history is round-scoped; stale on new round
    }

    // Spawns `count` random block tokens into the world-space shop (ShopController).
    // Falls back to nothing if ShopController is not present in the scene.
    public void SpawnRoundBlocks(int count)
    {
        if (cubePrefab == null || blocks == null || blocks.Length == 0) return;
        if (ShopController.Instance == null) return;

        var datas = new BlockData[count];
        for (int i = 0; i < count; i++)
            datas[i] = blocks[Random.Range(0, blocks.Length)];

        ShopController.Instance.SpawnItems(datas, cubePrefab, grid);
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
            else if (ShopController.Instance != null && ShopController.Instance.TryHandleClick())
            {
                // Shop viewport consumed the click — don't run main-camera selection.
            }
            else
            {
                TrySelectObject();
            }
        }

        // Delete — cancel current hold (Edit mode) or remove selected placed block.
        if (Input.GetKeyDown(KeyCode.Delete))
        {
            if (mode == PlacementMode.Edit)
                CancelEditMode();
            else
                TryDelete();
        }

        // Ctrl+Z — undo last placement or deletion.
        if (Input.GetKeyDown(KeyCode.Z)
            && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            TryUndo();

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
                // Non-turret blocks are locked during combat.
                if (GameFlowManager.Instance?.phase == GamePhase.Running
                    && selectedInstance.data?.blockType != BlockType.Turret)
                {
                    Debug.Log("[Placement] Block editing locked during combat.");
                    return;
                }

                isPickingUpObject = true;
                lastObjectPos   = selectedInstance.visualObject.transform.position;
                lastObjectRot   = selectedInstance.visualObject.transform.rotation;
                lastObjectCells = selectedInstance.occupiedCells.ToArray();

                // Snap depth so the preview block materialises where the picked-up block was.
                // This also prevents the camera from flying when editFocusAnchor is set below.
                SnapDepthToWorldPos(lastObjectPos);

                // Update count before removing from grid.
                ResourceManager.Instance?.OnBlockRemoved(selectedInstance.data.blockType);

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
            CancelEditMode();
        }
    }

    // Shared cancel path — called by Tab (HandleModeSwitch) and Delete key.
    void CancelEditMode()
    {
        if (isPickingUpObject)
        {
            CancelAndReturnObject();
        }
        else if (activePhysicsObject != null && _pendingShopPrice > 0)
        {
            // Player grabbed a shop item but cancelled before placing — give it back.
            ShopController.Instance?.RestoreItem(activePhysicsObject);
            _pendingShopPrice   = 0;
            currentBlock        = null;
            activePhysicsObject = null;
        }

        mode = PlacementMode.Select;
        previewParent.gameObject.SetActive(false);
        SetTrayVisible(true);
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
            // Non-turret tray tokens are locked during combat.
            if (GameFlowManager.Instance?.phase == GamePhase.Running
                && sb.data?.blockType != BlockType.Turret)
            {
                Debug.Log("[Placement] Block editing locked during combat.");
                return;
            }

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
                // Non-turret blocks are locked during combat.
                if (GameFlowManager.Instance?.phase == GamePhase.Running
                    && instance.data?.blockType != BlockType.Turret)
                {
                    Debug.Log("[Placement] Block editing locked during combat.");
                    return;
                }

                // Pick the block back up, same as Tab — remove from grid and re-enter edit mode.
                isPickingUpObject = true;
                lastObjectPos     = instance.visualObject.transform.position;
                lastObjectRot     = instance.visualObject.transform.rotation;
                lastObjectCells   = instance.occupiedCells.ToArray();

                SnapDepthToWorldPos(lastObjectPos);

                // Update count before removing from grid.
                ResourceManager.Instance?.OnBlockRemoved(instance.data.blockType);

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

        // ── Phase gate ────────────────────────────────────────────────────────
        var gfm = GameFlowManager.Instance;
        if (gfm != null && gfm.phase == GamePhase.Running)
        {
            bool isTurret = currentBlock.blockType == BlockType.Turret;
            if (!isTurret)
            {
                Debug.Log("[Placement] Block editing locked during combat.");
                return;
            }
            // Turret in combat: ensure it won't block the active enemy route.
            var worldCells = new Vector3Int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                worldCells[i] = currentGridPos + cells[i];
            if (gfm.WouldBlockPath(worldCells))
            {
                Debug.Log("[Placement] Can't place turret — would block enemy path.");
                return;
            }
        }

        // ── Resource check (new block from tray only; repositioning is free) ──
        bool isNewBlock  = !isPickingUpObject;
        int  priceForUndo = _pendingShopPrice;   // capture before zeroing (for undo refund)
        if (isNewBlock && ResourceManager.Instance != null)
        {
            if (!ResourceManager.Instance.TryBuy(_pendingShopPrice, currentBlock.blockType))
            {
                StartCoroutine(FlashPreviewRed());
                return;
            }
            _pendingShopPrice = 0;   // consumed
        }

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

        // Set scale to zero AFTER rendering so children resolve world positions
        // correctly before the parent scale collapses them for GrowIn.
        obj.transform.localScale = Vector3.zero;

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
        StartCoroutine(GrowIn(obj));

        // ── Push undo record ──────────────────────────────────────────────────
        if (isPickingUpObject)   // reposition: remember where it came from
        {
            PushUndo(new UndoRecord {
                actionType  = UndoType.Reposition,
                data        = ins.data,
                color       = currentColor,
                rotation    = _currentRotation,
                cells       = ins.occupiedCells.ToArray(),
                worldCenter = obj.transform.position,
                prevCells   = lastObjectCells,
                prevCenter  = lastObjectPos,
                prevRotation= lastObjectRot,
            });
        }
        else                     // new purchase from shop or tray
        {
            PushUndo(new UndoRecord {
                actionType  = UndoType.NewPlace,
                data        = ins.data,
                color       = currentColor,
                rotation    = _currentRotation,
                cells       = ins.occupiedCells.ToArray(),
                worldCenter = obj.transform.position,
                pricePaid   = priceForUndo,
            });
        }

        // Track placed count for shop price scaling (both new and repositioned blocks).
        ResourceManager.Instance?.OnBlockPlaced(ins.data.blockType);

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
            trayBlocks.Remove(activePhysicsObject);   // no-op if it came from ShopController
            // RemoveItemAnimated returns true when the shop owns the object and will
            // handle its animated destruction; otherwise destroy it immediately.
            bool shopHandled = ShopController.Instance?.RemoveItemAnimated(activePhysicsObject) ?? false;
            if (!shopHandled) Destroy(activePhysicsObject);
            activePhysicsObject = null;

            // Testing mode: immediately spawn a replacement so the shop never runs dry.
            if (shopHandled && (ResourceManager.Instance?.testing ?? false))
                SpawnRoundBlocks(3);
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
            // Switch material to alpha-blend so the colour's alpha is actually visible.
            var rend = c.GetComponent<Renderer>();
            if (rend != null) ConfigurePreviewMaterial(rend);
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
    // DELETE
    // =========================

    // Delete key in Select mode: remove the selected placed block from the grid.
    // Records an undo entry so it can be restored with Ctrl+Z.
    void TryDelete()
    {
        if (selectedInstance == null) return;

        // Phase gate — same rule as picking up a block.
        if (GameFlowManager.Instance?.phase == GamePhase.Running
            && selectedInstance.data?.blockType != BlockType.Turret)
        {
            Debug.Log("[Placement] Block deletion locked during combat.");
            return;
        }

        // Snapshot for undo before anything is destroyed.
        Color blockColor = selectedInstance.visualObject
                            ?.GetComponentInChildren<Renderer>()?.material.color
                            ?? Color.white;
        PushUndo(new UndoRecord {
            actionType  = UndoType.Delete,
            data        = selectedInstance.data,
            color       = blockColor,
            rotation    = selectedInstance.visualObject?.transform.rotation ?? Quaternion.identity,
            cells       = selectedInstance.occupiedCells.ToArray(),
            worldCenter = selectedInstance.visualObject?.transform.position ?? Vector3.zero,
        });

        ResourceManager.Instance?.OnBlockRemoved(selectedInstance.data.blockType);
        NotifyBlockLifted(selectedInstance.occupiedCells.ToArray());
        grid.RemoveInstance(selectedInstance);
        selectedInstance = null;
        UpdateHighlight(null);
        GameFlowManager.Instance?.EvaluateGrid();
    }

    // =========================
    // UNDO
    // =========================

    void PushUndo(UndoRecord rec)
    {
        _undoStack.Add(rec);
        if (_undoStack.Count > MaxUndoDepth)
            _undoStack.RemoveAt(0);   // drop oldest to stay within cap
    }

    public void ClearUndoHistory() => _undoStack.Clear();

    void TryUndo()
    {
        // No history.
        if (_undoStack.Count == 0)
        {
            Debug.Log("[Undo] Nothing to undo.");
            return;
        }

        // Block undo during combat (grid editing is locked).
        if (GameFlowManager.Instance?.phase == GamePhase.Running)
        {
            Debug.Log("[Undo] Undo locked during combat.");
            return;
        }

        // If currently holding a block, cancel the hold first without touching the stack.
        // The player must commit or cancel their pending action before undoing past it.
        if (mode == PlacementMode.Edit)
        {
            CancelEditMode();
            return;
        }

        var rec = _undoStack[_undoStack.Count - 1];
        _undoStack.RemoveAt(_undoStack.Count - 1);

        switch (rec.actionType)
        {
            case UndoType.NewPlace:   UndoNewPlace(rec);   break;
            case UndoType.Reposition: UndoReposition(rec); break;
            case UndoType.Delete:     UndoDelete(rec);     break;
        }

        selectedInstance = null;
        UpdateHighlight(null);
        GameFlowManager.Instance?.EvaluateGrid();
    }

    // ── Undo: new placement → remove from grid, refund price ─────────────────
    void UndoNewPlace(UndoRecord rec)
    {
        if (rec.cells == null || rec.cells.Length == 0) return;
        var ins = grid.GetInstanceAt(rec.cells[0]);
        if (ins == null || ins.data != rec.data)
        {
            Debug.LogWarning("[Undo] NewPlace target no longer matches — skipping.");
            return;
        }

        ResourceManager.Instance?.OnBlockRemoved(rec.data.blockType);
        NotifyBlockLifted(rec.cells);
        grid.RemoveInstance(ins);

        if (rec.pricePaid > 0)
            ResourceManager.Instance?.RefundBlock(rec.pricePaid);
    }

    // ── Undo: reposition → remove from new cells, restore at old cells ────────
    void UndoReposition(UndoRecord rec)
    {
        if (rec.cells == null || rec.cells.Length == 0) return;
        var ins = grid.GetInstanceAt(rec.cells[0]);
        if (ins == null || ins.data != rec.data)
        {
            Debug.LogWarning("[Undo] Reposition target no longer matches — skipping.");
            return;
        }

        ResourceManager.Instance?.OnBlockRemoved(rec.data.blockType);
        NotifyBlockLifted(rec.cells);
        grid.RemoveInstance(ins);

        // Check old cells are still free before restoring.
        bool oldCellsFree = true;
        foreach (var c in rec.prevCells)
            if (grid.IsOccupied(c)) { oldCellsFree = false; break; }

        if (oldCellsFree)
            PlaceBlockFromRecord(rec.data, rec.color, rec.prevCells, rec.prevCenter, rec.prevRotation);
        else
            Debug.LogWarning("[Undo] Reposition origin cells now occupied — block removed without restore.");
    }

    // ── Undo: delete → re-place block at its old cells ────────────────────────
    void UndoDelete(UndoRecord rec)
    {
        if (rec.cells == null || rec.cells.Length == 0) return;

        bool cellsFree = true;
        foreach (var c in rec.cells)
            if (grid.IsOccupied(c)) { cellsFree = false; break; }

        if (!cellsFree)
        {
            Debug.LogWarning("[Undo] Delete restore cells now occupied — cannot undo.");
            return;
        }

        PlaceBlockFromRecord(rec.data, rec.color, rec.cells, rec.worldCenter, rec.rotation);
    }

    // ── Shared: instantiate a placed block from saved state ───────────────────
    void PlaceBlockFromRecord(BlockData data, Color color, Vector3Int[] cells,
                              Vector3 center, Quaternion rotation)
    {
        var obj = new GameObject("PlacedBlock");
        obj.transform.position = center;
        obj.transform.rotation = rotation;

        var br      = obj.AddComponent<BlockRenderer>();
        br.cubePrefab = cubePrefab;
        Vector3Int origin = grid.WorldToGrid(center);
        var rel = new Vector3Int[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            rel[i] = cells[i] - origin;
        br.Render(origin, rel, grid.cellSize, grid);
        obj.transform.localScale = Vector3.zero;

        foreach (var r in obj.GetComponentsInChildren<Renderer>())
            r.material.color = color;

        var ins = new PlacedBlockInstance { data = data, visualObject = obj };
        foreach (var c in cells) ins.occupiedCells.Add(c);
        grid.RegisterInstance(ins);
        StartCoroutine(GrowIn(obj));
        ResourceManager.Instance?.OnBlockPlaced(data.blockType);
    }

    // =========================
    // COMBAT RIPPLE
    // =========================

    /// <summary>
    /// Called by GameFlowManager when entering the Running phase.
    /// The wave grows along the path start → end. Blocks not on the path bloom
    /// afterwards, rippling outward from their nearest path block.
    /// </summary>
    public void TriggerCombatRipple(List<FaceNode> path)
    {
        StartCoroutine(CombatRippleCoroutine(path));
    }

    System.Collections.IEnumerator CombatRippleCoroutine(List<FaceNode> path)
    {
        var all = grid.GetAllInstances();
        if (all.Count == 0) yield break;

        // Step 1: hide every placed block.
        foreach (var ins in all)
            if (ins.visualObject != null)
                ins.visualObject.transform.localScale = Vector3.zero;

        yield return null;

        // Step 2: build cell → first-occurrence-index map along the path.
        var pathIdx = new Dictionary<Vector3Int, int>();
        if (path != null)
            for (int i = 0; i < path.Count; i++)
                if (!pathIdx.ContainsKey(path[i].cell))
                    pathIdx[path[i].cell] = i;

        // Step 3: split blocks into on-path (with earliest path index) and off-path.
        var onPath  = new List<(PlacedBlockInstance ins, int idx)>();
        var offPath = new List<PlacedBlockInstance>();
        foreach (var ins in all)
        {
            if (ins.visualObject == null) continue;
            int earliest = int.MaxValue;
            foreach (var c in ins.occupiedCells)
                if (pathIdx.TryGetValue(c, out int idx) && idx < earliest)
                    earliest = idx;
            if (earliest < int.MaxValue) onPath.Add((ins, earliest));
            else offPath.Add(ins);
        }
        onPath.Sort((a, b) => a.idx.CompareTo(b.idx));

        // Step 4: sweep along the path, start → end.
        const float pathSpread = 1.2f;   // total time for the path sweep
        const float sproutDur  = 0.6f;   // matches WaveSproutIn duration
        int pathLen = path != null ? path.Count : 0;
        foreach (var (ins, idx) in onPath)
        {
            float t     = pathLen > 1 ? (float)idx / (pathLen - 1) : 0f;
            float delay = t * pathSpread;
            StartCoroutine(DelayedGrowIn(ins.visualObject, delay));
        }

        // Step 5: after the path-front passes, off-path blocks bloom outward
        // from their nearest path block — small overlap so it doesn't feel paused.
        if (offPath.Count == 0) yield break;

        float offStart = pathSpread + sproutDur * 0.35f;

        // Anchors = world positions of on-path blocks. Fallback to centroid
        // if the path has no blocks placed on it yet (defensive).
        var anchors = new List<Vector3>();
        foreach (var (ins, _) in onPath)
            if (ins.visualObject != null) anchors.Add(ins.visualObject.transform.position);
        if (anchors.Count == 0)
        {
            Vector3 c = Vector3.zero; int cn = 0;
            foreach (var ins in offPath)
                if (ins.visualObject != null) { c += ins.visualObject.transform.position; cn++; }
            if (cn > 0) anchors.Add(c / cn);
        }

        float maxOffDist = 0f;
        var offDists = new float[offPath.Count];
        for (int i = 0; i < offPath.Count; i++)
        {
            var pos = offPath[i].visualObject.transform.position;
            float minD = float.MaxValue;
            foreach (var a in anchors)
            {
                float d = Vector3.Distance(pos, a);
                if (d < minD) minD = d;
            }
            offDists[i]  = (minD == float.MaxValue) ? 0f : minD;
            if (offDists[i] > maxOffDist) maxOffDist = offDists[i];
        }
        if (maxOffDist < 0.001f) maxOffDist = 1f;

        const float offSpread = 0.6f;
        for (int i = 0; i < offPath.Count; i++)
        {
            float delay = offStart + (offDists[i] / maxOffDist) * offSpread;
            StartCoroutine(DelayedGrowIn(offPath[i].visualObject, delay));
        }
    }

    // Waits for this block's slot in the wave, then runs the wave-reveal sprout.
    System.Collections.IEnumerator DelayedGrowIn(GameObject obj, float delay)
    {
        if (delay > 0.001f) yield return new WaitForSeconds(delay);
        if (obj != null) StartCoroutine(WaveSproutIn(obj));
    }

    // ── Wave reveal: brightness flash + organic Y-leading unfurl ────────────
    // Combat-start only. Normal block placement still uses GrowIn (snappier).
    static System.Collections.IEnumerator WaveSproutIn(GameObject obj)
    {
        if (obj == null) yield break;

        var rends = obj.GetComponentsInChildren<Renderer>();
        int rc    = rends.Length;
        var orig  = new Color[rc];
        for (int i = 0; i < rc; i++)
            if (rends[i]) orig[i] = rends[i].material.color;

        const float dur = 0.6f;
        float elapsed   = 0f;

        while (elapsed < dur)
        {
            if (obj == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);

            // Y leads, X/Z follow ~15 % behind — tree-like unfurl.
            float ty  = Mathf.Clamp01(t / 0.7f);
            float ey  = 1f - Mathf.Pow(1f - ty, 4f);   // easeOutQuart
            float txz = Mathf.Clamp01((t - 0.15f) / 0.85f);
            float exz = 1f - Mathf.Pow(1f - txz, 3f);  // easeOutCubic

            obj.transform.localScale = new Vector3(exz, ey, exz);

            // Brightness from the passing wave: bright at arrival, fades into the growth.
            float bright = (1f - t) * (1f - t) * 0.7f;
            for (int i = 0; i < rc; i++)
                if (rends[i]) rends[i].material.color = Color.Lerp(orig[i], Color.white, bright);

            yield return null;
        }

        if (obj != null) obj.transform.localScale = Vector3.one;
        for (int i = 0; i < rc; i++)
            if (rends[i]) rends[i].material.color = orig[i];
    }

    // ── Growth animation: 0 → 1.12 → 1.0 with cubic ease-out ────────────────
    // Overshoot to 1.12 gives a satisfying "snap into place" feel.
    static System.Collections.IEnumerator GrowIn(GameObject obj)
    {
        if (obj == null) yield break;

        const float dur     = 0.22f;
        const float peak    = 1.12f;   // overshoot scale
        const float peakAt  = 0.55f;   // fraction of dur at which we hit peak
        float       elapsed = 0f;

        while (elapsed < dur)
        {
            if (obj == null) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dur);

            float scale;
            if (t < peakAt)
            {
                // Phase 1: 0 → peak  (ease-out cubic)
                float t1 = t / peakAt;
                float e  = 1f - (1f - t1) * (1f - t1) * (1f - t1);
                scale = e * peak;
            }
            else
            {
                // Phase 2: peak → 1  (ease-in-out)
                float t2 = (t - peakAt) / (1f - peakAt);
                float e  = t2 * t2 * (3f - 2f * t2);
                scale = Mathf.Lerp(peak, 1f, e);
            }

            obj.transform.localScale = Vector3.one * scale;
            yield return null;
        }

        if (obj != null) obj.transform.localScale = Vector3.one;
    }

    // =========================
    // SHOP GRAB
    // =========================

    // Called by ShopController when the player clicks a shop item.
    // Mirrors the tray-token grab path in TrySelectObject.
    public void GrabFromShop(SelectableBlock sb)
    {
        if (sb == null || sb.data == null) return;

        // Phase gate — same rule as tray tokens.
        if (GameFlowManager.Instance?.phase == GamePhase.Running
            && sb.data.blockType != BlockType.Turret)
        {
            Debug.Log("[Shop] Block editing locked during combat.");
            return;
        }

        currentBlock        = sb.data;
        currentColor        = sb.GetComponentInChildren<Renderer>()?.material.color
                              ?? GetRandomColor();
        activePhysicsObject = sb.gameObject;
        selectedInstance    = null;
        isPickingUpObject   = false;   // new purchase, not a reposition
        _pendingShopPrice   = sb.cachedPrice;

        UpdateHighlight(null);
        EnterEditMode(null);
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
            obj.transform.localScale = Vector3.zero;

            PlacedBlockInstance ins = new()
            {
                data         = currentBlock,
                visualObject = obj
            };

            foreach (var c in lastObjectCells)
                ins.occupiedCells.Add(c);

            grid.RegisterInstance(ins);
            StartCoroutine(GrowIn(obj));
            // Restore count — OnBlockRemoved was called on pickup, balance it back.
            ResourceManager.Instance?.OnBlockPlaced(ins.data.blockType);
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

    // Switches the renderer's instanced material to alpha-blend transparent mode.
    // Handles URP Lit/Unlit (_Surface property) and Built-in Standard (_Mode).
    static void ConfigurePreviewMaterial(Renderer rend)
    {
        var mat = rend.material;   // Unity auto-creates a per-instance copy here
        if (mat.HasProperty("_Surface"))
        {
            // URP Lit / Unlit
            mat.SetFloat("_Surface", 1f);    // 1 = Transparent
            mat.SetFloat("_ZWrite",  0f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        else if (mat.HasProperty("_Mode"))
        {
            // Built-in Standard fallback
            mat.SetFloat("_Mode", 3f);       // 3 = Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }

    // Flashes the preview cubes solid red for 0.3 s to signal "can't afford".
    System.Collections.IEnumerator FlashPreviewRed()
    {
        Color flashColor = new Color(1f, 0.15f, 0.15f, 0.85f);
        foreach (var c in previewCubes)
            if (c != null && c.activeSelf) c.GetComponent<Renderer>().material.color = flashColor;

        yield return new WaitForSeconds(0.3f);
        // UpdatePreview() will restore the correct tint next frame automatically.
    }
}
