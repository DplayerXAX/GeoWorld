using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Clepsydra — the observatory's water clock, seen from inside.
//
// A rotation puzzle built out of the game's own blocks, and connected by the game's
// own rule.
//
// There is no separate plumbing model here. A first version gave each block mouths on
// its faces and flooded between them, which was a second, parallel idea of what
// "connected" means — and it was the wrong one. The board asks exactly the question
// the main game asks: IS THERE A ROUTE FROM THE START BLOCK TO THE END BLOCK. It is
// answered by SurfaceGraphBuilder and SurfacePathfinding, over a real GridSystem that
// this game fills in — the same three classes GameFlowManager.EvaluateGrid uses.
//
// So a solved board is a board an enemy could walk across, and the water running
// through it is drawn on the very path the main game would have found.
//
// What makes this different from the flat version of this puzzle: a multi-cell block
// MOVES WHEN IT TURNS. Its cells swing around its pivot, so a rotation can be blocked
// by a neighbour or by the wall. The puzzle is therefore not "point the tile the right
// way" but "turn these so the water connects AND everything still fits" — which is
// the whole reason to use real blocks instead of one-cell tiles.
//
// Solvability comes from scrambling like a puzzle cube: start from a solved layout
// and apply N legal random turns. The reverse of that sequence is, by construction, a
// legal solution — and N doubles as the round's par. Generating a layout and then
// hoping it can be solved would fail both of those at once.
//
// Click selects a block, 1 / 2 / 3 turn it about X / Y / Z — the main game's rotation
// keys, so the one thing a player already knows transfers whole. ONE OF THE BLOCK'S
// OWN CUBES IS RED: that is the cell it swings around, and with free rotation that is
// the single fact deciding where the rest of its body lands.
public class Clepsydra : MonoBehaviour
{
    public static bool Active { get; private set; }

    // ── Stage ────────────────────────────────────────────────────────────────
    static readonly Vector3 StageOrigin = new(0f, 9000f, 0f);

    const float Cell = 1f;

    // Round shape. Everything that makes a round harder lives here, indexed by round.
    // Round one is deliberately small AND flat: one layer means the route cannot
    // climb, so the first board teaches turning without also asking the player to
    // orbit to see what they are doing.
    const int   BaseW = 3, BaseH = 2, BaseD = 3;
    const int   MaxW  = 6, MaxH  = 5, MaxD  = 6;
    const int   BaseScramble = 4;      // turns applied to the solved layout
    // Tries at a layout the pathfinder actually accepts. Cheap — a carve plus a graph
    // build — and the alternative is shipping a round nobody can finish.
    const int   LayoutAttempts = 12;
    const int   BaseSlack    = 6;      // moves granted over par
    const int   MinSlack     = 2;

    // The block vocabulary, spelled out here rather than read from
    // PlacementController.blocks.
    //
    // That was the first version and it silently produced nothing: PlacementController
    // is a GAMEPLAY-scene singleton, and this launches from LevelSelect, so the roster
    // was always empty, every shape match failed, and the board fell back to single
    // cells — which is why the pieces did not look like your blocks. Stack Well and
    // Balance Tower both carry their own table for exactly this reason.
    // Offsets FROM THE PIVOT, every one of them inside [-1, 1] on each axis.
    //
    // That bound is the whole trick behind "a turn can never be blocked". A block's
    // cells stay within Chebyshev radius 1 of its pivot no matter how it is turned,
    // so its swing shell is a fixed 3×3×3 around that pivot — and pivots sit Pitch
    // apart, so no two shells can ever intersect. Rotation legality stops being a
    // question the board has to answer.
    //
    // I4 is gone: four in a row cannot be centred inside that shell. Everything else
    // in the roster survives, re-expressed around its middle instead of its corner.
    static readonly Vector3Int[][] Shapes =
    {
        new[] { V(0,0,0) },                                                  // coupling
        new[] { V(0,0,0), V(1,0,0) },                                        // I2
        new[] { V(-1,0,0), V(0,0,0), V(1,0,0) },                             // I3
        new[] { V(0,0,0), V(1,0,0), V(0,0,1) },                              // L3  — elbow
        new[] { V(-1,0,0), V(0,0,0), V(1,0,0), V(1,0,1) },                   // L4
        new[] { V(-1,0,0), V(0,0,0), V(1,0,0), V(0,0,1) },                   // T4  — tee
        new[] { V(-1,0,0), V(0,0,0), V(0,0,1), V(1,0,1) },                   // S4 / Z
        new[] { V(0,0,0), V(1,0,0), V(0,0,1), V(1,0,1) },                    // O2x2
    };

    // Cells between pivots. Three is the smallest spacing where two neighbouring
    // blocks can still TOUCH — pivot p reaches p+1, pivot p+3 reaches p+2, and those
    // two cells are face-adjacent, which is what the surface graph needs — while
    // their 3×3×3 shells stay disjoint.
    const int Pitch = 3;

    static Vector3Int V(int x, int y, int z) => new(x, y, z);

    // A cube fills its cell, exactly as a placed block does in the main game.
    //
    // It was 0.68 with little braces bridging the gaps, which was a leftover from the
    // pipe version of this game: shrunken cubes plus connectors read as PLUMBING, and
    // that is the wrong sentence now that connection is decided by the surface graph.
    // At full size an I3 is one bar of three cubes — the same object the player places
    // on the board next door — and two blocks that meet actually meet.
    //
    // The gaps that remain are real: they are the pivot lattice's empty space, and
    // they are what a turn swings through.
    const float BlockSize = 1f;

    static readonly Color WetColor   = new(0.62f, 0.86f, 0.95f);
    static readonly Color LockedTint = new(0.30f, 0.26f, 0.22f);
    static readonly Color SelectCol  = new(1.00f, 0.86f, 0.35f);

    // Per-block colour comes from the synergy palette, so the board is made of the
    // same six inks the main game colours its blocks with rather than a private grey.
    static readonly BlockColor[] PieceColors =
    {
        BlockColor.Order, BlockColor.Harmony, BlockColor.Abundance,
        BlockColor.Enlightenment, BlockColor.Exploration, BlockColor.Heresy,
    };

    // The axis cell is a HIGHLIGHT — its own colour lifted toward white — not a red
    // repaint. Red said "warning" on a cell that is simply the one you turn around,
    // and it also destroyed the block's synergy colour exactly where you look most.
    const float PivotLift = 0.55f;

    // One colour for all three rings; the DIGIT is what tells them apart.
    //
    // They were red/green/blue per axis, borrowed from PlacementHintOverlay. Three
    // saturated hues around the selected block fought both the synergy inks the
    // blocks are painted in and the water — and the axis colours were carrying
    // information the labels already carry, twice.
    static readonly Color RingCol = new(0.92f, 0.90f, 0.82f);

    static Color AxisColor(int axis) => RingCol;

    // Blocks are glass. The board is a LATTICE — the interesting cells are as often
    // behind others as in front — and opaque cubes meant the only way to see the
    // middle was to fly around it. Transparent, the whole state is readable from any
    // angle, and it is also what lets the water be seen running INSIDE the blocks
    // rather than painted along their skin.
    const float BlockAlpha    = 0.42f;
    const float SelectedAlpha = 0.80f;   // the one you are working on comes forward

    static Material _glassMat;

    static Material GlassMaterial()
    {
        if (_glassMat != null) return _glassMat;

        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _glassMat = new Material(sh) { name = "ClepsydraGlass" };

        // The stock URP transparent recipe. ZWrite stays OFF so overlapping blocks
        // blend instead of one arbitrarily winning the depth test — with a lattice
        // this deep, z-write on transparent geometry reads as blocks flickering in
        // and out as the camera moves.
        if (_glassMat.HasProperty("_Surface"))
        {
            _glassMat.SetFloat("_Surface", 1f);
            _glassMat.SetFloat("_ZWrite", 0f);
            _glassMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _glassMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _glassMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        _glassMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return _glassMat;
    }

    // ── Launch / teardown ────────────────────────────────────────────────────

    public static void Launch(GameObject cubePrefab, string scoreId = null)
    {
        if (Active) return;
        var go = new GameObject("Clepsydra");
        var g  = go.AddComponent<Clepsydra>();
        g._cubePrefab = cubePrefab;
        g._scoreId    = scoreId;
        g.Begin();
    }

    GameObject _cubePrefab;
    string _scoreId;
    readonly MinigameStage _stage = new();

    Camera    _cam;
    Transform _root;
    Canvas    _canvas;
    TMP_Text  _hudText, _overLeft, _overRight, _reasonText;

    int   _round, _movesUsed, _budget, _par;
    bool  _gameOver, _newRecord, _solved;
    float _camYaw = 40f, _camPitch = 22f;
    float _camZoom = 1f;               // multiplier on the framing distance
    Vector3 _camFocus;                 // WASDQE offset from the board centre
    int   _ceiling;              // Z/X: layers above this are hidden

    void Begin()
    {
        Active = true;
        BuildCamera();
        BuildStage();
        BuildUI();
        _stage.SuppressHostUI(transform);
        _stage.PauseHostMusic();
        PlayMusic();
        StartRound(0);
    }

    void Quit()
    {
        Active = false;
        StopMusic();
        _stage.Restore();
        if (_cam != null) Destroy(_cam.gameObject);
        Destroy(gameObject);
    }

    void OnDestroy()
    {
        Active = false;
        StopMusic();
        _stage.ResumeHostMusic();
    }

    // ── Music ────────────────────────────────────────────────────────────────

    uint _musicPlayingId;

    void PlayMusic()
    {
        var cfg = MinigameAudio.Get();
        var evt = cfg != null ? cfg.clepsydraMusic : null;
        if (evt == null || !evt.IsValid()) return;   // silent is fine; the game still plays
        _musicPlayingId = evt.Post(gameObject);
    }

    void StopMusic()
    {
        if (_musicPlayingId == 0) return;
        var cfg = MinigameAudio.Get();
        int fadeMs = cfg != null ? cfg.stackWellMusicFadeOutMs : 500;
        AkUnitySoundEngine.StopPlayingID(_musicPlayingId, fadeMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _musicPlayingId = 0;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Board
    // ═════════════════════════════════════════════════════════════════════════

    internal class Piece
    {
        // Renderers of the pivot CELL, kept apart from body so the wet/dry/selected
        // tint never paints over the axis marker.
        public List<Renderer> pivotBody = new();

        public Vector3Int   pivot;       // world cell it turns around
        public Vector3Int[] cells;       // current world cells
        public bool         locked;      // an endpoint, or riveted: cannot be turned
        public bool         endpoint;    // the start or the end block
        public int          endKind;     // 1 = start, 2 = end, 0 = neither
        public Transform    root;
        public List<Renderer> body = new();
        public bool         onRoute;     // the found path runs over this block

        // The pose the board was BUILT in — solved, before any scramble turn. Kept so
        // riveting can be exact instead of guessed at.
        public HashSet<Vector3Int> solvedCells;

        public Color tint;      // this block's synergy ink
        public bool  fed;       // reachable from the source right now
        public bool  lit;       // in the victory run-through, already passed

        // Handed to GridSystem so the graph builder sees a real placed block.
        public PlacedBlockInstance inst = new();
    }

    readonly List<Piece> _pieces = new();
    readonly Dictionary<Vector3Int, Piece> _at = new();

    // A GridSystem of our own, so the real graph builder has something to read.
    //
    // Created on an INACTIVE object: GridSystem.Awake assigns the static instance,
    // and this game runs on top of LevelSelect, which has its own grid holding the
    // map. Letting Awake run would hand the map's singleton to a board floating nine
    // kilometres in the air. Inactive means no lifecycle callbacks, and Init() is
    // called by hand instead — every method the graph needs is a plain method.
    GridSystem          _grid;
    SurfaceGraphBuilder _graph = new();
    readonly List<FaceNode> _routeFaces = new();

    void EnsureGrid()
    {
        if (_grid != null) return;
        var go = new GameObject("ClepsydraGrid");
        go.SetActive(false);
        go.transform.SetParent(transform, false);
        _grid = go.AddComponent<GridSystem>();
        _grid.cellSize = Cell;
        _grid.Init();
    }

    // Republished from scratch after every turn. A piece's cells move when it turns,
    // so there is no incremental edit that is simpler than rebuilding — and at a few
    // dozen cells the rebuild is free.
    void RepublishGrid()
    {
        EnsureGrid();
        _grid.Init();
        foreach (var p in _pieces)
        {
            p.inst.occupiedCells.Clear();
            p.inst.occupiedCells.AddRange(p.cells);
            _grid.RegisterInstance(p.inst);
        }
    }

    int _w, _h, _d;
    Vector3Int _srcCell, _dstCell;
    Transform  _boardRoot;

    // _w/_h/_d count PIVOTS, not cells. Cell coordinates are pivot × Pitch + offset.
    bool PivotInBounds(Vector3Int pv) =>
        pv.x >= 0 && pv.x < _w && pv.y >= 0 && pv.y < _h && pv.z >= 0 && pv.z < _d;

    static Vector3Int CellOfPivot(Vector3Int pv) => pv * Pitch;

    Vector3 WorldOf(Vector3Int c) => new Vector3(
        c.x - (_w - 1) * Pitch * 0.5f,
        c.y - (_h - 1) * Pitch * 0.5f,
        c.z - (_d - 1) * Pitch * 0.5f) * Cell;

    // ── Plumbing, derived from shape ─────────────────────────────────────────

    static readonly Vector3Int[] Dirs =
    {
        new(1,0,0), new(-1,0,0), new(0,1,0), new(0,-1,0), new(0,0,1), new(0,0,-1),
    };

    // ── Rotation ─────────────────────────────────────────────────────────────

    static Vector3Int Turn(Vector3Int c, Vector3Int pivot, int axis)
    {
        Vector3Int v = c - pivot;
        Vector3Int r = axis switch
        {
            0 => new Vector3Int( v.x, -v.z,  v.y),
            1 => new Vector3Int( v.z,  v.y, -v.x),
            _ => new Vector3Int(-v.y,  v.x,  v.z),
        };
        return pivot + r;
    }

    // A turn is legal when every destination cell is inside the board and either free
    // or currently held by this same piece. Checked against the LIVE board, which is
    // what makes the scramble reversible: each scramble turn was legal at the moment
    // it was made, so undoing them in reverse order is legal too.
    // Only ever refused for being fixed. A block cannot leave its own 3×3×3 shell by
    // turning, and shells never overlap, so "another block is in the way" is not a
    // state this board can reach — see the Shapes table for why.
    bool CanTurn(Piece p, int axis) => !p.locked;

    void ApplyTurn(Piece p, int axis)
    {
        foreach (var c in p.cells) _at.Remove(c);
        for (int i = 0; i < p.cells.Length; i++) p.cells[i] = Turn(p.cells[i], p.pivot, axis);
        foreach (var c in p.cells) _at[c] = p;

        // Directions rotate about the origin, not about the pivot — a direction has
        // no position to swing around.
    }

    // ── Flow ─────────────────────────────────────────────────────────────────

    // The main game's question, asked with the main game's code.
    //
    // Rebuild the surface graph, collect the walkable faces of the start block and of
    // the end block, and hand both to SurfacePathfinding. A route existing IS the win
    // condition — there is nothing else to check, because "connected" already means
    // exactly one thing in this project and this is it.
    void RecomputeFlow()
    {
        RepublishGrid();

        _graph.SetData(_grid);
        _graph.Build();

        var startFaces = _graph.GetFaceNodes(_srcCell);
        var endFaces   = _graph.GetFaceNodes(_dstCell);

        _routeFaces.Clear();
        _solved = false;
        foreach (var p in _pieces) { p.onRoute = false; p.fed = false; }

        // Everything the water has REACHED, whether or not it has arrived. Flooding
        // the source's own connected component is what turns the board into a
        // progress bar: the wet frontier is exactly the block to work on next, and
        // lighting up only on a finished route would have told the player nothing
        // until the moment they no longer needed telling.
        if (startFaces != null && startFaces.Count > 0)
        {
            var seen  = new HashSet<FaceNode>();
            var queue = new Queue<FaceNode>();
            foreach (var f in startFaces) if (seen.Add(f)) queue.Enqueue(f);

            while (queue.Count > 0)
            {
                var f = queue.Dequeue();
                if (_at.TryGetValue(f.cell, out var owner)) owner.fed = true;
                foreach (var n in f.neighbors)
                    if (n != null && seen.Add(n)) queue.Enqueue(n);
            }
        }

        if (startFaces != null && startFaces.Count > 0 && endFaces != null && endFaces.Count > 0)
        {
            var path = SurfacePathfinding.FindPath(startFaces, endFaces);
            if (path != null && path.Count > 0)
            {
                _solved = true;
                _routeFaces.AddRange(path);
                foreach (var f in path)
                    if (_at.TryGetValue(f.cell, out var owner)) owner.onRoute = true;
            }
        }

        PaintWetness();
        RebuildFlowRibbon();
    }

    // A travelling ribbon along the completed route, drawn on the very faces the
    // pathfinder returned. Built only when the board is solved: before that the wet
    // tint is the progress report, and a ribbon crawling down a dead end would be
    // promising a route that is not there.
    LineRenderer _flow;

    void RebuildFlowRibbon()
    {
        if (_flow == null)
        {
            var go = new GameObject("Flow");
            go.transform.SetParent(_root, false);
            _flow = go.AddComponent<LineRenderer>();
            _flow.useWorldSpace   = true;
            // Fat enough to read as a body of water inside a cube rather than a wire
            // threaded through one, and comfortably inside the block so it never poke
            // out of the corners on a turn.
            _flow.widthMultiplier = 0.34f * Cell;
            _flow.numCapVertices  = 4;
            _flow.material        = FlowMaterial();
            _flow.textureMode     = LineTextureMode.Tile;
            // Set on the renderer as well as the material: which colour property a
            // URP Unlit exposes depends on the pipeline version, and a ribbon that
            // silently renders black is indistinguishable from one that is not
            // rendering at all.
            _flow.startColor = _flow.endColor = WetColor;
        }

        if (!_solved || _routeFaces.Count < 2) { _flow.positionCount = 0; return; }

        // Through the CENTRES of the cells the route crosses, not along their skin.
        //
        // It used to be offset out along each face's normal, which put the water on
        // the outside of the blocks — correct for a walking enemy, wrong for water,
        // and half of it ended up hidden behind whatever block it was hugging. Now
        // that the blocks are glass, running it down the middle is both visible and
        // the truer picture: the block is the conduit, not the riverbank.
        var pts = new List<Vector3>();
        foreach (var f in _routeFaces)
        {
            var at = _root.position + WorldOf(f.cell);
            if (pts.Count == 0 || (pts[pts.Count - 1] - at).sqrMagnitude > 0.0001f) pts.Add(at);
        }

        if (pts.Count < 2) { _flow.positionCount = 0; return; }

        _flow.positionCount = pts.Count;
        for (int i = 0; i < pts.Count; i++) _flow.SetPosition(i, pts[i]);
    }

    static Material _flowMat;

    static Material FlowMaterial()
    {
        if (_flowMat != null) return _flowMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _flowMat = new Material(sh) { name = "ClepsydraFlow" };
        if (_flowMat.HasProperty("_BaseColor")) _flowMat.SetColor("_BaseColor", WetColor);
        if (_flowMat.HasProperty("_Color"))     _flowMat.SetColor("_Color", WetColor);
        return _flowMat;
    }

    // Scrolls the ribbon so the water is visibly MOVING — a static line along the
    // route says "connected", a travelling one says "flowing", and the second is the
    // thing the round is about.
    void AnimateFlow()
    {
        if (_flow == null || _flow.positionCount == 0) return;
        var m = _flow.material;
        if (m != null && m.HasProperty("_BaseMap"))
            m.SetTextureOffset("_BaseMap", new Vector2(-Time.unscaledTime * 0.9f, 0f));
    }

    // ── Rotation rings ───────────────────────────────────────────────────────
    //
    // Three rings around the selected block, one per axis, each labelled with the key
    // that turns it. Same mapping and same three colours as PlacementHintOverlay in
    // the main game — 1/2/3 → X/Y/Z, red/green/blue — so the rings here are literally
    // teaching the ones the player will meet while building.
    //
    // Built once and re-aimed, not rebuilt per selection: they are three line loops
    // and a label each, and churning them on every click is work that shows up as a
    // hitch exactly when the player is interacting.
    class AxisRing
    {
        public GameObject   obj;
        public LineRenderer line;
        public TMP_Text     label;
        public int          axis;
        public float        flash;   // 1 right after its key is pressed, decaying
    }

    readonly List<AxisRing> _rings = new();
    const int   RingSegments = 48;
    const float RingRadius   = 1.15f;   // in cells, around the pivot

    void EnsureRings()
    {
        if (_rings.Count > 0) return;

        for (int axis = 0; axis < 3; axis++)
        {
            var go = new GameObject($"Ring{axis + 1}");
            go.transform.SetParent(_root, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace   = false;
            lr.loop            = true;
            lr.positionCount   = RingSegments;
            lr.widthMultiplier = 0.045f * Cell;
            lr.material        = FlowMaterial();
            lr.numCapVertices  = 2;

            // The ring lies in the plane PERPENDICULAR to its axis, because that is
            // the plane the block's cells actually travel through when you press it.
            for (int i = 0; i < RingSegments; i++)
            {
                float a = i / (float)RingSegments * Mathf.PI * 2f;
                float c = Mathf.Cos(a) * RingRadius * Cell, sN = Mathf.Sin(a) * RingRadius * Cell;
                lr.SetPosition(i, axis == 0 ? new Vector3(0f, c, sN)
                                : axis == 1 ? new Vector3(c, 0f, sN)
                                            : new Vector3(c, sN, 0f));
            }

            var labelGo = new GameObject("Key");
            labelGo.transform.SetParent(go.transform, false);
            var canvas = labelGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rt = (RectTransform)labelGo.transform;
            rt.sizeDelta  = new Vector2(120f, 120f);
            rt.localScale = Vector3.one * 0.006f;

            var txt = new GameObject("T", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
            txt.transform.SetParent(labelGo.transform, false);
            var trt = txt.rectTransform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = trt.offsetMax = Vector2.zero;
            txt.fontSize      = 64f;
            txt.alignment     = TextAlignmentOptions.Center;
            txt.fontStyle     = FontStyles.Bold;
            txt.raycastTarget = false;
            txt.isOverlay     = true;
            txt.text          = (axis + 1).ToString();
            txt.color         = AxisColor(axis);

            _rings.Add(new AxisRing { obj = go, line = lr, label = txt, axis = axis });
        }
    }

    void UpdateRings()
    {
        EnsureRings();

        bool show = _selected != null && !_gameOver && _accepting;
        foreach (var r in _rings)
        {
            r.obj.SetActive(show && !_selected.locked);
            if (!r.obj.activeSelf) continue;

            // Centred on the PIVOT cell, not the block's middle: the rings describe
            // what the block will swing around, and drawing them anywhere else would
            // point at the wrong cube.
            r.obj.transform.localPosition = WorldOf(_selected.pivot);

            r.flash = Mathf.Max(0f, r.flash - Time.unscaledDeltaTime * 2.2f);
            var col = Color.Lerp(AxisColor(r.axis), Color.white, r.flash);
            col.a = 0.55f + 0.45f * r.flash;
            r.line.startColor = r.line.endColor = col;

            // The key sits ON its own ring, and faces the camera so a digit is never
            // read edge-on.
            float ang = Time.unscaledTime * 0.4f + r.axis * 2.1f;
            float c = Mathf.Cos(ang) * RingRadius * Cell, sN = Mathf.Sin(ang) * RingRadius * Cell;
            Vector3 onRing = r.axis == 0 ? new Vector3(0f, c, sN)
                           : r.axis == 1 ? new Vector3(c, 0f, sN)
                                         : new Vector3(c, sN, 0f);
            r.label.transform.parent.localPosition = onRing;
            if (_cam != null)
                r.label.transform.parent.rotation = Quaternion.LookRotation(
                    r.label.transform.parent.position - _cam.transform.position, Vector3.up);
        }
    }

    void PaintWetness()
    {
        foreach (var p in _pieces)
        {
            // Its own ink, dimmed while dry. Blocks the water has reached come up to
            // full and lean toward the water's colour, so "how far has it got" is
            // readable without hiding whose block is whose.
            Color c = p.tint;
            if (p.locked && !p.endpoint) c = Color.Lerp(c, LockedTint, 0.55f);
            if (p.fed)     c = Color.Lerp(c, WetColor, 0.45f);
            else           c = Color.Lerp(c, Color.black, 0.42f);
            if (p == _selected) c = Color.Lerp(c, SelectCol, 0.45f);
            // The victory walk overrides everything: for that second and a half the
            // board is not a puzzle any more, it is a result.
            if (p.lit) c = Color.Lerp(Color.white, WetColor, 0.35f);

            // Alpha carries state too: the selected block firms up, everything else
            // stays glass so the lattice behind it keeps reading.
            c.a = p == _selected ? SelectedAlpha : BlockAlpha;

            // The axis cell is the same colour lifted, so it reads as "this cube, of
            // this block" rather than as a separate object sitting on it.
            var axisCol = Color.Lerp(c, Color.white, PivotLift);
            axisCol.a = Mathf.Min(1f, c.a + 0.25f);   // the axis cell reads through the glass
            foreach (var r in p.pivotBody) if (r != null) MpbColor.Set(r, axisCol);
            foreach (var r in p.body) if (r != null) MpbColor.Set(r, c);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Generation
    // ═════════════════════════════════════════════════════════════════════════

    void StartRound(int round)
    {
        _round     = round;
        _movesUsed = 0;
        _solved    = false;
        _accepting = true;

        _selected = null;
        _camFocus = Vector3.zero;
        _w = Mathf.Min(MaxW, BaseW + round / 2);
        _h = round == 0 ? 1 : Mathf.Min(MaxH, BaseH + round / 3);
        _d = Mathf.Min(MaxD, BaseD + round / 2);
        _ceiling = _h - 1;

        BuildBoard(round == 0 ? 2 : BaseScramble + round * 2);

        // Par is what UNDOING the board actually costs, not how many turns were spent
        // making it. Random scrambling doubles back on itself constantly — four turns
        // of one piece on one axis is a scramble of four and a restore of zero — so
        // counting the scramble handed out budgets that had nothing to do with the
        // board in front of the player.
        _par    = TurnsToRestore();
        _budget = _par + Mathf.Max(MinSlack, BaseSlack - round / 2);

        RecomputeFlow();
        RefreshHud();
    }


    void BuildBoard(int scramble)
    {
        if (_boardRoot != null) Destroy(_boardRoot.gameObject);
        _pieces.Clear();
        _at.Clear();
        _decoys = 0;    // per board, not per run — otherwise round 2 onward gets none

        _boardRoot = new GameObject("Board").transform;
        _boardRoot.SetParent(_root, false);

        var srcPivot = new Vector3Int(0,      Random.Range(0, _h), Random.Range(0, _d));
        var dstPivot = new Vector3Int(_w - 1, Random.Range(0, _h), Random.Range(0, _d));
        _srcCell = CellOfPivot(srcPivot);
        _dstCell = CellOfPivot(dstPivot);

        // Connect FIRST, and prove it, then scramble.
        //
        // Laying a route whose blocks reach toward each other is not the same as
        // having a route: the surface graph is the only thing that decides, and it
        // can disagree — a decoy can seal the very face the walk needed, and a carve
        // that doubles back can leave two route blocks sharing a wall instead of
        // facing each other. Scrambling from an unsolved layout produces a board that
        // is unsolvable no matter what the player does, because the state it would be
        // turning back toward was never a solution.
        //
        // So: build, ASK THE PATHFINDER, and only keep the layout if it says yes.
        for (int attempt = 0; attempt < LayoutAttempts; attempt++)
        {
            _pieces.Clear();
            _at.Clear();
            _decoys = 0;

            var route = CarvePath(srcPivot, dstPivot);
            LayRoute(route);
            SprinkleDecoys();

            RecomputeFlow();
            if (_solved) break;

            if (attempt == LayoutAttempts - 1)
                Debug.LogWarning($"[Clepsydra] No connected layout after {LayoutAttempts} attempts — " +
                                 "shipping it unscrambled so the round is at least finishable. " +
                                 "Check Pitch / shape roster.");
        }

        BuildVisuals();

        // Only scramble a layout that was proven connected. Scrambling an unsolved one
        // hands the player a board whose "solution" was never a solution — better to
        // give away a free round than an impossible one.
        // The count is not kept: par is measured from the board afterwards, by
        // TurnsToRestore, which is the only number that describes what the player is
        // actually looking at.
        if (_solved) Scramble(scramble);
    }

    // Every block on the route gets a shape that REACHES BOTH ITS NEIGHBOURS: it needs
    // a cell one step toward the previous pivot and one toward the next, because that
    // is what puts two cells face-to-face across the gap and lets the surface graph
    // cross it. The endpoints need only one arm.
    void LayRoute(List<Vector3Int> route)
    {
        for (int k = 0; k < route.Count; k++)
        {
            var pv = route[k];
            var need = new List<Vector3Int>();
            if (k > 0)                 need.Add(Step(route[k - 1] - pv));
            if (k < route.Count - 1)   need.Add(Step(route[k + 1] - pv));

            var cells = PickShapeReaching(need);
            if (cells == null)
            {
                // Never skip: a missing pivot is a cut route, which is a board that
                // cannot be solved no matter how it is turned. The literal arms plus
                // the pivot cell always form a legal shape, so this is a real piece
                // even when the roster had nothing prettier.
                var fallback = new HashSet<Vector3Int> { Vector3Int.zero };
                foreach (var dneed in need) fallback.Add(dneed);
                cells = new List<Vector3Int>(fallback);
                Debug.LogWarning($"[Clepsydra] No roster shape reached {need.Count} arm(s) at {pv} — used a bare joint.");
            }

            var world = new Vector3Int[cells.Count];
            var origin = CellOfPivot(pv);
            for (int m = 0; m < cells.Count; m++) world[m] = origin + cells[m];

            bool isEnd = k == 0 || k == route.Count - 1;
            var piece = AddPiece(world, origin, isEnd);
            piece.endKind = k == 0 ? 1 : k == route.Count - 1 ? 2 : 0;
        }
    }

    static Vector3Int Step(Vector3Int delta) => new(
        delta.x != 0 ? (delta.x > 0 ? 1 : -1) : 0,
        delta.x == 0 && delta.y != 0 ? (delta.y > 0 ? 1 : -1) : 0,
        delta.x == 0 && delta.y == 0 && delta.z != 0 ? (delta.z > 0 ? 1 : -1) : 0);

    // A random shape/orientation whose cells include every direction in `need`.
    // Shuffled rather than first-match so a straight run is not always an I3.
    List<Vector3Int> PickShapeReaching(List<Vector3Int> need)
    {
        var candidates = new List<List<Vector3Int>>();

        for (int si = 0; si < Shapes.Length; si++)
        {
            foreach (var rot in RotationsOf(si))
            {
                bool all = true;
                foreach (var d in need) if (!rot.Contains(d)) { all = false; break; }
                if (all && rot.Contains(Vector3Int.zero)) candidates.Add(new List<Vector3Int>(rot));
            }
        }
        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    // A self-avoiding walk through PIVOT space. Biased toward the goal but allowed to
    // wander, because a straight line is not a puzzle.
    List<Vector3Int> CarvePath(Vector3Int from, Vector3Int to)
    {
        var path    = new List<Vector3Int> { from };
        var visited = new HashSet<Vector3Int> { from };
        var cur     = from;

        int guard = _w * _h * _d * 4;
        while (cur != to && guard-- > 0)
        {
            var options = new List<Vector3Int>();
            foreach (var dir in Dirs)
            {
                var n = cur + dir;
                if (!PivotInBounds(n) || visited.Contains(n)) continue;
                options.Add(n);
            }
            if (options.Count == 0) break;

            Vector3Int pick;
            if (Random.value < 0.66f)
            {
                pick = options[0];
                int best = int.MaxValue;
                foreach (var o in options)
                {
                    int dist = Mathf.Abs(o.x - to.x) + Mathf.Abs(o.y - to.y) + Mathf.Abs(o.z - to.z);
                    if (dist < best) { best = dist; pick = o; }
                }
            }
            else pick = options[Random.Range(0, options.Count)];

            cur = pick;
            visited.Add(cur);
            path.Add(cur);
        }

        if (cur != to && PivotInBounds(to) && !visited.Contains(to)) path.Add(to);
        return path;
    }

    Piece AddPiece(Vector3Int[] cells, Vector3Int pivot, bool endpoint = false)
    {
        var p = new Piece
        {
            cells    = cells,
            pivot    = pivot,
            endpoint = endpoint,
            locked   = endpoint,     // the two anchors are fixed, like a level's endpoints
        };
        p.solvedCells = new HashSet<Vector3Int>(cells);
        p.tint = BlockColorPalette.Get(PieceColors[_pieces.Count % PieceColors.Length]);
        _pieces.Add(p);
        foreach (var c in cells) _at[c] = p;
        return p;
    }

    // Decoys on pivots the route did not use. Real shapes, so the answer is not
    // legible from the silhouette — a board where every spare piece is one cube tells
    // you which pivots are the route.
    void SprinkleDecoys()
    {
        // Round one is somebody's first look at a 3D rotation puzzle. A board packed
        // with pieces that turn out not to matter is not "hard", it is a search — and
        // the thing worth learning first is what a turn DOES.
        int want = _round == 0 ? 1 : Mathf.Max(2, (_w * _h * _d) / 5);

        for (int tries = 0; tries < want * 8 && _decoys < want; tries++)
        {
            var pv = new Vector3Int(Random.Range(0, _w), Random.Range(0, _h), Random.Range(0, _d));
            var origin = CellOfPivot(pv);
            if (_at.ContainsKey(origin)) continue;

            int si = Random.Range(0, Shapes.Length);
            var rots = RotationsOf(si);
            var rot  = new List<Vector3Int>(rots[Random.Range(0, rots.Count)]);

            var world = new Vector3Int[rot.Count];
            bool clear = true;
            for (int m = 0; m < rot.Count; m++)
            {
                world[m] = origin + rot[m];
                if (_at.ContainsKey(world[m])) { clear = false; break; }
            }
            // Every cell, not just the pivot. Checking only the pivot let a decoy
            // drop its arms straight through a neighbour — and through the endpoint
            // markers, which is how those disappeared.
            if (!clear) continue;

            AddPiece(world, origin);
            _decoys++;
        }
    }

    int _decoys;

    // Scramble like a puzzle cube: N legal turns from the solved layout, so the
    // reverse sequence is a legal solution and N is an honest par.
    int Scramble(int turns)
    {
        int done = 0;
        for (int i = 0; i < turns * 8 && done < turns; i++)
        {
            var p    = _pieces[Random.Range(0, _pieces.Count)];
            int axis = Random.Range(0, 3);
            if (!CanTurn(p, axis)) continue;

            // Only count a turn that actually MOVED something. Half the roster is
            // symmetric about at least one axis — an I3 spun about its own length, an
            // O2x2 spun about its face — and those turns leave the board identical.
            // Counting them meant a "2-turn scramble" could be two turns of nothing,
            // which is how rounds were arriving already solved.
            int before = PoseMask(p.cells, p.pivot);
            ApplyTurn(p, axis);
            if (PoseMask(p.cells, p.pivot) == before) continue;

            SyncVisual(p);
            done++;
        }

        // And it has to actually break the route. Even real turns can miss — every
        // one of them may have landed on a decoy, which changes the board without
        // changing whether the water arrives. Keep turning ROUTE pieces until the
        // board is genuinely unsolved.
        RecomputeFlow();
        for (int guard = 0; _solved && guard < 200; guard++)
        {
            var onRoute = new List<Piece>();
            foreach (var q in _pieces) if (q.onRoute && !q.locked && !q.endpoint) onRoute.Add(q);
            if (onRoute.Count == 0) break;

            var p    = onRoute[Random.Range(0, onRoute.Count)];
            int axis = Random.Range(0, 3);
            int before = PoseMask(p.cells, p.pivot);
            ApplyTurn(p, axis);
            if (PoseMask(p.cells, p.pivot) == before) continue;

            SyncVisual(p);
            done++;
            RecomputeFlow();
        }

        // Rivet only pieces the scramble happened to leave EXACTLY where they
        // started. That is the one test that cannot make a round unsolvable, because
        // a piece already in its solved pose never needs to move again.
        //
        // The previous test — "is this piece currently carrying a route" — was a
        // guess, and a wrong one: in a scrambled board a piece can sit on some
        // incidental path while being nowhere near its own solved pose, and riveting
        // it there welds the puzzle shut. That was the other half of "often
        // unsolvable".
        int rivets = Mathf.Min(_round / 2, Mathf.Max(0, _pieces.Count - 3));
        for (int i = 0; i < rivets * 8 && rivets > 0; i++)
        {
            var p = _pieces[Random.Range(0, _pieces.Count)];
            if (p.locked || !IsSolvedPose(p)) continue;
            p.locked = true;
            rivets--;
        }
        return done;
    }

    // The exact minimum number of turns to put every piece back.
    //
    // Exact, not an estimate, and only because a turn can never be blocked here: the
    // pieces are completely independent, so the board's cost is the sum of theirs.
    // Each piece is a breadth-first search over its own orientations — at most 24
    // states reachable by three moves — which is small enough to just do.
    int TurnsToRestore()
    {
        int total = 0;
        foreach (var p in _pieces)
        {
            if (p.locked || p.solvedCells == null) continue;
            total += TurnsForPiece(p);
        }
        return total;
    }

    // Offsets live in [-1,1] on each axis, so a whole pose fits in 27 bits — one per
    // cell of the shell. That makes a pose a single int, and "have I seen this pose"
    // a hash-set lookup instead of a set comparison.
    static int PoseMask(IEnumerable<Vector3Int> cells, Vector3Int pivot)
    {
        int m = 0;
        foreach (var c in cells)
        {
            var o = c - pivot;
            if (o.x < -1 || o.x > 1 || o.y < -1 || o.y > 1 || o.z < -1 || o.z > 1) continue;
            m |= 1 << ((o.x + 1) * 9 + (o.y + 1) * 3 + (o.z + 1));
        }
        return m;
    }

    static int TurnsForPiece(Piece p)
    {
        int goal = PoseMask(p.solvedCells, p.pivot);
        var start = new List<Vector3Int>(p.cells);
        if (PoseMask(start, p.pivot) == goal) return 0;

        var seen  = new HashSet<int> { PoseMask(start, p.pivot) };
        var queue = new Queue<(List<Vector3Int> cells, int depth)>();
        queue.Enqueue((start, 0));

        while (queue.Count > 0)
        {
            var (cells, depth) = queue.Dequeue();
            for (int axis = 0; axis < 3; axis++)
            {
                var next = new List<Vector3Int>(cells.Count);
                foreach (var c in cells) next.Add(Turn(c, p.pivot, axis));

                int mask = PoseMask(next, p.pivot);
                if (mask == goal) return depth + 1;
                if (seen.Add(mask)) queue.Enqueue((next, depth + 1));
            }
        }
        return 0;   // unreachable in practice — every pose came from these same moves
    }

    static bool IsSolvedPose(Piece p)
    {
        if (p.solvedCells == null || p.solvedCells.Count != p.cells.Length) return false;
        foreach (var c in p.cells) if (!p.solvedCells.Contains(c)) return false;
        return true;
    }

    // Kept next to AllRotations, which is the only thing that still needs them.
    static HashSet<Vector3Int> Normalize(IList<Vector3Int> cells)
    {
        var min = cells[0];
        foreach (var c in cells) min = Vector3Int.Min(min, c);
        var set = new HashSet<Vector3Int>();
        foreach (var c in cells) set.Add(c - min);
        return set;
    }

    static bool SameSet(HashSet<Vector3Int> a, HashSet<Vector3Int> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var c in a) if (!b.Contains(c)) return false;
        return true;
    }

    // The 24 rotations of a polycube, normalised. Cached per block: this runs inside
    // the layout search and would otherwise be recomputed for every candidate run.
    static readonly Dictionary<int, List<HashSet<Vector3Int>>> _rotCache = new();

    static List<HashSet<Vector3Int>> RotationsOf(int shapeIndex)
    {
        if (_rotCache.TryGetValue(shapeIndex, out var cached)) return cached;
        var built = AllRotations(Shapes[shapeIndex]);
        _rotCache[shapeIndex] = built;
        return built;
    }

    // The distinct orientations of a shape, ROTATED ABOUT THE ORIGIN AND LEFT THERE.
    //
    // This used to normalise each rotation to its minimum corner, and that one line
    // broke three things at once:
    //
    //   · Offsets could never be negative, so no block could reach toward -x/-y/-z.
    //     Half the route's blocks could not be built at all, LayRoute skipped those
    //     pivots, and the board arrived with the route already severed — "often
    //     unsolvable" was mostly this.
    //   · Cells sprawled up to 3 away from the pivot instead of staying within 1, so
    //     shells overlapped and blocks landed on top of their neighbours — including
    //     on top of the start and end markers, which is where those went.
    //   · The pivot stopped being one of the block's own cells, which is the thing
    //     the red marker is there to promise.
    //
    // The shapes in the table are already centred on (0,0,0) and bounded to [-1,1];
    // rotating about the origin keeps both properties exactly.
    static List<HashSet<Vector3Int>> AllRotations(Vector3Int[] cells)
    {
        var outSets = new List<HashSet<Vector3Int>>();
        var work    = new List<Vector3Int>(cells);

        for (int a = 0; a < 4; a++)
        {
            for (int b = 0; b < 4; b++)
            {
                for (int c = 0; c < 4; c++)
                {
                    var set = new HashSet<Vector3Int>(work);
                    bool dup = false;
                    foreach (var s in outSets) if (SameSet(s, set)) { dup = true; break; }
                    if (!dup) outSets.Add(set);
                    for (int i = 0; i < work.Count; i++) work[i] = Turn(work[i], Vector3Int.zero, 2);
                }
                for (int i = 0; i < work.Count; i++) work[i] = Turn(work[i], Vector3Int.zero, 1);
            }
            for (int i = 0; i < work.Count; i++) work[i] = Turn(work[i], Vector3Int.zero, 0);
        }
        return outSets;
    }



    Piece AddPiece(Vector3Int[] cells, bool endpoint = false)
    {
        var p = new Piece
        {
            cells    = cells,
            pivot    = cells[0],
            endpoint = endpoint,
            locked   = endpoint,     // the two anchors are fixed, like a level's endpoints
        };
        _pieces.Add(p);
        foreach (var c in cells) _at[c] = p;
        return p;
    }

    // ═════════════════════════════════════════════════════════════════════════
    // Visuals
    // ═════════════════════════════════════════════════════════════════════════

    void BuildVisuals()
    {
        foreach (var p in _pieces) BuildPiece(p);

    }

    void BuildPiece(Piece p)
    {
        var go = new GameObject("Pipe");
        go.transform.SetParent(_boardRoot, false);
        p.root = go.transform;
        SyncVisual(p);
    }

    // Rebuilt rather than transformed on each turn. A piece's cells move to new
    // lattice positions, so there is no single transform that expresses the change —
    // and at four cubes a piece, rebuilding is cheaper than being clever.
    void SyncVisual(Piece p)
    {
        if (p.root == null) return;
        for (int i = p.root.childCount - 1; i >= 0; i--) Destroy(p.root.GetChild(i).gameObject);
        p.body.Clear();
        p.pivotBody.Clear();

        // The anchors are the main game's own start / end markers, so the two things
        // the board is asking you to join look exactly like the two things a level
        // asks you to join.
        if (p.endpoint)
        {
            var prefab = EndpointPrefab(p.endKind);
            var marker = prefab != null
                ? Instantiate(prefab, p.root)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.transform.SetParent(p.root, false);
            marker.transform.localPosition = WorldOf(p.pivot);
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale    = Vector3.one;

            foreach (var col in marker.GetComponentsInChildren<Collider>()) Destroy(col);
            var hit = marker.AddComponent<BoxCollider>();
            hit.size = Vector3.one * BlockSize;
            marker.AddComponent<PipeClick>().Init(this, p);

            // The endpoint's OTHER cells, drawn as ordinary cubes.
            //
            // They were occupied on the grid and invisible on screen, which is what
            // put a cell of empty air between the marker and the first block: the
            // connection was real, the picture had a hole in it. An endpoint reaches
            // toward the route exactly like every other piece, so it should look like
            // it does.
            foreach (var c in p.cells)
            {
                if (c == p.pivot) continue;

                var arm = _cubePrefab != null
                    ? Instantiate(_cubePrefab, p.root)
                    : GameObject.CreatePrimitive(PrimitiveType.Cube);
                arm.transform.SetParent(p.root, false);
                arm.transform.localPosition = WorldOf(c);
                arm.transform.localRotation = Quaternion.identity;
                arm.transform.localScale    = Vector3.one * (BlockSize * Cell);

                foreach (var col in arm.GetComponentsInChildren<Collider>()) Destroy(col);
                var abox = arm.AddComponent<BoxCollider>();
                abox.size = Vector3.one;
                arm.AddComponent<PipeClick>().Init(this, p);
                foreach (var r in arm.GetComponentsInChildren<Renderer>())
                {
                    r.sharedMaterial    = GlassMaterial();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    p.body.Add(r);
                }
            }

            // The marker's own renderers stay OUT of p.body: it keeps the prefab's
            // material, which is the whole point of borrowing it.
            ApplyCeilingFade(p);
            return;
        }

        foreach (var c in p.cells)
        {
            // The game's own cube, not a primitive: these are supposed to BE your
            // blocks seen as plumbing, and a board of grey primitives says nothing
            // about that. Launch already receives the prefab — the first version
            // took it and threw it away.
            var hub = _cubePrefab != null
                ? Instantiate(_cubePrefab, p.root)
                : GameObject.CreatePrimitive(PrimitiveType.Cube);
            hub.transform.SetParent(p.root, false);
            hub.transform.localPosition = WorldOf(c);
            hub.transform.localRotation = Quaternion.identity;
            // Under a full cell so the pipe runs between blocks read as pipe rather
            // than as a seam.
            hub.transform.localScale = Vector3.one * (BlockSize * Cell);

            foreach (var col in hub.GetComponentsInChildren<Collider>()) Destroy(col);
            var box = hub.AddComponent<BoxCollider>();
            box.size = Vector3.one;
            hub.AddComponent<PipeClick>().Init(this, p);

            bool isPivotCell = c == p.pivot;
            var sink = isPivotCell ? p.pivotBody : p.body;
            foreach (var r in hub.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial    = GlassMaterial();
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                sink.Add(r);
            }
            if (isPivotCell) hub.name = "PivotCell";

            // No connectors. Full-size cubes in adjacent cells already share a face,
            // so a multi-cell block is one solid object on its own — and a drawn
            // connector would be a second description of "connected" competing with
            // the pathfinder's, which is the mistake this game already made once.
        }

        // The pivot marker is one of the block's OWN CELLS, turned red — not an extra
        // pip. The pip was 0.29 wide inside a 0.68 cube, i.e. entirely buried, which
        // is why no red showed at all. Colouring the cell itself is also the truer
        // statement: the axis is not a decoration sitting on the block, it IS one of
        // its cubes.

        ApplyCeilingFade(p);
    }

    // Loaded from Resources so this works from LevelSelect, where the gameplay
    // scene's LevelEndpointGenerator does not exist. The prefabs were moved under
    // Resources/GeoWorldEndpoints for exactly this — moved, not copied, so the
    // gameplay scene's references still resolve to the same assets.
    static GameObject _startPrefab, _endPrefab;
    static bool       _endpointsLoaded;

    static GameObject EndpointPrefab(int kind)
    {
        if (!_endpointsLoaded)
        {
            _endpointsLoaded = true;
            _startPrefab = Resources.Load<GameObject>("GeoWorldEndpoints/start");
            _endPrefab   = Resources.Load<GameObject>("GeoWorldEndpoints/end");
            if (_startPrefab == null || _endPrefab == null)
                Debug.LogWarning("[Clepsydra] Endpoint prefabs missing from Resources/GeoWorldEndpoints — falling back to plain cubes.");
        }
        return kind == 2 ? _endPrefab : _startPrefab;
    }

    // Q / E raise and lower a ceiling; everything above it dims. A 3D puzzle whose
    // back half is hidden behind its front half is not readable by orbiting alone —
    // and Q/E already means "up a layer, down a layer" in the main game.
    void ApplyCeilingFade(Piece p)
    {
        // Compared in PIVOT space. _ceiling counts pivot layers, but p.cells are cell
        // coordinates — and cells run Pitch times faster, so a block on the second
        // pivot layer read as y = 3 against a ceiling of 1 and switched itself off.
        // Everything above the bottom layer vanished, which is where the start and
        // end markers kept going.
        bool above = false;
        foreach (var c in p.cells)
            if (Mathf.FloorToInt(c.y / (float)Pitch) > _ceiling) { above = true; break; }
        if (p.root != null) p.root.gameObject.SetActive(!above);
    }

    void ApplyCeilingAll() { foreach (var p in _pieces) ApplyCeilingFade(p); }

    // ═════════════════════════════════════════════════════════════════════════
    // Input / loop
    // ═════════════════════════════════════════════════════════════════════════

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Quit(); return; }
        HandleCameraDrag();

        if (_gameOver)
        {
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            return;
        }

        AnimateFlow();
        UpdateRings();

        if (Input.GetKeyDown(KeyCode.Alpha1)) TurnSelected(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) TurnSelected(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) TurnSelected(2);

        // Z / X raise and lower a ceiling. Moved off Q/E now that those fly the
        // camera — with a movable camera this matters less, but a 6×5×6 lattice still
        // hides its own back half from every angle.
        if (Input.GetKeyDown(KeyCode.X)) { _ceiling = Mathf.Min(_h - 1, _ceiling + 1); ApplyCeilingAll(); }
        if (Input.GetKeyDown(KeyCode.Z)) { _ceiling = Mathf.Max(0,      _ceiling - 1); ApplyCeilingAll(); }

        HandleCameraMove();
    }

    // Called by PipeClick. One click = one turn: no select-then-act mode, because a
    // mode is one more thing to be in the wrong one of.
    // Click SELECTS. Turning is 1 / 2 / 3, the same keys that rotate a block in the
    // main game — so the one thing a player already knows how to do transfers whole.
    internal void ClickPiece(Piece p)
    {
        if (_gameOver || !_accepting || p == null) return;
        _selected = p;
        if (p.locked) Flash("Riveted — this one can't turn.");
        PaintWetness();
        RefreshHud();
    }

    void TurnSelected(int axis)
    {
        if (_gameOver || !_accepting) return;
        if (_selected == null) { Flash("Click a pipe first."); return; }
        if (_selected.locked)  { Flash("Riveted — this one can't turn."); return; }

        foreach (var r in _rings) if (r.axis == axis) r.flash = 1f;

        ApplyTurn(_selected, axis);
        SyncVisual(_selected);
        _movesUsed++;

        RecomputeFlow();
        RefreshHud();

        if (_solved) { StartCoroutine(CelebrateAndAdvance()); return; }
        if (_movesUsed >= _budget) GameOver("Out of turns");
    }

    static string AxisName(int axis) => axis == 0 ? "X" : axis == 1 ? "Y" : "Z";

    Piece _selected;

    // A beat between finishing a board and getting the next one. Swapping on the
    // same frame is a jump cut: the player never sees the run they just completed,
    // and the new board reads as the game losing its place.
    System.Collections.IEnumerator CelebrateAndAdvance()
    {
        _accepting = false;
        Flash(_movesUsed <= _par ? $"Flowing — {_movesUsed} turns, par {_par}"
                                 : $"Flowing — {_movesUsed} turns");

        // Light the route's BLOCKS one after another, starting at the source.
        //
        // This was a line drawn along the path's faces, and it never showed up — a
        // thin LineRenderer against a lattice of cubes is lost the moment anything is
        // in front of it. The blocks are already the biggest, most legible thing on
        // screen, so the run is told with them instead: the same information, carried
        // by the object the player has been staring at all round.
        var order = new List<Piece>();
        foreach (var f in _routeFaces)
            if (_at.TryGetValue(f.cell, out var owner) && !order.Contains(owner)) order.Add(owner);

        for (int i = 0; i < order.Count; i++)
        {
            order[i].lit = true;
            PaintWetness();
            float step = RouteDrawTime / Mathf.Max(1, order.Count);
            float w = 0f;
            while (w < step) { w += Time.unscaledDeltaTime; yield return null; }
        }

        float t = 0f;
        while (t < 0.5f) { t += Time.unscaledDeltaTime; yield return null; }
        foreach (var q in _pieces) q.lit = false;

        _accepting = true;
        StartRound(_round + 1);
    }

    const float RouteDrawTime = 1.0f;

    bool _accepting = true;

    // WASD pans in the camera's own plane, QE lifts and drops — the main game's
    // movement, so nothing has to be relearned to look at a board.
    void HandleCameraMove()
    {
        Vector3 move = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) move += Vector3.forward;
        if (Input.GetKey(KeyCode.S)) move += Vector3.back;
        if (Input.GetKey(KeyCode.D)) move += Vector3.right;
        if (Input.GetKey(KeyCode.A)) move += Vector3.left;
        if (Input.GetKey(KeyCode.E)) move += Vector3.up;
        if (Input.GetKey(KeyCode.Q)) move += Vector3.down;
        if (move.sqrMagnitude < 0.0001f) return;

        // Flattened to the ground plane so W always means "away from me across the
        // board" rather than "into the floor" when the camera is looking down.
        var yaw = Quaternion.Euler(0f, _camYaw, 0f);
        Vector3 planar = yaw * new Vector3(move.x, 0f, move.z);
        _camFocus += (planar + Vector3.up * move.y) * (CamPanSpeed * _camZoom * Time.unscaledDeltaTime);

        float reach = Mathf.Max(_w, Mathf.Max(_h, _d)) * Cell;
        _camFocus = Vector3.ClampMagnitude(_camFocus, reach);
    }

    const float CamPanSpeed = 9f;

    void HandleCameraDrag()
    {
        // Zoom first, and outside the drag check — scrolling is not a drag, and
        // gating it behind the right button is the kind of thing you only find by
        // wondering why the wheel does nothing.
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(wheel) > 0.0001f)
            _camZoom = Mathf.Clamp(_camZoom * (1f - wheel * 1.4f), 0.45f, 2.4f);

        if (!Input.GetMouseButton(1)) return;
        _camYaw  += Input.GetAxis("Mouse X") * 180f * Time.unscaledDeltaTime;
        _camPitch = Mathf.Clamp(_camPitch - Input.GetAxis("Mouse Y") * 120f * Time.unscaledDeltaTime, 5f, 78f);
    }

    void BuildCamera()
    {
        var go = new GameObject("ClepsydraCamera");
        _cam = go.AddComponent<Camera>();
        _cam.clearFlags  = CameraClearFlags.Skybox;
        _cam.depth       = 50f;
        _cam.fieldOfView = 52f;
        PlaceCamera();
    }

    void PlaceCamera()
    {
        if (_cam == null) return;
        float span = Mathf.Max(_w, Mathf.Max(_h, _d)) * Cell;
        var rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
        Vector3 focus = StageOrigin + _camFocus;
        _cam.transform.position = focus + rot * new Vector3(0f, 0f, -(span * 2.4f + 4f) * _camZoom);
        _cam.transform.LookAt(focus);
    }

    void LateUpdate() => PlaceCamera();

    void BuildStage()
    {
        _root = new GameObject("Stage").transform;
        _root.SetParent(transform, false);
        _root.position = StageOrigin;

        // The observatory's own night, not the workshop sky it was borrowing. Same
        // banded flat-ink construction as the other two, inverted: darkest at the
        // horizon and opening toward the zenith, because that is where the building
        // this game belongs to is pointing.
        // LevelSelect's own sky shader, tuned deep blue. Borrowing the map's shader
        // rather than the bespoke one written for this game: the minigame lives inside
        // the observatory on that map, and arriving in a visibly different night said
        // "different game" instead of "inside the building you were standing next to".
        _stage.SetSkybox(Resources.Load<Material>("GeoWorldShaderKeepalive/ObservatoryNight_keep"));
        _stage.SetLinearFog(new Color(0.035f, 0.055f, 0.125f), 34f, 180f);

        BuildObservatoryStage();
    }

    // ── The room the board is standing in ────────────────────────────────────
    //
    // Same job the workshop dressing does for the Balancing Yard: put the puzzle
    // somewhere. All scenery — no colliders, nothing the board can touch, parented
    // under _root so Quit takes it and a restart leaves it alone.
    //
    // Everything here is a RING around the board rather than furniture beside it,
    // because the player orbits freely: an observatory that only looks built from one
    // angle is a flat, and this one gets walked around.
    static readonly Color StoneDark = new(0.055f, 0.070f, 0.130f);
    static readonly Color BrassCol  = new(0.55f, 0.45f, 0.24f);

    // Almost nothing.
    //
    // The first version put three turning armillary hoops, ten pillars, a ringed
    // plaza and its own star field around the board — and every one of those competed
    // with the thing the player is actually reading, which is a lattice of small
    // coloured cubes and a thin line of water. A puzzle needs its background to SHUT
    // UP; the sky already carries the whole Enlightenment mood, so the stage's only
    // job is to give the board a floor to be above.
    void BuildObservatoryStage()
    {
        var yard = new GameObject("Observatory").transform;
        yard.SetParent(_root, false);

        float span   = Mathf.Max(_w, Mathf.Max(_h, _d)) * Pitch * Cell;
        float floorY = -((_h - 1) * Pitch * 0.5f + Pitch) * Cell;

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        floor.name = "Plaza";
        floor.transform.SetParent(yard, false);
        floor.transform.localPosition = new Vector3(0f, floorY, 0f);
        floor.transform.localScale    = new Vector3(span * 1.35f, 0.2f, span * 1.35f);
        Destroy(floor.GetComponent<Collider>());
        MpbColor.Set(floor.GetComponent<Renderer>(), StoneDark);

        // One brass rim, and only one. It reads as the edge of an instrument's dial
        // and gives the eye a fixed horizontal to judge the board against while
        // orbiting — which is the single thing the dressing has to earn.
        var rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        rim.name = "Rim";
        rim.transform.SetParent(yard, false);
        rim.transform.localPosition = new Vector3(0f, floorY + 0.11f, 0f);
        rim.transform.localScale    = new Vector3(span * 1.30f, 0.02f, span * 1.30f);
        Destroy(rim.GetComponent<Collider>());
        MpbColor.Set(rim.GetComponent<Renderer>(), BrassCol);
    }

    // ═════════════════════════════════════════════════════════════════════════
    // HUD
    // ═════════════════════════════════════════════════════════════════════════

    float _flashUntil;

    void Flash(string msg)
    {
        if (_reasonText == null) return;
        _reasonText.gameObject.SetActive(true);
        _reasonText.text = msg;
        _flashUntil = Time.unscaledTime + 1.4f;
        CancelInvoke(nameof(ClearFlash));
        Invoke(nameof(ClearFlash), 1.4f);
    }

    void ClearFlash()
    {
        if (_reasonText != null && Time.unscaledTime >= _flashUntil - 0.01f)
            _reasonText.gameObject.SetActive(false);
    }

    void RefreshHud()
    {
        if (_hudText == null) return;
        int best = _scoreId != null ? SaveSystem.Profile.GetMinigameBest(_scoreId) : 0;
        string bestLine = best > 0 ? $"   ·   best {best}" : "";
        // Moves against par, not a countdown: par is what the board actually cost to
        // scramble, so it is a promise the puzzle can keep.
        // Said as REMAINING. "12 / 18 used" makes the player do the subtraction
        // the counter exists to save them.
        int left = Mathf.Max(0, _budget - _movesUsed);
        string sel = _selected == null ? "click a pipe to select it"
                   : _selected.locked  ? "riveted — pick another"
                                       : "1 2 3 to turn X Y Z";
        _hudText.text = $"ROUND {_round + 1}   ·   {left} TURNS LEFT\n<size=42%>par {_par}   ·   used {_movesUsed}{bestLine}\n{sel}</size>";
    }

    void GameOver(string reason)
    {
        if (_gameOver) return;
        _gameOver = true;

        if (_scoreId != null)
        {
            _newRecord = SaveSystem.Profile.RecordMinigameScore(_scoreId, _round);
            if (_newRecord) SaveSystem.Save();
        }
        int best = _scoreId != null ? SaveSystem.Profile.GetMinigameBest(_scoreId) : 0;
        string line = _newRecord ? "NEW RECORD" : (best > 0 ? $"best {best}" : "");

        SetOverColumns(
            left:  $"THE CLOCK\n<size=60%>{_round} cleared\n<size=80%>R to retry</size></size>",
            right: $"STOPS\n<size=60%>{line}\n<size=80%>Esc to leave</size></size>");

        if (_reasonText != null) { _reasonText.gameObject.SetActive(true); _reasonText.text = reason; }
    }

    void Restart()
    {
        _gameOver = false; _newRecord = false; _decoys = 0;
        SetOverColumns(null, null);
        if (_reasonText != null) _reasonText.gameObject.SetActive(false);
        StartRound(0);
    }

    const float OverGap = 400f;

    void BuildUI()
    {
        var canvasGo = new GameObject("ClepsydraCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 60;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        _hudText = NewText("Hud", canvasGo.transform, 40f, GeoPalette.Paper, TextAlignmentOptions.TopLeft);
        Anchor(_hudText.rectTransform, new Vector2(0f, 1f), new Vector2(60f, -46f), new Vector2(760f, 150f));

        // Pivoted on their INNER edges, so the space between the columns is exactly
        // OverGap instead of whatever falls out of two overlapping centred boxes.
        _overLeft = NewText("OverL", canvasGo.transform, 84f, Color.white, TextAlignmentOptions.TopRight);
        Anchor(_overLeft.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-OverGap * 0.5f, 150f),
               new Vector2(640f, 300f), new Vector2(1f, 1f));

        _overRight = NewText("OverR", canvasGo.transform, 84f, Color.white, TextAlignmentOptions.TopLeft);
        Anchor(_overRight.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(OverGap * 0.5f, 150f),
               new Vector2(640f, 300f), new Vector2(0f, 1f));

        _reasonText = NewText("Reason", canvasGo.transform, 26f, GeoPalette.Gold, TextAlignmentOptions.Center);
        Anchor(_reasonText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -200f),
               new Vector2(900f, 40f), new Vector2(0.5f, 0.5f));
        _reasonText.gameObject.SetActive(false);

        var help = NewText("Help", canvasGo.transform, 20f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.55f),
                           TextAlignmentOptions.BottomLeft);
        Anchor(help.rectTransform, new Vector2(0f, 0f), new Vector2(60f, 40f), new Vector2(1500f, 84f));
        help.text = "Click / select     ·     1 2 3 / turn X Y Z     ·     WASD QE / move camera\nZ X / layer     ·     Right-drag / orbit     ·     Scroll / zoom";

        SetOverColumns(null, null);
    }

    void SetOverColumns(string left, string right)
    {
        if (_overLeft  != null) { _overLeft.text  = left  ?? ""; }
        if (_overRight != null) { _overRight.text = right ?? ""; }
    }

    // Pivot defaults to the ANCHOR, not to the centre. Pinning a wide box to a corner
    // while pivoting it at its middle pushes half of it off the screen — which is what
    // hid the move counter and threw the settlement columns across each other.
    static void Anchor(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size, Vector2? pivot = null)
    {
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot ?? anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.alignment = align;
        t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false;
        return t;
    }

    // Click target on each piece part. A component rather than a per-frame raycast in
    // Update, so the pipe geometry itself decides what is clickable and there is no
    // second copy of the hit test to keep in step with the visuals.
    class PipeClick : MonoBehaviour
    {
        Clepsydra _game;
        Piece     _piece;

        internal void Init(Clepsydra game, Piece piece) { _game = game; _piece = piece; }

        void OnMouseDown()
        {
            if (_game != null && _game._cam != null) _game.ClickPiece(_piece);
        }
    }
}
