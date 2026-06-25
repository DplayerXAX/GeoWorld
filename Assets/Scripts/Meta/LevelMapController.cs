using System.Collections;
using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("UGUI level-info panel (right side). Wire it and the old IMGUI box is skipped.")]
    public LevelInfoPanel infoPanel;

    [Header("Scenes (must be in Build Settings)")]
    public string gameplayScene = "gamePlay";
    public string titleScene    = "Title";

    [Header("Movement")]
    public float pawnSpeed = 6f;
    [Tooltip("How high the pawn floats above the block TOP FACE while surface-walking.")]
    public float pawnSurfaceLift = 0.5f;

    [Header("Camera focus")]
    [Tooltip("On click, smoothly slide the camera to frame the target cell (keeps its current offset/angle).")]
    public bool  cameraFocus = true;
    public float cameraLerp  = 4f;
    [Tooltip("Where the focused cell sits horizontally on screen. 0.5 = centre, ~0.3 = left-centre (leaves room for the right info panel).")]
    [Range(0f, 1f)] public float focusViewportX = 0.3f;
    [Range(0f, 1f)] public float focusViewportY = 0.5f;

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

    void Start()
    {
        _cam = Camera.main;

        if (buildFromFile) BuildMap();
        else _nodes.AddRange(FindObjectsByType<LevelNode>(FindObjectsSortMode.None));

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

        // Auto-link face-adjacent nodes (so a placed path is walkable with no manual wiring).
        for (int i = 0; i < _nodes.Count; i++)
            for (int j = i + 1; j < _nodes.Count; j++)
                if (_nodes[i].IsAdjacentTo(_nodes[j]))
                {
                    _nodes[i].neighbors.Add(_nodes[j]);
                    _nodes[j].neighbors.Add(_nodes[i]);
                }
    }

    void Update()
    {
        if (_moving || SettingsScreen.Open) return;
        if (Input.GetMouseButtonDown(0)) HandleClick();
    }

    void HandleClick()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        if (!Physics.Raycast(_cam.ScreenPointToRay(Input.mousePosition), out var hit)) return;

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
            StartCoroutine(WalkCells(cellPath));
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

    // Walk the pawn across the given surface cells at constant speed — no easing,
    // no pause; leftover movement carries across cells so corners don't slow it.
    IEnumerator WalkCells(List<Vector3Int> cells)
    {
        _moving = true;

        // Build world waypoints; when two consecutive cells differ in height, hug
        // the shared edge: go to the boundary, climb up/down the wall, then onto
        // the next top — so the pawn crawls over edges instead of cutting through air.
        var pts = new List<Vector3>();
        pts.Add(SurfaceTop(cells[0]));
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
                yield return null;
            }
        }

        _currentCell = cells[cells.Count - 1];
        _moving      = false;

        // Arrived: if this cell belongs to a level block, surface its panel.
        if (_cellToNode.TryGetValue(_currentCell, out var n))
        {
            _current = n;
            OpenPanel(n);
        }
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
