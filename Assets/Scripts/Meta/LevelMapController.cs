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
public class LevelMapController : MonoBehaviour
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

    [Header("Build mode (overworld map extension)")]
    [Tooltip("Blocks the player can place on this map, earned via LevelDefinition.mapBlockRewards. Only blocks in THIS list are placeable, even if granted — keep it in sync with what levels can reward.")]
    public BlockData[] buildableBlocks;
    [Tooltip("Key that enters/exits build mode.")]
    public KeyCode buildModeKey = KeyCode.B;
    [Tooltip("Key that rotates the held ghost 90° around Y while placing.")]
    public KeyCode rotateGhostKey = KeyCode.R;
    public Color ghostValidColor   = new Color(0.35f, 1f, 0.45f, 0.55f);
    public Color ghostInvalidColor = new Color(1f, 0.30f, 0.30f, 0.55f);
    [Tooltip("Tint applied to a freshly player-built node so it visually reads as 'built', distinct from the authored map.")]
    public Color playerBuiltColor = new Color(0.55f, 0.85f, 0.95f, 1f);

    readonly List<LevelNode> _nodes = new();
    LevelNode _current, _selected;
    bool      _moving;
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
    BlockData    _ghostBlock;
    int          _ghostRotY;
    Vector3Int   _ghostOrigin;
    Vector3Int[] _ghostCells;
    bool         _ghostDirty = true;
    bool         _placementValid;
    readonly List<GameObject> _ghostGOs = new();
    Transform    _ghostRoot;

    Canvas        _trayCanvas;
    RectTransform _trayList;
    TMP_Text      _trayHint;

    void Start()
    {
        timeLoop.Post(this.gameObject);
        _cam = Camera.main;

        if (buildFromFile) BuildMap();
        else _nodes.AddRange(FindObjectsByType<LevelNode>(FindObjectsSortMode.None));

        RebuildPlacedMapBlocks();   // replay the player's own map-building from the save
        LinkAllNodes();             // adjacency across BOTH the authored map and player-built nodes

        // Unlock default levels, then refresh state/colour on every node.
        var defs = new List<LevelDefinition>();
        foreach (var n in _nodes) if (n.level != null) defs.Add(n.level);
        SaveSystem.EnsureDefaultsUnlocked(defs);
        foreach (var n in _nodes) n.Refresh();

        BuildSurface();

        _current = _nodes.Find(n => n.isStart) ?? (_nodes.Count > 0 ? _nodes[0] : null);
        if (_current != null)
        {
            _currentCell = TopCellOf(_current);
            if (pawn != null) pawn.position = SurfaceTop(_currentCell);
        }

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
    }

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
            ln.themeColor = node.color;
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
            SpawnMapBlockNode(p.cells, p.blockAssetName);
        }
    }

    // Instantiates a plain waypoint node (level == null — a pure connector) at the
    // given ABSOLUTE cells, exactly like BuildMap()'s per-node loop. Shared by the
    // save-replay path and the live build-mode commit.
    LevelNode SpawnMapBlockNode(Vector3Int[] absCells, string blockAssetName)
    {
        Vector3 centroid = Vector3.zero;
        foreach (var c in absCells) centroid += gridSystem.GridToWorld(c);
        centroid /= absCells.Length;

        var obj = new GameObject("PlayerBuilt_" + blockAssetName);
        obj.transform.position = centroid;

        var br = obj.AddComponent<BlockRenderer>();
        br.cubePrefab = cubePrefab;
        br.Render(Vector3Int.zero, absCells, gridSystem.cellSize, gridSystem);

        var ln = obj.AddComponent<LevelNode>();
        ln.cells      = absCells;
        ln.level      = null;
        ln.isStart    = false;
        ln.themeColor = playerBuiltColor;
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

    void Update()
    {
        if (SettingsScreen.Open) return;

        if (_buildMode) { UpdateBuildMode(); return; }

        if (!_moving && Input.GetKeyDown(buildModeKey)) EnterBuildMode();
        if (_moving) return;
        if (Input.GetMouseButtonDown(0) || VirtualCursor.ConfirmPressedThisFrame) HandleClick();
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

        var cellPath = SurfaceBfs(_currentCell, new HashSet<Vector3Int> { cell }, _surface);
        if (cellPath != null)
        {
            if (cameraFocus) FocusCameraOn(SurfaceTop(cell));   // frame the destination cell
            var pts = BuildWorldPath(cellPath);
            ShowTrail(pts);
            StartCoroutine(WalkCells(cellPath, pts));
        }
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
    List<Vector3> BuildWorldPath(List<Vector3Int> cells)
    {
        var pts = new List<Vector3> { SurfaceTop(cells[0]) };
        for (int i = 1; i < cells.Count; i++)
        {
            Vector3 prevTop = SurfaceTop(cells[i - 1]);
            Vector3 curTop  = SurfaceTop(cells[i]);
            if (cells[i].y != cells[i - 1].y)
            {
                Vector3 edge = (gridSystem.GridToWorld(cells[i - 1]) + gridSystem.GridToWorld(cells[i])) * 0.5f;
                pts.Add(new Vector3(edge.x, prevTop.y, edge.z));   // out to the wall at the current height
                pts.Add(new Vector3(edge.x, curTop.y,  edge.z));   // climb up / down the wall
            }
            pts.Add(curTop);
        }
        return pts;
    }

    // Walk the pawn across the given surface cells at constant speed — no easing,
    // no pause; leftover movement carries across cells so corners don't slow it.
    IEnumerator WalkCells(List<Vector3Int> cells, List<Vector3> pts)
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
                    if (d <= step) { pawn.position = target; step -= d; idx++; }
                    else           { pawn.position = Vector3.MoveTowards(pawn.position, target, step); break; }
                }
                UpdateTrail(pawn.position, idx, pts);   // erase the segment already walked
                yield return null;
            }
        }

        HideTrail();
        _currentCell = cells[cells.Count - 1];
        _moving      = false;

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

    // BFS across the surface: from a top cell, step to each 4-neighbour COLUMN's
    // top-exposed cell at ANY height — the pawn climbs the shared edge to get there.
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
        string status = _selected.NodeState switch
        {
            LevelNode.State.Locked  => "Locked",
            LevelNode.State.Cleared => "Cleared",
            _                       => "Unlocked",
        };
        string best   = (rec != null && rec.bestWave > 0) ? $"Best wave: {rec.bestWave}" : null;
        bool   canEnter = _selected.NodeState != LevelNode.State.Locked;
        infoPanel.Show(title, lv.description, status, best, canEnter, () => EnterLevel(lv));
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
        _buildMode = true;
        _ghostBlock = null;
        BuildTrayUIIfNeeded();
        RefreshTray();
        _trayCanvas.enabled = true;
    }

    void ExitBuildMode()
    {
        _buildMode = false;
        ClearGhostCubes();
        _ghostBlock = null;
        if (_trayCanvas != null) _trayCanvas.enabled = false;
    }

    void UpdateBuildMode()
    {
        if (Input.GetKeyDown(buildModeKey)) { ExitBuildMode(); return; }
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (_ghostBlock != null) { _ghostBlock = null; ClearGhostCubes(); }   // step 1: drop the held block
            else ExitBuildMode();                                                 // step 2: leave build mode
            return;
        }
        if (_ghostBlock == null) return;   // waiting on a tray pick

        if (Input.GetKeyDown(rotateGhostKey)) { _ghostRotY = (_ghostRotY + 1) % 4; _ghostDirty = true; }

        if (_cam == null) _cam = Camera.main;
        if (_cam == null || gridSystem == null) return;
        if (!Physics.Raycast(_cam.ScreenPointToRay(VirtualCursor.Position), out var hit)) return;

        // Candidate origin: stack the new block on top of whatever surface cell the
        // cursor is over — guarantees the new node is face-adjacent to the existing
        // network (LinkAllNodes then just works, no special-case needed).
        Vector3Int hoverCell = TopOfColumn(gridSystem.WorldToGrid(hit.collider.transform.position));
        Vector3Int candidateOrigin = hoverCell + Vector3Int.up;

        if (_ghostDirty || candidateOrigin != _ghostOrigin)
        {
            _ghostOrigin = candidateOrigin;
            _ghostDirty  = false;
            RebuildGhost();
        }

        if (Input.GetMouseButtonDown(0) && _placementValid) CommitPlacement();
    }

    void RebuildGhost()
    {
        ClearGhostCubes();
        if (_ghostBlock == null || _ghostBlock.cells == null || gridSystem == null || cubePrefab == null) return;

        var rotated = RotateCellsY(_ghostBlock.cells, _ghostRotY);
        _ghostCells = new Vector3Int[rotated.Length];
        bool valid = true;
        for (int i = 0; i < rotated.Length; i++)
        {
            var c = _ghostOrigin + rotated[i];
            _ghostCells[i] = c;
            if (_allCells.Contains(c)) valid = false;
        }
        _placementValid = valid;

        if (_ghostRoot == null) { _ghostRoot = new GameObject("BuildGhost").transform; _ghostRoot.SetParent(transform, false); }
        Color tint = valid ? ghostValidColor : ghostInvalidColor;
        for (int i = 0; i < _ghostCells.Length; i++)
        {
            var cube = Instantiate(cubePrefab, _ghostRoot);
            cube.transform.position = gridSystem.GridToWorld(_ghostCells[i]);
            var rends = cube.GetComponentsInChildren<Renderer>();
            for (int r = 0; r < rends.Length; r++) MpbColor.Set(rends[r], tint);
            foreach (var col in cube.GetComponentsInChildren<Collider>()) col.enabled = false;   // ghost never blocks raycasts
            _ghostGOs.Add(cube);
        }
    }

    void ClearGhostCubes()
    {
        for (int i = 0; i < _ghostGOs.Count; i++)
            if (_ghostGOs[i] != null) Destroy(_ghostGOs[i]);
        _ghostGOs.Clear();
        _ghostCells = null;
        _placementValid = false;
    }

    void CommitPlacement()
    {
        if (!_placementValid || _ghostBlock == null || _ghostCells == null) return;
        if (!SaveSystem.Profile.ConsumeMapBlock(_ghostBlock.name)) { RefreshTray(); return; }   // stale count — bail safely

        var absCells = _ghostCells;   // captured before ClearGhostCubes() nulls it
        SpawnMapBlockNode(absCells, _ghostBlock.name);
        LinkAllNodes();
        BuildSurface();
        foreach (var n in _nodes) n.Refresh();

        SaveSystem.Profile.placedMapBlocks.Add(new PlacedMapBlock
        {
            cells = absCells, blockAssetName = _ghostBlock.name, rotationY = _ghostRotY
        });
        SaveSystem.Save();

        ClearGhostCubes();
        _ghostBlock = null;   // back to the tray — pick again for the next placement
        RefreshTray();
    }

    static Vector3Int[] RotateCellsY(Vector3Int[] cells, int rot90)
    {
        if (cells == null) return System.Array.Empty<Vector3Int>();
        var r = new Vector3Int[cells.Length];
        var q = Quaternion.Euler(0f, 90f * rot90, 0f);
        for (int i = 0; i < cells.Length; i++)
            r[i] = Vector3Int.RoundToInt(q * (Vector3)cells[i]);
        return r;
    }

    // ── Build tray (UGUI) ────────────────────────────────────────────────────
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
        sc.matchWidthOrHeight = 0.5f;
        BlockInfoPanel.EnsureEventSystem();

        var panel = NewRect("Panel", canvasGo.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.anchoredPosition = new Vector2(0f, 20f);
        panel.sizeDelta = new Vector2(760f, 120f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.07f, 0.82f);

        _trayHint = NewText("Hint", panel, 16f, new Color(0.85f, 0.85f, 0.88f),
                            TextAlignmentOptions.Top, new Vector2(0f, -8f), new Vector2(-20f, 24f));

        _trayList = NewRect("List", panel);
        _trayList.anchorMin = new Vector2(0f, 0f);
        _trayList.anchorMax = new Vector2(1f, 1f);
        _trayList.offsetMin = new Vector2(10f, 10f);
        _trayList.offsetMax = new Vector2(-10f, -34f);
        var hlg = _trayList.gameObject.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = 8f; hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = hlg.childControlHeight = false;
        hlg.childForceExpandWidth = hlg.childForceExpandHeight = false;

        var exitBtnRt = NewRect("Exit", panel);
        exitBtnRt.anchorMin = exitBtnRt.anchorMax = exitBtnRt.pivot = new Vector2(1f, 1f);
        exitBtnRt.anchoredPosition = new Vector2(-8f, -8f);
        exitBtnRt.sizeDelta = new Vector2(70f, 26f);
        var exitImg = exitBtnRt.gameObject.AddComponent<Image>();
        exitImg.color = new Color(0.7f, 0.2f, 0.2f, 1f);
        var exitBtn = exitBtnRt.gameObject.AddComponent<Button>();
        exitBtn.targetGraphic = exitImg;
        exitBtn.onClick.AddListener(ExitBuildMode);
        var exitLabel = NewFillText("Label", exitBtnRt, 14f, Color.white, TextAlignmentOptions.Center);
        exitLabel.text = "Exit";

        _trayCanvas.enabled = false;
    }

    void RefreshTray()
    {
        if (_trayList == null) return;
        for (int i = _trayList.childCount - 1; i >= 0; i--)
            Destroy(_trayList.GetChild(i).gameObject);

        var inv = SaveSystem.Profile.mapBlockInventory;
        bool any = false;
        if (inv != null)
        {
            foreach (var g in inv)
            {
                if (g == null || g.count <= 0) continue;
                var bd = FindBuildableBlock(g.blockAssetName);
                if (bd == null) continue;
                any = true;
                SpawnTrayEntry(bd, g.count);
            }
        }

        _trayHint.text = _ghostBlock != null
            ? $"Placing {_ghostBlock.ShapeName} — click to place, {rotateGhostKey} to rotate, Esc to cancel."
            : (any ? "Pick a block, then click the map to place it." : "No blocks earned yet — clear levels to earn map blocks.");
    }

    void SpawnTrayEntry(BlockData bd, int count)
    {
        var rt = NewRect("Entry", _trayList);
        rt.sizeDelta = new Vector2(96f, 64f);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.18f, 0.19f, 0.22f, 1f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => { _ghostBlock = bd; _ghostRotY = 0; _ghostDirty = true; RefreshTray(); });

        var label = NewFillText("Label", rt, 16f, Color.white, TextAlignmentOptions.Center);
        label.text = $"{bd.ShapeName}\n×{count}";
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
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

    void EnterLevel(LevelDefinition lv) { RunConfig.SetLevel(lv); LoadScene(gameplayScene); }

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
