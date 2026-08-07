using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Drives the level-select map. Either builds the map at runtime from a saved file
// (authored with the real placement tool — see LevelMapAuthor), or uses LevelNodes
// already placed in the scene. Click ANY cell of ANY block and the pawn walks the
// whole surface to it (cell-level BFS over the combined top faces). Arriving on a
// level block shows its panel; locked levels can be walked onto but not Entered.
public partial class LevelMapController : MonoBehaviour
{
    [Header("Build from saved map (authored with PlacementController)")]
    [Tooltip("If on, build the map at Start (from mapAsset if set, else the dev JSON). Off = use LevelNodes already in the scene.")]
    public bool buildFromFile = true;
    [Tooltip("Baked, ship-in-build map (GeoWorld ▸ Level Map ▸ Bake JSON → Asset). Preferred over the dev JSON when set.")]
    public LevelMapAsset mapAsset;
    public string mapName = "map";
    public LevelDatabase database;
    [Tooltip("Lightweight grid (for GridToWorld / cellSize) used to position rebuilt blocks.")]
    public GridSystem gridSystem;
    [Tooltip("The same cube prefab placement uses, so map blocks look identical.")]
    public GameObject cubePrefab;

    [Header("Refs")]
    public Transform pawn;
    public AK.Wwise.Event timeLoop;
    [Tooltip("UGUI level-info panel (right side). Wire it and the old IMGUI box is skipped.")]
    public LevelInfoPanel infoPanel;
    [Tooltip("Played once, the very first time the player ever lands on this scene (SaveSystem.Profile.seenLevelSelectIntro). Author it like any other DialogueConversation asset. Leave null for no intro.")]
    public DialogueConversation firstVisitConversation;
    [Tooltip("Same VinePrefab gameplay's HarmonyVineVisualizer grows on a claimed Harmony piece — reused here so a Harmony-cleared level's badge actually sprouts real vines instead of a generic ring. Leave null to fall back to the ring for every theme.")]
    public GameObject harmonyVinePrefab;

    [Header("Scenes (must be in Build Settings)")]
    public string gameplayScene = "gamePlay";
    public string titleScene    = "Title";

    [Header("Movement")]
    public float pawnSpeed = 6f;
    [Tooltip("How high the pawn floats above the block TOP FACE while surface-walking.")]
    public float pawnSurfaceLift = 0.5f;

    [Header("Path trail")]
    [Tooltip("Line material for the walk-path trail (e.g. PathFlowManager's laser material). Null = no trail drawn.")]
    public Material trailMaterial;
    public Color trailColor = new Color(1f, 0.95f, 0.75f, 0.9f);
    [Range(0.02f, 0.3f)] public float trailWidth = 0.07f;

    [Header("Camera focus")]
    [Tooltip("On click, smoothly slide the camera to frame the target cell (keeps its current offset/angle).")]
    public bool  cameraFocus = true;
    public float cameraLerp  = 4f;
    [Tooltip("Where the focused cell sits horizontally on screen. 0.5 = centre, ~0.3 = left-centre (leaves room for the right info panel).")]
    [Range(0f, 1f)] public float focusViewportX = 0.3f;
    [Range(0f, 1f)] public float focusViewportY = 0.3f;
    [Tooltip("Hold the middle mouse button and drag to nudge focusViewportX/Y live — a manual composition tweak on top of the authored default. The new values stick (no auto-reset) until dragged again.")]
    public bool  middleDragAdjustsFocus = true;
    [Tooltip("Drag speed, in viewport fraction per second at Input.GetAxis's typical magnitude.")]
    public float focusDragSpeed = 0.6f;

    [Header("Camera zoom (scroll)")]
    [Tooltip("Mouse wheel changes the camera's field of view (a perspective zoom) instead of distance. Disabled while a block is held (that scroll pushes the block instead — see HandleGhostScroll).")]
    public bool  scrollZoomsFov = true;
    public float minFov = 25f;
    public float maxFov = 65f;
    public float fovScrollSpeed = 6f;
    [Tooltip("How quickly the lens eases toward the scrolled-to FOV. Higher = snappier, lower = dreamier.")]
    public float fovSmoothSpeed = 8f;

    [Header("Build mode (overworld map extension — mirrors gameplay's block editing)")]
    [Tooltip("Blocks the player can place on this map, earned via LevelDefinition.mapBlockRewards. Only blocks in THIS list are placeable, even if granted — keep it in sync with what levels can reward.")]
    public BlockData[] buildableBlocks;
    [Tooltip("Key that opens/closes the build bar (place earned reward blocks). Matches gameplay's shop key.")]
    public KeyCode buildModeKey = KeyCode.F;
    [Tooltip("Ghost rotation ease speed — same formula/feel as PlacementController's HandleRotate.")]
    public float rotateSpeed = 10f;
    [Tooltip("How fast the held ghost GLIDES toward its snapped grid target. Purely visual — placement still lands on the exact snapped cell — but easing the render position (instead of hard-snapping cell to cell) is what makes moving a held block feel fluid instead of jumpy. Higher = snappier.")]
    public float ghostFollowSpeed = 16f;
    public Color ghostValidColor   = new Color(0.35f, 1f, 0.45f, 0.55f);
    public Color ghostInvalidColor = new Color(1f, 0.30f, 0.30f, 0.55f);
    [Tooltip("Tint applied to a freshly player-built node so it visually reads as 'built', distinct from the authored map.")]
    public Color playerBuiltColor = new Color(0.55f, 0.85f, 0.95f, 1f);
    [Tooltip("Uniform scale applied to the pawn once at Start.")]
    public float pawnScale = 1.1f;

    [Header("Testing (turn OFF for a real build)")]
    [Tooltip("Redirects saving to a throwaway dev-temp file (SaveSystem.DevTempActive) and wipes it, ONCE per Play session (not on every LevelSelect visit — clearing a level and coming back keeps that session's progress). Never touches your real numbered save slots, and the temp file is deleted automatically the moment you stop Play mode. Editor-only by nature (Play mode has no meaning in a build) — leave on freely, it's a no-op in a real build.")]
    public bool resetSaveOnStart = false;
    [Tooltip("Auto-place the carved bridge blocks below on Start so the whole map is connected and you can walk to any level to test. OFF = they stay carved out and the player must earn + place them (the real progression).")]
    public bool autoFill = true;
    [Tooltip("Grant one of EVERY reward block (all buildableBlocks) into the F build panel, without clearing any level first. OFF = the panel only shows blocks actually earned.")]
    public bool autoReward = false;
    [Tooltip("The bridge blocks removed from the Select map and handed out as level rewards — restored by Auto Fill so testing can reach every level. Cells are absolute (LevelSelect grid space).")]
    public List<PlacedMapBlock> editorAutoFillBlocks = new();

    readonly List<LevelNode> _nodes = new();
    LevelNode _current, _selected;
    bool      _moving;
    Coroutine _walkRoutine;   // the in-flight WalkCells, so a new click can cancel it
    Camera    _cam;
    GUIStyle  _title, _label, _btn;

    // Global walkable surface (cell-level): every top-exposed cell of every block,
    // plus a cell→node lookup so arriving on a level block shows its panel.
    readonly HashSet<Vector3Int> _allCells = new();
    readonly HashSet<Vector3Int> _surface  = new();
    readonly Dictionary<Vector3Int, LevelNode> _cellToNode = new();
    // (x,z) column → its top-exposed cell. Lets the pawn step between adjacent
    // columns at ANY height difference (climbing the shared edge).
    readonly Dictionary<Vector2Int, Vector3Int> _columnTop = new();
    Vector3Int _currentCell;
    OrbitCamera _orbit;        // if the main camera has one, it owns the transform — we drive it
    Quaternion _camRot;        // camera orientation, captured at Start (we only translate it)
    float      _camDepth;      // forward distance from camera to its focus point
    Vector3    _camFocus;      // world point the camera frames
    bool       _camReady;

    LineRenderer _trailLr;   // walk-path preview — shrinks from the start as the pawn walks it off

    // ── Build mode state ──────────────────────────────────────────────────────
    bool         _buildMode;
    // Read by DialogueRunner to dim itself out of the way while the player is
    // placing blocks — the map's counterpart to gameplay's PlacementMode.Edit.
    public bool BuildMode => _buildMode;
    BlockData    _ghostBlock;
    // Full 3-axis — some shapes (e.g. "corner") have a vertical arm a Y-only spin
    // could never reach. Two-value split mirrors PlacementController.HandleRotate
    // exactly: _ghostTargetRotation snaps instantly on 1/2/3, _ghostCurrentRotation
    // eases toward it every frame and is what actually drives the preview cells —
    // so the ghost visibly flips through intermediate orientations, not an instant
    // snap, same as gameplay.
    Quaternion   _ghostTargetRotation  = Quaternion.identity;
    Quaternion   _ghostCurrentRotation = Quaternion.identity;
    Vector3Int   _ghostOrigin;
    // WASDQE nudge on top of wherever the mouse is hovering — same convention as
    // gameplay's manualOffset (HandleKeyboardOffset): additive, persists across
    // mouse movement, reset to zero only when a fresh hold begins.
    Vector3Int   _ghostManualOffset;
    bool         _ghostHoveringPawnColumn;   // cursor is over the column the pawn is standing on — never a valid target
    Vector3Int[] _ghostCells;
    bool         _placementValid;
    readonly List<GameObject> _ghostGOs = new();
    Transform    _ghostRoot;

    // Eased render anchor: cells snap to the grid, cubes draw offset from this so
    // moves glide instead of pop. Snap flag skips the glide on a fresh hold.
    Vector3 _ghostVisualAnchor;
    bool    _ghostAnchorSnap;
    // Last surface height the cursor crossed, for gliding past the built edge.
    int     _ghostPlaneY;

    // True when the held ghost was SPENT from inventory at grab time (a fresh tray
    // pick). Cancelling refunds it; committing just keeps it spent. A re-picked
    // existing piece sets this false — it was already paid for on its first placement.
    bool         _ghostFromInventory;

    // Set while editing an ALREADY-PLACED piece picked back up (vs a fresh tray
    // grab): non-null origCells means Escape must restore the original instance
    // instead of just discarding it, and Commit must NOT spend inventory again
    // (it was already consumed the first time this piece was placed).
    Vector3Int[] _pickedOrigCells;
    Quaternion   _pickedOrigRotation;

    Canvas        _trayCanvas;
    RectTransform _trayTop, _trayBottom;
    RectTransform _trayList;
    TMP_Text      _trayHint;
    const float   TrayBarHeight   = 180f;   // ≈ gameplay shop's 0.16 × 1080
    const float   TrayListMargin  = 80f;    // reference-px kept clear at each end of the strip
    const float   TrayEntrySize   = 120f;   // natural entry size before any fit-to-width shrink
    const float   TrayEntrySpacing = 12f;
    [Tooltip("Bar open/close speed — matches gameplay shop's expandSpeed feel.")]
    public float  trayExpandSpeed = 9f;
    float         _trayScale;        // 0 = fully closed, 1 = fully open — animated
    float         _trayTargetScale;  // what _trayScale eases toward

    // ── "Can't reach" toast ────────────────────────────────────────────────────
    Canvas      _toastCanvas;
    CanvasGroup _toastGroup;
    TMP_Text    _toastText;
    float       _toastHideAt;

    public static LevelMapController Instance;
    // True while a block is being held/edited on the map — same shape of question as
    // PlacementController.mode==Edit, so shared UI (PlacementHintBar) can show for both.
    public bool IsEditingBlock => _buildMode && _ghostBlock != null;

    [Header("Aside test")]
    public DialogueCharacter defaultCharacter;

    void Awake() => Instance = this;

    void Start()
    {
        // Ambient source is Skybox with a PROCEDURAL skybox and no baked GI. The
        // editor recomputes the ambient probe from it live, but a standalone build
        // does NOT — leaving ambient at a dark default (the whole map looked dimmer
        // than in-editor). Force the refresh once, same as TitleShaderSwap does.
        DynamicGI.UpdateEnvironment();

        // LevelSelect has no AudioManager and plays its BGM directly — push the saved
        // volumes to Wwise here so the settings sliders actually affect it.
        GameSettings.Load();
        GameSettings.ApplyAudio();

        // Must run before ANYTHING else touches SaveSystem.Profile this session —
        // including RebuildPlacedMapBlocks below and LevelNode.Refresh's unlock
        // checks — so a fresh profile is what every system sees from frame one.
        //
        // Gated on !DevTempActive rather than firing every Start(): this scene
        // reloads every time the player returns from a level, and re-wiping on
        // every one of those visits (the original bug report) meant progress made
        // THIS SESSION never survived a single level clear. DevTempActive itself
        // doubles as the "have we already reset this session" flag — it's reset to
        // false only when Play mode actually exits (SaveSystem's editor cleanup
        // hook), so it correctly stays true across every LevelSelect reload within
        // one session and resets fresh on the next.
        if (resetSaveOnStart && !SaveSystem.DevTempActive)
        {
            SaveSystem.DevTempActive = true;
            SaveSystem.ResetProfile();
        }

        timeLoop.Post(this.gameObject);
        _cam = Camera.main;

        if (buildFromFile) BuildMap();
        else _nodes.AddRange(FindObjectsByType<LevelNode>(FindObjectsSortMode.None));

        RebuildPlacedMapBlocks();   // replay the player's own map-building from the save
        MaybeEditorAutoFill();      // editor/QA: restore the carved bridge blocks so everything's reachable
        LinkAllNodes();             // adjacency across BOTH the authored map and player-built nodes

        // Unlock default levels, then refresh state/colour on every node.
        var defs = new List<LevelDefinition>();
        foreach (var n in _nodes) if (n.level != null) defs.Add(n.level);
        SaveSystem.EnsureDefaultsUnlocked(defs);

        BuildSurface();        // must precede RefreshNodes — connectivity is read off the surface
        SpawnLevelMarkers();   // ...and must precede it too, so the badges get their first lit/dark state
        RefreshNodes();   // floating badge over every actual-level block
        // Scenery only, so it deliberately runs AFTER connectivity is settled. Returns
        // true exactly once — the very first visit after this field's gate level was
        // cleared — in which case the grow-in cutscene below plays before any dialogue.
        bool decorGrowthPending = TryBuildDecor();

        // Resume at the node the pawn last entered a level from, if we have one —
        // otherwise the usual home-block default.
        LevelNode resumeNode = null;
        if (!string.IsNullOrEmpty(RunConfig.LastLevelSelectNodeId))
            resumeNode = _nodes.Find(n => n != null && n.level != null && n.level.levelId == RunConfig.LastLevelSelectNodeId);
        _current = resumeNode ?? _nodes.Find(n => n.isStart) ?? (_nodes.Count > 0 ? _nodes[0] : null);
        if (_current != null)
        {
            _currentCell = TopCellOf(_current);
            if (pawn != null) pawn.position = SurfaceTop(_currentCell);
        }
        if (pawn != null) pawn.localScale *= pawnScale;

        // Decide who frames the camera. If the main camera has an OrbitCamera, it
        // owns the transform — we just feed it the focus point + the left bias.
        // Otherwise we translate the camera ourselves (LateUpdate).
        _orbit = _cam != null ? _cam.GetComponent<OrbitCamera>() : null;

        _camFocus = (_current != null) ? SurfaceTop(_currentCell)
                  : (pawn != null ? pawn.position : (_cam != null ? _cam.transform.position : Vector3.zero));

        if (_orbit != null)
        {
            _orbit.focusViewport = new Vector2(focusViewportX, focusViewportY);
            _orbit.FocusOnPoint(_camFocus);
        }
        else if (_cam != null)
        {
            _camRot   = _cam.transform.rotation;
            Vector3 local = Quaternion.Inverse(_camRot) * (_camFocus - _cam.transform.position);
            _camDepth = Mathf.Max(0.1f, local.z);
            _camReady = true;
        }

        // Two one-time dialogue beats (mutually exclusive per Start, intro wins the
        // tie-break): (1) firstVisitConversation, gated on seenLevelSelectIntro,
        // flag set on play not finish so it can't replay from a mid-quit; (2) a
        // level's rewardConversation, queued by GameFlowManager on first clear,
        // consumed immediately. Also subscribes the hands-on tutorial gating
        // (OnLineEvent → HandleTutorialGateEvent → CanOpenBuildPanel/CanEnterLevel,
        // completed via CompleteGate in WalkCells/EnterBuildMode/CommitPlacement/
        // EnterLevel) — scene-scoped, so no explicit unsubscribe needed.
        if (DialogueRunner.Instance != null)
        {
            DialogueRunner.Instance.OnLineEvent += HandleTutorialGateEvent;
            DialogueRunner.Instance.OnFinished  += HandleTutorialConvoFinished;
        }

        // A pending grow-in cutscene takes priority: it plays out (camera locked on
        // the new decoration while it rises into place), THEN fires whichever of the
        // two dialogue beats below applies — never the other way around, or the
        // player would be mid-conversation while the camera yanks away to the field.
        if (decorGrowthPending) StartCoroutine(PlayDecorGrowthCutscene());
        else                    PlayEntryDialogueIfAny();
    }

    void PlayEntryDialogueIfAny()
    {
        if (!SaveSystem.Profile.seenLevelSelectIntro && firstVisitConversation != null)
        {
            SaveSystem.Profile.seenLevelSelectIntro = true;
            SaveSystem.Save();
            DialogueRunner.Instance.Play(firstVisitConversation);
        }
        else if (RunConfig.PendingRewardConversation != null)
        {
            var convo   = RunConfig.PendingRewardConversation;
            var levelId = RunConfig.PendingRewardLevelId;
            RunConfig.PendingRewardConversation = null;
            RunConfig.PendingRewardLevelId       = null;

            // "Here's what you earned, and here's where you earned it" — focus the
            // camera on the level that granted this reward BEFORE the dialogue (and
            // the build tutorial it kicks off) starts, instead of leaving the camera
            // wherever it happened to be left facing.
            LevelNode grantingNode = null;
            if (!string.IsNullOrEmpty(levelId))
                grantingNode = _nodes.Find(n => n != null && n.level != null && n.level.levelId == levelId);
            if (_orbit != null && grantingNode != null)
                _orbit.FocusOnPoint(grantingNode.transform.position, snap: false);

            // Remembered so the build-tutorial gate (below) knows whose
            // rewardSuggestCubeSide/rewardSuggestOrigin to show a hint box at.
            _activeRewardLevel = grantingNode != null ? grantingNode.level : null;

            DialogueRunner.Instance.Play(convo);
        }
    }

    // Looks up a level's node position on the map by levelId — used by
    // LevelSelectTutorialGuide to point the world-space tutorial arrow at a
    // specific level (e.g. "only this one is connected") instead of the pawn.
    public Vector3? FindLevelNodePosition(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return null;
        var node = _nodes.Find(n => n != null && n.level != null && n.level.levelId == levelId);
        return node != null ? node.transform.position : (Vector3?)null;
    }

    // ── Hands-on tutorial gate ───────────────────────────────────────────────
    // Non-null while a DialogueLine.actionGateId is currently on screen waiting
    // for CompleteGate — the one operation whose id matches is allowed, every
    // other gated operation is refused (with a toast) until it's this one's turn.
    // null = no tutorial gate active → everything allowed, same "no tutorial →
    // unrestricted" default TutorialDirector uses in gameplay.
    string _tutorialGate;

    // Set in PlayEntryDialogueIfAny() when a reward conversation starts — which
    // level's rewardSuggestCubeSide/rewardSuggestOrigin the build-tutorial
    // suggestion box (below) should show while ls.openbuild/ls.place is active.
    LevelDefinition _activeRewardLevel;

    // Fires whenever the active gate changes (including to null on finish/skip).
    // Purely a notification — LevelSelectTutorialGuide listens to this to show a
    // camera nudge + arrow at whatever the gate wants the player to do next,
    // without this class needing to know anything about that presentation.
    public static event System.Action<string> OnTutorialGateChanged;

    public static class TutorialGateIds
    {
        public const string Walk       = "ls.walk";       // T_levelSelect: click a block and arrive
        public const string EnterLevel = "ls.enter";       // T_levelSelect: click Enter on a level's panel
        public const string OpenBuild  = "ls.openbuild";   // T_rewardBlock: press F
        public const string Place      = "ls.place";       // T_rewardBlock: successfully place the earned block
    }

    void HandleTutorialGateEvent(string id)
    {
        if (id == TutorialGateIds.Walk || id == TutorialGateIds.EnterLevel
            || id == TutorialGateIds.OpenBuild || id == TutorialGateIds.Place)
        {
            _tutorialGate = id;
            OnTutorialGateChanged?.Invoke(id);
            UpdateBuildSuggestionBox();
        }
    }

    // Fires on skip AND natural completion alike (DialogueRunner.Finish is the
    // common tail for both), so a skipped tutorial can never leave an operation
    // stuck locked with no dialogue left on screen to explain why.
    void HandleTutorialConvoFinished(DialogueConversation _)
    {
        _tutorialGate = null;
        OnTutorialGateChanged?.Invoke(null);
        UpdateBuildSuggestionBox();
    }

    // ── Build-tutorial suggestion box ───────────────────────────────────────
    // Translucent, non-binding hint — one cube per cell of the reward block's
    // shape, shown while the reward-block placement gate is active.
    [Header("Build-tutorial suggestion box")]
    [Tooltip("Tint of the translucent placement hint.")]
    public Color rewardSuggestColor = new Color(1f, 1f, 1f, 0.6f);
    [Tooltip("Suggestion cubes are this much larger than a cell, so they stand proud of the ground.")]
    public float rewardSuggestOverscale = 1.08f;
    [Tooltip("OrbitCamera zoom when the box first appears. LevelSelect's default distance is 10.")]
    public float rewardSuggestZoom = 5f;

    GameObject _rewardSuggestBox;
    readonly List<Renderer> _rewardSuggestRends = new();
    static Material _rewardSuggestMat;

    void UpdateBuildSuggestionBox()
    {
        bool wantShow = _activeRewardLevel != null && _activeRewardLevel.rewardSuggestCubeSide > 0
                     && (_tutorialGate == TutorialGateIds.OpenBuild || _tutorialGate == TutorialGateIds.Place);

        if (wantShow)
        {
            if (_rewardSuggestBox == null)
            {
                BuildRewardSuggestBox();
                if (_orbit != null && gridSystem != null)
                {
                    _orbit.FocusOnPoint(gridSystem.GridToWorld(_activeRewardLevel.rewardSuggestOrigin), snap: false);
                    if (rewardSuggestZoom > 0f) _orbit.SetZoom(rewardSuggestZoom);
                }
            }
        }
        else if (_rewardSuggestBox != null)
        {
            Destroy(_rewardSuggestBox);
            _rewardSuggestBox = null;
            _rewardSuggestRends.Clear();
        }
    }

    // Absolute cells the placement hint occupies, or null. Single source of truth
    // for both the translucent box and RewardPlacementBlocked below.
    Vector3Int[] RewardSuggestCells()
    {
        var lv = _activeRewardLevel;
        if (lv == null || lv.rewardSuggestCubeSide <= 0) return null;

        var rewards = lv.mapBlockRewards;
        var shapeCells = (rewards != null && rewards.Length > 0 && rewards[0] != null && rewards[0].cells != null && rewards[0].cells.Length > 0)
                        ? rewards[0].cells
                        : new[] { Vector3Int.zero };

        var r   = lv.rewardSuggestRotation90;   // same rotation convention as TutorialStep.TargetCells()
        var rot = Quaternion.Euler(90f * r.x, 90f * r.y, 90f * r.z);

        var outCells = new Vector3Int[shapeCells.Length];
        for (int i = 0; i < shapeCells.Length; i++)
            outCells[i] = lv.rewardSuggestOrigin + Vector3Int.RoundToInt(rot * (Vector3)shapeCells[i]);
        return outCells;
    }

    // True when the reward tutorial's placement step is waiting and `cells` isn't
    // the spot — folded into _placementValid so the ghost just reads red instead of
    // landing wrong and softlocking (F is itself gated to OpenBuild).
    bool RewardPlacementBlocked(Vector3Int[] cells)
    {
        if (_tutorialGate != TutorialGateIds.Place) return false;
        var want = RewardSuggestCells();
        return want != null && !CellsEqual(want, cells);
    }

    void BuildRewardSuggestBox()
    {
        if (gridSystem == null) return;
        var cellsAbs = RewardSuggestCells();
        if (cellsAbs == null) return;

        float cs = gridSystem.cellSize;
        _rewardSuggestBox = new GameObject("RewardSuggestBox");
        _rewardSuggestBox.transform.SetParent(transform, true);

        foreach (var abs in cellsAbs)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "RewardSuggestCell";
            cube.transform.SetParent(_rewardSuggestBox.transform, true);
            cube.transform.position   = gridSystem.GridToWorld(abs);
            cube.transform.localScale = Vector3.one * cs * rewardSuggestOverscale;
            var col = cube.GetComponent<Collider>(); if (col != null) Destroy(col);
            var rend = cube.GetComponent<Renderer>();
            rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rend.sharedMaterial    = RewardSuggestMaterial();
            MpbColor.Set(rend, rewardSuggestColor);
            _rewardSuggestRends.Add(rend);
        }
    }

    // Same pulse formula as TutorialDirector's suggestion box, so a hint reads
    // identically whether it's on the gameplay grid or the LevelSelect map.
    void PulseRewardSuggestBox()
    {
        if (_rewardSuggestRends.Count == 0) return;
        // Floor raised to 70% of peak (vs. TutorialDirector's 50%) — this hint
        // competes with a much busier backdrop (farm decor, colored map blocks)
        // than gameplay's flat grid, so it can't fade as low without disappearing.
        float a = rewardSuggestColor.a * (0.7f + 0.3f * Mathf.PingPong(Time.time * 0.8f, 1f));
        var c = new Color(rewardSuggestColor.r, rewardSuggestColor.g, rewardSuggestColor.b, a);
        for (int i = 0; i < _rewardSuggestRends.Count; i++)
            if (_rewardSuggestRends[i] != null) MpbColor.Set(_rewardSuggestRends[i], c);
    }

    static Material RewardSuggestMaterial()
    {
        if (_rewardSuggestMat != null) return _rewardSuggestMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _rewardSuggestMat = new Material(sh);
        if (_rewardSuggestMat.HasProperty("_Surface"))
        {
            _rewardSuggestMat.SetFloat("_Surface", 1f);
            _rewardSuggestMat.SetFloat("_ZWrite", 0f);
            _rewardSuggestMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _rewardSuggestMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _rewardSuggestMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _rewardSuggestMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _rewardSuggestMat;
    }

    bool CanOpenBuildPanel() => string.IsNullOrEmpty(_tutorialGate) || _tutorialGate == TutorialGateIds.OpenBuild;
    bool CanEnterLevel()     => string.IsNullOrEmpty(_tutorialGate) || _tutorialGate == TutorialGateIds.EnterLevel;

    // The loop was Post()'d against this GameObject — Wwise doesn't stop it on its own
    // just because the scene unloads, so stop it explicitly or it bleeds into gameplay.
    void OnDestroy() => timeLoop.Stop(this.gameObject);

    void LateUpdate()
    {
        // OrbitCamera (if present) owns the transform — we drove it via FocusOnPoint.
        if (!cameraFocus || _orbit != null || !_camReady || _cam == null) return;
        _cam.transform.position = Vector3.Lerp(
            _cam.transform.position,
            DesiredCamPos(),
            1f - Mathf.Exp(-cameraLerp * Time.deltaTime));
    }

    // Route a focus request to the OrbitCamera if present, else our own framer.
    void FocusCameraOn(Vector3 worldPoint)
    {
        _camFocus = worldPoint;
        if (_orbit != null)
        {
            _orbit.focusViewport = new Vector2(focusViewportX, focusViewportY);
            _orbit.FocusOnPoint(worldPoint, snap: false);   // glide to the clicked cell
        }
    }

    // Exact: place _camFocus at viewport (focusViewportX, focusViewportY) keeping the
    // captured rotation and depth — works at any camera angle (no centring drift).
    Vector3 DesiredCamPos()
    {
        float zc = _camDepth;
        float halfW, halfH;
        if (_cam.orthographic)
        {
            halfH = _cam.orthographicSize;
            halfW = halfH * _cam.aspect;
        }
        else
        {
            halfH = zc * Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            halfW = halfH * _cam.aspect;
        }

        // Focus position in camera space for the desired viewport point.
        Vector3 local = new Vector3(
            (focusViewportX - 0.5f) * 2f * halfW,
            (focusViewportY - 0.5f) * 2f * halfH,
            zc);

        return _camFocus - _camRot * local;
    }

    // ── Build the map from the saved layout ──────────────────────────────────
    void BuildMap()
    {
        // Prefer the baked asset (ships in build); fall back to the dev JSON.
        var data = mapAsset != null ? mapAsset.data : LevelMapIO.Load(mapName);
        if (data == null || data.nodes == null || data.nodes.Count == 0
            || gridSystem == null || cubePrefab == null)
        {
            Debug.LogWarning("[LevelMap] cannot build — missing map data, GridSystem, or cubePrefab.");
            return;
        }
        if (database == null)
            Debug.LogWarning("[LevelMap] No Database assigned — level blocks can't resolve their LevelDefinition, so no level panel will show. Assign a LevelDatabase on LevelMapController.");

        foreach (var node in data.nodes)
        {
            if (node.cells == null || node.cells.Length == 0) continue;

            // Block centroid (so the pawn lands on the node); cubes are placed at
            // their world cells by BlockRenderer regardless of the parent.
            Vector3 centroid = Vector3.zero;
            foreach (var c in node.cells) centroid += gridSystem.GridToWorld(c);
            centroid /= node.cells.Length;

            var obj = new GameObject(string.IsNullOrEmpty(node.levelId) ? "Waypoint" : "Level_" + node.levelId);
            obj.transform.position = centroid;

            var br = obj.AddComponent<BlockRenderer>();
            br.cubePrefab = cubePrefab;
            br.Render(Vector3Int.zero, node.cells, gridSystem.cellSize, gridSystem);

            var ln = obj.AddComponent<LevelNode>();
            ln.cells      = node.cells;
            ln.level      = database != null ? database.Find(node.levelId) : null;
            ln.isStart    = node.isStart;
            // node.color is whatever the renderer happened to be tinted with when
            // the map was captured, so authored maps drift into near-identical
            // shades (two greens at different brightness, etc). Pull every block
            // onto the canonical synergy palette: use the authored synergy colour
            // when there is one, otherwise snap the captured tint to whichever
            // theme it's closest to.
            ln.themeColor = node.synergyColor != BlockColor.None
                ? BlockColorPalette.Get(node.synergyColor)
                : BlockColorPalette.Snap(node.color);
            _nodes.Add(ln);

            if (!string.IsNullOrEmpty(node.levelId) && ln.level == null)
                Debug.LogWarning($"[LevelMap] block tagged level '{node.levelId}' but it isn't in the Database — add it, or this block won't show a panel.");
        }
    }

    // Auto-link face-adjacent nodes (so a placed/built path is walkable with no
    // manual wiring). Idempotent (skips pairs already linked) — safe to call again
    // after build-mode adds a new node, instead of re-deriving the whole map.
    void LinkAllNodes()
    {
        for (int i = 0; i < _nodes.Count; i++)
            for (int j = i + 1; j < _nodes.Count; j++)
                if (_nodes[i].IsAdjacentTo(_nodes[j]) && !_nodes[i].neighbors.Contains(_nodes[j]))
                {
                    _nodes[i].neighbors.Add(_nodes[j]);
                    _nodes[j].neighbors.Add(_nodes[i]);
                }
    }

    // Replays every block the player has placed on this map in a previous session
    // (SaveSystem.Profile.placedMapBlocks) so their extended network persists.
    // Skipped silently (with a warning) if a block's asset can no longer be
    // resolved — e.g. buildableBlocks was edited after the block was placed.
    void RebuildPlacedMapBlocks()
    {
        if (gridSystem == null || cubePrefab == null) return;
        var placed = SaveSystem.Profile.placedMapBlocks;
        if (placed == null) return;

        foreach (var p in placed)
        {
            if (p?.cells == null || p.cells.Length == 0) continue;
            var bd = FindBuildableBlock(p.blockAssetName);
            if (bd == null)
            {
                Debug.LogWarning($"[LevelMap] saved player-built block '{p.blockAssetName}' isn't in buildableBlocks — skipped (it stays in the save in case the block comes back).");
                continue;
            }
            SpawnMapBlockNode(p.cells, bd, p.rotation);
        }
    }

    // Testing convenience (gated by `autoFill`): restore the carved-out bridge
    // blocks so the whole map is connected and you can walk to any level to test.
    // Writes NOTHING to the save — with autoFill off the blocks stay carved so the
    // earn-reward-then-build progression is what the player actually sees.
    void MaybeEditorAutoFill()
    {
        if (!autoFill) return;
        if (editorAutoFillBlocks == null || gridSystem == null || cubePrefab == null) return;

        // Don't stack on cells already taken by an authored node or a save-replayed
        // player build (so this is idempotent with whatever the player already did).
        var covered = new HashSet<Vector3Int>();
        foreach (var n in _nodes) if (n?.cells != null) foreach (var c in n.cells) covered.Add(c);

        foreach (var p in editorAutoFillBlocks)
        {
            if (p?.cells == null || p.cells.Length == 0) continue;
            bool overlap = false;
            foreach (var c in p.cells) if (covered.Contains(c)) { overlap = true; break; }
            if (overlap) continue;
            var bd = FindBuildableBlock(p.blockAssetName);
            SpawnMapBlockNode(p.cells, bd, p.rotation);   // bd may be null if no longer in buildableBlocks — the node just won't be re-pickable
            foreach (var c in p.cells) covered.Add(c);
        }
    }

    // Instantiates a plain waypoint node (level == null — a pure connector) at the
    // given ABSOLUTE cells, exactly like BuildMap()'s per-node loop. Shared by the
    // save-replay path and the live build-mode commit. `block`/`rotation` are
    // remembered on the node (LevelNode.sourceBlock/builtRotation) so a player-built
    // piece can be picked back up and re-edited later — pass block=null for
    // auto-fill pieces whose asset no longer resolves (inert, not re-pickable).
    LevelNode SpawnMapBlockNode(Vector3Int[] absCells, BlockData block, Quaternion rotation)
    {
        Vector3 centroid = Vector3.zero;
        foreach (var c in absCells) centroid += gridSystem.GridToWorld(c);
        centroid /= absCells.Length;

        var obj = new GameObject("PlayerBuilt_" + (block != null ? block.name : "?"));
        obj.transform.position = centroid;

        var br = obj.AddComponent<BlockRenderer>();
        br.cubePrefab = cubePrefab;
        br.Render(Vector3Int.zero, absCells, gridSystem.cellSize, gridSystem);

        var ln = obj.AddComponent<LevelNode>();
        ln.cells         = absCells;
        ln.level         = null;
        ln.isStart       = false;
        ln.themeColor    = playerBuiltColor;
        ln.sourceBlock   = block;
        ln.builtRotation = rotation;
        _nodes.Add(ln);
        return ln;
    }

    BlockData FindBuildableBlock(string name)
    {
        if (buildableBlocks == null || string.IsNullOrEmpty(name)) return null;
        foreach (var b in buildableBlocks)
            if (b != null && b.name == name) return b;
        return null;
    }

    // ── Level markers ──────────────────────────────────────────────────────────
    // A floating, spinning diamond over every ACTUAL-level block, so levels read as
    // distinct from the plain connector / player-built path blocks around them.
    void SpawnLevelMarkers()
    {
        foreach (var n in _nodes)
        {
            if (n == null || n.level == null || n.cells == null || n.cells.Length == 0) continue;
            // Same height the pawn itself rests at when it walks onto this node —
            // reads as "the piece for this level", not a banner floating above it.
            BuildLevelMarker(n, SurfaceTop(TopCellOf(n)));
        }
    }

    // Runtime CreatePrimitive cubes get the built-in default material, whose shader
    // is stripped from standalone builds (renders magenta there while fine in the
    // editor). Reuse the map block's OWN material — proven to render in the build,
    // since the blocks do — with a URP Lit fallback. MpbColor still drives colour.
    static Material _badgeMat;
    Material BadgeMaterial()
    {
        if (_badgeMat != null) return _badgeMat;
        var src = cubePrefab != null ? cubePrefab.GetComponentInChildren<Renderer>() : null;
        if (src != null && src.sharedMaterial != null) { _badgeMat = src.sharedMaterial; return _badgeMat; }
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _badgeMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _badgeMat;
    }

    void BuildLevelMarker(LevelNode node, Vector3 worldPos)
    {
        float cs = gridSystem != null ? gridSystem.cellSize : 1f;

        var root = new GameObject("LevelMarker_" + (node.level != null ? node.level.levelId : "?"));
        root.transform.SetParent(node.transform, worldPositionStays: false);
        root.transform.position = worldPos;

        // Diamond = a cube tilted 45°, small + bright, floating above the level block.
        var diamond = GameObject.CreatePrimitive(PrimitiveType.Cube);
        diamond.name = "Badge";
        diamond.transform.SetParent(root.transform, false);
        diamond.transform.localScale    = Vector3.one * (cs * 0.34f);
        diamond.transform.localRotation = Quaternion.Euler(45f, 0f, 45f);
        if (diamond.TryGetComponent<Collider>(out var col)) Destroy(col);   // never blocks map raycasts
        var mr = diamond.GetComponent<MeshRenderer>();
        mr.sharedMaterial    = BadgeMaterial();   // build-safe — see BadgeMaterial()
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // Sibling of the badge, not a child — the synergy effect grows out of the
        // block and stays put instead of riding the badge's spin/bob.
        var celeb = new GameObject("Celebration");
        celeb.transform.SetParent(node.transform, worldPositionStays: false);
        celeb.transform.position = worldPos;
        celeb.transform.rotation = Quaternion.identity;   // node rotation would tip the vine/flowers over

        // Top faces this level's block exposes — where an Abundance field plants.
        // BlockTop (no pawnSurfaceLift), not SurfaceTop, or flowers float above the block.
        var tops = new List<Vector3>();
        foreach (var c in node.cells)
            if (_surface.Contains(c)) tops.Add(BlockTop(c));
        if (tops.Count == 0) tops.Add(BlockTop(TopCellOf(node)));

        // Fallback ring for themes with no bespoke effect. Built once, hidden until Cleared.
        var ring = MakeSquareFrame(celeb.transform, "ClearRing", cs * 0.52f, cs * 0.03f, GeoPalette.Gold);
        ring.localPosition = Vector3.up * (cs * 0.04f);   // rest just above the surface, not clipping into it
        ring.gameObject.SetActive(false);
        var ringRends = ring.GetComponentsInChildren<Renderer>();

        var m = root.AddComponent<MapLevelMarker>();
        m.Bind(mr, diamond.transform, node.themeColor, node.lockedColor,
               0f,           // rise: 0 = same resting height as the pawn (SurfaceTop) — they float in sync
               cs * 0.09f,   // grounded: well below its own half-height, so a dead badge visibly sinks/lies low
               celeb.transform, ring, ringRends, harmonyVinePrefab, tops.ToArray(), cs);
        _markers[node] = m;
    }

    // Unreachable = badge lies dead flat on the block, grey. Connected = it lifts
    // off, tips onto its point, and spins — no legend needed to tell them apart.
    class MapLevelMarker : MonoBehaviour
    {
        Vector3    _base;          // resting spot ON the block, captured on first frame
        bool       _captured;
        Renderer   _rend;
        Transform  _badge;         // the tilted cube child — tips upright/flat with power
        Color      _lit, _dark;
        float      _rise;          // how far above _base it floats when powered — 0 matches the pawn's own bob height
        float      _grounded;      // lift while dark, so it rests ON the surface, not in it
        bool       _powered = true;
        float      _t;             // 0 = fully dead, 1 = fully alive; eased, so changes read as a transition
        float      _spin;          // accumulated Y rotation — driven, not integrated onto the transform

        // Celebration effect — plays once the level is Cleared, themed to whatever
        // synergy was winning at the buzzer (BlockColor.None or Universal picks a
        // random theme instead, once, cached so it doesn't flicker between
        // refreshes). Harmony gets the REAL thing — gameplay's own vine growth,
        // reused verbatim — since a generic ring can't read as "vines" no matter
        // how it's tinted. Every other theme falls back to a ring in that theme's
        // colour until it earns its own bespoke treatment.
        Transform    _celebRoot;   // stable, on the block surface — where the effect actually lives
        Transform    _ring;
        Renderer[]   _ringRends;
        GameObject   _vinePrefab;
        Vector3[]    _cellTops;    // this block's top faces — where an Abundance field plants
        float        _cellSize;
        bool         _cleared;
        Color        _themeColor;
        bool         _themePicked;
        bool         _effectGrown; // a bespoke effect (vine / bloom) took over — suppress the fallback ring
        float        _ringT;

        public void Bind(Renderer rend, Transform badge, Color lit, Color dark, float rise, float grounded,
                         Transform celebRoot, Transform ring, Renderer[] ringRends, GameObject vinePrefab,
                         Vector3[] cellTops, float cellSize)
        {
            _rend  = rend;
            _badge = badge;
            _lit   = lit;  _lit.a  = 1f;
            _dark  = dark; _dark.a = 1f;
            _rise  = rise;
            _grounded = grounded;
            _celebRoot   = celebRoot;
            _ring        = ring;
            _ringRends   = ringRends;
            _vinePrefab  = vinePrefab;
            _cellTops    = cellTops;
            _cellSize    = cellSize;
            _t = 1f;   // assume alive; RebuildWindLinks corrects it before the first frame renders
        }

        public void SetPowered(bool on) => _powered = on;

        // `color` is the synergy theme active at the moment this level was last
        // cleared (LevelRecord.clearSynergyColor). Resolved once, the first time
        // `cleared` goes true:
        //   • A real theme → that theme's own effect, reusing gameplay's actual
        //     visualizer geometry: Harmony grows vines (VineEffect), Abundance
        //     blooms a flower field (BloomPatch), Enlightenment hangs a star-sphere
        //     (ConstellationView). Themes without a bespoke effect authored yet fall
        //     back to the ring tinted in their colour.
        //   • Universal / None → the plain original GOLD frame, no theme colour and
        //     no bespoke effect — "you cleared it, but not under any one banner".
        public void SetCleared(bool cleared, BlockColor color)
        {
            _cleared = cleared;
            if (!cleared || _themePicked) return;
            _themePicked = true;

            if (color == BlockColor.None || color == BlockColor.Universal)
            {
                _themeColor = GeoPalette.Gold;   // original frame, unchanged
                return;                          // ring only — never a bespoke effect
            }

            _themeColor = BlockColorPalette.Get(color);
            if      (color == BlockColor.Harmony && _vinePrefab != null) GrowVine();
            else if (color == BlockColor.Abundance)                      GrowBloom();
            else if (color == BlockColor.Enlightenment)                  GrowStars();
        }

        // Enlightenment — the SAME ConstellationView star-sphere gameplay's
        // EnlightenmentConstellationVisualizer orbits around a claimed cube, rebuilt
        // here with the visualizer's own defaults (fibonacci sphere, nearest-neighbour
        // mesh, theme×white stars with additive glow, twinkle + slow tumble) so the
        // celebration reads identically to the in-game synergy.
        void GrowStars()
        {
            if (_effectGrown || _cellTops == null || _cellTops.Length == 0) return;
            _effectGrown = true;

            // Sphere wraps the block's footprint. Bounds from the top faces →
            // circumscribed radius = side·√3/2·radiusMul, matching the visualizer.
            Vector3 mn = _cellTops[0], mx = _cellTops[0];
            foreach (var p in _cellTops) { mn = Vector3.Min(mn, p); mx = Vector3.Max(mx, p); }
            Vector3 center = (mn + mx) * 0.5f + Vector3.up * (_cellSize * 0.5f);
            float side   = Mathf.Max(mx.x - mn.x, mx.z - mn.z) + _cellSize;
            float radius = side * 0.5f * 1.7320508f * 1.15f;

            const int N = 24;
            const float GA = 2.399963229728653f;   // golden angle
            var stars = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                float t   = (i + 0.5f) / N;
                float y   = 1f - 2f * t;
                float r   = Mathf.Sqrt(Mathf.Max(0f, 1f - y * y));
                float phi = GA * i;
                stars[i] = center + new Vector3(Mathf.Cos(phi) * r, y, Mathf.Sin(phi) * r) * radius;
            }
            var edges = NearestLinks(stars, 3);

            // Colours + brightness copied from EnlightenmentConstellationVisualizer.
            Color starCol = Color.Lerp(_themeColor, Color.white, 0.5f) * 2f;  starCol.a = 1f;
            Color lineCol = _themeColor * 1.2f;                                lineCol.a = 0.5f;

            var go = new GameObject("EnlightenmentStarSphere");
            go.transform.SetParent(_celebRoot, worldPositionStays: true);
            var view = go.AddComponent<ConstellationView>();
            view.twinkleSpeed = 3f; view.twinkleDepth = 0.5f;
            view.spinSpeed = 24f;   view.tiltSpeed = 6f;
            view.fadeInDuration = 0.6f;
            view.Build(center, stars, edges, starCol, lineCol, 0.35f * _cellSize);
        }

        // Each star linked to its k nearest neighbours (deduped) — the same faceted
        // sphere mesh EnlightenmentConstellationVisualizer.BuildSphereLinks makes.
        static List<Vector2Int> NearestLinks(Vector3[] pts, int k)
        {
            var edges = new List<Vector2Int>();
            int n = pts.Length;
            if (n < 2) return edges;
            k = Mathf.Clamp(k, 1, n - 1);
            var have  = new HashSet<long>();
            var order = new int[n];
            var dist  = new float[n];
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++) { order[j] = j; dist[j] = (pts[j] - pts[i]).sqrMagnitude; }
                System.Array.Sort(order, (x, y) => dist[x].CompareTo(dist[y]));
                int added = 0;
                for (int m = 0; m < n && added < k; m++)
                {
                    int j = order[m];
                    if (j == i) continue;
                    long key = i < j ? ((long)i << 32) | (uint)j : ((long)j << 32) | (uint)i;
                    if (have.Add(key)) edges.Add(new Vector2Int(i, j));
                    added++;
                }
            }
            return edges;
        }

        // Abundance — the same BloomPatch flower field gameplay plants on a claimed
        // Abundance block, driven straight off this level block's own top faces.
        // BloomPatch positions itself at the centre of the tops it's given, so the
        // field lands on the block regardless of where _celebRoot sits.
        void GrowBloom()
        {
            if (_effectGrown || _cellTops == null || _cellTops.Length == 0) return;
            _effectGrown = true;

            var go = new GameObject("AbundanceBloom");
            go.transform.SetParent(_celebRoot, worldPositionStays: true);

            var patch = go.AddComponent<BloomPatch>();
            patch.stemHeight   = _cellSize * 0.18f;
            patch.bobAmplitude = _cellSize * 0.03f;
            patch.Grow(_cellTops,
                       PetalPalette(_themeColor),
                       new Color(0.98f, 0.80f, 0.30f, 1f),   // golden heart, same accent the visualizer uses
                       maxFlowersPerCell: 2,
                       flowerSizeWorld:   _cellSize * 0.34f,
                       scatterWorld:      _cellSize * 0.26f,
                       maxFlowers:        18);
        }

        // Four analogous petal tints around the theme hue (from AbundanceVisualizer.BuildPalette).
        static Color[] PetalPalette(Color baseCol)
        {
            Color.RGBToHSV(baseCol, out float h, out float s, out float v);
            s = Mathf.Clamp(s, 0.45f, 0.92f);
            v = Mathf.Clamp(v, 0.70f, 1f);
            const float sp = 0.08f;
            return new[]
            {
                Color.HSVToRGB(Mathf.Repeat(h,          1f), s,                        v),
                Color.HSVToRGB(Mathf.Repeat(h + sp,     1f), Mathf.Clamp01(s * 0.88f), Mathf.Clamp01(v * 1.03f)),
                Color.HSVToRGB(Mathf.Repeat(h - sp,     1f), s,                        Mathf.Clamp01(v * 0.94f)),
                Color.HSVToRGB(Mathf.Repeat(h + sp * 2f, 1f), Mathf.Clamp01(s * 0.78f), v),
            };
        }

        // One-shot (VineEffect grows itself then sits still), planted under
        // _celebRoot so it grows out of the block, not this spinning/bobbing marker.
        void GrowVine()
        {
            if (_effectGrown || _celebRoot == null) return;
            _effectGrown = true;

            var vine = Instantiate(_vinePrefab, _celebRoot);
            vine.transform.localPosition = Vector3.zero;
            if (vine.TryGetComponent<VineEffect>(out var fx))
            {
                var root = _themeColor;
                var tip  = Color.Lerp(root, Color.white, 0.5f);
                // Stable per-node direction so vines don't all sprout the same way.
                var outward = Quaternion.Euler(0f, Hash01(GetInstanceID()) * 360f, 0f) * Vector3.forward;
                fx.Grow(root, tip, outward, _vinePrefab);
            }
        }

        static float Hash01(int h)
        {
            unchecked { h = (h ^ 61) ^ (h >> 16); h += h << 3; h ^= h >> 4; h *= 0x27d4eb2d; h ^= h >> 15; }
            return (h & 0x7fffffff) / (float)0x7fffffff;
        }

        void Update()
        {
            if (!_captured) { _base = transform.position; _captured = true; }

            _t = Mathf.Lerp(_t, _powered ? 1f : 0f, 1f - Mathf.Exp(-4f * Time.deltaTime));

            // Height: grounded when dead, risen + bobbing when alive.
            float h = Mathf.Lerp(_grounded, _rise, _t) + Mathf.Sin(Time.time * 2f) * 0.12f * _t;
            transform.position = _base + Vector3.up * h;

            // Spin: driven from an accumulator scaled by _t, so a dying badge eases to
            // a stop instead of freezing mid-turn (and a waking one spins up smoothly).
            _spin += 90f * _t * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, _spin, 0f);

            // Tip: on its point when alive, flat on a face when dead — the difference
            // between a badge and a discarded block.
            if (_badge != null)
                _badge.localRotation = Quaternion.Slerp(Quaternion.identity,
                                                        Quaternion.Euler(45f, 0f, 45f), _t);

            transform.localScale = Vector3.one * Mathf.Lerp(0.8f, 1f, _t);

            if (_rend != null) MpbColor.Set(_rend, Color.Lerp(_dark, _lit, _t));

            UpdateRing();
        }

        // Frame effect: a slow spin + a smooth breathing pulse (sine, NOT a sawtooth
        // — no snap-back at the loop point). Shows the moment the level is Cleared
        // and stays, since clearing is permanent (independent of whether the road is
        // currently connected). Only for themes without a bespoke effect — Harmony
        // grows a real vine instead (GrowVine) and never shows this ring.
        void UpdateRing()
        {
            if (_ring == null || _effectGrown) return;

            if (_ring.gameObject.activeSelf != _cleared) _ring.gameObject.SetActive(_cleared);
            if (!_cleared) return;

            const float period = 1.8f;
            _ringT += Time.deltaTime;
            float scale = Mathf.Lerp(0.9f, 1.15f, 0.5f + 0.5f * Mathf.Sin(_ringT * (Mathf.PI * 2f / period)));
            _ring.localScale = Vector3.one * scale;
            _ring.localRotation = Quaternion.Euler(0f, _ringT * 22f, 0f);   // slow ceremonial turn

            if (_ringRends != null)
            {
                var c = _themeColor; c.a = 1f;
                foreach (var r in _ringRends) if (r != null) MpbColor.Set(r, c);
            }
        }
    }

    // Same idle up/down float as MapLevelMarker's bob, applied to the pawn while it's
    // NOT walking — WalkCells owns pawn.position exclusively during a walk (it drives
    // exact MoveTowards targets), so this only ever touches it between walks, layered
    // on top of the pawn's real resting spot (recomputed fresh each frame — nothing to
    // "capture", so it can never drift).
    void UpdatePawnBob()
    {
        if (_moving || pawn == null || _nodes.Count == 0) return;
        pawn.position = SurfaceTop(_currentCell) + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.12f);
    }

    void Update()
    {
        UpdateToast();      // fades independently of build/move state
        UpdateTrayAnim();   // bars keep easing open/closed even mid-transition out of build mode
        UpdatePawnBob();    // same idle up/down float as the level markers
        UpdateWind();       // fades the start→level wind ribbons in/out (drift is the shader's job)
        UpdateFovZoom();    // eases toward _fovTarget every frame — runs regardless of build mode so a
                            // zoom already in flight doesn't freeze mid-ease the instant F is pressed
        PulseRewardSuggestBox();    // runs whether or not build mode is actually open yet (the box can
                                     // show before F is pressed, while the ls.openbuild gate is waiting)
        HandleFocusViewportDrag();   // middle-mouse drag — no conflict with build mode, so it runs unconditionally
        if (SettingsScreen.Open || _decorCutscenePlaying) return;   // no clicking/walking/building while the camera's locked on the grow-in reveal

        if (_buildMode) { UpdateBuildMode(); return; }   // scroll is reserved for HandleGhostScroll in there

        HandleCameraZoomScroll();

        if (!_moving && Input.GetKeyDown(buildModeKey)) EnterBuildMode();
        // NOT gated on !_moving — clicking mid-walk re-targets the pawn (see HandleClick).
        if ((Input.GetMouseButtonDown(0) || VirtualCursor.ConfirmPressedThisFrame)
            && !PointerOverInfoPanel() && !AxisGizmo.PointerOver)
            HandleClick();
    }

    // Without this, a click on the open LevelInfoPanel also raycasts into the map
    // underneath. Scoped to the panel's own rect rather than a blanket
    // EventSystem.IsPointerOverGameObject() check, which false-positived against
    // DialogueRunner's full-screen CanvasGroup and broke the tutorial's click-to-walk.
    bool PointerOverInfoPanel()
    {
        if (infoPanel == null || !infoPanel.IsShown) return false;
        var rt = infoPanel.PanelRect;
        if (rt == null) return false;
        var canvas = rt.GetComponentInParent<Canvas>();
        var cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null;
        return RectTransformUtility.RectangleContainsScreenPoint(rt, VirtualCursor.Position, cam);
    }

    // Scroll nudges a target FOV; UpdateFovZoom eases toward it each frame (setting
    // fieldOfView directly off the raw scroll delta stepped visibly instead of
    // gliding). -1 = not seeded yet — picks up whatever FOV the camera shipped with.
    float _fovTarget = -1f;

    void UpdateFovZoom()
    {
        if (_cam == null) return;
        if (_fovTarget < 0f) _fovTarget = _cam.fieldOfView;
        float k = 1f - Mathf.Exp(-fovSmoothSpeed * Time.deltaTime);
        _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, _fovTarget, k);
    }

    void HandleCameraZoomScroll()
    {
        if (!scrollZoomsFov || _cam == null) return;
        float s = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(s) < 0.001f) return;
        if (_fovTarget < 0f) _fovTarget = _cam.fieldOfView;
        _fovTarget = Mathf.Clamp(_fovTarget - s * fovScrollSpeed, minFov, maxFov);
    }

    // Middle-mouse drag nudges focusViewportX/Y directly (not through OrbitCamera's
    // own pan, which moves the world position — this instead moves WHERE ON SCREEN
    // the already-framed focus point sits, the same knob a click's camera-focus
    // uses). Writes straight to _orbit.focusViewport so the shift is visible while
    // still dragging, not just on the next click. No auto-reset: the composition
    // change sticks, matching how a click never restores the authored default
    // either once you've dragged away from it.
    void HandleFocusViewportDrag()
    {
        if (!middleDragAdjustsFocus || _orbit == null || _cam == null) return;
        if (!Input.GetMouseButton(2)) return;

        float dx = Input.GetAxis("Mouse X");
        float dy = Input.GetAxis("Mouse Y");
        if (Mathf.Abs(dx) < 0.0001f && Mathf.Abs(dy) < 0.0001f) return;

        focusViewportX = Mathf.Clamp01(focusViewportX + dx * focusDragSpeed * Time.unscaledDeltaTime);
        focusViewportY = Mathf.Clamp01(focusViewportY + dy * focusDragSpeed * Time.unscaledDeltaTime);
        _orbit.focusViewport = new Vector2(focusViewportX, focusViewportY);
    }

    void HandleClick()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        if (!Physics.Raycast(_cam.ScreenPointToRay(VirtualCursor.Position), out var hit)) return;

        var node = hit.collider.GetComponentInParent<LevelNode>();
        if (node != null) OpenPanel(node);   // show level info right away

        // Which cell did we click? Map the cube back to grid, climb to the top of
        // its column, then walk the whole surface to it.
        if (gridSystem == null || _surface.Count == 0) return;
        Vector3Int cell = TopOfColumn(gridSystem.WorldToGrid(hit.collider.transform.position));
        if (!_surface.Contains(cell) || cell == _currentCell) return;

        // _currentCell tracks the pawn live during a walk (see WalkCells), so this
        // re-plans from wherever it actually is.
        var cellPath = SurfaceBfs(_currentCell, new HashSet<Vector3Int> { cell }, _surface);
        if (cellPath != null)
        {
            // Cancel the old walk only once the new destination is confirmed reachable.
            if (_walkRoutine != null) { StopCoroutine(_walkRoutine); _walkRoutine = null; _moving = false; HideTrail(); }

            if (cameraFocus) FocusCameraOn(SurfaceTop(cell));   // frame the destination cell
            var ptCell = new List<int>();
            var pts = BuildWorldPath(cellPath, ptCell);
            ShowTrail(pts);
            _walkRoutine = StartCoroutine(WalkCells(cellPath, pts, ptCell));
        }
        else if (node != null && node.level != null)
        {
            // A real level the pawn can't surface-walk to yet — the bridge to it
            // hasn't been built. Tell the player how to get there.
            ShowCantReach();
        }
    }

    // ── Toast ─────────────────────────────────────────────────────────────────
    void ShowCantReach() =>
        ShowToast($"Can't reach there yet — press {buildModeKey} to build a path with blocks earned from clearing levels.");

    void ShowCantPickUp() =>
        ShowToast("You're standing on that block — move off it before picking it up.");

    void ShowToast(string message)
    {
        BuildToastIfNeeded();
        _toastText.text = message;
        _toastHideAt = Time.unscaledTime + 3f;
        _toastCanvas.enabled = true;
        _toastGroup.alpha = 1f;
    }

    void UpdateToast()
    {
        if (_toastCanvas == null || !_toastCanvas.enabled) return;
        float left = _toastHideAt - Time.unscaledTime;
        if (left <= 0f) { _toastGroup.alpha = 0f; _toastCanvas.enabled = false; return; }
        _toastGroup.alpha = Mathf.Clamp01(left / 0.5f);   // hold, then fade over the last 0.5s
    }

    void BuildToastIfNeeded()
    {
        if (_toastCanvas != null) return;

        var go = new GameObject("MapToastCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _toastCanvas = go.GetComponent<Canvas>();
        _toastCanvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        _toastCanvas.sortingOrder = 80;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        BlockInfoPanel.EnsureEventSystem();

        var panel = NewRect("Toast", go.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.anchoredPosition = new Vector2(0f, 150f);
        panel.sizeDelta = new Vector2(820f, 90f);
        _toastGroup = panel.gameObject.AddComponent<CanvasGroup>();
        _toastGroup.interactable = _toastGroup.blocksRaycasts = false;
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.9f);
        bg.raycastTarget = false;

        _toastText = NewFillText("Label", panel, 26f, new Color(1f, 0.82f, 0.4f), TextAlignmentOptions.Center);
        _toastText.fontStyle = FontStyles.Bold;
        _toastText.textWrappingMode = TextWrappingModes.Normal;
        _toastText.raycastTarget = false;

        _toastCanvas.enabled = false;
    }

    // ── Surface (cell-level) ───────────────────────────────────────────────────
    // Build the global walkable surface: every top-exposed cell of every block,
    // plus a cell→node lookup so arriving on a level block shows its panel.
    void BuildSurface()
    {
        _allCells.Clear(); _surface.Clear(); _cellToNode.Clear(); _columnTop.Clear();

        foreach (var n in _nodes)
            if (n.cells != null)
                foreach (var c in n.cells) { _allCells.Add(c); _cellToNode[c] = n; }

        foreach (var c in _allCells)
            if (!_allCells.Contains(c + Vector3Int.up))
            {
                _surface.Add(c);
                var col = new Vector2Int(c.x, c.z);
                if (!_columnTop.TryGetValue(col, out var ex) || c.y > ex.y) _columnTop[col] = c;
            }
    }

    // Recompute "is there a road from START to here" for every node, then re-tint.
    // ALWAYS call this instead of looping n.Refresh() by hand — Refresh() reads
    // connectedToStart, so tinting without marking first shows stale state.
    void RefreshNodes()
    {
        MarkConnectivity();
        foreach (var n in _nodes) if (n != null) n.Refresh();
        RebuildWindLinks();   // the lit-up roads follow whatever connectivity just found
    }

    // Flood the walkable surface out from the START node and flag which nodes it
    // touches. This is the anti-skip rule: a level activates only once it's joined
    // to the start by real, PLACED geometry. Ferrying one reward block forward
    // doesn't help — the moment you pick the bridge back up the far level goes dark
    // again, so reaching two levels genuinely costs two blocks.
    void MarkConnectivity()
    {
        _reachedFrom.Clear();
        _startNode = _nodes.Find(n => n != null && n.isStart);

        if (_startNode != null)
        {
            // Seed from every top-exposed cell of the start block, not just its
            // highest — a wide start block can be walked onto from any of its tops.
            // Seeds map to themselves in _reachedFrom, which terminates the walk-back.
            var q = new Queue<Vector3Int>();
            if (_startNode.cells != null)
                foreach (var c in _startNode.cells)
                    if (_surface.Contains(c) && _reachedFrom.TryAdd(c, c)) q.Enqueue(c);

            Vector2Int[] horiz = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };
            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                foreach (var h in horiz)
                {
                    var col = new Vector2Int(cur.x + h.x, cur.z + h.y);
                    if (!_columnTop.TryGetValue(col, out var nc)) continue;
                    if (!ClimbClear(cur, nc)) continue;   // same rule as the pawn walk, so reachability matches
                    if (_reachedFrom.TryAdd(nc, cur)) q.Enqueue(nc);   // remember who lit it
                }
            }
        }

        foreach (var n in _nodes)
        {
            if (n == null) continue;
            bool ok = _startNode == null || n == _startNode;   // no start authored → don't gate anything
            if (!ok && n.cells != null)
                foreach (var c in n.cells)
                    if (_reachedFrom.ContainsKey(c)) { ok = true; break; }
            n.connectedToStart = ok;
        }
    }

    // Highest top-exposed cell of a node (where the pawn rests on it).
    Vector3Int TopCellOf(LevelNode n)
    {
        Vector3Int best = (n != null && n.cells != null && n.cells.Length > 0) ? n.cells[0] : default;
        bool found = false;
        if (n != null && n.cells != null)
            foreach (var c in n.cells)
                if (_surface.Contains(c) && (!found || c.y > best.y)) { best = c; found = true; }
        return best;
    }

    // Climb to the top-exposed cell of the clicked cell's column.
    Vector3Int TopOfColumn(Vector3Int c)
    {
        while (_allCells.Contains(c + Vector3Int.up)) c += Vector3Int.up;
        return c;
    }

    // Build world waypoints for a surface-cell path; when two consecutive cells differ
    // in height, hug the shared edge: go to the boundary, climb up/down the wall, then
    // onto the next top — so the pawn crawls over edges instead of cutting through air.
    // Shared by the trail preview (ShowTrail) and the pawn's own walk (WalkCells) so
    // they always trace the exact same line.
    // `ptCell` (optional) maps each emitted point back to its cell's index in
    // `cells`, so WalkCells can keep _currentCell accurate mid-walk.
    List<Vector3> BuildWorldPath(List<Vector3Int> cells, List<int> ptCell = null)
    {
        var pts = new List<Vector3> { SurfaceTop(cells[0]) };
        ptCell?.Add(0);
        for (int i = 1; i < cells.Count; i++)
        {
            Vector3 prevTop = SurfaceTop(cells[i - 1]);
            Vector3 curTop  = SurfaceTop(cells[i]);
            if (cells[i].y != cells[i - 1].y)
            {
                Vector3 edge = (gridSystem.GridToWorld(cells[i - 1]) + gridSystem.GridToWorld(cells[i])) * 0.5f;
                pts.Add(new Vector3(edge.x, prevTop.y, edge.z));   // out to the wall at the current height
                ptCell?.Add(i - 1);
                pts.Add(new Vector3(edge.x, curTop.y,  edge.z));   // climb up / down the wall
                ptCell?.Add(i - 1);
            }
            pts.Add(curTop);
            ptCell?.Add(i);
        }
        return pts;
    }

    // Walk the pawn across the given surface cells at constant speed — no easing,
    // no pause; leftover movement carries across cells so corners don't slow it.
    IEnumerator WalkCells(List<Vector3Int> cells, List<Vector3> pts, List<int> ptCell)
    {
        _moving = true;

        if (pawn != null)
        {
            int idx = 0;
            while (idx < pts.Count)
            {
                float step = pawnSpeed * Time.deltaTime;
                while (step > 0f && idx < pts.Count)
                {
                    Vector3 target = pts[idx];
                    float d = Vector3.Distance(pawn.position, target);
                    if (d <= step)
                    {
                        pawn.position = target; step -= d;
                        if (ptCell != null && idx < ptCell.Count) _currentCell = cells[ptCell[idx]];   // keep live for HandleClick re-plans
                        idx++;
                    }
                    else { pawn.position = Vector3.MoveTowards(pawn.position, target, step); break; }
                }
                UpdateTrail(pawn.position, idx, pts);   // erase the segment already walked
                yield return null;
            }
        }

        HideTrail();
        _currentCell = cells[cells.Count - 1];
        _moving      = false;
        _walkRoutine = null;
        // Every arrival counts as "the player walked somewhere" for the tutorial
        // gate — CompleteGate no-ops unless this exact id is what's pending, so
        // it's safe to fire unconditionally rather than only when a tutorial is
        // suspected to be running.
        DialogueRunner.Instance?.CompleteGate(TutorialGateIds.Walk);

        // Arrived: if this cell belongs to a level block, surface its panel.
        if (_cellToNode.TryGetValue(_currentCell, out var n))
        {
            _current = n;
            OpenPanel(n);
        }
    }

    // ── Path trail (LineRenderer) ────────────────────────────────────────────────
    // Draws the FULL path the moment a destination is clicked, then each frame the
    // walking coroutine redraws it from the pawn's current position onward — so the
    // segment already walked visibly erases itself as the pawn crosses it.

    void ShowTrail(List<Vector3> pts)
    {
        if (pts == null || pts.Count < 2) return;
        EnsureTrail();
        _trailLr.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++) _trailLr.SetPosition(i, pts[i]);
        _trailLr.enabled = true;
    }

    void UpdateTrail(Vector3 pawnPos, int idx, List<Vector3> pts)
    {
        if (_trailLr == null || !_trailLr.enabled) return;
        int remaining = Mathf.Max(0, pts.Count - idx);
        _trailLr.positionCount = remaining + 1;
        _trailLr.SetPosition(0, pawnPos);
        for (int i = 0; i < remaining; i++) _trailLr.SetPosition(i + 1, pts[idx + i]);
    }

    void HideTrail()
    {
        if (_trailLr != null) _trailLr.enabled = false;
    }

    void EnsureTrail()
    {
        if (_trailLr != null) return;
        var go = new GameObject("PawnTrail");
        go.transform.SetParent(transform, false);
        _trailLr = go.AddComponent<LineRenderer>();
        var baseMat = trailMaterial != null ? trailMaterial : GetTrailFallbackMaterial();
        if (baseMat == null) return;
        var mat = new Material(baseMat);
        mat.color = trailColor;   // Sprites/Default (fallback) and most unlit line shaders expose _Color via .color
        _trailLr.material          = mat;
        _trailLr.useWorldSpace     = true;
        _trailLr.positionCount     = 0;
        _trailLr.startWidth        = trailWidth;
        _trailLr.endWidth          = trailWidth;
        _trailLr.numCapVertices    = 6;
        _trailLr.numCornerVertices = 6;
        _trailLr.textureMode       = LineTextureMode.Tile;
    }

    // Lazy-built fallback so the trail draws even if `trailMaterial` isn't wired up in
    // the Inspector — same "runtime fallback" convention as EnemyBaseManager's outline
    // material. Assign trailMaterial (e.g. PathFlowManager's laser material) for a
    // nicer look; this just guarantees SOMETHING renders out of the box.
    static Material _trailFallbackMat;
    static Material GetTrailFallbackMaterial()
    {
        if (_trailFallbackMat != null) return _trailFallbackMat;
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        if (sh == null)
        {
            Debug.LogWarning("[LevelMap] No trailMaterial assigned and no fallback shader found — path trail won't draw.");
            return null;
        }
        _trailFallbackMat = new Material(sh) { name = "PawnTrail_Fallback" };
        return _trailFallbackMat;
    }

    Vector3 SurfaceTop(Vector3Int c)
        => gridSystem.GridToWorld(c) + Vector3.up * (gridSystem.cellSize * 0.5f + pawnSurfaceLift);

    // Actual top face, no pawnSurfaceLift — for anything that sits ON the block
    // (flowers, decoration) rather than floating where the pawn walks.
    Vector3 BlockTop(Vector3Int c)
        => gridSystem.GridToWorld(c) + Vector3.up * (gridSystem.cellSize * 0.5f);

    // A step between adjacent columns' tops is only walkable if the wall being
    // climbed is SOLID between the two heights — no empty cell in the middle.
    // Height itself is unlimited (you can scale a tall contiguous stack); it's the
    // gap that's forbidden. Ascending checks the taller TARGET column, descending
    // checks the taller SOURCE column.
    bool ClimbClear(Vector3Int from, Vector3Int to)
    {
        if (to.y == from.y) return true;
        if (to.y > from.y)
        {
            for (int y = from.y + 1; y <= to.y; y++)
                if (!_allCells.Contains(new Vector3Int(to.x, y, to.z))) return false;
        }
        else
        {
            for (int y = to.y + 1; y <= from.y; y++)
                if (!_allCells.Contains(new Vector3Int(from.x, y, from.z))) return false;
        }
        return true;
    }

    // BFS across the surface: from a top cell, step to each 4-neighbour COLUMN's
    // top-exposed cell — climbing the shared edge, provided ClimbClear allows it.
    List<Vector3Int> SurfaceBfs(Vector3Int start, HashSet<Vector3Int> goals, HashSet<Vector3Int> surface)
    {
        if (!surface.Contains(start)) return null;

        var prev = new Dictionary<Vector3Int, Vector3Int>();
        var seen = new HashSet<Vector3Int> { start };
        var q    = new Queue<Vector3Int>();
        q.Enqueue(start);

        Vector3Int end = start;
        bool reached = goals.Contains(start);

        Vector2Int[] horiz = { new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

        while (!reached && q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var h in horiz)
            {
                var col = new Vector2Int(cur.x + h.x, cur.z + h.y);
                if (!_columnTop.TryGetValue(col, out var nc)) continue;
                if (!ClimbClear(cur, nc)) continue;   // no empty cell in the wall being climbed
                if (!seen.Contains(nc))
                {
                    seen.Add(nc);
                    prev[nc] = cur;
                    if (goals.Contains(nc)) { end = nc; reached = true; }
                    q.Enqueue(nc);
                }
                if (reached) break;
            }
        }

        if (!reached) return null;

        var path = new List<Vector3Int>();
        var node = end;
        path.Add(node);
        while (node != start) { node = prev[node]; path.Add(node); }
        path.Reverse();
        return path;
    }

    void OpenPanel(LevelNode node)
    {
        _selected = (node != null && node.level != null) ? node : null;
        if (infoPanel == null) return;

        if (_selected == null) { infoPanel.Hide(); return; }

        var lv  = _selected.level;
        var rec = SaveSystem.Profile.GetRecord(lv.levelId);
        string title  = string.IsNullOrEmpty(lv.displayName) ? lv.levelId : lv.displayName;
        // "No route" is its own message: the player hasn't failed a requirement,
        // they just haven't built the road there yet — pointing them at the F panel
        // is far more useful than a flat "Locked".
        string status = _selected.Unreachable
            ? "No route — build a path from the start"
            : _selected.NodeState switch
              {
                  LevelNode.State.Locked  => "Locked",
                  LevelNode.State.Cleared => "Cleared",
                  _                       => "Unlocked",
              };
        string best   = (rec != null && rec.bestWave > 0) ? $"Best wave: {rec.bestWave}" : null;
        bool   canEnter = _selected.NodeState != LevelNode.State.Locked;
        infoPanel.Show(title, lv.description, status, best, canEnter, () => EnterLevel(lv), lv);
    }

    // ── Build mode ────────────────────────────────────────────────────────────
    // Freeform placement (like real level building): the player earns real
    // BlockData pieces from level clears (LevelDefinition.mapBlockRewards) and
    // places them on THIS map to extend the walkable network toward locked
    // levels — the overworld equivalent of gameplay's grid placement, just
    // simplified (no shop, no cost, no synergy; just "does it fit and connect").

    public void ToggleBuildMode()
    {
        if (_buildMode) ExitBuildMode(); else EnterBuildMode();
    }

    void EnterBuildMode()
    {
        if (_moving) return;   // don't interrupt a walk
        if (!CanOpenBuildPanel()) { ShowToast("Finish the current tutorial step first."); return; }
        _buildMode = true;
        BuildTrayUIIfNeeded();
        RefreshTray();
        _trayTargetScale = 1f;   // bars ease open — see UpdateTrayAnim
        DialogueRunner.Instance?.CompleteGate(TutorialGateIds.OpenBuild);
    }

    void ExitBuildMode()
    {
        _buildMode = false;
        CancelGhostHold();   // restores a re-picked piece to its original spot; just drops a fresh tray pick
        _trayTargetScale = 0f;   // bars ease closed — UpdateTrayAnim disables the canvas once fully shut
    }

    // Eases the build-panel bars open/closed, same feel (and formula) as
    // ShopController.AnimateRift's letterbox: exponential approach to the target,
    // bar height = TrayBarHeight × scale. Runs every frame regardless of
    // _buildMode so closing finishes its animation even after Exit has already
    // flipped _buildMode off.
    void UpdateTrayAnim()
    {
        if (_trayCanvas == null) return;

        float t = 1f - Mathf.Exp(-trayExpandSpeed * Time.deltaTime);
        _trayScale = Mathf.Lerp(_trayScale, _trayTargetScale, t);

        float h = TrayBarHeight * _trayScale;
        bool show = h > 0.5f;
        _trayCanvas.enabled = show;
        if (!show) return;

        _trayTop.sizeDelta    = new Vector2(0f, h);
        _trayBottom.sizeDelta = new Vector2(0f, h);
        FitTrayList();
    }

    // Shrink the whole strip uniformly if the entries don't fit the window's width.
    // Scaling the container beats resizing each entry: the thumbnails keep their
    // aspect (so nothing stretches, the bug we already fixed once in the shop) and
    // the layout group's spacing shrinks in proportion. Never scales ABOVE 1 — a
    // wide window gets a centred strip at natural size, not a blown-up one.
    void FitTrayList()
    {
        if (_trayList == null) return;
        int n = _trayList.childCount;
        if (n == 0) { _trayList.localScale = Vector3.one; return; }

        float needed    = n * TrayEntrySize + (n - 1) * TrayEntrySpacing;
        float available = _trayList.rect.width;
        if (available <= 1f) return;   // layout hasn't resolved yet this frame

        _trayList.localScale = Vector3.one * Mathf.Min(1f, available / needed);
    }

    void UpdateBuildMode()
    {
        if (Input.GetKeyDown(buildModeKey)) { ExitBuildMode(); return; }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_ghostBlock != null) CancelGhostHold();   // step 1: drop / restore the held block
            else ExitBuildMode();                          // step 2: leave build mode
            return;
        }

        if (_cam == null) _cam = Camera.main;

        if (_ghostBlock == null)
        {
            // Nothing held — a click tries to pick an EXISTING player-built piece
            // back up for re-editing (gameplay's PickUpSelected). Picking a NEW
            // block is the tray buttons' job (SpawnTrayEntry), not this click.
            if (Input.GetMouseButtonDown(0)) TryPickUpExisting();
            return;
        }

        // 1/2/3 = world X/Y/Z, same keys as gameplay's HandleRotate — full 3-axis,
        // since a shape like "corner" has a vertical arm a Y-only spin could never
        // reach. Only the TARGET snaps on keypress; the actual preview/placement
        // rotation eases toward it every frame below, so the ghost visibly flips
        // through intermediate orientations exactly like gameplay's block editing.
        if (Input.GetKeyDown(KeyCode.Alpha1)) _ghostTargetRotation = Quaternion.Euler(90, 0, 0) * _ghostTargetRotation;
        if (Input.GetKeyDown(KeyCode.Alpha2)) _ghostTargetRotation = Quaternion.Euler(0, 90, 0) * _ghostTargetRotation;
        if (Input.GetKeyDown(KeyCode.Alpha3)) _ghostTargetRotation = Quaternion.Euler(0, 0, 90) * _ghostTargetRotation;

        _ghostCurrentRotation = Quaternion.Slerp(_ghostCurrentRotation, _ghostTargetRotation,
                                                 1f - Mathf.Exp(-rotateSpeed * Time.deltaTime));

        HandleGhostKeyboardOffset();   // WASDQE nudge, same convention as gameplay's HandleKeyboardOffset
        HandleGhostScroll();           // wheel = push the held block forward / back, like gameplay's edit-mode scroll

        TrackGhostOrigin();

        UpdateGhostPreview();   // every frame (not just on change) so the rotation ease actually animates

        if (Input.GetMouseButtonDown(0))
        {
            if (_placementValid) CommitPlacement();
            // Explain the ONE refusal the player can't reason about from the ghost
            // alone — "it's red but the space is clearly empty".
            else if (_ghostCells != null && RewardPlacementBlocked(_ghostCells))
                ShowToast("Line the block up with the highlighted area to continue.");
        }
    }

    // Tracks the held block's target cell continuously from hit.point (not the
    // block's pivot, which teleported between cube centres). Off the geometry,
    // falls back to a build plane at the last surface height crossed (mirrors
    // gameplay's HandleMouseMove) so the ghost can glide past the built edge.
    void TrackGhostOrigin()
    {
        if (gridSystem == null || _cam == null) return;
        float cs = gridSystem.cellSize;
        Ray ray = _cam.ScreenPointToRay(VirtualCursor.Position);

        Vector3Int hoverColumn;
        if (Physics.Raycast(ray, out var hit))
        {
            // Nudge slightly INTO the surface so a hit right on a face boundary
            // resolves to the block, not the empty cell beyond it.
            hoverColumn = TopOfColumn(gridSystem.WorldToGrid(hit.point - ray.direction * (cs * 0.05f)));
            _ghostPlaneY = hoverColumn.y + 1;   // remember this layer for empty-space gliding
        }
        else
        {
            // Empty space: intersect the ray with the build plane at the remembered
            // layer's centre. Its own cell IS the placement layer (no +up).
            float planeY = _ghostPlaneY * cs + cs * 0.5f;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return;   // ray parallel to plane — keep last origin
            float t = (planeY - ray.origin.y) / ray.direction.y;
            if (t <= 0f) return;                              // plane is behind the camera — keep last origin
            var cell = gridSystem.WorldToGrid(ray.origin + ray.direction * t);
            hoverColumn = new Vector3Int(cell.x, _ghostPlaneY - 1, cell.z);   // -1 so the +up below lands on _ghostPlaneY
        }

        _ghostOrigin = hoverColumn + Vector3Int.up + _ghostManualOffset;
        // Never let a placement stack directly on the column the pawn is standing
        // on right now — it would bury/trap the pawn under the new piece.
        _ghostHoveringPawnColumn = hoverColumn.x == _currentCell.x && hoverColumn.z == _currentCell.z;
    }

    // Same WASDQE convention as gameplay's HandleKeyboardOffset: A/D shift relative
    // to camera-right, W/S shift relative to camera-forward, Q/E shift world up/down.
    // Nudges accumulate into _ghostManualOffset, layered on top of wherever the
    // mouse is hovering (see UpdateBuildMode) — persists across mouse movement,
    // reset to zero only when a fresh hold begins (tray pick or re-pickup).
    void HandleGhostKeyboardOffset()
    {
        if (_cam == null) return;
        Vector3Int right   = SnapToHorizontalAxis(_cam.transform.right);
        Vector3Int forward = SnapToHorizontalAxis(_cam.transform.forward);

        if (Input.GetKeyDown(KeyCode.A)) _ghostManualOffset -= right;
        if (Input.GetKeyDown(KeyCode.D)) _ghostManualOffset += right;
        if (Input.GetKeyDown(KeyCode.W)) _ghostManualOffset += forward;
        if (Input.GetKeyDown(KeyCode.S)) _ghostManualOffset -= forward;
        if (Input.GetKeyDown(KeyCode.Q)) _ghostManualOffset += Vector3Int.up;
        if (Input.GetKeyDown(KeyCode.E)) _ghostManualOffset += Vector3Int.down;
    }

    // Mouse wheel pushes the held block away from / toward the camera, one cell per
    // notch — the map's equivalent of gameplay's edit-mode scroll (which walks the
    // block along the build plane instead of zooming). Nothing else on this map
    // binds the wheel, so there's no conflict with camera zoom here.
    void HandleGhostScroll()
    {
        if (_cam == null) return;
        float s = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(s) < 0.001f) return;

        Vector3Int forward = SnapToHorizontalAxis(_cam.transform.forward);
        _ghostManualOffset += s > 0f ? forward : -forward;
    }

    static Vector3Int SnapToHorizontalAxis(Vector3 dir)
    {
        dir.y = 0;
        dir = dir.normalized;
        if (Mathf.Abs(dir.x) >= Mathf.Abs(dir.z))
            return dir.x >= 0 ? Vector3Int.right : Vector3Int.left;
        else
            return dir.z >= 0 ? new Vector3Int(0, 0, 1) : new Vector3Int(0, 0, -1);
    }

    // Click an existing player-built piece (LevelNode.sourceBlock != null — never
    // the authored map's own level/decoration blocks) to lift it back into a held
    // ghost, exactly like gameplay's PickUpSelected: it disappears from the board,
    // and you're holding it again at its original shape/rotation, free to move,
    // rotate, or re-place it. Refused if the pawn is currently standing on it.
    void TryPickUpExisting()
    {
        if (_cam == null) return;
        if (!Physics.Raycast(_cam.ScreenPointToRay(VirtualCursor.Position), out var hit)) return;

        var node = hit.collider.GetComponentInParent<LevelNode>();
        if (node == null || node.sourceBlock == null) return;   // only re-buildable player pieces — not authored map/level blocks

        if (node.cells != null)
            foreach (var c in node.cells)
                if (c == _currentCell) { ShowCantPickUp(); return; }

        // No connectivity restriction on pick-up. MarkConnectivity is the anti-skip
        // rule now — a level only stays lit while a road actually joins it to START —
        // so ferrying a block forward gains nothing and there's no reason to police
        // WHAT you pick up. Worst case the pawn parks itself on an island, which it
        // can always undo by re-placing the piece it's holding.
        var block    = node.sourceBlock;
        var rotation = node.builtRotation;
        var origCells = node.cells;

        _nodes.Remove(node);
        Destroy(node.gameObject);
        LinkAllNodes();
        BuildSurface();
        RefreshNodes();

        _ghostBlock            = block;
        _ghostTargetRotation   = rotation;
        _ghostCurrentRotation  = rotation;   // snap — no need to animate INTO its own current orientation
        _ghostManualOffset     = Vector3Int.zero;
        _pickedOrigCells       = origCells;
        _pickedOrigRotation    = rotation;
        _ghostFromInventory    = false;      // already paid for on its first placement
        _ghostAnchorSnap       = true;       // appear at the cursor, don't fly in from the last hold's spot
        _trayTargetScale       = 0f;         // tuck the bars away while placing
    }

    // Drops whatever's currently held. A fresh tray pick is simply discarded; a
    // RE-PICKED existing piece is restored to the exact spot/rotation it came
    // from, so Escape (and switching to a different tray block mid-hold) is a
    // clean round-trip — never a silent loss of an already-placed bridge.
    void CancelGhostHold()
    {
        if (_pickedOrigCells != null && _ghostBlock != null)
        {
            SpawnMapBlockNode(_pickedOrigCells, _ghostBlock, _pickedOrigRotation);
            LinkAllNodes();
            BuildSurface();
            RefreshNodes();
        }
        else if (_ghostFromInventory && _ghostBlock != null)
        {
            // Fresh tray grab that never landed — put it back in stock.
            SaveSystem.Profile.GrantMapBlock(_ghostBlock.name, 1);
            SaveSystem.Save();
        }

        ClearGhostCubes();
        _ghostBlock         = null;
        _pickedOrigCells    = null;
        _ghostFromInventory = false;

        if (_buildMode) { RefreshTray(); _trayTargetScale = 1f; }   // hand empty again — bring the panel back
    }

    // Every frame while a ghost is held: recompute cells from the CURRENT (eased)
    // rotation and reposition a pooled set of preview cubes — same technique as
    // gameplay's UpdatePreview (reuse/resize the pool, don't destroy-and-recreate
    // every frame), which is what lets the rotation animate smoothly instead of
    // popping.
    void UpdateGhostPreview()
    {
        if (_ghostBlock == null || _ghostBlock.cells == null || gridSystem == null || cubePrefab == null) return;

        var rotated = RotateCells(_ghostBlock.cells, _ghostCurrentRotation);
        if (_ghostCells == null || _ghostCells.Length != rotated.Length) _ghostCells = new Vector3Int[rotated.Length];

        bool valid = !_ghostHoveringPawnColumn;
        for (int i = 0; i < rotated.Length; i++)
        {
            var c = _ghostOrigin + rotated[i];
            _ghostCells[i] = c;
            if (_allCells.Contains(c) || c == _currentCell) valid = false;
        }
        // The reward tutorial's placement step additionally demands an exact match
        // with the hint, so the ghost reads red anywhere else and the commit click
        // below simply can't land.
        if (valid && RewardPlacementBlocked(_ghostCells)) valid = false;
        _placementValid = valid;

        if (_ghostRoot == null) { _ghostRoot = new GameObject("BuildGhost").transform; _ghostRoot.SetParent(transform, false); }
        _ghostRoot.gameObject.SetActive(true);

        while (_ghostGOs.Count < _ghostCells.Length)
        {
            var cube = Instantiate(cubePrefab, _ghostRoot);
            foreach (var col in cube.GetComponentsInChildren<Collider>()) col.enabled = false;   // ghost never blocks raycasts
            _ghostGOs.Add(cube);
        }

        // Cells stay snapped (for validity + CommitPlacement); the cubes are drawn
        // offset from an eased anchor so a one-cell move slides instead of popping.
        Vector3 targetAnchor = gridSystem.GridToWorld(_ghostOrigin);
        if (!GameSettings.SmoothBlockEditing || _ghostAnchorSnap) { _ghostVisualAnchor = targetAnchor; _ghostAnchorSnap = false; }
        else _ghostVisualAnchor = Vector3.Lerp(_ghostVisualAnchor, targetAnchor,
                                               1f - Mathf.Exp(-ghostFollowSpeed * Time.deltaTime));

        Color tint = valid ? ghostValidColor : ghostInvalidColor;
        for (int i = 0; i < _ghostGOs.Count; i++)
        {
            bool active = i < _ghostCells.Length;
            _ghostGOs[i].SetActive(active);
            if (!active) continue;
            _ghostGOs[i].transform.position =
                _ghostVisualAnchor + (gridSystem.GridToWorld(_ghostCells[i]) - targetAnchor);
            var rends = _ghostGOs[i].GetComponentsInChildren<Renderer>();
            for (int r = 0; r < rends.Length; r++) MpbColor.Set(rends[r], tint);
        }
    }

    void ClearGhostCubes()
    {
        if (_ghostRoot != null) _ghostRoot.gameObject.SetActive(false);
        _ghostCells = null;
        _placementValid = false;
    }

    void CommitPlacement()
    {
        if (!_placementValid || _ghostBlock == null || _ghostCells == null) return;

        // Nothing to spend here: a fresh tray grab was already deducted at grab time
        // (and would be refunded if cancelled), and a re-picked piece was paid for on
        // its original placement. Either way, landing it just keeps it spent.
        bool isRepick = _pickedOrigCells != null;

        var absCells = (Vector3Int[])_ghostCells.Clone();
        var rotation = _ghostCurrentRotation;
        var block    = _ghostBlock;

        SpawnMapBlockNode(absCells, block, rotation);
        LinkAllNodes();
        BuildSurface();
        RefreshNodes();

        // Repick: this piece already has an entry in the save from its ORIGINAL
        // placement — replace it rather than appending a duplicate.
        if (isRepick)
            SaveSystem.Profile.placedMapBlocks.RemoveAll(p =>
                p != null && p.blockAssetName == block.name && CellsEqual(p.cells, _pickedOrigCells));

        SaveSystem.Profile.placedMapBlocks.Add(new PlacedMapBlock
        {
            cells = absCells, blockAssetName = block.name, rotation = rotation
        });
        SaveSystem.Save();

        ClearGhostCubes();
        _ghostBlock         = null;
        _pickedOrigCells    = null;
        _ghostFromInventory = false;

        DialogueRunner.Instance?.CompleteGate(TutorialGateIds.Place);

        // Placing exits build mode outright — a placed piece is no longer something
        // a stray click can grab. The only way back into a piece is deliberately
        // pressing F again, which re-enters the tray and re-arms TryPickUpExisting.
        // (ExitBuildMode's CancelGhostHold() is a no-op here — the ghost is already
        // cleared above — so this doesn't refund or restore anything.)
        ExitBuildMode();
    }

    static bool CellsEqual(Vector3Int[] a, Vector3Int[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        var set = new HashSet<Vector3Int>(a);
        foreach (var c in b) if (!set.Contains(c)) return false;
        return true;
    }

    // 90° principal-axis rotations map integer cells bijectively, so rounding never
    // collapses two cells onto each other — same guarantee gameplay's rotation relies on.
    static Vector3Int[] RotateCells(Vector3Int[] cells, Quaternion rot)
    {
        if (cells == null) return System.Array.Empty<Vector3Int>();
        var r = new Vector3Int[cells.Length];
        for (int i = 0; i < cells.Length; i++)
            r[i] = Vector3Int.RoundToInt(rot * (Vector3)cells[i]);
        return r;
    }

    // ── Build panel (UGUI — cinematic letterbox bars, like gameplay's shop) ─────
    // Same visual language as ShopController's letterbox: a black bar top AND
    // bottom, animated open/closed (see UpdateTrayAnim). Instead of selling, the
    // bottom bar lists every reward block the player owns — shown as an actual
    // rendered miniature of the block's shape (BlockShapeThumbnail), not just a
    // name — click one, then click the map to place it. No close button: press
    // the build key again (or Esc) to leave, same as gameplay's shop.
    void BuildTrayUIIfNeeded()
    {
        if (_trayCanvas != null) return;

        var canvasGo = new GameObject("BuildTrayCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _trayCanvas = canvasGo.GetComponent<Canvas>();
        _trayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _trayCanvas.sortingOrder = 60;
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;   // match height, so the bars stay a fixed fraction of screen height
        BlockInfoPanel.EnsureEventSystem();

        var barColor = new Color(0f, 0f, 0f, 1f);    // solid black cinematic bars

        // Top bar — pure black, no content (just the letterbox framing).
        _trayTop = NewRect("TopBar", canvasGo.transform);
        _trayTop.anchorMin = new Vector2(0f, 1f); _trayTop.anchorMax = new Vector2(1f, 1f); _trayTop.pivot = new Vector2(0.5f, 1f);
        _trayTop.sizeDelta = Vector2.zero; _trayTop.anchoredPosition = Vector2.zero;   // starts closed — UpdateTrayAnim grows it
        _trayTop.gameObject.AddComponent<Image>().color = barColor;

        // Bottom bar — holds the reward-block strip.
        _trayBottom = NewRect("BottomBar", canvasGo.transform);
        _trayBottom.anchorMin = new Vector2(0f, 0f); _trayBottom.anchorMax = new Vector2(1f, 0f); _trayBottom.pivot = new Vector2(0.5f, 0f);
        _trayBottom.sizeDelta = Vector2.zero; _trayBottom.anchoredPosition = Vector2.zero;
        _trayBottom.gameObject.AddComponent<Image>().color = barColor;

        // sizeDelta.x is NEGATIVE against a full-width stretch: "parent width minus
        // 40", so the hint reflows with the window instead of clipping at 1920.
        _trayHint = NewText("Hint", _trayBottom, 22f, new Color(0.9f, 0.9f, 0.92f),
                            TextAlignmentOptions.Top, new Vector2(0f, -10f), new Vector2(-40f, 30f));
        _trayHint.rectTransform.anchorMin = new Vector2(0f, 1f);
        _trayHint.rectTransform.anchorMax = new Vector2(1f, 1f);
        _trayHint.textWrappingMode = TMPro.TextWrappingModes.Normal;

        // Stretch the strip across the bar instead of pinning it to a fixed 1500px.
        // At the 1920×1080 reference those are the same thing, which is why this only
        // showed up off-ratio: on any window narrower than 1500 reference units (a
        // 4:3 or portrait "free aspect" game view, where matching HEIGHT makes the
        // canvas' reference WIDTH shrink) the strip ran off both edges of the screen.
        _trayList = NewRect("List", _trayBottom);
        _trayList.anchorMin = new Vector2(0f, 0.5f);
        _trayList.anchorMax = new Vector2(1f, 0.5f);
        _trayList.pivot = new Vector2(0.5f, 0.5f);
        _trayList.anchoredPosition = new Vector2(0f, -10f);
        _trayList.sizeDelta = new Vector2(-TrayListMargin * 2f, TrayBarHeight - 60f);
        var hlg = _trayList.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = TrayEntrySpacing; hlg.childAlignment = TextAnchor.MiddleCenter;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        _trayCanvas.enabled = false;
    }

    void RefreshTray()
    {
        if (_trayList == null) return;
        for (int i = _trayList.childCount - 1; i >= 0; i--)
            Destroy(_trayList.GetChild(i).gameObject);

        // Merge earned inventory with (optionally) the full reward set for testing.
        // Every reward block is capped at 1 in stock (see GrantMapBlock) — a
        // single-use bridge piece, not a stockpile — so there's nothing to count.
        var owned = new HashSet<string>();
        var inv = SaveSystem.Profile.mapBlockInventory;
        if (inv != null)
            foreach (var g in inv)
                if (g != null && g.count > 0) owned.Add(g.blockAssetName);

        if (autoReward && buildableBlocks != null)
            foreach (var b in buildableBlocks)
                if (b != null) owned.Add(b.name);   // testing: grant one of every reward block

        bool any = false;
        foreach (var name in owned)
        {
            var bd = FindBuildableBlock(name);
            if (bd == null) continue;
            any = true;
            SpawnTrayEntry(bd);
        }

        _trayHint.text = _ghostBlock != null
            ? $"Placing {_ghostBlock.ShapeName} — click the map to place, 1/2/3 to rotate, Esc to cancel."
            : (any ? "Pick a reward block, or click a piece you've already placed to move it." : "No blocks earned yet — clear levels to earn map blocks.");
    }

    // Warm gold tint for reward-block thumbnails — distinct from the cool cyan
    // used for already-built path blocks (playerBuiltColor), so "what you can
    // place" and "what you've placed" read as different things.
    static readonly Color RewardBlockTint = new Color(0.95f, 0.8f, 0.35f);

    void SpawnTrayEntry(BlockData bd)
    {
        var rt = NewRect("Entry", _trayList);
        rt.sizeDelta = new Vector2(TrayEntrySize, TrayEntrySize);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.16f, 0.17f, 0.20f, 1f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() =>
        {
            CancelGhostHold();   // if something was already held (esp. a re-picked piece), restore/drop it first

            // Spend it the moment it's grabbed, so the panel visibly loses the block
            // right away instead of only after it lands. Cancelling refunds it (see
            // CancelGhostHold) — so the only way to keep it spent is to actually place it.
            if (!autoReward && !SaveSystem.Profile.ConsumeMapBlock(bd.name)) { RefreshTray(); return; }
            _ghostFromInventory = !autoReward;

            _ghostBlock = bd;
            _ghostTargetRotation = _ghostCurrentRotation = Quaternion.identity;
            _ghostManualOffset = Vector3Int.zero;
            _ghostAnchorSnap = true;   // appear at the cursor, don't glide in from wherever the last hold sat
            RefreshTray();
            _trayTargetScale = 0f;   // tuck the bars away so the map is fully visible while placing
        });

        var shapeRt = NewRect("Shape", rt);
        shapeRt.anchorMin = Vector2.zero; shapeRt.anchorMax = Vector2.one;
        shapeRt.offsetMin = new Vector2(6f, 6f); shapeRt.offsetMax = new Vector2(-6f, -6f);
        var shapeImg = shapeRt.gameObject.AddComponent<Image>();
        shapeImg.raycastTarget = false;
        float cellSize = gridSystem != null ? gridSystem.cellSize : 1f;
        BlockShapeThumbnail.Apply(shapeImg, BlockShapeThumbnail.GetOrCreate(bd, cubePrefab, RewardBlockTint, cellSize));
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    // Top-anchored, fixed-height strip (used for the hint line).
    TMP_Text NewText(string name, Transform parent, float size, Color color,
                     TextAlignmentOptions align, Vector2 anchoredPos, Vector2 sizeDelta)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(1f, 1f); rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = sizeDelta;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.alignment = align;
        t.raycastTarget = false;
        return t;
    }

    // Stretches to fill its parent's whole rect (used for button/entry labels).
    TMP_Text NewFillText(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.alignment = align;
        return t;
    }

    // Wire a UGUI "← Title" button to this.
    public void GoToTitle() => LoadScene(titleScene);

    // Last line of defence — the panel's Enter button is already disabled for a
    // locked/unreachable node, but the gate lives here too so no other caller (the
    // IMGUI fallback below, a future hotkey) can slip past it.
    void EnterLevel(LevelDefinition lv)
    {
        if (_selected != null && _selected.level == lv &&
            _selected.NodeState == LevelNode.State.Locked) return;
        if (!CanEnterLevel()) { ShowToast("Finish the current tutorial step first."); return; }
        DialogueRunner.Instance?.CompleteGate(TutorialGateIds.EnterLevel);
        RunConfig.LastLevelSelectNodeId = lv != null ? lv.levelId : null;
        RunConfig.SetLevel(lv);
        LoadScene(gameplayScene);
    }

    void LoadScene(string s)
    {
        if (!string.IsNullOrEmpty(s) && Application.CanStreamedLevelBeLoaded(s))
            LoadingScreen.Go(s);   // spinning-cube loading page, then async-load
        else
            Debug.LogWarning($"[LevelMap] scene '{s}' not in Build Settings.");
    }

    // ── Minimal IMGUI fallback (used only until the UGUI infoPanel is wired) ────
    void OnGUI()
    {
        if (infoPanel != null) return;   // UGUI panel takes over

        EnsureStyles();
        if (GUI.Button(new Rect(16f, 16f, 130f, 38f), "← Title", _btn))
            LoadScene(titleScene);

        if (_selected == null) return;
        var lv  = _selected.level;
        var rec = SaveSystem.Profile.GetRecord(lv.levelId);

        float w = 150f, h = 130f;
        GUILayout.BeginArea(new Rect(Screen.width - w - 24f, (Screen.height - h) * 0.5f, w, h),
                            GUIContent.none, GUI.skin.box);
        GUILayout.Label(string.IsNullOrEmpty(lv.displayName) ? lv.levelId : lv.displayName, _title);
        if (!string.IsNullOrEmpty(lv.description)) GUILayout.Label(lv.description, _label);

        GUILayout.Space(6f);
        GUILayout.Label(_selected.NodeState switch
        {
            LevelNode.State.Locked  => "Locked",
            LevelNode.State.Cleared => "Cleared",
            _                       => "Unlocked",
        }, _label);
        if (rec != null && rec.bestWave > 0) GUILayout.Label($"Best wave: {rec.bestWave}", _label);

        GUILayout.FlexibleSpace();
        GUI.enabled = _selected.NodeState != LevelNode.State.Locked;
        if (GUILayout.Button(GUI.enabled ? "Enter" : "Locked", _btn, GUILayout.Height(42f)))
            EnterLevel(lv);
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    void EnsureStyles()
    {
        if (_btn != null) return;
        _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
        _title.normal.textColor = Color.white;
        _label = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true };
        _label.normal.textColor = new Color(0.82f, 0.82f, 0.85f);
        _btn   = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
    }
}
