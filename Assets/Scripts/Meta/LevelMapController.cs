using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// Drives the level-select map. Either builds the map at runtime from a saved file
// (authored with the real placement tool — see LevelMapAuthor), or uses LevelNodes
// already placed in the scene. Then it walks a pawn node→node when you click a
// reachable block and lets you Enter unlocked levels. Reachability is a BFS over
// passable nodes, so a locked level blocks the path beyond it until it's cleared.
public class LevelMapController : MonoBehaviour
{
    [Header("Build from saved map (authored with PlacementController)")]
    [Tooltip("If on, build the map from the saved JSON at Start. Off = use LevelNodes already in the scene.")]
    public bool buildFromFile = true;
    public string mapName = "map";
    public LevelDatabase database;
    [Tooltip("Lightweight grid (for GridToWorld / cellSize) used to position rebuilt blocks.")]
    public GridSystem gridSystem;
    [Tooltip("The same cube prefab placement uses, so map blocks look identical.")]
    public GameObject cubePrefab;

    [Header("Refs")]
    public Transform pawn;

    [Header("Scenes (must be in Build Settings)")]
    public string gameplayScene = "gamePlay";
    public string titleScene    = "Title";

    [Header("Movement")]
    public float pawnSpeed = 6f;
    [Tooltip("How high the pawn floats above the block TOP FACE while surface-walking.")]
    public float pawnSurfaceLift = 0.5f;

    readonly List<LevelNode> _nodes = new();
    LevelNode _current, _selected;
    bool      _moving;
    Camera    _cam;
    GUIStyle  _title, _label, _btn;

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

        _current = _nodes.Find(n => n.isStart) ?? (_nodes.Count > 0 ? _nodes[0] : null);
        if (_current != null && pawn != null) pawn.position = SurfaceTopOf(_current);
    }

    // ── Build the map from the saved layout ──────────────────────────────────
    void BuildMap()
    {
        var data = LevelMapIO.Load(mapName);
        if (data == null || gridSystem == null || cubePrefab == null)
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
        if (_moving) return;
        if (Input.GetMouseButtonDown(0)) HandleClick();
    }

    void HandleClick()
    {
        if (_cam == null) { _cam = Camera.main; if (_cam == null) return; }
        if (!Physics.Raycast(_cam.ScreenPointToRay(Input.mousePosition), out var hit)) return;

        var node = hit.collider.GetComponentInParent<LevelNode>();
        if (node == null) return;

        OpenPanel(node);              // show its info immediately (cleared if it has no level)
        if (node == _current) return;

        var path = FindPath(_current, node);
        if (path != null) StartCoroutine(WalkTo(path, node));
    }

    // BFS over passable nodes. The destination may be locked (you can step up to
    // read it), but you can't route THROUGH a locked node to reach beyond it.
    List<LevelNode> FindPath(LevelNode from, LevelNode to)
    {
        if (from == null || to == null) return null;

        var prev = new Dictionary<LevelNode, LevelNode> { [from] = null };
        var q    = new Queue<LevelNode>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            if (cur == to) break;
            foreach (var nb in cur.neighbors)
            {
                if (nb == null || prev.ContainsKey(nb)) continue;
                if (nb != to && !nb.Passable) continue;
                prev[nb] = cur;
                q.Enqueue(nb);
            }
        }
        if (!prev.ContainsKey(to)) return null;

        var path = new List<LevelNode>();
        for (var n = to; n != null; n = prev[n]) path.Add(n);
        path.Reverse();
        return path;
    }

    IEnumerator WalkTo(List<LevelNode> route, LevelNode dest)
    {
        _moving = true;

        // Prefer a surface-hugging cell path (walk across the block tops). Fall
        // back to a straight node-centre lerp if cell data isn't available.
        var pts = BuildSurfaceWaypoints(route);
        if (pts == null || pts.Count == 0)
        {
            pts = new List<Vector3>();
            foreach (var n in route) pts.Add(n.PawnPoint);
        }

        // Constant-speed walk over every waypoint — no easing, no pause. Leftover
        // movement carries across waypoints so corners don't slow the pawn down.
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

        _current = dest;
        _moving  = false;
        OpenPanel(dest);
    }

    // ── Surface walking ───────────────────────────────────────────────────────
    // World waypoints that hug the tops of the blocks along `route`, from the
    // pawn's current spot to the target block's top. Null if it can't be built
    // (no grid / no cell data) so the caller falls back to a node-centre lerp.
    List<Vector3> BuildSurfaceWaypoints(List<LevelNode> route)
    {
        if (gridSystem == null || route == null || route.Count == 0) return null;

        // Cells we may walk across = every cell of the route's blocks.
        var allowed = new HashSet<Vector3Int>();
        foreach (var n in route)
            if (n.cells != null) foreach (var c in n.cells) allowed.Add(c);
        if (allowed.Count == 0) return null;

        // Surface = top-exposed cells (nothing directly above).
        var surface = new HashSet<Vector3Int>();
        foreach (var c in allowed)
            if (!allowed.Contains(c + Vector3Int.up)) surface.Add(c);
        if (surface.Count == 0) return null;

        Vector3Int start = NearestSurfaceCell(route[0], surface);

        var goals = new HashSet<Vector3Int>();
        var last  = route[route.Count - 1];
        if (last.cells != null)
            foreach (var c in last.cells) if (surface.Contains(c)) goals.Add(c);
        if (goals.Count == 0) return null;

        var cellPath = SurfaceBfs(start, goals, surface);
        if (cellPath == null) return null;

        var pts = new List<Vector3>(cellPath.Count);
        foreach (var c in cellPath) pts.Add(SurfaceTop(c));
        return pts;
    }

    Vector3Int NearestSurfaceCell(LevelNode n, HashSet<Vector3Int> surface)
    {
        Vector3 p = pawn != null ? pawn.position : Vector3.zero;
        Vector3Int best = (n.cells != null && n.cells.Length > 0) ? n.cells[0] : default;
        float bestD = float.MaxValue;
        if (n.cells != null)
            foreach (var c in n.cells)
            {
                if (!surface.Contains(c)) continue;
                float d = (SurfaceTop(c) - p).sqrMagnitude;
                if (d < bestD) { bestD = d; best = c; }
            }
        return best;
    }

    Vector3 SurfaceTop(Vector3Int c)
        => gridSystem.GridToWorld(c) + Vector3.up * (gridSystem.cellSize * 0.5f + pawnSurfaceLift);

    // Resting spot on a node's surface (highest top-exposed cell). Used to seat
    // the pawn at Start without it floating inside a tall block.
    Vector3 SurfaceTopOf(LevelNode n)
    {
        if (gridSystem != null && n != null && n.cells != null && n.cells.Length > 0)
        {
            var set = new HashSet<Vector3Int>(n.cells);
            Vector3Int best = n.cells[0];
            bool found = false;
            foreach (var c in n.cells)
                if (!set.Contains(c + Vector3Int.up) && (!found || c.y > best.y))
                { best = c; found = true; }
            if (found) return SurfaceTop(best);
        }
        return n != null ? n.PawnPoint : (pawn != null ? pawn.position : Vector3.zero);
    }

    // BFS across the surface: step to a 4-neighbour column at the same height or
    // one step up/down, staying on top-exposed cells.
    List<Vector3Int> SurfaceBfs(Vector3Int start, HashSet<Vector3Int> goals, HashSet<Vector3Int> surface)
    {
        if (!surface.Contains(start)) return null;

        var prev = new Dictionary<Vector3Int, Vector3Int>();
        var seen = new HashSet<Vector3Int> { start };
        var q    = new Queue<Vector3Int>();
        q.Enqueue(start);

        Vector3Int end = start;
        bool reached = goals.Contains(start);

        Vector3Int[] horiz = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };

        while (!reached && q.Count > 0)
        {
            var cur = q.Dequeue();
            foreach (var h in horiz)
            {
                // First valid surface cell in this direction: same level, then up, then down.
                Vector3Int[] cand = { cur + h, cur + h + Vector3Int.up, cur + h + Vector3Int.down };
                foreach (var nc in cand)
                {
                    if (!surface.Contains(nc)) continue;
                    if (!seen.Contains(nc))
                    {
                        seen.Add(nc);
                        prev[nc] = cur;
                        if (goals.Contains(nc)) { end = nc; reached = true; }
                        q.Enqueue(nc);
                    }
                    break;
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

    void OpenPanel(LevelNode node) => _selected = (node != null && node.level != null) ? node : null;

    void EnterLevel(LevelDefinition lv) { RunConfig.SetLevel(lv); LoadScene(gameplayScene); }

    void LoadScene(string s)
    {
        if (!string.IsNullOrEmpty(s) && Application.CanStreamedLevelBeLoaded(s))
            UnityEngine.SceneManagement.SceneManager.LoadScene(s);
        else
            Debug.LogWarning($"[LevelMap] scene '{s}' not in Build Settings.");
    }

    // ── Minimal IMGUI (placeholder until the Persona UGUI pass) ────────────────
    void OnGUI()
    {
        EnsureStyles();
        if (GUI.Button(new Rect(16f, 16f, 130f, 38f), "← Title", _btn))
            LoadScene(titleScene);

        if (_selected == null) return;
        var lv  = _selected.level;
        var rec = SaveSystem.Profile.GetRecord(lv.levelId);

        float w = 300f, h = 230f;
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
