using System.Collections.Generic;
using UnityEngine;

// Rectangular multi-select ("box select") on the Select-mode board, plus the
// two batch actions it unlocks: move the whole selection as one rigid group,
// or sell all of it.
//
// Deliberately kept as its OWN, fully independent state machine rather than
// threading batch behaviour through the existing single-block fields
// (currentBlock / isPickingUpObject / mode==Edit). Those are exercised by a
// lot of surrounding code (TryPlace, HandleKeyboardOffset, HandleRotate, the
// tutorial's placementConstraint, undo...) that all assume "one block, one
// currentBlock". Reusing them for an N-block operation would mean auditing
// every one of those call sites for what happens when N != 1; owning a
// separate `_batchMoving` flag instead means Update() can just skip ALL of
// that machinery for the frame and this feature can't corrupt (or be
// corrupted by) the single-block path.
public partial class PlacementController
{
    // ── Box-select state ─────────────────────────────────────────────────────
    bool    _boxDragCandidate;   // mouse went down on empty space; waiting to see if it's a drag
    bool    _boxDragging;        // past the drag threshold — actively marqueeing
    Vector2 _boxStartScreen;
    const float BoxDragThresholdPx = 6f;

    readonly List<PlacedBlockInstance> _multiSelection = new();

    bool _multiMoveRequested;
    bool _multiSellRequested;

    // ── Batch-move state ─────────────────────────────────────────────────────
    class BatchMoveRecord
    {
        public BlockData    data;
        public Vector3Int[] origCells;   // absolute — used to restore exactly on cancel
        public Vector3Int[] baseRel;     // UNROTATED offsets from the group anchor at pickup — the fixed shape group-rotate spins
        public Vector3Int[] relCells;    // baseRel rotated by the committed target rotation — used to project to a new spot
        public Color        color;
        public BlockColor   synergyColor;
        public Quaternion   rotation;       // current mesh orientation (mutated by group rotate)
        public Quaternion   origRotation;   // orientation at pickup — restored verbatim on cancel
        public int basicPowerUpgradeLevel, basicBurstUpgradeLevel, aoeFireUpgradeLevel, aoeGravityUpgradeLevel;
    }

    bool _batchMoving;
    readonly List<BatchMoveRecord> _batchRecords = new();
    readonly List<GameObject>      _batchPreviewCubes = new();

    // Group-rotation animation. _batchTargetRot snaps 90° per keypress (drives the
    // committed layout); _batchDisplayRot slerps toward it and drives ONLY the
    // preview cube positions — the exact same "slerp the rotation, round the cells"
    // trick single-block editing uses (GetRotatedCells rounds the slerped
    // _currentRotation), so the group spins with the same feel instead of snapping.
    Quaternion _batchTargetRot  = Quaternion.identity;
    Quaternion _batchDisplayRot = Quaternion.identity;

    // ── Public entry point: called once per frame from Update() ─────────────

    // Handles the mouse-down that STARTS a potential drag. Only called when
    // nothing else claimed the click (no panel, not Edit mode, not the shop) —
    // see the call site in Update().
    void TryBeginBoxSelect()
    {
        if (RaycastHitsSelectableTarget()) { TrySelectObject(); return; }

        _boxDragCandidate = true;
        _boxDragging      = false;
        _boxStartScreen   = VirtualCursor.Position;
    }

    // Advances an in-flight drag (or resolves a non-drag click). Called every
    // frame from Update() while a candidate is live.
    void UpdateBoxSelect()
    {
        if (!_boxDragCandidate) return;

        if (mode != PlacementMode.Select) { CancelBoxSelect(); return; }

        Vector2 cur = VirtualCursor.Position;
        if (!_boxDragging && (cur - _boxStartScreen).sqrMagnitude >= BoxDragThresholdPx * BoxDragThresholdPx)
            _boxDragging = true;

        if (!Input.GetMouseButtonUp(0)) return;   // still held — keep dragging next frame

        if (_boxDragging) FinalizeBoxSelection(_boxStartScreen, cur);
        else              TrySelectObject();       // plain click on empty space — normal deselect

        _boxDragCandidate = false;
        _boxDragging      = false;
    }

    void CancelBoxSelect()
    {
        _boxDragCandidate = false;
        _boxDragging      = false;
    }

    // Replicates TrySelectObject's target-recognition (chaos block / tray token /
    // endpoint / placed block) WITHOUT any of its side effects — used purely to
    // decide "did this mouse-down land on something that already has its own
    // click behaviour", so box-select only ever starts from genuinely empty space.
    bool RaycastHitsSelectableTarget()
    {
        Ray ray = cam.myCam.ScreenPointToRay(VirtualCursor.Position);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return false;

        if (hit.transform.GetComponentInParent<ChaosBlockUnit>()   != null) return true;
        if (hit.transform.GetComponentInParent<SelectableBlock>()  != null) return true;
        if (hit.transform.GetComponentInParent<GridEndpoint>()     != null) return true;

        Vector3Int gPos = grid.WorldToGrid(hit.point - hit.normal * (grid.cellSize * 0.1f));
        return grid.GetInstanceAt(gPos) != null;
    }

    // ── Finalize the drag into a selection ───────────────────────────────────

    void FinalizeBoxSelection(Vector2 startScreen, Vector2 endScreen)
    {
        // A box-select always REPLACES whatever was selected before, single or multi.
        ClearMultiSelection();
        selectedInstance  = null;
        _selectedEndpoint = null;
        _selectedChaos    = null;
        UpdateHighlight(null);

        Rect box = ScreenRectFromPoints(startScreen, endScreen);

        Vector3 worldSum = Vector3.zero;
        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.visualObject == null || ins.occupiedCells.Count == 0) continue;

            Vector3 center = Vector3.zero;
            foreach (var c in ins.occupiedCells) center += grid.GridToWorld(c);
            center /= ins.occupiedCells.Count;

            Vector3 sp = cam.myCam.WorldToScreenPoint(center);
            if (sp.z <= 0f) continue;   // behind the camera

            if (box.Contains(new Vector2(sp.x, sp.y)))
            {
                _multiSelection.Add(ins);
                worldSum += center;
            }
        }

        if (_multiSelection.Count == 0) return;   // empty drag — nothing to show

        foreach (var ins in _multiSelection)
            SetOutlineHighlight(ins.visualObject, restoreDefault: false);

        Vector3 worldCentroid = worldSum / _multiSelection.Count;
        MultiSelectPanel.Show(_multiSelection.Count, worldCentroid,
            onMove: () => _multiMoveRequested = true,
            onSell: () => _multiSellRequested = true);
    }

    static Rect ScreenRectFromPoints(Vector2 a, Vector2 b)
    {
        float xMin = Mathf.Min(a.x, b.x), xMax = Mathf.Max(a.x, b.x);
        float yMin = Mathf.Min(a.y, b.y), yMax = Mathf.Max(a.y, b.y);
        return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
    }

    public void ClearMultiSelection()
    {
        foreach (var ins in _multiSelection)
            if (ins?.visualObject != null) SetOutlineHighlight(ins.visualObject, restoreDefault: true);
        _multiSelection.Clear();
        MultiSelectPanel.Hide();
    }

    // ── Batch guard: turret-support check generalized to a WHOLE removal set ──
    // Same rule as FindOrphanedTurret (a turret must keep at least one 26-neighbour
    // outside whatever's being removed), just checked against every cell in the
    // batch at once instead of one instance's cells.
    PlacedBlockInstance FindOrphanedTurretForBatch(HashSet<PlacedBlockInstance> toRemove)
    {
        if (toRemove == null || toRemove.Count == 0 || grid == null) return null;

        var excludeCells = new HashSet<Vector3Int>();
        foreach (var r in toRemove)
            if (r?.occupiedCells != null)
                foreach (var c in r.occupiedCells) excludeCells.Add(c);

        foreach (var other in grid.GetAllInstances())
        {
            if (other == null || toRemove.Contains(other) || other.data == null) continue;
            if (!TurretTypes.Is(other.data.blockType)) continue;
            if (!HasExternalSupportExcluding(other, excludeCells)) return other;
        }
        return null;
    }

    bool HasExternalSupportExcluding(PlacedBlockInstance instance, HashSet<Vector3Int> excludeCells)
    {
        foreach (var cell in instance.occupiedCells)
            for (int dx = -1; dx <= 1; dx++)
            for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                var n = new Vector3Int(cell.x + dx, cell.y + dy, cell.z + dz);
                if (!grid.IsOccupied(n)) continue;
                if (excludeCells.Contains(n)) continue;
                if (instance.occupiedCells.Contains(n)) continue;   // own cell — not external
                return true;
            }
        return false;
    }

    // ── Sell all ──────────────────────────────────────────────────────────────
    void SellAllSelected()
    {
        if (GameFlowManager.SettlementUp) return;
        if (_multiSelection.Count == 0) return;

        if (GameFlowManager.Instance?.phase == GamePhase.Running)
        {
            ShowPlacementPopup("Selling is locked during combat.");
            return;
        }
        if (!TutorialDirector.CanSell()) return;   // tutorial gate, same as single sell

        // Locked (level-fixed) blocks were never sellable individually either —
        // skip them rather than vetoing the whole batch over something that was
        // never going to work anyway.
        var sellable = new List<PlacedBlockInstance>();
        int lockedSkipped = 0;
        foreach (var ins in _multiSelection)
        {
            if (ins?.data == null) continue;
            if (ins.locked) { lockedSkipped++; continue; }
            sellable.Add(ins);
        }

        if (sellable.Count == 0)
        {
            ShowPlacementPopup("Nothing sellable in the selection.");
            ClearMultiSelection();
            return;
        }

        // Orphan check is all-or-nothing: figuring out which SUBSET could be sold
        // without stranding the turret is a much harder problem than this needs
        // to solve — reject the whole action and tell the player to move the
        // turret first, exactly like the single-sell guard already does.
        var orphan = FindOrphanedTurretForBatch(new HashSet<PlacedBlockInstance>(sellable));
        if (orphan != null)
        {
            ShowPlacementPopup("Selling this would strand a turret with nothing to attach to — move it first.");
            return;
        }

        int totalRefund = 0;
        foreach (var ins in sellable)
        {
            bool isTurret = TurretTypes.Is(ins.data.blockType);
            int  refund   = ComputeSellRefund(ins);
            Vector3 soldPos = ins.visualObject != null ? ins.visualObject.transform.position : transform.position;

            ResourceManager.Instance?.OnBlockRemoved(ins.data.blockType);
            SynergyEvaluator.Instance?.OnPieceRemoved(ins.placedPiece);
            NotifyBlockLifted(ins.occupiedCells.ToArray());
            grid.RemoveInstance(ins);   // destroys visualObject

            if (isTurret) ResourceManager.Instance?.AddTurretCurrency(refund);
            else          ResourceManager.Instance?.RefundBlock(refund);
            CurrencyFlyFx.Fly(soldPos, isTurret, refund);
            BlockSold?.Invoke(ins.data);

            totalRefund += refund;
        }

        ClearMultiSelection();
        GameFlowManager.Instance?.EvaluateGrid();

        string msg = lockedSkipped > 0
            ? $"Sold {sellable.Count} for +{totalRefund} ({lockedSkipped} locked block(s) skipped)"
            : $"Sold {sellable.Count} for +{totalRefund}";
        ShowPlacementPopup(msg);
    }

    // ── Move as a whole ───────────────────────────────────────────────────────

    void TryBeginBatchMove()
    {
        if (GameFlowManager.Instance?.phase == GamePhase.Running)
        {
            ShowPlacementPopup("Editing placed objects is locked during combat.");
            return;
        }
        if (_multiSelection.Count == 0) return;

        // All-or-nothing guards — a batch move can't leave the board in a state
        // where some pieces moved and others silently didn't.
        foreach (var ins in _multiSelection)
        {
            if (ins.locked)
            {
                ShowPlacementPopup("Selection includes level-fixed blocks that can't be moved.");
                return;
            }
            if (ins.sealedByEnemy)
            {
                ShowPlacementPopup("Selection includes an enemy-sealed block — it can only be sold, not moved.");
                return;
            }
        }

        var moveSet = new HashSet<PlacedBlockInstance>(_multiSelection);
        var orphan  = FindOrphanedTurretForBatch(moveSet);
        if (orphan != null)
        {
            ShowPlacementPopup("Moving this would strand a turret with nothing to attach to.");
            return;
        }

        // Snapshot + clear the selection UI now, BEFORE anything is destroyed —
        // every member is about to be removed unconditionally (the guards above
        // already vetoed the whole batch if any single one couldn't be), so
        // there's nothing left to restore an outline colour on afterward. Clearing
        // first (rather than calling ClearMultiSelection() after the loop below)
        // avoids depending on Unity's destroyed-object fake-null check to make
        // that a no-op — this way it's just never attempted.
        var toMove = new List<PlacedBlockInstance>(_multiSelection);
        _multiSelection.Clear();
        MultiSelectPanel.Hide();
        HideRangeIndicator();

        // Anchor = minimum corner of the combined footprint. Doesn't matter WHERE
        // it sits, only that every record's relCells is measured from the same
        // fixed point, so the whole group can be re-projected by moving one cell.
        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (var ins in toMove)
            foreach (var c in ins.occupiedCells)
                min = Vector3Int.Min(min, c);

        _batchRecords.Clear();
        foreach (var ins in toMove)
        {
            var rend = ins.visualObject != null ? ins.visualObject.GetComponentInChildren<Renderer>() : null;
            var rec = new BatchMoveRecord
            {
                data         = ins.data,
                origCells    = ins.occupiedCells.ToArray(),
                color        = rend != null ? MpbColor.Get(rend) : Color.white,
                synergyColor = ins.color,
                rotation     = ins.visualObject != null ? ins.visualObject.transform.rotation : Quaternion.identity,
                origRotation = ins.visualObject != null ? ins.visualObject.transform.rotation : Quaternion.identity,
                basicPowerUpgradeLevel = ins.basicPowerUpgradeLevel,
                basicBurstUpgradeLevel = ins.basicBurstUpgradeLevel,
                aoeFireUpgradeLevel    = ins.aoeFireUpgradeLevel,
                aoeGravityUpgradeLevel = ins.aoeGravityUpgradeLevel,
            };
            rec.baseRel  = new Vector3Int[rec.origCells.Length];
            rec.relCells = new Vector3Int[rec.origCells.Length];
            for (int i = 0; i < rec.origCells.Length; i++)
            {
                rec.baseRel[i]  = rec.origCells[i] - min;
                rec.relCells[i] = rec.baseRel[i];   // no rotation yet
            }

            // Lift it off the grid NOW — same bookkeeping as PickUpSelected, just
            // looped. Safe to do unconditionally here because every guard above
            // already passed for the WHOLE batch before this loop started.
            ResourceManager.Instance?.OnBlockRemoved(ins.data.blockType);
            SynergyEvaluator.Instance?.OnPieceRemoved(ins.placedPiece);
            NotifyBlockLifted(ins.occupiedCells.ToArray());
            grid.RemoveInstance(ins);   // destroys ins.visualObject

            _batchRecords.Add(rec);
        }

        // The single-block edit preview pool (previewCubes) and the batch pool
        // (_batchPreviewCubes) share previewParent. Single-block editing hides its
        // ghosts by toggling previewParent OFF, but leaves each cube individually
        // active — so turning previewParent back ON here would re-reveal a stale
        // row of green ghosts sitting at the LAST single-block edit position (they
        // then only vanish when previewParent goes off again at commit/cancel,
        // reading exactly as "blocks that didn't clear until placement"). Hide them
        // explicitly; RenderBatchPreview manages _batchPreviewCubes on top.
        foreach (var c in previewCubes) if (c != null) c.SetActive(false);

        _batchTargetRot = _batchDisplayRot = Quaternion.identity;
        previewParent.gameObject.SetActive(true);
        _batchMoving = true;
    }

    // Drives the held group every frame while _batchMoving is true. Owns the
    // click (commit) and Delete (cancel) for that frame — see the early-return
    // gate in Update().
    void UpdateBatchMove()
    {
        // 1/2/3 rotate the WHOLE group 90° about world X/Y/Z — same keys and world
        // frame as single-block editing (HandleRotate), so the two feel identical.
        if (Input.GetKeyDown(KeyCode.Alpha1)) RotateBatch(Quaternion.Euler(90, 0, 0));
        if (Input.GetKeyDown(KeyCode.Alpha2)) RotateBatch(Quaternion.Euler(0, 90, 0));
        if (Input.GetKeyDown(KeyCode.Alpha3)) RotateBatch(Quaternion.Euler(0, 0, 90));
        if (GamepadInput.RotateDown)          RotateBatch(Quaternion.Euler(0, 90, 0));   // shoulder mirrors yaw, same as single-block

        // Ease the preview toward the snapped target — identical curve to the
        // single-block _currentRotation slerp (same rotateSpeed), so the group spin
        // reads the same. Only the visual preview uses this; the committed layout
        // (relCells) is already the fully-rotated target set in RotateBatch.
        _batchDisplayRot = Quaternion.Slerp(_batchDisplayRot, _batchTargetRot,
                                            1f - Mathf.Exp(-rotateSpeed * Time.deltaTime));

        Vector3Int anchor = baseGridPos;   // cursor-tracked, same field HandleMouseMove already updates

        var targetCells = new List<Vector3Int>();
        bool allValid = true;
        foreach (var rec in _batchRecords)
            foreach (var rel in rec.relCells)
            {
                var cell = anchor + rel;
                if (grid.IsOccupied(cell)) allValid = false;
                targetCells.Add(cell);
            }

        if (allValid && !grid.HasOccupiedNeighbor26(targetCells))
            allValid = false;   // the whole formation must touch something existing

        RenderBatchPreview(anchor, allValid);

        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverSelectionPanel() || HudSidePanels.PointerOver || PointerOverInfoPanel())
            {
                // click landed on UI — ignore, keep holding the batch
            }
            else if (allValid)
            {
                CommitBatchMove(anchor);
            }
            else
            {
                ShowPlacementPopup("Can't place the group there.");
            }
        }

        if (Input.GetKeyDown(KeyCode.Delete) || GamepadInput.DeleteDown)
            CancelBatchMove();
    }

    // Snaps the committed group layout to the next 90° step. _batchTargetRot
    // accumulates the world-frame rotation; the committed integer cells (relCells)
    // and each block's mesh orientation (rotation) are recomputed from the fixed,
    // never-mutated baseRel — recomputing from base (rather than re-rounding the
    // already-rotated cells) avoids any cumulative rounding drift. The preview then
    // eases toward this target via _batchDisplayRot (see UpdateBatchMove). Cell
    // offsets drive the footprint; rotation spins each mesh so a turret faces the
    // way the formation turned. 90° principal-axis rotations map integer cells
    // bijectively, so no two cells ever collapse together.
    void RotateBatch(Quaternion delta)
    {
        if (_batchRecords.Count == 0) return;
        AudioManager.Instance?.PlayRotate();

        _batchTargetRot = delta * _batchTargetRot;
        ApplyBatchLayout(_batchTargetRot);
    }

    // Rotates every record's baseRel by `rot`, rounds to cells, and re-anchors the
    // group's min corner to rel (0,0,0) so it keeps tracking the cursor the same way
    // it did at pickup. Writes the committed relCells + per-record mesh rotation.
    void ApplyBatchLayout(Quaternion rot)
    {
        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (var rec in _batchRecords)
            foreach (var b in rec.baseRel)
                min = Vector3Int.Min(min, Vector3Int.RoundToInt(rot * (Vector3)b));

        foreach (var rec in _batchRecords)
        {
            for (int i = 0; i < rec.baseRel.Length; i++)
                rec.relCells[i] = Vector3Int.RoundToInt(rot * (Vector3)rec.baseRel[i]) - min;
            rec.rotation = rot * rec.origRotation;   // world-frame, matches HandleRotate
        }
    }

    void RenderBatchPreview(Vector3Int anchor, bool valid)
    {
        int needed = 0;
        foreach (var rec in _batchRecords) needed += rec.baseRel.Length;

        while (_batchPreviewCubes.Count < needed)
        {
            var c = Instantiate(cubePrefab, previewParent);
            foreach (var col in c.GetComponentsInChildren<Collider>()) col.enabled = false;
            var rend = c.GetComponent<Renderer>();
            if (rend != null) ConfigurePreviewMaterial(rend);
            _batchPreviewCubes.Add(c);
        }
        for (int i = 0; i < _batchPreviewCubes.Count; i++)
            _batchPreviewCubes[i].SetActive(i < needed);

        Color tint = valid
            ? new Color(0.25f, 1.00f, 0.35f, 0.55f)
            : new Color(1.00f, 0.20f, 0.20f, 0.45f);

        // Position from the SLERPED display rotation (rounded + min-anchored), the
        // same way single-block preview rounds the slerped _currentRotation — this
        // is what animates the spin. At rest _batchDisplayRot == _batchTargetRot so
        // the cubes sit exactly on the committed relCells.
        Vector3Int min = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (var rec in _batchRecords)
            foreach (var b in rec.baseRel)
                min = Vector3Int.Min(min, Vector3Int.RoundToInt(_batchDisplayRot * (Vector3)b));

        int idx = 0;
        foreach (var rec in _batchRecords)
            foreach (var b in rec.baseRel)
            {
                var disp = Vector3Int.RoundToInt(_batchDisplayRot * (Vector3)b) - min;
                var cube = _batchPreviewCubes[idx++];
                cube.transform.position = grid.GridToWorld(anchor + disp);
                cube.GetComponent<Renderer>().material.color = tint;
            }
    }

    void HideBatchPreview()
    {
        foreach (var c in _batchPreviewCubes) if (c != null) c.SetActive(false);
    }

    void CommitBatchMove(Vector3Int anchor)
    {
        HideBatchPreview();

        foreach (var rec in _batchRecords)
        {
            var worldCells = new Vector3Int[rec.relCells.Length];
            for (int i = 0; i < rec.relCells.Length; i++)
                worldCells[i] = anchor + rec.relCells[i];

            var ins = ReconstructBlock(rec, worldCells, rec.rotation);
            if (ins == null) continue;

            ResourceManager.Instance?.OnBlockPlaced(ins.data.blockType);
            BlockPlaced?.Invoke(ins.data, ins.occupiedCells.ToArray());
        }

        _batchRecords.Clear();
        _batchMoving = false;
        previewParent.gameObject.SetActive(false);
        GameFlowManager.Instance?.EvaluateGrid();
        ShowPlacementPopup("Group moved");
    }

    void CancelBatchMove()
    {
        HideBatchPreview();

        foreach (var rec in _batchRecords)
        {
            // Restore to the EXACT pickup state — origCells and origRotation, not the
            // (possibly group-rotated) working copies — so a cancel is a true undo.
            var ins = ReconstructBlock(rec, rec.origCells, rec.origRotation);
            if (ins == null) continue;

            ResourceManager.Instance?.OnBlockPlaced(ins.data.blockType);
            // No BlockPlaced event here — this is a cancelled move restoring the
            // status quo, not a new placement the tutorial / listeners should react to.
        }

        _batchRecords.Clear();
        _batchMoving = false;
        previewParent.gameObject.SetActive(false);
        GameFlowManager.Instance?.EvaluateGrid();
    }

    // Mirrors PlaceBlockDirect's construction (turret vs BlockRenderer + colour),
    // reused for both committing a move and restoring on cancel. Not routed
    // through PlaceBlockDirect itself because that method is also the snapshot/
    // starting-layout restore path — keeping this local avoids adding a batch-only
    // parameter to a function other systems depend on.
    PlacedBlockInstance ReconstructBlock(BatchMoveRecord rec, Vector3Int[] worldCells, Quaternion rotation)
    {
        if (rec?.data == null || worldCells == null || worldCells.Length == 0) return null;

        Vector3 center = Vector3.zero;
        foreach (var c in worldCells) center += grid.GridToWorld(c);
        center /= worldCells.Length;

        var obj = new GameObject("PlacedBlock");
        obj.transform.position = center;
        obj.transform.rotation = rotation;

        if (TurretTypes.Is(rec.data.blockType))
        {
            SpawnTurretVisual(obj, rec.data);
        }
        else
        {
            var br = obj.AddComponent<BlockRenderer>();
            br.cubePrefab = cubePrefab;

            var origin = grid.WorldToGrid(center);
            var rel    = new Vector3Int[worldCells.Length];
            for (int i = 0; i < worldCells.Length; i++) rel[i] = worldCells[i] - origin;
            br.Render(origin, rel, grid.cellSize, grid);

            foreach (var r in obj.GetComponentsInChildren<Renderer>())
                MpbColor.Set(r, rec.color);
        }

        var ins = new PlacedBlockInstance
        {
            data         = rec.data,
            visualObject = obj,
            color        = rec.synergyColor,
            basicPowerUpgradeLevel = rec.basicPowerUpgradeLevel,
            basicBurstUpgradeLevel = rec.basicBurstUpgradeLevel,
            aoeFireUpgradeLevel    = rec.aoeFireUpgradeLevel,
            aoeGravityUpgradeLevel = rec.aoeGravityUpgradeLevel,
        };
        foreach (var c in worldCells) ins.occupiedCells.Add(c);

        RegisterPlacedBlock(ins);
        ins.placedPiece = SynergyEvaluator.Instance?.OnPiecePlaced(ins.data, ins.color, ins.occupiedCells.ToArray());

        obj.transform.localScale = Vector3.zero;
        StartCoroutine(GrowIn(obj));

        return ins;
    }

    // ── Drag-box visual (IMGUI, matching this file's existing popup/panel style) ──
    void DrawBoxSelect()
    {
        if (!_boxDragging) return;

        Vector2 cur = VirtualCursor.Position;
        Rect worldRect = ScreenRectFromPoints(_boxStartScreen, cur);
        // Input/world space is bottom-left origin; GUI space is top-left.
        Rect guiRect = new Rect(worldRect.x, Screen.height - worldRect.yMax, worldRect.width, worldRect.height);

        Color prev = GUI.color;
        GUI.color = new Color(0.4f, 0.85f, 1f, 0.12f);
        GUI.DrawTexture(guiRect, Texture2D.whiteTexture);
        GUI.color = new Color(0.4f, 0.85f, 1f, 0.9f);
        float t = 2f;
        GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, guiRect.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.x, guiRect.yMax - t, guiRect.width, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.x, guiRect.y, t, guiRect.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(guiRect.xMax - t, guiRect.y, t, guiRect.height), Texture2D.whiteTexture);
        GUI.color = prev;
    }
}
