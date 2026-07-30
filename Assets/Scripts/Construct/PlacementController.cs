using System.Collections.Generic;
using UnityEngine;

public enum PlacementMode { Edit, Select }

[RequireComponent(typeof(GridSystem))]
public partial class PlacementController : MonoBehaviour
{
    public PlacementMode mode = PlacementMode.Select;
    public BlockData[] blocks;
    public GridSystem grid;
    public BlockData currentBlock;
    // Synergy theme color of the currently-held token. Set when picking up
    // from shop / tray / repositioning an existing block; consumed by
    // TryPlace to forward into SynergyEvaluator. BlockColor.None means the
    // piece won't participate in synergies.
    public BlockColor currentSynergyColor = BlockColor.None;
    public GameObject cubePrefab;
    public Transform previewParent;
    public OrbitCamera cam;

    // Tutorial hook: when set, a placement is only allowed if this returns true for
    // the would-be block + world cells (after geometry is otherwise valid). Lets the
    // tutorial force the player onto the highlighted guide.
    public System.Func<BlockData, Vector3Int[], bool> placementConstraint;
    // Fired after a block is successfully placed (block + absolute world cells).
    public static event System.Action<BlockData, Vector3Int[]> BlockPlaced;
    // Fired when the held block is rotated (tutorial 'rotate' step).
    public static event System.Action BlockRotated;
    // Fired when a block is grabbed (bought) from the shop.
    public static event System.Action<BlockData> BlockPurchased;
    // Fired when the player selects (clicks) a placed block.
    public static event System.Action<BlockData> BlockSelected;
    // Fired when the selected block is sold.
    public static event System.Action<BlockData> BlockSold;
    // Fired when the shop is successfully refreshed.
    public static event System.Action ShopRefreshed;
    // Fired when a selected turret is successfully upgraded.
    public static event System.Action TurretUpgraded;
    // Fired with the highest branch level reached on that turret after an upgrade.
    public static event System.Action<int> TurretUpgradeLevelReached;
    public Vector3Int SnappedGridPos => baseGridPos;
    public Vector3Int CurrentGridPos => currentGridPos;
    [Range(0.5f, 4f)] public float snapGridRadius = 1.5f;
    public float minDepth = 2f, maxDepth = 40f, scrollSpeed = 3f, rotateSpeed = 10f;
    public float panSpeed = 8f;

    [Header("Cube Palette (Constructivism)")]
    [Tooltip("Block-type cubes pick a random colour from this palette. Strong saturated reds / yellows / blues / blacks read clearly against a textured skybox.")]
    public Color[] blockPalette = new[]
    {
        new Color(0.85f, 0.18f, 0.12f),  // red
        new Color(0.10f, 0.20f, 0.65f),  // deep blue
        new Color(0.95f, 0.75f, 0.10f),  // yellow
        new Color(0.10f, 0.50f, 0.30f),  // green
        new Color(0.10f, 0.10f, 0.14f),  // near-black
        new Color(0.92f, 0.90f, 0.84f),  // cream
    };
    [Tooltip("Turret cubes pick from this cooler palette so they read as a different family.")]
    public Color[] turretPalette = new[]
    {
        new Color(0.25f, 0.85f, 0.95f),  // cyan
        new Color(0.40f, 0.60f, 1.00f),  // sky blue
        new Color(0.55f, 0.30f, 0.90f),  // violet
        new Color(0.92f, 0.90f, 0.84f),  // cream
    };

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

    [Header("Selection highlight")]
    [Tooltip("Color the existing cartoon outline (GeoWorld/BlockOutline) recolors to while a block is selected. On deselect it reverts to the material's authored color. NO second extruded outline is added on top.")]
    public Color selectionOutlineColor = new Color(1f, 0.85f, 0.10f, 1f);

    // Shared MPB used by UpdateHighlight to override _OutlineColor on the
    // existing cartoon outline material — preserves the shared material asset
    // (no instancing break) and never adds a second outline mesh.
    static readonly int _OutlineColorID = Shader.PropertyToID("_OutlineColor");
    MaterialPropertyBlock _highlightMpb;

    private Vector3Int baseGridPos, currentGridPos, manualOffset;
    private float _depth = 10f;

    [Tooltip("Ghost glide speed when GameSettings.SmoothBlockEditing is on — matches LevelMapController's ghostFollowSpeed.")]
    public float ghostFollowSpeed = 16f;
    Vector3 _ghostVisualAnchor;
    bool    _ghostAnchorSnap = true;   // true = next UpdatePreview() snaps instantly instead of gliding in
    [Header("Ortho placement")]
    [Tooltip("Current build-plane Y in cells when camera is orthographic. Scroll wheel changes it.")]
    public int _buildY = 0;
    [Tooltip("Clamp range for the ortho build-plane Y.")]
    public Vector2Int buildYRange = new Vector2Int(0, 20);
    private Quaternion _currentRotation = Quaternion.identity, _targetRotation = Quaternion.identity;


    [Header("Shop Refresh")]
    public int refreshBaseCost = 5;
    public int refreshCostStep = 2;
    public int RefreshCost => currentRefreshCost;
    int currentRefreshCost;
    // Read-only: the rotation actually applied to the preview/placed block
    // this frame. PlacementHintOverlay reads it to size hint arrows.
    public Quaternion CurrentRotation => _currentRotation;
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
    private int lastBasicPowerUpgradeLevel;
    private int lastBasicBurstUpgradeLevel;
    private int lastAoeFireUpgradeLevel;
    private int lastAoeGravityUpgradeLevel;

    // Tray tracking kept so we can show/hide tokens on edit mode enter/exit.
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
        public int          pricePaid;  // > 0 only for NewPlace refunded on undo
        public int          basicPowerUpgradeLevel;
        public int          basicBurstUpgradeLevel;
        public int          aoeFireUpgradeLevel;
        public int          aoeGravityUpgradeLevel;
        // Reposition only: state before the move
        public Vector3Int[] prevCells;
        public Vector3      prevCenter;
        public Quaternion   prevRotation;
        public int          prevBasicPowerUpgradeLevel;
        public int          prevBasicBurstUpgradeLevel;
        public int          prevAoeFireUpgradeLevel;
        public int          prevAoeGravityUpgradeLevel;
    }

    public static PlacementController Instance;

    string   _popupMsg;
    float    _popupExpire;
    float    _popupDuration;
    GUIStyle _popupStyle;

    // ── Selection info panel (Arknights-style) ────────────────────────────────
    [Header("Selection info panel")]
    [Tooltip("Color (with alpha) of the semi-transparent attack-range sphere shown when a turret is selected.")]
    public Color rangeSphereColor = new Color(1f, 0.85f, 0.20f, 0.12f);
    [Tooltip("Color (with alpha) of the red SHADOW VOLUME filling the range a turret can't shoot into (blocks occlude line of sight). Matches TurretController's LOS test. Lowish alpha — the translucent shell overlaps itself.")]
    public Color rangeBlockedColor = new Color(1f, 0.20f, 0.14f, 0.32f);
    [Tooltip("Fraction of a block's recomputed base price returned when sold.")]
    [Range(0f, 1f)] public float sellRefundFraction = 0.5f;

    // The shared turret model (BlockData.turretPrefab → tryprism) is authored
    // tiny, so every spawn site has to scale it up by the same amount. It used to
    // be a bare 50 in PlaceBlock and a bare 1 in the two PlaceBlockDirect paths —
    // meaning turrets restored from a save OR spawned as a level's starting
    // layout came out at 1/50 size and read as "not there at all", even though
    // they were fully functional and firing.
    // Legacy fixed multiplier — now only a FALLBACK for a turret prefab we can't
    // measure (no renderers / degenerate bounds). The normal path fits each turret
    // to one grid cell by its own measured bounds instead (see SpawnTurretVisual),
    // so prefabs of different intrinsic mesh size all read the same on the board.
    public const float TurretVisualScale = 50f;

    // How much of one grid cell a fitted turret should span (largest dimension).
    // 1 = exactly cube-sized; drop toward ~0.85 if turrets should sit a touch
    // smaller than a full block.
    public const float TurretVisualCellFraction = 1f;

    [Tooltip("Seconds for the info panel + turret range to pop in when selected.")]
    [Range(0.01f, 0.6f)] public float selectionPopDuration = 0.16f;

    // Cached screen rect of the info panel this frame; used to swallow clicks so
    // clicking the panel doesn't deselect. default (zero) when hidden.
    Rect _panelRect;
    // Deferred button actions — set in OnGUI, consumed in Update so grid/mode
    // mutations never run mid-IMGUI-layout (which corrupts GUILayout state).
    bool _panelPickUpRequested;
    bool _panelSellRequested;
    bool _panelPowerUpgradeRequested;
    bool _panelBurstUpgradeRequested;
    bool _panelAoeFireUpgradeRequested;
    bool _panelAoeGravityUpgradeRequested;

    // Auto-size: the selection panel hugs its measured content height (from the
    // previous repaint), so it never leaves a long empty gap.
    float _selPanelHeight;
    PlacedBlockInstance _selPanelFor;

    // Pop-in animation state — restarts whenever the selected target changes.
    PlacedBlockInstance _panelAnimFor;
    float               _panelAnimStart;
    PlacedBlockInstance _rangeShownFor;
    float               _rangeAnimStart;

    // Faint range bubble for a selected turret, plus a RED translucent "shadow
    // volume" filling the parts of the range that blocks occlude (the turret can't
    // shoot there). The shadow volume is rebuilt per selection.
    GameObject _rangeSphere;
    GameObject _rangeShadow;
    Mesh       _rangeShadowMesh;
    PlacedBlockInstance _shadowFor;

    GUIStyle _panelBox, _panelTitle, _panelLabel, _panelValue, _panelButton, _panelProgress;

    // Spawn-point selection -> wave-intel panel. Cached per round so the
    // (non-destructive) forecast isn't recomputed every OnGUI pass.
    GameObject _selectedEndpoint;
    bool       _selectedEndpointIsStart;
    GameFlowManager.WaveForecast _startForecast;
    int        _startForecastRound = int.MinValue;

    // Chaos-block selection -> read-only panel showing its per-round drain.
    ChaosBlockUnit _selectedChaos;

    // Shrine selection -> read-only panel showing its aura buff. Same pattern.
    ShrineUnit _selectedShrine;

    [Header("R key: tap = refresh shop, hold = restart level")]
    [Tooltip("Release before this and R counts as a TAP (refresh shop). Also how long the screen stays clear before the white wash starts, so a tap never flashes.")]
    public float restartTapMaxSeconds = 0.25f;
    [Tooltip("Total hold time before the level restarts. The screen reaches full white exactly here.")]
    public float restartHoldSeconds = 1.2f;

    float _rHeld;
    bool  _rRestarting;
    bool  _rArmed;   // a fresh key-press is required — see UpdateRestartHold

    void Awake()
    {
        Instance        = this;
        editFocusAnchor = new GameObject("EditFocusAnchor").transform;
    }

    public void ShowPlacementPopup(string msg, float duration = 1.5f)
    {
        if (string.IsNullOrEmpty(msg)) return;
        _popupMsg      = msg;
        _popupDuration = duration;
        _popupExpire   = Time.unscaledTime + duration;
    }

    public bool TryRefreshShop()
    {
        // Validate every dependency BEFORE charging. If we charged first and
        // then bailed on a null manager, the player would lose currency for a
        // refresh that never happened.
        if (!TutorialDirector.CanRefresh()) return false;   // tutorial gate

        var rm   = ResourceManager.Instance;
        var gfm  = GameFlowManager.Instance;
        var shop = ShopController.Instance;
        if (rm == null || gfm == null || shop == null)
            return false;

        if (!rm.CanAfford(currentRefreshCost, BlockType.Home))
            return false;

        rm.TryBuy(currentRefreshCost, BlockType.Home);
        currentRefreshCost += refreshCostStep;

        shop.ClearItems();
        SpawnRoundBlocks(gfm.blocksPerTurn, gfm.turretsPerTurn);

        ShopRefreshed?.Invoke();
        return true;
    }
    void OnGUI()
    {
        if (GameFlowManager.SettlementUp) return;   // clear settlement — hide selection panel / popups
        DrawSelectionPanel();
        DrawPopup();
        DrawBoxSelect();
    }

    void DrawPopup()
    {
        if (string.IsNullOrEmpty(_popupMsg)) return;
        float remaining = _popupExpire - Time.unscaledTime;
        if (remaining <= 0f) { _popupMsg = null; return; }

        if (_popupStyle == null)
        {
            _popupStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize  = 18,
                alignment = TextAnchor.MiddleCenter,
                wordWrap  = false,
            };
            _popupStyle.normal.textColor = Color.white;
        }

        var content = new GUIContent(_popupMsg);
        var size    = _popupStyle.CalcSize(content);
        size.x += 28f;
        size.y += 14f;

        float fade = Mathf.Clamp01(remaining / Mathf.Min(0.4f, _popupDuration));
        var prev   = GUI.color;
        GUI.color  = new Color(1f, 1f, 1f, fade);

        var rect = new Rect(
            (Screen.width - size.x) * 0.5f,
            Screen.height * 0.78f,
            size.x, size.y
        );
        GUI.Box(rect, content, _popupStyle);
        GUI.color = prev;
    }

    void Start()
    {
        currentColor = GetRandomColor();
        currentRefreshCost = refreshBaseCost;
    }


    public void ResetRefreshCost()
    {
        currentRefreshCost = refreshBaseCost;
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

    public void SpawnRoundBlocks(int blockCount, int turretCount)
    {
        if (cubePrefab == null || blocks == null || blocks.Length == 0) return;
        if (ShopController.Instance == null) return;

        var turretTypes = new List<BlockData>();
        var normalTypes = new List<BlockData>();
        foreach (var b in blocks)
        {
            if (b == null) continue;
            if (TurretTypes.Is(b.blockType)) turretTypes.Add(b);
            else                                 normalTypes.Add(b);
        }

        // Use the run-scoped seeded RNG so a fixed runSeed gives a deterministic
        // shop (important for tutorials / reproducible runs).
        var rng   = GameFlowManager.Instance?.Rng;
        var blockRow  = RollRow(normalTypes, blockCount, rng);
        var turretRow = RollRow(turretTypes, turretCount, rng);

        // Roll a synergy color for each token. Non-turrets sample from the
        // run's ColorDistribution; turrets stay BlockColor.None (combat
        // pieces don't participate in synergies for now).
        var dist  = GameFlowManager.Instance?.colorDistribution;
        var blockColors  = RollColors(blockRow.Length,  dist, rng, isTurret: false);
        var turretColors = RollColors(turretRow.Length, dist, rng, isTurret: true);

        // Opening hand: overwrite the front of each row with the level's authored
        // entries. Done AFTER rolling (rather than instead of it) so the RNG draw
        // order is identical whether or not a level authors a starting shop — the
        // run RNG is shared with wave generation and upgrade offers, so skipping
        // draws here would shift every other system's results for the same seed.
        if (!_startingShopApplied)
        {
            _startingShopApplied = true;
            ApplyStartingShop(RunConfig.Mode == GameMode.Level ? RunConfig.Level : null,
                              blockRow, blockColors, turretRow, turretColors);
        }

        ShopController.Instance.SetShopItems(
            blockRow, turretRow, blockColors, turretColors, cubePrefab, grid);
    }

    // Set once per run (a level restart reloads the scene, so this resets with it).
    bool _startingShopApplied;

    // Forces LevelDefinition.startingShop entries into the first shop. Turret
    // entries fill the turret row, everything else the block row; anything beyond
    // a row's size is dropped rather than resizing the shop.
    static void ApplyStartingShop(LevelDefinition lv,
                                  BlockData[] blockRow,  BlockColor[] blockColors,
                                  BlockData[] turretRow, BlockColor[] turretColors)
    {
        if (lv?.startingShop == null || lv.startingShop.Length == 0) return;

        int bi = 0, ti = 0;
        foreach (var e in lv.startingShop)
        {
            if (e?.block == null) continue;

            bool isTurret = TurretTypes.Is(e.block.blockType);
            var  row      = isTurret ? turretRow    : blockRow;
            var  colors   = isTurret ? turretColors : blockColors;
            int  i        = isTurret ? ti : bi;

            if (i >= row.Length)
            {
                Debug.LogWarning($"[Shop] startingShop entry '{e.block.name}' dropped — " +
                                 $"the {(isTurret ? "turret" : "block")} row only has {row.Length} slot(s) " +
                                 $"({(isTurret ? "turretsPerTurn" : "blocksPerTurn")}).");
                continue;
            }

            row[i] = e.block;
            // None = let the rolled colour stand, so an entry can pin the block
            // but still take whatever colour the level's pool gave that slot.
            if (e.color != BlockColor.None) colors[i] = e.color;

            if (isTurret) ti = i + 1; else bi = i + 1;
        }
    }

    static BlockData[] RollRow(List<BlockData> pool, int count, Xoshiro256StarStar rng)
    {
        if (count <= 0 || pool == null || pool.Count == 0) return System.Array.Empty<BlockData>();
        var row = new BlockData[count];
        for (int i = 0; i < count; i++)
            row[i] = pool[rng != null ? rng.NextInt(pool.Count) : Random.Range(0, pool.Count)];
        return row;
    }

    static BlockColor[] RollColors(int count, ColorDistribution dist, Xoshiro256StarStar rng, bool isTurret)
    {
        if (count <= 0) return System.Array.Empty<BlockColor>();
        var arr = new BlockColor[count];
        for (int i = 0; i < count; i++)
        {
            if (isTurret || dist == null || rng == null) arr[i] = BlockColor.None;
            else                                          arr[i] = dist.Pick(rng);
        }
        return arr;
    }

    // Hides or shows all tray tokens that haven't been consumed yet.
    void SetTrayVisible(bool visible)
    {
        trayBlocks.RemoveAll(b => b == null);
        foreach (var b in trayBlocks) b.SetActive(visible);
    }

    void Update()
    {
        if (SettingsScreen.Open || IntroDirector.Playing || GameFlowManager.SettlementUp) return;   // modal / intro / clear settlement — block placement input

        _currentRotation = Quaternion.Slerp(
            _currentRotation,
            _targetRotation,
            1f - Mathf.Exp(-rotateSpeed * Time.unscaledDeltaTime)
        );

        HandleScroll();
        HandleMouseMove();

        // Batch-move owns the whole rest of the frame while active: mode switch,
        // single-block edit keys, undo, and normal click dispatch all assume
        // "one currentBlock" and must not run concurrently with an N-block hold.
        // See PlacementController.BatchEdit.cs for why this is a separate state
        // machine instead of reusing mode==Edit.
        if (_batchMoving)
        {
            UpdateBatchMove();
            currentGridPos = baseGridPos + manualOffset;
            return;
        }

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
        UpdateInfoPanel();   // drive the UGUI selection panel (if wired)

        // Process deferred info-panel button clicks (queued during OnGUI).
        if (_panelPowerUpgradeRequested)      { _panelPowerUpgradeRequested      = false; TryUpgradeSelectedBasicTurret(BasicTurretUpgradePath.Power); }
        if (_panelBurstUpgradeRequested)      { _panelBurstUpgradeRequested      = false; TryUpgradeSelectedBasicTurret(BasicTurretUpgradePath.Burst); }
        if (_panelAoeFireUpgradeRequested)    { _panelAoeFireUpgradeRequested    = false; TryUpgradeSelectedAoeTurret(AoeTurretUpgradePath.Fire); }
        if (_panelAoeGravityUpgradeRequested) { _panelAoeGravityUpgradeRequested = false; TryUpgradeSelectedAoeTurret(AoeTurretUpgradePath.Gravity); }
        if (_panelPickUpRequested)            { _panelPickUpRequested            = false; PickUpSelected(); }
        if (_panelSellRequested)              { _panelSellRequested              = false; SellSelected();   }
        if (_multiMoveRequested)              { _multiMoveRequested              = false; TryBeginBatchMove(); }
        if (_multiSellRequested)              { _multiSellRequested              = false; SellAllSelected(); }

        UpdateRestartHold();

        if (Input.GetMouseButtonDown(0) || VirtualCursor.ConfirmPressedThisFrame)
        {
            if (IsPointerOverSelectionPanel() || HudSidePanels.PointerOver || PointerOverInfoPanel()
                || MultiSelectPanel.IsPointerOver(VirtualCursor.Position))
            {
                // Click landed on an HUD panel (info / synergies / controls) or the
                // UGUI selection panel — it handles the click. Skip world
                // selection / placement underneath.
            }
            else if (mode == PlacementMode.Edit)
            {
                if (currentBlock != null) TryPlace();
            }
            else if (ShopController.Instance != null && ShopController.Instance.TryHandleClick())
            {
                // Shop viewport consumed the click don't run main-camera selection.
            }
            else if (mode == PlacementMode.Select && Input.GetMouseButtonDown(0))
            {
                // Mouse only (VirtualCursor.ConfirmPressedThisFrame covers gamepad,
                // which has no held/drag state to marquee with) — box-select only
                // ever starts here; a click straight onto a target still resolves
                // instantly via TryBeginBoxSelect's own target check.
                TryBeginBoxSelect();
            }
            else
            {
                TrySelectObject();
            }
        }

        UpdateBoxSelect();   // advances (or resolves) a drag started above, across frames

        // Delete cancel current hold (Edit mode) or remove selected placed block.
        if (Input.GetKeyDown(KeyCode.Delete) || GamepadInput.DeleteDown)
        {
            if (mode == PlacementMode.Edit)
                CancelEditMode();
            else
                TryDelete();
        }

        // Ctrl+Z undo last placement or deletion.
        if ((Input.GetKeyDown(KeyCode.Z)
            && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
            || GamepadInput.UndoDown)
            TryUndo();

        currentGridPos = baseGridPos + manualOffset;

        UpdateRangeIndicator();
    }

    // =========================
    // INPUT
    // =========================

    void HandleScroll()
    {
        float s = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(s) < 0.001f) return;

        // A group (batch) move is a "hold" just like single-block Edit, even though
        // mode is still Select — so scroll must adjust the build plane (height/depth)
        // to move the held group up and down, NOT zoom the camera. Only a plain
        // Select-mode cursor (nothing held) zooms.
        if (mode == PlacementMode.Select && !_batchMoving)
        {
            cam.AddDistance(-s * scrollSpeed * 10f);
            return;
        }

        // Edit mode (or a batch-move hold — same build-plane logic)
        if (cam != null && cam.useOrthographic)
        {
            // Ortho: scroll moves the build plane up/down. Discrete steps so
            // each notch == one cell visible AND grid-snapped.
            int step  = s > 0f ? 1 : -1;
            _buildY   = Mathf.Clamp(_buildY + step, buildYRange.x, buildYRange.y);
        }
        else
        {
            _depth = Mathf.Clamp(_depth - s * scrollSpeed * _depth, minDepth, maxDepth);
        }
    }

    void HandleMouseMove()
    {
        Ray r = cam.myCam.ScreenPointToRay(VirtualCursor.Position);
        Vector3 world;

        if (cam != null && cam.useOrthographic && Mathf.Abs(r.direction.y) > 0.001f)
        {
            // Ortho: intersect the mouse ray with the build-plane (world Y).
            // Plane sits at the centre of cell row _buildY (= cellSize * y + cs/2).
            float cs       = grid != null ? grid.cellSize : 1f;
            float planeY   = _buildY * cs + cs * 0.5f;
            float t        = (planeY - r.origin.y) / r.direction.y;
            world          = t > 0f ? r.origin + r.direction * t
                                    : r.origin + r.direction * _depth; // fallback
        }
        else
        {
            world = r.origin + r.direction * _depth;
        }

        Vector3Int raw = grid.WorldToGrid(world);

        // Snapping placement (GameSettings.FreeMove == false, the default): the mouse
        // still moves the ghost freely, but the cell it lands on is pulled to the
        // nearest one where the held block's shape actually has support — so you
        // can't leave it hanging in empty space far from the build, only ever
        // riding the surface of what's already there. WASDQE/scroll still nudge
        // it further from wherever it snaps to.
        baseGridPos = (mode == PlacementMode.Edit && !GameSettings.FreeMove)
            ? SnapToNearestSupported(raw)
            : raw;
    }

    // Last cell SnapToNearestSupported actually resolved to. Doubles as the "stay
    // put" fallback when the mouse (or a scroll depth change) moves `raw` outside
    // snapGridRadius of anything supported — without this the ghost would jump to
    // the unsnapped raw position instead, which is exactly the "runs off the edge"
    // escape the radius search was supposed to prevent.
    Vector3Int _lastSnappedBaseGridPos;

    // Searches a small cube of cells around `raw` for the nearest one where the
    // currently-held block (its rotated shape, offset by the existing manualOffset
    // nudge) would actually be a valid, supported placement — i.e. touches an
    // existing block/endpoint and doesn't overlap anything. If nothing in range
    // qualifies, holds at the last cell that DID qualify instead of snapping back
    // to the raw (unsupported) mouse position — the mouse can keep moving, but the
    // ghost simply won't follow it off into empty space.
    Vector3Int SnapToNearestSupported(Vector3Int raw)
    {
        var cells = GetRotatedCells();
        if (cells.Length == 0) return raw;
        if (CanPlace(raw + manualOffset, cells)) return _lastSnappedBaseGridPos = raw;

        int cellRadius = Mathf.Max(1, Mathf.CeilToInt(snapGridRadius / Mathf.Max(0.01f, grid.cellSize)));
        Vector3Int best = _lastSnappedBaseGridPos;
        int bestSqr = int.MaxValue;
        bool found = false;

        for (int dx = -cellRadius; dx <= cellRadius; dx++)
        for (int dy = -cellRadius; dy <= cellRadius; dy++)
        for (int dz = -cellRadius; dz <= cellRadius; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var cand = raw + new Vector3Int(dx, dy, dz);
            if (!CanPlace(cand + manualOffset, cells)) continue;
            int sqr = dx * dx + dy * dy + dz * dz;
            if (sqr < bestSqr) { bestSqr = sqr; best = cand; found = true; }
        }
        if (found) _lastSnappedBaseGridPos = best;
        return _lastSnappedBaseGridPos;
    }

    // Edit mode only.
    // A / D move block left/right relative to camera's horizontal facing
    // W / S move block forward / back relative to camera's horizontal facing
    // Q / E move block UP / DOWN in world Y
    void HandleKeyboardOffset()
    {
        Vector3Int right   = SnapToHorizontalAxis(cam.transform.right);
        Vector3Int forward = SnapToHorizontalAxis(cam.transform.forward);

        if (Input.GetKeyDown(KeyCode.A)) manualOffset -= right;
        if (Input.GetKeyDown(KeyCode.D)) manualOffset += right;
        if (Input.GetKeyDown(KeyCode.W)) manualOffset += forward;
        if (Input.GetKeyDown(KeyCode.S)) manualOffset -= forward;
        if (Input.GetKeyDown(KeyCode.Q)) manualOffset += Vector3Int.up;
        if (Input.GetKeyDown(KeyCode.E)) manualOffset += Vector3Int.down;

        // Gamepad d-pad mirrors A/D/W/S (Q/E depth stays mouse/keyboard-only, low value on a pad).
        if (GamepadInput.CycleBlockPrevDown) manualOffset -= right;
        if (GamepadInput.CycleBlockNextDown) manualOffset += right;
        if (GamepadInput.DPadUpDown)         manualOffset += Vector3Int.up;
        if (GamepadInput.DPadDownDown)       manualOffset += Vector3Int.down;
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
        if (Input.GetKey(KeyCode.Q)) delta += Vector3.up;
        if (Input.GetKey(KeyCode.E)) delta -= Vector3.up;

        // Note: left stick isn't wired here — it already drives VirtualCursor's pointer
        // in Select mode, so reusing it for camera pan too would fight the cursor.
        // Gamepad users pan by orbiting the right stick (OrbitCamera) instead.
        if (delta.sqrMagnitude > 0.0001f)
            cam.Pan(delta.normalized * panSpeed * Time.unscaledDeltaTime);
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

        // World-space rotation (pre-multiply). delta * old applies delta in
        // world frame, so 1/2/3 always rotate around world X/Y/Z regardless
        // of how the block has been turned before. Keeps the visual ring
        // overlay axis-aligned and predictable.
        bool rotated = false;
        if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            AudioManager.Instance.PlayRotate();
            _targetRotation = Quaternion.Euler(90, 0, 0) * _targetRotation;
            rotated = true;
        }

        if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            AudioManager.Instance.PlayRotate();
            _targetRotation = Quaternion.Euler(0, 90, 0) * _targetRotation;
            rotated = true;
        }

        if (Input.GetKeyDown(KeyCode.Alpha3))
        {
            AudioManager.Instance.PlayRotate();
            _targetRotation = Quaternion.Euler(0, 0, 90) * _targetRotation;
            rotated = true;
        }

        // Right shoulder — a single gamepad button can't cover all 3 axes, so it
        // mirrors the most common one (yaw, same as Alpha2).
        if (GamepadInput.RotateDown)
        {
            AudioManager.Instance.PlayRotate();
            _targetRotation = Quaternion.Euler(0, 90, 0) * _targetRotation;
            rotated = true;
        }

        if (rotated) BlockRotated?.Invoke();
    }

    void HandleModeSwitch()
    {
        if (!Input.GetKeyDown(KeyCode.Tab) && !GamepadInput.ToggleModeDown) return;

        if (mode == PlacementMode.Select)
        {
            if (selectedInstance != null)
                PickUpSelected();   // no-op + log if combat-locked
            else
                EnterEditMode(null);
        }
        else
        {
            CancelEditMode();
        }
    }

    // Lifts the currently-selected placed block off the grid and enters Edit
    // mode holding it, so it can be repositioned. Shared by Tab, double-click,
    // and the info-panel "Pick up" button. Returns false (and logs) when there
    // is nothing selected or the block is combat-locked.
    bool PickUpSelected()
    {
        if (selectedInstance == null || selectedInstance.visualObject == null)
            return false;

        // Editing PLACED objects (pick up / move) is locked during combat.
        if (GameFlowManager.Instance?.phase == GamePhase.Running)
        {
            Debug.Log("[Placement] Editing placed objects is locked during combat.");
            return false;
        }
        if (selectedInstance.locked)
        {
            Debug.Log("[Placement] This block is part of the level's fixed layout and can't be moved.");
            return false;
        }
        if (selectedInstance.sealedByEnemy)
        {
            ShowPlacementPopup("Sealed by the enemy — this block can't be moved. You can still sell it.");
            return false;
        }
        if (FindOrphanedTurret(selectedInstance) != null)
        {
            ShowPlacementPopup("There's still turret on this block, try move it first");
            return false;
        }

        isPickingUpObject = true;
        lastObjectPos   = selectedInstance.visualObject.transform.position;
        lastObjectRot   = selectedInstance.visualObject.transform.rotation;
        lastObjectCells = selectedInstance.occupiedCells.ToArray();
        lastBasicPowerUpgradeLevel = selectedInstance.basicPowerUpgradeLevel;
        lastBasicBurstUpgradeLevel = selectedInstance.basicBurstUpgradeLevel;
        lastAoeFireUpgradeLevel = selectedInstance.aoeFireUpgradeLevel;
        lastAoeGravityUpgradeLevel = selectedInstance.aoeGravityUpgradeLevel;

        // Keep the original height plane (and steady the camera focus), but let the
        // block follow the cursor directly — no offset back to its old cell.
        SnapDepthToWorldPos(lastObjectPos);

        // Update count before removing from grid.
        ResourceManager.Instance?.OnBlockRemoved(selectedInstance.data.blockType);
        SynergyEvaluator.Instance?.OnPieceRemoved(selectedInstance.placedPiece);

        grid.RemoveInstance(selectedInstance);
        NotifyBlockLifted(lastObjectCells);
        selectedInstance = null;
        HideRangeIndicator();
        EnterEditMode(null);   // leave the camera where it is — SnapDepthToWorldPos already fixed the height plane
        return true;
    }

    void CancelEditMode()
    {
        if (isPickingUpObject)
        {
            CancelAndReturnObject();
        }
        else if (activePhysicsObject != null && _pendingShopPrice > 0)
        {
            // Player grabbed a shop item but cancelled before placing give it back.
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
        ClearMultiSelection();   // any plain click replaces a box-selection, even one that lands on nothing

        Ray ray = cam.myCam.ScreenPointToRay(VirtualCursor.Position);

        if (!Physics.Raycast(ray, out RaycastHit hit))
        {
            // Empty-space click — dismiss any current selection (Arknights-style).
            selectedInstance  = null;
            _selectedEndpoint = null;
            _selectedChaos    = null;
            _selectedShrine   = null;
            UpdateHighlight(null);
            _lastClickTarget = null;
            if (WavePreview.Active) WavePreview.Exit();
            return;
        }

        // --- Chaos block: read-only panel explaining what it's costing you ---
        var chaos = hit.transform.GetComponentInParent<ChaosBlockUnit>();
        if (chaos != null)
        {
            selectedInstance    = null;
            activePhysicsObject = null;
            _selectedEndpoint   = null;
            _selectedShrine     = null;
            _selectedChaos      = chaos;
            UpdateHighlight(chaos.gameObject);
            return;
        }
        _selectedChaos = null;

        // --- Shrine: read-only panel explaining the aura buff it grants ---
        var shrine = hit.transform.GetComponentInParent<ShrineUnit>();
        if (shrine != null)
        {
            selectedInstance    = null;
            activePhysicsObject = null;
            _selectedEndpoint   = null;
            _selectedShrine     = shrine;
            UpdateHighlight(shrine.gameObject);
            return;
        }
        _selectedShrine = null;

        // --- Tray token: single click immediately grab and enter Edit ---
        var sb = hit.transform.GetComponentInParent<SelectableBlock>();
        if (sb != null)
        {
            // Non-turret tray tokens are locked during combat.
            if (GameFlowManager.Instance?.phase == GamePhase.Running
                && !TurretTypes.Is(sb.data?.blockType ?? BlockType.Empty))
            {
                Debug.Log("[Placement] Block editing locked during combat.");
                return;
            }

            currentBlock        = sb.data;
            currentSynergyColor = sb.color;
            _ghostAnchorSnap    = true;   // appear at the cursor, don't glide in from the last hold
            // Derive the visual tint directly from the synergy color so the
            // placed block matches the palette exactly, instead of round-
            // tripping through the shop renderer (which has different
            // lighting and would show subtle color drift). Falls back to
            // reading the renderer's MPB for None / legacy tokens.
            currentColor        = sb.color != BlockColor.None
                ? BlockColorPalette.Get(sb.color)
                : MpbColor.Get(sb.GetComponentInChildren<Renderer>());
            activePhysicsObject = sb.gameObject;
            selectedInstance    = null;
            _selectedEndpoint   = null;

            UpdateHighlight(activePhysicsObject);
            EnterEditMode(null);
            return;
        }

        // --- Endpoint markers (start / end block): highlight, double-click focus ---
        var ep = hit.transform.GetComponentInParent<GridEndpoint>();
        if (ep != null)
        {
            bool isStart = ep.gameObject.name.IndexOf(
                "start", System.StringComparison.OrdinalIgnoreCase) >= 0;

            // Start points: click toggles the zoomed-in wave preview instead of the
            // old side-panel forecast. Not available mid-combat (the forecast is for
            // the NEXT wave, ambiguous while one is running) — falls through to plain
            // selection in that case, same as before this feature existed.
            bool combatRunning = GameFlowManager.Instance != null && GameFlowManager.Instance.phase == GamePhase.Running;
            if (isStart && !combatRunning)
            {
                selectedInstance    = null;
                activePhysicsObject = null;
                _selectedEndpoint   = null;   // old side panel stays off for starts now
                UpdateHighlight(ep.gameObject);
                WavePreview.Toggle(ep, GameFlowManager.Instance != null ? GameFlowManager.Instance.GetNextWaveForecast() : default);
                return;
            }

            UpdateHighlight(ep.gameObject);
            selectedInstance    = null;
            activePhysicsObject = null;

            // Track the endpoint so the spawn-intel panel can show the upcoming
            // wave when it's a START point. Endpoints are named "startBlock" /
            // "endBlock" by LevelEndpointGenerator.
            _selectedEndpoint        = ep.gameObject;
            _selectedEndpointIsStart = isStart;
            _startForecastRound      = int.MinValue;   // force a fresh forecast

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
            _selectedEndpoint   = null;
            BlockSelected?.Invoke(instance.data);
            currentBlock        = instance.data;
            currentSynergyColor = instance.color;
            _ghostAnchorSnap    = true;   // appear at the cursor, don't glide in from the last hold
            // Re-derive tint from synergy color when present keeps placed
            // block visuals exactly on-palette across pickup→replace cycles.
            currentColor        = instance.color != BlockColor.None
                ? BlockColorPalette.Get(instance.color)
                : MpbColor.Get(instance.visualObject.GetComponentInChildren<Renderer>());
            activePhysicsObject = null;

            UpdateHighlight(instance.visualObject);

            bool isDouble = instance.visualObject == _lastClickTarget
                         && Time.time - _lastClickTime < DoubleClickInterval;
            _lastClickTime   = Time.time;
            _lastClickTarget = instance.visualObject;

            // Double-click picks the block back up for repositioning, same as
            // Tab. selectedInstance == instance here, so PickUpSelected handles
            // the combat lock + lift uniformly.
            if (isDouble)
                PickUpSelected();
        }
    }

    void UpdateHighlight(GameObject target)
    {
        // Recolor the EXISTING cartoon outline pass via MaterialPropertyBlock —
        // no extruded outline component gets added or destroyed.
        if (lastHighlightedObject != null && lastHighlightedObject != target)
            SetOutlineHighlight(lastHighlightedObject, restoreDefault: true);

        if (target != null && target != lastHighlightedObject)
        {
            SetOutlineHighlight(target, restoreDefault: false);
            AudioManager.Instance?.PlaySelect();
        }

        lastHighlightedObject = target;
    }

    // Finds the renderer using the cartoon outline shader and overrides its
    // `_OutlineColor` via MPB (read first so we don't clobber other overrides
    // like silkscreen `_BaseColor`). Restore reads the material's authored
    // color back.
    void SetOutlineHighlight(GameObject obj, bool restoreDefault)
    {
        if (obj == null) return;
        if (_highlightMpb == null) _highlightMpb = new MaterialPropertyBlock();

        var rends = obj.GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null) continue;

            Material outlineMat = null;
            var mats = r.sharedMaterials;
            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null || mat.shader == null) continue;
                var name = mat.shader.name;
                if (name == "GeoWorld/BlockOutline" || name == "Custom/ObjectOutline")
                {
                    outlineMat = mat;
                    break;
                }
            }
            if (outlineMat == null || !outlineMat.HasProperty(_OutlineColorID)) continue;

            r.GetPropertyBlock(_highlightMpb);
            var color = restoreDefault
                ? outlineMat.GetColor(_OutlineColorID)
                : selectionOutlineColor;
            _highlightMpb.SetColor(_OutlineColorID, color);
            r.SetPropertyBlock(_highlightMpb);
        }
    }

    // =========================
    // SELL
    // =========================

    // Recomputes a fresh base price for the block (fluctuation 1.0) and returns
    // the sell value. Placed instances don't store what was actually paid (only
    // the consumed tray token did), so this is the best available estimate.
    int ComputeSellRefund(PlacedBlockInstance ins)
    {
        if (ins?.data == null || ResourceManager.Instance == null) return 0;
        int basePrice = ResourceManager.Instance.ComputePrice(ins.data, 1f);
        return Mathf.Max(1, Mathf.RoundToInt(basePrice * sellRefundFraction));
    }

    // Upgrading a turret costs turret currency equal to the turret's own price
    // (fluctuation-free base). Fixed per turret type — doesn't climb with level.
    int ComputeUpgradeCost(PlacedBlockInstance ins)
    {
        if (ins?.data == null || ResourceManager.Instance == null) return 0;
        return Mathf.Max(1, ResourceManager.Instance.ComputePrice(ins.data, 1f));
    }

    void TryUpgradeSelectedBasicTurret(BasicTurretUpgradePath path)
    {
        if (!TutorialDirector.CanUpgrade()) return;   // tutorial gate
        var ins = selectedInstance;
        var turret = ins?.visualObject != null
            ? ins.visualObject.GetComponentInChildren<TurretController>()
            : null;
        if (ins == null || turret == null) return;

        // Costs turret currency equal to the turret's own price; gate before touching state.
        int cost = ComputeUpgradeCost(ins);
        var rm   = ResourceManager.Instance;
        if (rm != null && !rm.CanAfford(cost, ins.data.blockType))
        {
            ShowPlacementPopup($"Need {cost} turret currency to upgrade");
            return;
        }

        if (!turret.TryUpgradeBasicPath(path))
        {
            if (!turret.CanUpgradeBasicPath(path, out string reason))
                ShowPlacementPopup(reason);
            return;
        }

        rm?.TryBuy(cost, ins.data.blockType);   // charge only after a successful upgrade

        ins.basicPowerUpgradeLevel = turret.PowerPathLevel;
        ins.basicBurstUpgradeLevel = turret.BurstPathLevel;

        string branch = path == BasicTurretUpgradePath.Power ? "Power" : "Burst";
        ShowPlacementPopup($"{branch} upgraded (-{cost})");
        TurretUpgraded?.Invoke();
        TurretUpgradeLevelReached?.Invoke(Mathf.Max(turret.PowerPathLevel, turret.BurstPathLevel));

        selectedInstance = null;   // collapse the detail/upgrade panels instead of re-showing every frame
        UpdateHighlight(null);
        HideRangeIndicator();
    }

    void TryUpgradeSelectedAoeTurret(AoeTurretUpgradePath path)
    {
        if (!TutorialDirector.CanUpgrade()) return;   // tutorial gate
        var ins = selectedInstance;
        var turret = ins?.visualObject != null
            ? ins.visualObject.GetComponentInChildren<TurretController>()
            : null;
        if (ins == null || turret == null) return;

        // Costs turret currency equal to the turret's own price; gate before touching state.
        int cost = ComputeUpgradeCost(ins);
        var rm   = ResourceManager.Instance;
        if (rm != null && !rm.CanAfford(cost, ins.data.blockType))
        {
            ShowPlacementPopup($"Need {cost} turret currency to upgrade");
            return;
        }

        if (!turret.TryUpgradeAoePath(path))
        {
            if (!turret.CanUpgradeAoePath(path, out string reason))
                ShowPlacementPopup(reason);
            return;
        }

        rm?.TryBuy(cost, ins.data.blockType);   // charge only after a successful upgrade

        ins.aoeFireUpgradeLevel = turret.AoeFirePathLevel;
        ins.aoeGravityUpgradeLevel = turret.AoeGravityPathLevel;

        string branch = path == AoeTurretUpgradePath.Fire ? "Fire" : "Gravity";
        ShowPlacementPopup($"{branch} upgraded (-{cost})");
        TurretUpgraded?.Invoke();
        TurretUpgradeLevelReached?.Invoke(Mathf.Max(turret.AoeFirePathLevel, turret.AoeGravityPathLevel));

        selectedInstance = null;   // collapse the detail/upgrade panels instead of re-showing every frame
        UpdateHighlight(null);
        HideRangeIndicator();
    }

    // R is overloaded: a TAP refreshes the shop, a HOLD restarts the level with a
    // white wash that builds while you hold. The wash is the confirmation — it's
    // why restart needs no dialog, and why letting go early has to cleanly abort.
    void UpdateRestartHold()
    {
        if (Input.GetKeyDown(KeyCode.R)) { _rHeld = 0f; _rRestarting = false; _rArmed = true; }

        // Only a hold that STARTED in this scene counts. After a restart this is a
        // brand-new controller with _rHeld back at 0, and the player is very likely
        // still holding R from the press that caused it — without this gate the
        // wash would immediately start climbing again and restart a second time.
        if (_rArmed && !_rRestarting && Input.GetKey(KeyCode.R))
        {
            // Unscaled: restarting has to work while paused / mid-settlement.
            _rHeld += Time.unscaledDeltaTime;

            float hold = Mathf.Max(restartTapMaxSeconds + 0.01f, restartHoldSeconds);
            HoldRestartFade.SetAlpha(Mathf.InverseLerp(restartTapMaxSeconds, hold, _rHeld));

            if (_rHeld >= hold)
            {
                _rRestarting = true;   // latch: don't re-fire while the key is still down
                HoldRestartFade.SetAlpha(1f);
                GameFlowManager.Instance?.RestartGame();
            }
            return;
        }

        if (Input.GetKeyUp(KeyCode.R))
        {
            if (_rArmed && !_rRestarting)
            {
                if (_rHeld < restartTapMaxSeconds) TryRefreshShop();
                HoldRestartFade.FadeOut();
            }
            _rHeld  = 0f;
            _rArmed = false;
        }
    }

    // Removes the selected block and refunds part of its value to the matching
    // currency pool (turret pool for turrets, block pool otherwise). Selling is
    // final — no undo record is pushed.
    void SellSelected()
    {
        if (GameFlowManager.SettlementUp) return;   // locked during clear settlement
        var ins = selectedInstance;
        if (ins == null || ins.data == null) return;

        bool isTurret = TurretTypes.Is(ins.data.blockType);

        // Selling a PLACED object is locked during combat.
        if (GameFlowManager.Instance?.phase == GamePhase.Running)
        {
            Debug.Log("[Placement] Selling is locked during combat.");
            return;
        }
        if (ins.locked)
        {
            Debug.Log("[Placement] This block is part of the level's fixed layout and can't be sold.");
            return;
        }
        if (FindOrphanedTurret(ins) != null)
        {
            ShowPlacementPopup("There's still turret on this block, try move it first");
            return;
        }
        if (!TutorialDirector.CanSell()) { ShowPlacementPopup("Sell banned during tutorial!"); return; }   // tutorial gate

        int refund = ComputeSellRefund(ins);
        Vector3 soldPos = ins.visualObject != null ? ins.visualObject.transform.position : transform.position;

        ResourceManager.Instance?.OnBlockRemoved(ins.data.blockType);
        SynergyEvaluator.Instance?.OnPieceRemoved(ins.placedPiece);
        NotifyBlockLifted(ins.occupiedCells.ToArray());
        grid.RemoveInstance(ins);     // destroys visualObject

        selectedInstance = null;
        UpdateHighlight(null);
        HideRangeIndicator();

        if (isTurret) ResourceManager.Instance?.AddTurretCurrency(refund);
        else          ResourceManager.Instance?.RefundBlock(refund);
        CurrencyFlyFx.Fly(soldPos, isTurret, refund);

        GameFlowManager.Instance?.EvaluateGrid();
        ShowPlacementPopup($"Sold for +{refund}");
        BlockSold?.Invoke(ins.data);
    }

    // =========================
    // EDIT
    // =========================

    // Keeps the picked-up block's original height plane (via _depth) but leaves NO
    // manual offset — so the block tracks the cursor directly instead of snapping
    // back to its old cell with an offset.
    void SnapDepthToWorldPos(Vector3 worldPos)
    {
        Ray r    = cam.myCam.ScreenPointToRay(VirtualCursor.Position);
        _depth   = Mathf.Clamp(Vector3.Dot(worldPos - r.origin, r.direction), minDepth, maxDepth);
        baseGridPos  = grid.WorldToGrid(r.origin + r.direction * _depth);
        manualOffset = Vector3Int.zero;
    }

    // focusPos: if provided, camera pivots there once. Pass null to leave camera in place.
    void EnterEditMode(Vector3? focusPos)
    {
        ClearMultiSelection();   // single choke point — edit mode and a box-selection can never coexist
        mode = PlacementMode.Edit;
        _selectedEndpoint = null;   // leaving Select hides the spawn-intel panel
        SetTrayVisible(false);  // hide tokens while placing so they don't clutter the view
        previewParent.gameObject.SetActive(currentBlock != null);
        if (focusPos == null) manualOffset = Vector3Int.zero;
        // Seed the snap anchor at wherever the mouse/pickup already put baseGridPos
        // (HandleMouseMove keeps updating it even in Select mode) — otherwise the
        // very first snap search this hold would fall back to a stale cell left
        // over from a previous hold instead of somewhere near the cursor.
        _lastSnappedBaseGridPos = baseGridPos;
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

        // Priority-ordered checks: combat lock path block funds geometry.
        var  gfm       = GameFlowManager.Instance;
        bool inRunning = gfm != null && gfm.phase == GamePhase.Running;
        bool isTurret  = TurretTypes.Is(currentBlock.blockType);
        // In combat you may place NEW blocks/turrets (but not reposition existing),
        // as long as it doesn't fully wall off the path — blocked enemies re-route.
        if (inRunning)
        {
            if (isPickingUpObject)   // repositioning an existing piece is a "modify" → locked
            {
                ShowPlacementPopup(ReasonToMessage(PlaceFailureReason.CombatLocked));
                return;
            }
            var worldCells = new Vector3Int[cells.Length];
            for (int i = 0; i < cells.Length; i++)
                worldCells[i] = currentGridPos + cells[i];
            if (gfm.WouldBlockPath(worldCells))
            {
                ShowPlacementPopup(ReasonToMessage(PlaceFailureReason.WouldBlockPath));
                return;
            }
        }

        // Repositioning is free; only new purchases need funds.
        bool isNewBlock   = !isPickingUpObject;
        int  priceForUndo = _pendingShopPrice;
        if (isNewBlock && ResourceManager.Instance != null
            && !ResourceManager.Instance.CanAfford(_pendingShopPrice, currentBlock.blockType))
        {
            ShowPlacementPopup(ReasonToMessage(PlaceFailureReason.InsufficientFunds));
            StartCoroutine(FlashPreviewRed());
            return;
        }

        var reason = Validate(currentGridPos, cells);
        if (reason != PlaceFailureReason.None)
        {
            ShowPlacementPopup(ReasonToMessage(reason));
            return;
        }

        // Tutorial guide: must land exactly on the highlighted ghost.
        if (placementConstraint != null)
        {
            var wc = new Vector3Int[cells.Length];
            for (int i = 0; i < cells.Length; i++) wc[i] = currentGridPos + cells[i];
            if (!placementConstraint(currentBlock, wc))
            {
                ShowPlacementPopup("Place it on the highlighted guide.", 2f);
                return;
            }
        }

        if (isNewBlock && ResourceManager.Instance != null)
        {
            ResourceManager.Instance.TryBuy(_pendingShopPrice, currentBlock.blockType);
            _pendingShopPrice = 0;
        }

        Vector3 center = Vector3.zero;
        foreach (var c in cells)
            center += grid.GridToWorld(currentGridPos + c);
        center /= cells.Length;

        GameObject obj = new GameObject("PlacedBlock");
        obj.transform.position = center;
        obj.transform.rotation = _currentRotation;

        if (TurretTypes.Is(currentBlock.blockType))
        {
            SpawnTurretVisual(obj, currentBlock);
        }
        else
        {
            var br = obj.AddComponent<BlockRenderer>();
            br.cubePrefab = cubePrefab;
            br.Render(currentGridPos, cells, grid.cellSize, grid);

            foreach (var r in obj.GetComponentsInChildren<Renderer>())
                MpbColor.Set(r, currentColor);
        }

        PlacedBlockInstance ins = new()
        {
            data         = currentBlock,
            visualObject = obj,
            color        = currentSynergyColor,
            basicPowerUpgradeLevel = isPickingUpObject ? lastBasicPowerUpgradeLevel : 0,
            basicBurstUpgradeLevel = isPickingUpObject ? lastBasicBurstUpgradeLevel : 0,
            aoeFireUpgradeLevel = isPickingUpObject ? lastAoeFireUpgradeLevel : 0,
            aoeGravityUpgradeLevel = isPickingUpObject ? lastAoeGravityUpgradeLevel : 0,
        };

        foreach (var c in cells)
            ins.occupiedCells.Add(currentGridPos + c);

        RegisterPlacedBlock(ins);

        // Synergy hook: register the piece so SynergyEvaluator can re-run
        // its pattern detection. Returns the PlacedPiece reference we stash
        // for later removal.
        ins.placedPiece = SynergyEvaluator.Instance?.OnPiecePlaced(
            ins.data, ins.color, ins.occupiedCells.ToArray());

        // Collapse to zero for the GrowIn pop ONLY AFTER synergy decoration has
        // run. CellMaterialVisualizer matches cells to child renderers by world
        // bounds, which all collapse onto the block centre at scale 0 — doing it
        // here (block still full-size) is what lets the just-placed block, the
        // one that activates the synergy, light up along with the rest.
        obj.transform.localScale = Vector3.zero;
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
                basicPowerUpgradeLevel = ins.basicPowerUpgradeLevel,
                basicBurstUpgradeLevel = ins.basicBurstUpgradeLevel,
                aoeFireUpgradeLevel = ins.aoeFireUpgradeLevel,
                aoeGravityUpgradeLevel = ins.aoeGravityUpgradeLevel,
                prevCells   = lastObjectCells,
                prevCenter  = lastObjectPos,
                prevRotation= lastObjectRot,
                prevBasicPowerUpgradeLevel = lastBasicPowerUpgradeLevel,
                prevBasicBurstUpgradeLevel = lastBasicBurstUpgradeLevel,
                prevAoeFireUpgradeLevel = lastAoeFireUpgradeLevel,
                prevAoeGravityUpgradeLevel = lastAoeGravityUpgradeLevel,
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
                basicPowerUpgradeLevel = ins.basicPowerUpgradeLevel,
                basicBurstUpgradeLevel = ins.basicBurstUpgradeLevel,
                aoeFireUpgradeLevel = ins.aoeFireUpgradeLevel,
                aoeGravityUpgradeLevel = ins.aoeGravityUpgradeLevel,
                pricePaid   = priceForUndo,
            });
        }

        // Track placed count for shop price scaling (both new and repositioned blocks).
        ResourceManager.Instance?.OnBlockPlaced(ins.data.blockType);

        // Tutorial / listeners: announce the successful placement.
        BlockPlaced?.Invoke(ins.data, ins.occupiedCells.ToArray());

        // Auto-check path after every block placement updates live preview line.
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
            // (Removed: testing-mode refill that re-rolled a FULL round on every
            //  placement — it flooded the shop. Use the Refresh button instead.)
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

        // Valid green, invalid red. Preview always reads as a placement hint;
        // the random per-block color is applied only on successful placement.
        Color tint = valid
            ? new Color(0.25f, 1.00f, 0.35f, 0.55f)
            : new Color(1.00f, 0.20f, 0.20f, 0.45f);

        // Cells stay snapped (for validity + TryPlace); cubes draw offset from an
        // eased anchor so a one-cell move slides instead of popping — same technique
        // as LevelMapController's build ghost. Toggle: GameSettings.SmoothBlockEditing.
        Vector3 targetAnchor = grid.GridToWorld(currentGridPos);
        if (!GameSettings.SmoothBlockEditing || _ghostAnchorSnap)
        {
            _ghostVisualAnchor = targetAnchor;
            _ghostAnchorSnap   = false;
        }
        else
        {
            _ghostVisualAnchor = Vector3.Lerp(_ghostVisualAnchor, targetAnchor,
                                               1f - Mathf.Exp(-ghostFollowSpeed * Time.unscaledDeltaTime));
        }

        for (int i = 0; i < cells.Length; i++)
        {
            previewCubes[i].transform.position =
                _ghostVisualAnchor + (grid.GridToWorld(currentGridPos + cells[i]) - targetAnchor);
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

    // Direct block spawn bypassing player input, payment, preview, and grow
    // animation. Used by snapshot restore; caller must ensure `worldCells`
    // are unoccupied. Pass the original `synergyColor` so reloaded boards
    // keep their synergies.
    public PlacedBlockInstance PlaceBlockDirect(
    BlockData data,
    Vector3Int[] worldCells,
    Quaternion rotation,
    Color color,
    BlockColor synergyColor = BlockColor.None)
    {
        if (data == null || worldCells == null || worldCells.Length == 0)
            return null;

        Vector3 center = Vector3.zero;
        foreach (var c in worldCells)
            center += grid.GridToWorld(c);
        center /= worldCells.Length;

        var obj = new GameObject("PlacedBlock");
        obj.transform.position = center;
        obj.transform.rotation = rotation;

        if (TurretTypes.Is(data.blockType))
        {
            SpawnTurretVisual(obj, data);
        }
        else
        {
            var br = obj.AddComponent<BlockRenderer>();
            br.cubePrefab = cubePrefab;

            var origin = grid.WorldToGrid(center);
            var rel = new Vector3Int[worldCells.Length];

            for (int i = 0; i < worldCells.Length; i++)
                rel[i] = worldCells[i] - origin;

            br.Render(origin, rel, grid.cellSize, grid);

            foreach (var r in obj.GetComponentsInChildren<Renderer>())
                MpbColor.Set(r, color);
        }

        var ins = new PlacedBlockInstance
        {
            data = data,
            visualObject = obj,
            color = synergyColor
        };

        foreach (var c in worldCells)
            ins.occupiedCells.Add(c);

        RegisterPlacedBlock(ins);

        ins.placedPiece = SynergyEvaluator.Instance?.OnPiecePlaced(
            ins.data,
            ins.color,
            ins.occupiedCells.ToArray());

        return ins;
    }

    public BlockData FindBlockData(BlockType type)
    {
        if (blocks == null) return null;
        foreach (var b in blocks)
            if (b != null && b.blockType == type) return b;
        return null;
    }

    // Unambiguous lookup by asset name — unlike FindBlockData(BlockType), which just
    // returns the first match and silently picks the wrong shape if two BlockData
    // assets share a BlockType. Used by LevelMapAuthor/GameFlowManager to resolve
    // LevelMapNode.blockAssetName.
    public BlockData FindBlockDataByName(string name)
    {
        if (blocks == null || string.IsNullOrEmpty(name)) return null;
        foreach (var b in blocks)
            if (b != null && b.name == name) return b;
        return null;
    }

    public enum PlaceFailureReason
    {
        None,
        CombatLocked,
        WouldBlockPath,
        InsufficientFunds,
        OutOfBounds,
        Occupied,
        NotAdjacent,
    }

    bool CanPlace(Vector3Int bp, Vector3Int[] cs) => Validate(bp, cs) == PlaceFailureReason.None;

    // Geometric validation only combat/funds checks live in TryPlace so they
    // can be reported in priority order.
    PlaceFailureReason Validate(Vector3Int bp, Vector3Int[] cs)
    {
        if (cs == null || cs.Length == 0) return PlaceFailureReason.None;

        var worldCells = new Vector3Int[cs.Length];
        for (int i = 0; i < cs.Length; i++)
        {
            var p = bp + cs[i];
            if (grid.IsOccupied(p)) return PlaceFailureReason.Occupied;
            worldCells[i] = p;
        }

        // Underground (y < 0) is allowed — you can build down into the earth — but
        // a block must still touch an existing block or endpoint. The Chaos Block
        // doesn't count as support (HasSupportingNeighbor26 skips it), so you can't
        // stack a turret straight onto it to attack it.
        if (!grid.HasSupportingNeighbor26(worldCells))
            return PlaceFailureReason.NotAdjacent;

        return PlaceFailureReason.None;
    }

    // ── Turret-support check (pickup / sell guard) ────────────────────────────
    // Mirrors Validate()'s NotAdjacent rule but in reverse: placing a block
    // requires it to touch something existing; removing one must not leave a
    // TURRET with nothing left to attach to. Only turrets are checked — regular
    // blocks are allowed to end up disconnected (no such rule exists for them).

    // First turret that would have zero occupied 26-neighbor cells if
    // `toRemove` were taken off the grid — null if none. Skips `toRemove`
    // itself (picking a turret up doesn't need to "support" itself).
    PlacedBlockInstance FindOrphanedTurret(PlacedBlockInstance toRemove)
    {
        if (toRemove == null || grid == null) return null;

        foreach (var other in grid.GetAllInstances())
        {
            if (other == null || other == toRemove || other.data == null) continue;
            if (!TurretTypes.Is(other.data.blockType)) continue;
            if (!HasExternalSupport(other, toRemove.occupiedCells)) return other;
        }
        return null;
    }

    // True if ANY cell of `instance` has a 26-neighbor occupied by something
    // OTHER than `instance` itself or a cell in `excludeCells` (the block about
    // to be removed) — i.e. it would still have something to attach to.
    bool HasExternalSupport(PlacedBlockInstance instance, IList<Vector3Int> excludeCells)
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

    static string ReasonToMessage(PlaceFailureReason r) => r switch
    {
        PlaceFailureReason.CombatLocked      => "Only turrets can be placed during combat",
        PlaceFailureReason.WouldBlockPath    => "Turret would block the enemy path",
        PlaceFailureReason.InsufficientFunds => "Not enough resources",
        PlaceFailureReason.OutOfBounds       => "Can't place below ground",
        PlaceFailureReason.Occupied          => "Cell is already occupied",
        PlaceFailureReason.NotAdjacent       => "Must touch an existing block or endpoint",
        _                                    => "",
    };

    // =========================
    // DELETE
    // =========================

    // Delete key in Select mode: remove the selected placed block from the grid.
    // Records an undo entry so it can be restored with Ctrl+Z.
    void TryDelete()
    {
        if (selectedInstance == null) return;

        // Deleting a PLACED object is locked during combat.
        if (GameFlowManager.Instance?.phase == GamePhase.Running)
        {
            Debug.Log("[Placement] Deletion is locked during combat.");
            return;
        }
        if (selectedInstance.locked)
        {
            Debug.Log("[Placement] This block is part of the level's fixed layout and can't be deleted.");
            return;
        }
        // Sealed blocks are sell-only: delete pushes an undo record, and restoring
        // from it would hand back an unsealed block — laundering the seal away.
        if (selectedInstance.sealedByEnemy)
        {
            ShowPlacementPopup("Sealed by the enemy — this block can't be deleted. You can still sell it.");
            return;
        }

        // Snapshot for undo before anything is destroyed.
        Color blockColor = MpbColor.Get(selectedInstance.visualObject
                            ?.GetComponentInChildren<Renderer>());
        PushUndo(new UndoRecord {
            actionType  = UndoType.Delete,
            data        = selectedInstance.data,
            color       = blockColor,
            rotation    = selectedInstance.visualObject?.transform.rotation ?? Quaternion.identity,
            cells       = selectedInstance.occupiedCells.ToArray(),
            worldCenter = selectedInstance.visualObject?.transform.position ?? Vector3.zero,
            basicPowerUpgradeLevel = selectedInstance.basicPowerUpgradeLevel,
            basicBurstUpgradeLevel = selectedInstance.basicBurstUpgradeLevel,
            aoeFireUpgradeLevel = selectedInstance.aoeFireUpgradeLevel,
            aoeGravityUpgradeLevel = selectedInstance.aoeGravityUpgradeLevel,
        });

        ResourceManager.Instance?.OnBlockRemoved(selectedInstance.data.blockType);
        SynergyEvaluator.Instance?.OnPieceRemoved(selectedInstance.placedPiece);
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
        if (!TutorialDirector.CanUndo())
        {
            Debug.Log("[Undo] Locked during the tutorial's first round.");
            return;
        }

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

    // ── Undo: new placement remove from grid, refund price ─────────────────
    void UndoNewPlace(UndoRecord rec)
    {
        if (rec.cells == null || rec.cells.Length == 0) return;
        var ins = grid.GetInstanceAt(rec.cells[0]);
        if (ins == null || ins.data != rec.data)
        {
            Debug.LogWarning("[Undo] NewPlace target no longer matches skipping.");
            return;
        }

        ResourceManager.Instance?.OnBlockRemoved(rec.data.blockType);
        SynergyEvaluator.Instance?.OnPieceRemoved(ins.placedPiece);
        NotifyBlockLifted(rec.cells);
        grid.RemoveInstance(ins);

        if (rec.pricePaid > 0)
            ResourceManager.Instance?.RefundBlock(rec.pricePaid);
    }

    // ── Undo: reposition remove from new cells, restore at old cells ────────
    void UndoReposition(UndoRecord rec)
    {
        if (rec.cells == null || rec.cells.Length == 0) return;
        var ins = grid.GetInstanceAt(rec.cells[0]);
        if (ins == null || ins.data != rec.data)
        {
            Debug.LogWarning("[Undo] Reposition target no longer matches skipping.");
            return;
        }

        ResourceManager.Instance?.OnBlockRemoved(rec.data.blockType);
        SynergyEvaluator.Instance?.OnPieceRemoved(ins.placedPiece);
        NotifyBlockLifted(rec.cells);
        grid.RemoveInstance(ins);

        // Check old cells are still free before restoring.
        bool oldCellsFree = true;
        foreach (var c in rec.prevCells)
            if (grid.IsOccupied(c)) { oldCellsFree = false; break; }

        if (oldCellsFree)
            PlaceBlockFromRecord(
                rec.data, rec.color, rec.prevCells, rec.prevCenter, rec.prevRotation,
                rec.prevBasicPowerUpgradeLevel, rec.prevBasicBurstUpgradeLevel,
                rec.prevAoeFireUpgradeLevel, rec.prevAoeGravityUpgradeLevel);
        else
            Debug.LogWarning("[Undo] Reposition origin cells now occupied block removed without restore.");
    }

    // ── Undo: delete re-place block at its old cells ────────────────────────
    void UndoDelete(UndoRecord rec)
    {
        if (rec.cells == null || rec.cells.Length == 0) return;

        bool cellsFree = true;
        foreach (var c in rec.cells)
            if (grid.IsOccupied(c)) { cellsFree = false; break; }

        if (!cellsFree)
        {
            Debug.LogWarning("[Undo] Delete restore cells now occupied cannot undo.");
            return;
        }

        PlaceBlockFromRecord(
            rec.data, rec.color, rec.cells, rec.worldCenter, rec.rotation,
            rec.basicPowerUpgradeLevel, rec.basicBurstUpgradeLevel,
            rec.aoeFireUpgradeLevel, rec.aoeGravityUpgradeLevel);
    }

    // ── Shared: instantiate a placed block from saved state ───────────────────
    void PlaceBlockFromRecord(
    BlockData data,
    Color color,
    Vector3Int[] cells,
    Vector3 center,
    Quaternion rotation,
    int basicPowerUpgradeLevel = 0,
    int basicBurstUpgradeLevel = 0,
    int aoeFireUpgradeLevel = 0,
    int aoeGravityUpgradeLevel = 0)
    {
        var obj = new GameObject("PlacedBlock");
        obj.transform.position = center;
        obj.transform.rotation = rotation;

        if (TurretTypes.Is(data.blockType))
        {
            SpawnTurretVisual(obj, data);
        }
        else
        {
            var br = obj.AddComponent<BlockRenderer>();
            br.cubePrefab = cubePrefab;

            Vector3Int origin = grid.WorldToGrid(center);

            var rel = new Vector3Int[cells.Length];

            for (int i = 0; i < cells.Length; i++)
                rel[i] = cells[i] - origin;

            br.Render(origin, rel, grid.cellSize, grid);

            foreach (var r in obj.GetComponentsInChildren<Renderer>())
                MpbColor.Set(r, color);
        }

        obj.transform.localScale = Vector3.zero;

        var ins = new PlacedBlockInstance
        {
            data = data,
            visualObject = obj,
            basicPowerUpgradeLevel = basicPowerUpgradeLevel,
            basicBurstUpgradeLevel = basicBurstUpgradeLevel,
            aoeFireUpgradeLevel = aoeFireUpgradeLevel,
            aoeGravityUpgradeLevel = aoeGravityUpgradeLevel
        };

        foreach (var c in cells)
            ins.occupiedCells.Add(c);

        RegisterPlacedBlock(ins);

        StartCoroutine(GrowIn(obj));

        ResourceManager.Instance?.OnBlockPlaced(data.blockType);
    }

    // =========================
    // SHOP GRAB
    // =========================

    // Called by ShopController when the player clicks a shop item.
    // Mirrors the tray-token grab path in TrySelectObject.
    public void GrabFromShop(SelectableBlock sb)
    {
        if (sb == null || sb.data == null) return;
        if (GameFlowManager.SettlementUp) return;   // locked during clear settlement

        // Buying NEW items is allowed during combat. Tutorial may restrict to the
        // current step's block (blocks buying the wrong one).
        if (!TutorialDirector.CanPurchase(sb.data))
        {
            ShowPlacementPopup("Not this one — follow the tutorial.");
            return;
        }

        BlockPurchased?.Invoke(sb.data);
        currentBlock        = sb.data;
        currentSynergyColor = sb.color;
        _ghostAnchorSnap    = true;   // appear at the cursor, don't glide in from the last hold
        currentColor        = sb.color != BlockColor.None
            ? BlockColorPalette.Get(sb.color)
            : MpbColor.Get(sb.GetComponentInChildren<Renderer>());
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

            PlacedBlockInstance ins = new()
            {
                data         = currentBlock,
                visualObject = obj,
                color        = currentSynergyColor,
                basicPowerUpgradeLevel = lastBasicPowerUpgradeLevel,
                basicBurstUpgradeLevel = lastBasicBurstUpgradeLevel,
                aoeFireUpgradeLevel = lastAoeFireUpgradeLevel,
                aoeGravityUpgradeLevel = lastAoeGravityUpgradeLevel,
            };

            foreach (var c in lastObjectCells)
                ins.occupiedCells.Add(c);

            RegisterPlacedBlock(ins);

            ins.placedPiece = SynergyEvaluator.Instance?.OnPiecePlaced(
                ins.data, ins.color, ins.occupiedCells.ToArray());

            // Collapse for the GrowIn pop AFTER synergy decoration has run, so
            // CellMaterialVisualizer sees the block at full size (its cell→renderer
            // match uses world bounds, which collapse onto the centre at scale 0).
            obj.transform.localScale = Vector3.zero;
            StartCoroutine(GrowIn(obj));
            // Restore count OnBlockRemoved was called on pickup, balance it back.
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
            MpbColor.Set(c.GetComponent<Renderer>(), currentColor);
        }

        obj.AddComponent<Rigidbody>();
        obj.AddComponent<SelectableBlock>().data = currentBlock;
        obj.AddComponent<BoxCollider>().size = Vector3.one * 2.5f;
    }

    // =========================
    // UTIL
    // =========================

    // Builds a turret block's visual as a child of `obj`. Shared by every spawn
    // path (player placement, starting layout, snapshot restore) because they
    // MUST agree: they used to each roll their own copy, and the direct paths
    // silently dropped the beacon (so restored turrets never span) and the
    // collider (so they couldn't be clicked or inspected at all), on top of
    // spawning at 1/50 scale.
    void SpawnTurretVisual(GameObject obj, BlockData data)
    {
        if (data?.turretPrefab == null) return;

        var visual = Instantiate(data.turretPrefab, obj.transform);
        visual.transform.localPosition = Vector3.zero;
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale    = Vector3.one;   // measure the raw prefab first

        // Fit the turret to one grid cell by ITS OWN measured bounds, rather than a
        // shared magic multiplier. The old fixed ×50 was tuned to the basic turret's
        // tiny mesh; a prefab with a different intrinsic size (e.g. the AOE turret)
        // sailed past one cell and read as oversized. Measuring each prefab's bounds
        // makes every turret — and any future prefab — snap to the same cube size.
        float target = (grid != null ? grid.cellSize : 1f) * TurretVisualCellFraction;
        FitTurretToCell(visual, target);

        visual.AddComponent<TurretBeacon>();    // idle spin + bob (rotation/pos only, doesn't touch scale)

        // Type colour, NOT the block's synergy colour — turret BlockDatas share (or
        // reuse) a prefab, so this tint is the only thing telling Basic / Slow / AOE
        // apart on the board.
        foreach (var r in visual.GetComponentsInChildren<Renderer>())
            MpbColor.Set(r, TurretTypes.DisplayColor(data.blockType));
    }

    // Uniformly scales `visual` so its largest measured renderer dimension equals
    // `targetSize` (≈ one grid cell), then adds a click-select BoxCollider matched
    // to that fitted size. Falls back to the legacy fixed multiplier if the prefab
    // has nothing measurable. Assumes `visual` starts at localScale 1 / localPos 0 /
    // identity rotation (SpawnTurretVisual sets that up).
    void FitTurretToCell(GameObject visual, float targetSize)
    {
        if (TurretVisualFit.Fit(visual, targetSize, out var localCenter, out var maxDim))
        {
            // Collider lives on `visual`, so it inherits the fitted scale: local size
            // maxDim × that scale == targetSize in world. center scales with the mesh,
            // so the box stays on the gun rather than drifting to origin.
            var col = visual.AddComponent<BoxCollider>();
            col.center = localCenter;
            col.size   = Vector3.one * maxDim;
            return;
        }

        // No renderers / degenerate bounds — fall back to the legacy fixed scale so
        // the turret is at least present and clickable.
        visual.transform.localScale = Vector3.one * TurretVisualScale;
        visual.AddComponent<BoxCollider>().size = Vector3.one / TurretVisualScale;
    }

    void RegisterPlacedBlock(PlacedBlockInstance ins)
    {
        grid.RegisterInstance(ins);
        AttachTurretController(ins);
        //AttachTurretBeacon(ins);
    }

    void AttachTurretController(PlacedBlockInstance ins)
    {
        if (ins?.data == null || ins.visualObject == null) return;
        if (!TurretTypes.Is(ins.data.blockType)) return;

        Transform target = ins.visualObject.transform;

        if (TurretTypes.Is(ins.data.blockType) &&
            ins.visualObject.transform.childCount > 0)
        {
            target = ins.visualObject.transform.GetChild(0);
        }

        var turret = target.GetComponent<TurretController>();
        if (turret == null)
            turret = target.gameObject.AddComponent<TurretController>();
        turret.Configure(ins.data.blockType, ins.data.bulletPrefab, ins.data.bulletScale);
        turret.SetBasicUpgradeLevels(ins.basicPowerUpgradeLevel, ins.basicBurstUpgradeLevel);
        turret.SetAoeUpgradeLevels(ins.aoeFireUpgradeLevel, ins.aoeGravityUpgradeLevel);
    }

    //Legacy function for debugging turret appearance
    //void AttachTurretBeacon(PlacedBlockInstance ins)
    //{
    //    if (ins?.data == null || ins.visualObject == null) return;
    //    if (!TurretTypes.Is(ins.data.blockType)) return;

    //    Vector3 centroid = Vector3.zero;
    //    int     n        = 0;
    //    foreach (Transform child in ins.visualObject.transform)
    //    {
    //        if (child.GetComponent<TurretBeacon>() != null) continue;
    //        centroid += child.position;
    //        n++;
    //    }
    //    centroid = (n == 0) ? ins.visualObject.transform.position : centroid / n;

    //    // Hide the cube meshes the beacon is the only visible part.
    //    if (ins.data.turretPrefab == null)
    //        return;
    //    float cs    = grid != null ? grid.cellSize : 1f;
    //    var marker  = GameObject.CreatePrimitive(PrimitiveType.Cube);
    //    marker.name = "TurretBeacon";
    //    marker.transform.SetParent(ins.visualObject.transform, worldPositionStays: false);
    //    marker.transform.position      = centroid;
    //    marker.transform.localScale    = Vector3.one * (0.62f * cs);
    //    marker.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);

    //    var col = marker.GetComponent<Collider>();
    //    if (col != null) Destroy(col);

    //    var rend = marker.GetComponent<Renderer>();
    //    if (rend != null)
    //    {
    //        // Use the same material asset as cubePrefab so beacons share the
    //        // silkscreen shader. (Primitives spawn with URP Lit by default.)
    //        var prefabRend = cubePrefab != null ? cubePrefab.GetComponentInChildren<Renderer>() : null;
    //        if (prefabRend != null && prefabRend.sharedMaterial != null)
    //            rend.sharedMaterial = prefabRend.sharedMaterial;

    //        // Color per turret subtype — Basic = cyan, Slow = blue-violet,
    //        // AOE = orange. Defined centrally in TurretTypes.DisplayColor.
    //        MpbColor.Set(rend, TurretTypes.DisplayColor(ins.data.blockType));
    //        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    //    }

    //    marker.AddComponent<TurretBeacon>();
    //}

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

    // Public palette picker used by ShopController so block/turret items share
    // the same colour vocabulary as placed blocks.
    public Color PickPaletteColor(BlockType type)
    {
        if (TurretTypes.Is(type)) return TurretTypes.DisplayColor(type);
        var pal = blockPalette;
        if (pal == null || pal.Length == 0) return Color.white;
        return pal[Random.Range(0, pal.Length)];
    }

    // Fallback only most code paths read the colour from the shop item the
    // player picked up. Kept consistent with the palette so any path produces
    // an in-vocabulary colour.
    Color GetRandomColor() => PickPaletteColor(BlockType.Home);

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
