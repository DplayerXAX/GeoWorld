using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Balance Tower - a set of scales you build on.
//
// A small pan holds a tower you extend one block at a time. Placing a block WELDS it
// to the pan and to its neighbours, so the whole tower is a single rigid body - you
// can hang it out past the pan's edge and it stays put as a cantilever. What you are
// managing is the torque that cantilever puts on the pan.
//
// The mechanics are real, not modelled. The pan is one Rigidbody and every landed
// block adds a collider to it, so PhysX derives the centre of mass and the inertia
// tensor from the actual union of blocks - there is no hand-written mass model that
// can disagree with what's on screen. A ConfigurableJoint pins the pan at its top
// face and a drive spring pulls it back level, making it a spring-loaded balance pan:
// gravity acts at the true centre of mass, its torque about the anchor is real, and
// the spring is what that torque is fighting. Lean settles at torque/stiffness, which
// is why light blocks buy you more tower before the pan gives.
//
// Welding is what makes physics work here where a loose stack didn't. Boxes resting
// on each other fail through friction and micro-contacts - unreadable, unplannable,
// and the collapse looks like the engine's opinion rather than a consequence. One
// welded body has exactly one thing going on: weight, offset from a pivot. Overhang
// becomes the interesting move and counterweighting it is the answer.
//
// The held piece and its preview are children of the pan, so the placement lattice
// tips with it. Building on a leaning tower while it leans is the game.
public class BalanceTower : MonoBehaviour
{
    public static bool Active { get; private set; }

    // Flat shapes, matching Stack Well's table — the same vocabulary of blocks, so
    // the two games read as one world's toys.
    static readonly Vector3Int[][] Shapes =
    {
        new[] { V(0,0,0) },
        new[] { V(0,0,0), V(1,0,0) },
        new[] { V(0,0,0), V(1,0,0), V(2,0,0) },
        new[] { V(0,0,0), V(1,0,0), V(1,0,1) },
        new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(2,0,1) },
        new[] { V(0,0,0), V(1,0,0), V(2,0,0), V(1,0,1) },
        new[] { V(0,0,0), V(1,0,0), V(1,0,1), V(2,0,1) },
        new[] { V(0,0,0), V(1,0,0), V(0,0,1), V(1,0,1) },
    };

    static Vector3Int V(int x, int y, int z) => new(x, y, z);

    static readonly Color[] Palette =
    {
        new(0.886f, 0.141f, 0.106f),
        new(0.910f, 0.698f, 0.227f),
        new(0.169f, 0.424f, 0.690f),
        new(0.298f, 0.686f, 0.314f),
        new(0.72f,  0.36f,  0.80f),
        new(0.20f,  0.72f,  0.72f),
    };

    // ── Stage ────────────────────────────────────────────────────────────────
    // Parked far from the host scene; nothing here shares space with the map.
    static readonly Vector3 StageOrigin = new(0f, 6000f, 0f);

    const float Cell = 1f;

    // Pedestal: 3×3 cells. Small on purpose — a wide base would let the player build
    // for a long time before balance mattered at all, and balance IS the game.
    const int   BaseHalfCells = 1;
    const float BaseHeight    = 0.5f;

    // Pan tilt past this and the weld gives - the tower comes apart and falls for
    // real. A limit rather than a physical tipping point because a welded body on a
    // spring never actually goes over on its own: the spring would hold it at some
    // absurd angle forever. This is the pan's structure failing under the torque.
    const float MaxTilt = 24f;

    // -- The feel knobs -------------------------------------------------------
    // Per-block mass, light on purpose: mass is the numerator of the lean and the
    // spring is the denominator, so this is directly "how much lean does one block
    // out at arm's length buy". Turn it down for a more forgiving tower.
    const float MassPerBlock = 0.34f;

    // The pan's COLLIDER is a thin slab even though it LOOKS BaseHeight tall. PhysX
    // spreads a body's mass across its colliders by volume, so this number is the
    // empty pan's own centring weight - how much it shrugs off the first overhang
    // before any lean shows up at all.
    const float PanColliderThickness = 0.9f;

    // The level spring. Stiffer = less lean per block and a snappier settle; more
    // damping = it settles instead of bobbing. These, plus MassPerBlock, are what you
    // tune if the balance feels wrong - nothing else in here needs touching.
    const float LevelSpring = 120f;
    const float LevelDamper = 30f;
    const float PanDamping  = 1.4f;

    // ── Order workshop dressing ──────────────────────────────────────────────
    // Brass, steel and oil. Deliberately narrow in hue: a machine room reads as one
    // material lit from one place, and the pan's own gold rim is the only warm accent
    // that should catch the eye.
    static readonly Color FogColor   = new(0.20f, 0.23f, 0.27f);
    static readonly Color SteelDark  = new(0.13f, 0.145f, 0.17f);
    static readonly Color SteelMid   = new(0.26f, 0.28f, 0.32f);
    static readonly Color Brass      = new(0.62f, 0.47f, 0.21f);
    static readonly Color BrassDim   = new(0.38f, 0.30f, 0.16f);

    const int   GearRing    = 7;     // gears standing around the pit
    const float GearRadiusIn  = 11f;
    const float GearRadiusOut = 20f;

    const float SpawnAbove   = 6f;    // cells above the tower top a piece appears
    const float DescendSpeed = 1.1f;  // cells/sec it sinks unaided
    const float SlamSpeed    = 14f;   // Space
    const float GroundDrop   = 9f;    // cells below the pan where debris lands
    const float ToppleTime   = 2.2f;  // how long the collapse plays before the card

    // ── Launch / teardown ────────────────────────────────────────────────────

    public static void Launch(GameObject cubePrefab, string scoreId = null)
    {
        if (Active) return;
        var go = new GameObject("BalanceTower");
        var g  = go.AddComponent<BalanceTower>();
        g._cubePrefab = cubePrefab;
        g._scoreId    = scoreId;
        g.Begin();
    }

    GameObject _cubePrefab;
    string     _scoreId;

    readonly MinigameStage _stage = new();

    Camera    _cam;
    Transform _root;    // stage root, never moves
    Transform _pivot;   // the pan: Rigidbody + joint. Pedestal AND tower hang off it, so both tip
    Transform _tower;   // everything placed, plus the held piece
    Transform _debris;  // where blocks go once the weld gives

    Rigidbody         _body;
    ConfigurableJoint _joint;
    Canvas    _canvas;
    TMP_Text  _scoreText, _overLeft, _overRight;

    // Occupied cells in TOWER space. y = 0 is the layer resting on the pedestal.
    readonly HashSet<Vector3Int> _filled = new();

    Transform    _held;
    // The held shape as INTEGER cell offsets, already rotated and normalised so its
    // minimum corner is (0,0,0). Rotation is baked into these rather than kept as a
    // quaternion on the transform: the visual used to be placed from fractional
    // offsets while occupancy was computed from rounded ones, so the two disagreed
    // the moment a shape had an even span (a 2-cell piece could round both cells to
    // the same integer and occupy one). Cells ARE the shape now — there's only one
    // description of it left to get wrong.
    Vector3Int[] _heldCells;
    Vector3Int   _heldCell;       // where the shape's origin sits
    float        _heldY;          // continuous height in cells, tower space
    Color        _heldColor;

    Transform _preview;

    int     _score;
    bool    _gameOver, _newRecord;
    bool    _collapsing;
    float   _collapseT;
    int     _shownLean = -1;               // last lean % drawn, so the HUD rebuilds only on change
    float   _camYaw = 35f, _camPitch = 16f;

    void Begin()
    {
        Active = true;
        BuildCamera();
        BuildStage();
        BuildUI();
        _stage.SuppressHostUI(transform);
        _stage.PauseHostMusic();
        PlayMusic();
        SpawnPiece();
    }


    // A minigame can also go away WITHOUT Quit running — a scene load, or the object
    // being destroyed by something else. Host music left paused across that is an
    // instance nobody owns any more, and the next thing to stop it stops a paused
    // segment, which is what the music engine asserts on. Safe after Quit too:
    // ResumeHostMusic clears its own list, so the second call does nothing.
    void OnDestroy()
    {
        Active = false;
        StopMusic();
        _stage.ResumeHostMusic();
    }

    void Quit()
    {
        Active = false;
        StopMusic();
        _stage.Restore();
        if (_cam != null) Destroy(_cam.gameObject);
        Destroy(gameObject);
    }

    // ── Music ────────────────────────────────────────────────────────────────

    uint _musicPlayingId;

    void PlayMusic()
    {
        var cfg = MinigameAudio.Get();
        var evt = cfg != null ? cfg.balanceTowerMusic : null;
        if (evt == null || !evt.IsValid())
        {
            Debug.LogWarning("[BalanceTower] balanceTowerMusic not assigned on MinigameAudio.asset — nothing to play.");
            return;
        }
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

    // ── Loop ─────────────────────────────────────────────────────────────────

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Quit(); return; }
        HandleCameraDrag();

        if (_collapsing) { UpdateCollapse(); return; }

        if (_gameOver)
        {
            if (Input.GetKeyDown(KeyCode.R)) Restart();
            return;
        }

        if (_held != null)
        {
            HandleSteer();
            Descend();
            UpdatePreview();
        }
        CheckBalance();
    }

    // ── Balance ────────────────────────────────────────────────────────────────────────

    // The pan's collider volume, in cells. Mass is handed to PhysX as a total and
    // spread across colliders by volume, so scaling that total with the block count
    // is what keeps every block worth the same weight as it goes on.
    float PanCellVolume
    {
        get { float side = (BaseHalfCells * 2 + 1) * Cell; return side * side * PanColliderThickness; }
    }

    void RefreshMass()
    {
        if (_body == null) return;
        _body.mass = MassPerBlock * (PanCellVolume + _filled.Count);
        // The union of colliders changed, so the centre of mass and inertia tensor
        // PhysX cached are stale - and those two ARE the simulation here.
        _body.ResetCenterOfMass();
        _body.ResetInertiaTensor();
    }

    /// <summary>The pan's real lean in degrees. Twist is joint-locked, so this is pure tip.</summary>
    float Tilt => _pivot != null ? Quaternion.Angle(Quaternion.identity, _pivot.localRotation) : 0f;

    void CheckBalance()
    {
        float tilt = Tilt;

        int lean = Mathf.RoundToInt(Mathf.Clamp01(tilt / MaxTilt) * 100f);
        if (lean != _shownLean) { _shownLean = lean; RefreshScore(); }

        if (tilt > MaxTilt) Collapse();
    }

    // The weld gives. Every block becomes its own body and gravity takes it from
    // there - the collapse is fully simulated even though the lean that caused it was
    // a spring, because past this point there is nothing left worth keeping legible.
    void Collapse()
    {
        if (_collapsing || _gameOver) return;
        _collapsing = true;
        _collapseT  = 0f;

        if (_held != null)    { Destroy(_held.gameObject);    _held = null; }
        if (_preview != null) { Destroy(_preview.gameObject); _preview = null; }

        // Velocities are sampled BEFORE the joint goes, while the pan is still one
        // body - so each block launches with the motion the player could already see
        // rather than dropping from a standstill.
        var boxes = _tower.GetComponentsInChildren<BoxCollider>();
        var loose = new List<Transform>();
        var vels  = new List<Vector3>();
        foreach (var b in boxes)
        {
            loose.Add(b.transform);
            vels.Add(_body.GetPointVelocity(b.transform.position));
        }
        Vector3 spin = _body.angularVelocity;

        if (_joint != null) { Destroy(_joint); _joint = null; }   // the pan drops too

        for (int k = 0; k < loose.Count; k++)
        {
            loose[k].SetParent(_debris, true);
            var rb = loose[k].gameObject.AddComponent<Rigidbody>();
            rb.mass            = MassPerBlock;
            rb.linearVelocity  = vels[k];
            rb.angularVelocity = spin;
        }
        RefreshMass();   // the pan is on its own now
    }

    void UpdateCollapse()
    {
        // Scaled time: this is watching physics, not driving UI.
        _collapseT += Time.deltaTime;
        if (_collapseT < ToppleTime) return;
        _collapsing = false;
        GameOver("The tower went over");
    }

    // ── Steering ─────────────────────────────────────────────────────────────

    // Grid movement, one cell per press, along whichever axis the camera is closest
    // to facing — the same scheme Stack Well uses.
    void HandleSteer()
    {
        if (Input.GetKeyDown(KeyCode.D)) Step(CamRight());
        if (Input.GetKeyDown(KeyCode.A)) Step(-CamRight());
        if (Input.GetKeyDown(KeyCode.W)) Step(CamForward());
        if (Input.GetKeyDown(KeyCode.S)) Step(-CamForward());

        if (Input.GetKeyDown(KeyCode.Alpha1)) Rotate(Quaternion.Euler(90f, 0f, 0f));
        if (Input.GetKeyDown(KeyCode.Alpha2)) Rotate(Quaternion.Euler(0f, 90f, 0f));
        if (Input.GetKeyDown(KeyCode.Alpha3)) Rotate(Quaternion.Euler(0f, 0f, 90f));
    }

    // NOT clamped to the pedestal. Building out past the edge is the whole point —
    // the only thing stopping you is what it does to the balance.
    void Step(Vector3Int dir)
    {
        _heldCell += dir;
        ApplyHeld();
    }

    // 90° turns map integer cells to integer cells exactly, so this stays on the
    // lattice no matter how many times it's applied.
    void Rotate(Quaternion delta)
    {
        var rotated = new Vector3Int[_heldCells.Length];
        for (int i = 0; i < _heldCells.Length; i++)
            rotated[i] = Vector3Int.RoundToInt(delta * (Vector3)_heldCells[i]);

        _heldCells = Normalise(rotated);
        BuildHeldVisual();
        BuildPreview();
        ApplyHeld();
    }

    // Shifts a shape so its minimum corner is the origin — without this a rotation
    // would also translate the piece, drifting it across the tower each turn.
    static Vector3Int[] Normalise(Vector3Int[] cells)
    {
        var min = cells[0];
        foreach (var c in cells) min = Vector3Int.Min(min, c);
        var r = new Vector3Int[cells.Length];
        for (int i = 0; i < cells.Length; i++) r[i] = cells[i] - min;
        return r;
    }

    // Camera axes are read in TOWER space, so a leaning tower still moves the piece
    // along its own grid — otherwise "right" would drift off the lattice the blocks
    // actually sit on.
    Vector3Int CamRight()   => SnapAxis(_tower.InverseTransformDirection(_cam.transform.right));
    Vector3Int CamForward() => SnapAxis(_tower.InverseTransformDirection(_cam.transform.forward));

    static Vector3Int SnapAxis(Vector3 v)
    {
        v.y = 0f;
        return Mathf.Abs(v.x) >= Mathf.Abs(v.z)
            ? new Vector3Int(v.x >= 0f ? 1 : -1, 0, 0)
            : new Vector3Int(0, 0, v.z >= 0f ? 1 : -1);
    }

    void ApplyHeld()
    {
        if (_held == null) return;
        _held.localPosition = new Vector3(_heldCell.x * Cell, _heldY * Cell, _heldCell.z * Cell);
    }

    // ── Descent / landing ────────────────────────────────────────────────────

    void Descend()
    {
        // Scaled time, unlike the rest of the overlay: the descent now shares a clock
        // with the physics it is about to land on, so a pause freezes both together
        // instead of sinking the piece into a frozen tower.
        float speed = Input.GetKey(KeyCode.Space) ? SlamSpeed : DescendSpeed;
        _heldY -= speed * Time.deltaTime;
        ApplyHeld();

        int landing = LandingLayer();
        if (_heldY <= landing) { _heldY = landing; ApplyHeld(); Land(landing); }
    }

    // Lowest layer the piece can occupy: one above the highest filled cell under any
    // of its own cells, or 0 (resting on the pedestal) when there's nothing below.
    //
    // A cell with nothing under it does NOT let the piece keep falling — blocks stick
    // to each other, so an overhanging cell is a cantilever, not a hole. That is what
    // lets the tower grow sideways, and therefore what creates the balance problem.
    int LandingLayer()
    {
        int layer = 0;
        foreach (var c in _heldCells)
        {
            int top = -1;
            foreach (var f in _filled)
                if (f.x == _heldCell.x + c.x && f.z == _heldCell.z + c.z && f.y > top) top = f.y;
            layer = Mathf.Max(layer, top + 1 - c.y);
        }
        return layer;
    }

    void Land(int layer)
    {
        foreach (var c in _heldCells)
            _filled.Add(new Vector3Int(_heldCell.x + c.x, layer + c.y, _heldCell.z + c.z));

        // The weld: each cube gains a collider and so joins the pan's compound
        // Rigidbody. That is what "glued to the base and to the other blocks" means
        // mechanically — one rigid body, which gives PhysX a real shape to weigh
        // instead of a pile of boxes leaning on each other's friction. Colliders in
        // the same body never collide with each other, so nothing to resolve either.
        foreach (Transform cube in _held)
        {
            var box = cube.gameObject.AddComponent<BoxCollider>();
            box.size = Vector3.one;      // local: the cube's own scale is one cell
        }
        _held.name = $"Block{_score}";
        _held = null;
        RefreshMass();
        if (_preview != null) { Destroy(_preview.gameObject); _preview = null; }

        _score++;
        RefreshScore();

        // Physics re-weighs the tower on the next step, so the consequence of the
        // placement starts showing before the next piece has come down.
        SpawnPiece();
    }

    // ── Pieces ───────────────────────────────────────────────────────────────

    void SpawnPiece()
    {
        _heldCells = Normalise(Shapes[Random.Range(0, Shapes.Length)]);
        _heldColor = Palette[Random.Range(0, Palette.Length)];
        // Spawns roughly over the pedestal rather than at its corner, so the first
        // move isn't spent walking the piece into reach.
        _heldCell  = new Vector3Int(-BaseHalfCells, 0, -BaseHalfCells);

        BuildHeldVisual();
        _heldY = TowerTop() + SpawnAbove;
        ApplyHeld();
        BuildPreview();
    }

    // Cubes sit at their whole-cell offsets — exactly the offsets occupancy uses, so
    // what you see and what gets written to _filled cannot drift apart.
    void BuildHeldVisual()
    {
        if (_held != null) Destroy(_held.gameObject);

        var go = new GameObject("Held");
        go.transform.SetParent(_tower, false);

        foreach (var c in _heldCells)
        {
            var cube = _cubePrefab != null ? Instantiate(_cubePrefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(go.transform, false);
            cube.transform.localPosition = (Vector3)c * Cell;
            cube.transform.localScale    = Vector3.one * Cell;
            // Nothing here is simulated, so colliders would only cost raycast time
            // and risk interacting with the host scene.
            foreach (var col in cube.GetComponentsInChildren<Collider>()) Destroy(col);
            foreach (var r in cube.GetComponentsInChildren<Renderer>()) MpbColor.Set(r, _heldColor);
        }
        _held = go.transform;
    }

    int TowerTop()
    {
        int top = 0;
        foreach (var c in _filled) top = Mathf.Max(top, c.y + 1);
        return top;
    }

    // ── Landing preview ──────────────────────────────────────────────────────

    void UpdatePreview()
    {
        if (_preview == null) return;
        int layer = LandingLayer();
        _preview.localPosition = new Vector3(_heldCell.x * Cell, layer * Cell, _heldCell.z * Cell);
    }

    void BuildPreview()
    {
        if (_preview != null) Destroy(_preview.gameObject);

        var go = new GameObject("Preview");
        go.transform.SetParent(_tower, false);

        var tint = new Color(_heldColor.r, _heldColor.g, _heldColor.b, 0.32f);
        foreach (var c in _heldCells)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(go.transform, false);
            cube.transform.localPosition = (Vector3)c * Cell;
            cube.transform.localScale    = Vector3.one * (Cell * 0.9f);
            Destroy(cube.GetComponent<Collider>());

            var r = cube.GetComponent<Renderer>();
            r.sharedMaterial    = PreviewMaterial();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows    = false;
            MpbColor.Set(r, tint);
        }
        _preview = go.transform;
    }

    static Material _previewMat;
    static Material PreviewMaterial()
    {
        if (_previewMat != null) return _previewMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _previewMat = new Material(sh) { name = "BalanceTowerPreview" };
        if (_previewMat.HasProperty("_Surface"))
        {
            _previewMat.SetFloat("_Surface", 1f);
            _previewMat.SetFloat("_ZWrite", 0f);
            _previewMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _previewMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _previewMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _previewMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _previewMat;
    }

    // ── Stage build ──────────────────────────────────────────────────────────

    void BuildStage()
    {
        BuildPan();
        // The Order hall, not the farm dusk the Stacking Well sits in — same banded
        // flat-ink construction, different room.
        _stage.SetSkybox(Resources.Load<Material>("GeoWorldShaderKeepalive/OrderHallSkybox_keep"));
        // Cool oiled steel instead of the warm dusk this started with. 1-2's map decor
        // is the Order workshop, and a scale you are told is a machine should be
        // standing in the same building as the gears outside.
        _stage.SetLinearFog(FogColor, 30f, 190f);
    }

    // Everything physical, rebuilt wholesale on restart — a collapsed pan has loose
    // bodies, a dead joint and a stale inertia tensor scattered through it, and
    // unpicking that is more error-prone than starting over.
    void BuildPan()
    {
        // Parented to us so Quit() takes the whole stage with it. Unparented it used
        // to just leak, which was invisible when nothing here was simulated — a leaked
        // pan with a live joint and a dozen rigidbodies keeps stepping physics forever.
        _root = new GameObject("Stage").transform;
        _root.SetParent(transform, false);
        _root.position = StageOrigin;

        float side = (BaseHalfCells * 2 + 1) * Cell;

        // Somewhere for the debris to land, and it gives the pan a floor to read
        // against instead of hanging in fog.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.SetParent(_root, false);
        ground.transform.localPosition = new Vector3(0f, -GroundDrop, 0f);
        ground.transform.localScale    = new Vector3(46f, 1f, 46f);
        MpbColor.Set(ground.GetComponent<Renderer>(), SteelDark);

        BuildWorkshop();

        _debris = new GameObject("Debris").transform;
        _debris.SetParent(_root, false);

        // The pan's transform origin IS the hinge: the pedestal's top face, where the
        // tower actually rests. Pivoting at the body's own centre would sink half the
        // tower into the pedestal as it leaned.
        _pivot = new GameObject("Pan").transform;
        _pivot.SetParent(_root, false);
        _pivot.localPosition = new Vector3(0f, -Cell * 0.5f, 0f);

        _body = _pivot.gameObject.AddComponent<Rigidbody>();
        _body.angularDamping        = PanDamping;
        _body.maxAngularVelocity    = 20f;                  // default 7 rad/s throttles the collapse
        _body.interpolation         = RigidbodyInterpolation.Interpolate;
        // Constraints are deliberately left at None. RigidbodyConstraints freeze
        // velocity about the body's CENTRE OF MASS, which climbs as the tower grows;
        // the joint anchors a POINT instead, which is what a hinge actually is. Using
        // both makes them fight each other and the pan jitters.
        _body.constraints = RigidbodyConstraints.None;

        // The pedestal's collider is a thin slab at the top face even though the
        // visual below is BaseHeight tall — see PanColliderThickness. It's the pan's
        // centring weight, not its shape.
        var slab = _pivot.gameObject.AddComponent<BoxCollider>();
        slab.center = new Vector3(0f, -PanColliderThickness * 0.5f, 0f);
        slab.size   = new Vector3(side, PanColliderThickness, side);

        // A 2-DOF spring gimbal anchored to the world. Twist about the pan's vertical
        // is locked (a spinning pan means nothing here); swing in any direction is
        // free, and angularYZDrive is the spring that pulls it back level. Gravity
        // still acts at the true centre of mass, so the torque it makes about this
        // anchor is real — the spring is the only thing that isn't measured.
        _joint = _pivot.gameObject.AddComponent<ConfigurableJoint>();
        _joint.autoConfigureConnectedAnchor = false;
        _joint.connectedBody   = null;              // anchored to the world
        _joint.anchor          = Vector3.zero;      // the pedestal's top face
        _joint.connectedAnchor = _pivot.position;
        _joint.axis            = Vector3.up;        // twist axis = the pan's vertical
        _joint.secondaryAxis   = Vector3.forward;
        _joint.xMotion = _joint.yMotion = _joint.zMotion = ConfigurableJointMotion.Locked;
        _joint.angularXMotion = ConfigurableJointMotion.Locked;   // no yaw
        _joint.angularYMotion = ConfigurableJointMotion.Free;     // tip...
        _joint.angularZMotion = ConfigurableJointMotion.Free;     // ...any direction
        _joint.rotationDriveMode = RotationDriveMode.XYAndZ;
        _joint.angularYZDrive = new JointDrive
        {
            positionSpring = LevelSpring,
            positionDamper = LevelDamper,
            maximumForce   = float.MaxValue,
        };
        _joint.targetRotation = Quaternion.identity;              // rest = level

        // Visual pedestal — no collider of its own; the slab above is the physical one.
        var plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plinth.name = "Plinth";
        plinth.transform.SetParent(_pivot, false);
        plinth.transform.localPosition = new Vector3(0f, -BaseHeight * 0.5f, 0f);
        plinth.transform.localScale    = new Vector3(side, BaseHeight, side);
        Destroy(plinth.GetComponent<Collider>());
        MpbColor.Set(plinth.GetComponent<Renderer>(), GeoPalette.Ink);

        var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rim.name = "Rim";
        rim.transform.SetParent(_pivot, false);
        rim.transform.localPosition = new Vector3(0f, -BaseHeight - 0.03f, 0f);
        rim.transform.localScale    = new Vector3(side * 1.25f, 0.06f, side * 1.25f);
        Destroy(rim.GetComponent<Collider>());
        MpbColor.Set(rim.GetComponent<Renderer>(), GeoPalette.Gold);

        _tower = new GameObject("Tower").transform;
        _tower.SetParent(_pivot, false);
        _tower.localPosition = new Vector3(0f, Cell * 0.5f, 0f);

        RefreshMass();
    }

    // ── Order workshop backdrop ──────────────────────────────────────────────
    //
    // Everything here is scenery: no colliders, no physics, nothing the balance can
    // touch. It is parented under _root so a restart rebuilds it with the pan, and so
    // Quit takes it with everything else.
    //
    // The gears share GearMeshFactory with the map's Order decor and with the Order
    // synergy VFX. One gear mesh across all three is why this reads as the same
    // faction rather than as three different people's idea of a cog.
    void BuildWorkshop()
    {
        var shop = new GameObject("Workshop").transform;
        shop.SetParent(_root, false);

        // A ring of gears standing in the pit, turning. Alternating direction, and
        // speed inversely proportional to radius — the same rule the map's workshop
        // uses, because a train of gears that all spin the same way at the same rate
        // is the one thing that reads instantly as fake.
        for (int i = 0; i < GearRing; i++)
        {
            float t   = i / (float)GearRing;
            float ang = t * Mathf.PI * 2f;
            float rad = Mathf.Lerp(GearRadiusIn, GearRadiusOut, (i * 0.37f) % 1f);
            float size = Mathf.Lerp(2.6f, 5.4f, (i * 0.61f) % 1f);

            var go = new GameObject($"Gear{i}");
            go.transform.SetParent(shop, false);
            go.transform.localPosition = new Vector3(Mathf.Cos(ang) * rad,
                                                     -GroundDrop + 0.55f + size * 0.5f,
                                                     Mathf.Sin(ang) * rad);
            // Standing on edge, facing the pan, so the teeth are seen side-on.
            go.transform.localRotation = Quaternion.Euler(90f, -ang * Mathf.Rad2Deg, 0f);
            go.transform.localScale    = Vector3.one * size;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = GearMeshFactory.Get(i % 2 == 0 ? 12 : 10);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = SceneryMaterial();
            MpbColor.Set(mr, i % 2 == 0 ? BrassDim : SteelMid);

            var spin = go.AddComponent<WorkshopSpin>();
            spin.degreesPerSecond = (i % 2 == 0 ? 1f : -1f) * (26f / size);
        }

        // Columns behind the gears. Plain boxes on purpose — they are there to give
        // the fog something to swallow, and anything more detailed at that distance
        // just competes with the tower.
        for (int i = 0; i < 10; i++)
        {
            float ang = (i / 10f) * Mathf.PI * 2f + 0.31f;
            float rad = 26f;
            var col = GameObject.CreatePrimitive(PrimitiveType.Cube);
            col.name = $"Column{i}";
            col.transform.SetParent(shop, false);
            col.transform.localPosition = new Vector3(Mathf.Cos(ang) * rad, -GroundDrop + 9f, Mathf.Sin(ang) * rad);
            col.transform.localScale    = new Vector3(2.2f, 20f, 2.2f);
            col.transform.localRotation = Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f);
            Destroy(col.GetComponent<Collider>());   // debris must never land on scenery
            MpbColor.Set(col.GetComponent<Renderer>(), SteelDark);
        }

        // A brass ring set into the floor around the pan — it puts the pedestal on a
        // mounting rather than on an empty plate, and gives the eye a fixed horizontal
        // to read the tower's lean against.
        for (int i = 0; i < 24; i++)
        {
            float ang = (i / 24f) * Mathf.PI * 2f;
            var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
            seg.name = "Rail";
            seg.transform.SetParent(shop, false);
            seg.transform.localPosition = new Vector3(Mathf.Cos(ang) * 6.5f, -GroundDrop + 0.55f, Mathf.Sin(ang) * 6.5f);
            seg.transform.localRotation = Quaternion.Euler(0f, -ang * Mathf.Rad2Deg, 0f);
            seg.transform.localScale    = new Vector3(1.5f, 0.22f, 0.5f);
            Destroy(seg.GetComponent<Collider>());
            MpbColor.Set(seg.GetComponent<Renderer>(), Brass);
        }
    }

    // Shared unlit-lit material for the scenery meshes, so a URP build does not hand
    // them the built-in Default-Material it never shipped.
    static Material _sceneryMat;
    static Material SceneryMaterial()
    {
        if (_sceneryMat != null) return _sceneryMat;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _sceneryMat = new Material(sh) { name = "BalanceTowerScenery" };
        return _sceneryMat;
    }

    void BuildCamera()
    {
        var go = new GameObject("BalanceTowerCamera");
        _cam = go.AddComponent<Camera>();
        _cam.clearFlags  = CameraClearFlags.Skybox;
        _cam.depth       = 50f;
        _cam.fieldOfView = 55f;
        PlaceCamera(snap: true);
    }

    // How fast the framing chases the tower's height. Both the focus point and the
    // pull-back are derived from it, so easing this ONE number keeps them consistent —
    // easing the camera position instead would let the aim and the distance disagree
    // mid-move and swing the horizon.
    const float FrameEase = 4f;

    float _shownTop = -1f;   // < 0 = not framed yet, snap on the next pass

    void PlaceCamera(bool snap = false)
    {
        if (_cam == null) return;

        // TowerTop() steps by a whole cell the instant a piece lands, so reading it
        // straight put a hard cut in the framing on every placement. Eased, the same
        // step reads as the camera giving the tower room.
        float top = TowerTop();
        if (snap || _shownTop < 0f) _shownTop = top;
        else _shownTop = Mathf.Lerp(_shownTop, top, 1f - Mathf.Exp(-FrameEase * Time.unscaledDeltaTime));

        Vector3 focus = StageOrigin + Vector3.up * (_shownTop * 0.5f * Cell + 1.2f);
        var rot = Quaternion.Euler(_camPitch, _camYaw, 0f);
        _cam.transform.position = focus + rot * new Vector3(0f, 0f, -(10f + _shownTop * 0.7f));
        _cam.transform.LookAt(focus);
    }

    void LateUpdate() => PlaceCamera();

    void HandleCameraDrag()
    {
        if (!Input.GetMouseButton(1)) return;
        _camYaw  += Input.GetAxis("Mouse X") * 180f * Time.unscaledDeltaTime;
        _camPitch = Mathf.Clamp(_camPitch - Input.GetAxis("Mouse Y") * 120f * Time.unscaledDeltaTime, 2f, 70f);
    }

    // ── Score / end ──────────────────────────────────────────────────────────

    void RefreshScore()
    {
        if (_scoreText == null) return;
        int best = _scoreId != null ? SaveSystem.Profile.GetMinigameBest(_scoreId) : 0;
        string bestLine = best > 0 ? $"   ·   best {best}" : "";
        // Lean as a percentage of what the pan can take — the number the player is
        // actually playing against. Raw degrees wouldn't say how much room is left.
        int lean = Mathf.RoundToInt(Mathf.Clamp01(Tilt / MaxTilt) * 100f);
        _scoreText.text = $"{_score}\n<size=45%>stacked   ·   lean {lean}%{bestLine}</size>";
    }

    void GameOver(string reason)
    {
        if (_gameOver) return;
        _gameOver = true;

        if (_held != null)    { Destroy(_held.gameObject);    _held = null; }
        if (_preview != null) { Destroy(_preview.gameObject); _preview = null; }

        if (_scoreId != null)
        {
            _newRecord = SaveSystem.Profile.RecordMinigameScore(_scoreId, _score);
            if (_newRecord) SaveSystem.Save();
        }
        int best = _scoreId != null ? SaveSystem.Profile.GetMinigameBest(_scoreId) : 0;
        string line = _newRecord ? "NEW RECORD" : (best > 0 ? $"best {best}" : "");

        SetOverColumns(
            left:  $"TOWER\n<size=60%>{_score} stacked\n<size=80%>R to retry</size></size>",
            right: $"DOWN\n<size=60%>{line}\n<size=80%>Esc to leave</size></size>");

        if (_reasonText != null)
        {
            _reasonText.gameObject.SetActive(true);
            _reasonText.text = reason;
        }
    }

    void Restart()
    {
        _filled.Clear();
        _held = null; _preview = null;

        if (_root != null) Destroy(_root.gameObject);
        BuildPan();

        _score = 0; _gameOver = false; _newRecord = false;
        _collapsing = false; _collapseT = 0f; _shownLean = -1;
        PlaceCamera(snap: true);   // a fresh tower is a cut; easing down would read as a rewind

        SetOverColumns(null, null);
        if (_reasonText != null) _reasonText.gameObject.SetActive(false);
        RefreshScore();
        SpawnPiece();
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    const float OverGap = 400f;

    TMP_Text _reasonText;

    void BuildUI()
    {
        var canvasGo = new GameObject("BalanceTowerCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1000;
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 0.5f;

        _scoreText = NewText("Score", 40f, TextAlignmentOptions.TopLeft);
        _scoreText.color = GeoPalette.Ink;
        var srt = _scoreText.rectTransform;
        srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0f, 1f);
        srt.anchoredPosition = new Vector2(48f, -40f);
        srt.sizeDelta = new Vector2(520f, 200f);
        RefreshScore();

        var help = NewText("Help", 24f, TextAlignmentOptions.BottomLeft);
        help.color = GeoPalette.WithAlpha(GeoPalette.Paper, 0.8f);
        help.text = "WASD steer   ·   1/2/3 rotate   ·   Space drop faster\n"
                  + "Right-drag orbit";
        var hrt = help.rectTransform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 0f);
        hrt.anchoredPosition = new Vector2(48f, 36f);
        hrt.sizeDelta = new Vector2(1100f, 120f);

        BuildLeaveButton();

        _overLeft  = BuildOverColumn("OverLeft",  right: false);
        _overRight = BuildOverColumn("OverRight", right: true);

        // Why it fell, under the banner — a balance loss is only instructive if the
        // game names what went wrong.
        _reasonText = NewText("Reason", 26f, TextAlignmentOptions.Center);
        _reasonText.color = GeoPalette.Paper;
        var rrt = _reasonText.rectTransform;
        rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(0.5f, 0.5f);
        rrt.anchoredPosition = new Vector2(0f, -170f);
        rrt.sizeDelta = new Vector2(900f, 60f);
        _reasonText.gameObject.SetActive(false);
    }

    TMP_Text BuildOverColumn(string name, bool right)
    {
        var t = NewText(name, 84f, right ? TextAlignmentOptions.Left : TextAlignmentOptions.Right);
        t.color = GeoPalette.Paper;

        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot     = new Vector2(right ? 0f : 1f, 0.5f);
        rt.anchoredPosition = new Vector2(right ? OverGap : -OverGap, 0f);
        rt.sizeDelta = new Vector2(700f, 360f);

        t.gameObject.SetActive(false);
        return t;
    }

    void SetOverColumns(string left, string right)
    {
        if (_overLeft != null)
        {
            _overLeft.gameObject.SetActive(left != null);
            if (left != null) _overLeft.text = left;
        }
        if (_overRight != null)
        {
            _overRight.gameObject.SetActive(right != null);
            if (right != null) _overRight.text = right;
        }
    }

    void BuildLeaveButton()
    {
        var go = new GameObject("Leave", typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.anchoredPosition = new Vector2(-40f, 36f);
        rt.sizeDelta = new Vector2(190f, 62f);

        var img = go.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(12);
        img.type   = Image.Type.Sliced;
        img.color  = GeoPalette.WithAlpha(GeoPalette.Ink, 0.12f);

        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        var colors = btn.colors; colors.highlightedColor = GeoPalette.Gold; btn.colors = colors;
        btn.onClick.AddListener(Quit);

        var label = NewText("Label", 26f, TextAlignmentOptions.Center);
        label.transform.SetParent(rt, false);
        label.color = GeoPalette.Paper;
        label.text = "Leave  (Esc)";
        var lrt = label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;
    }

    TMP_Text NewText(string name, float size, TextAlignmentOptions align)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(_canvas.transform, false);
        var t = go.AddComponent<TextMeshProUGUI>();
        t.fontSize      = size;
        t.fontStyle     = FontStyles.Bold;
        t.alignment     = align;
        t.raycastTarget = false;
        return t;
    }
}
