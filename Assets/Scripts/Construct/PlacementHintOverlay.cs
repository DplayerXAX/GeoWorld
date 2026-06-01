using System.Collections.Generic;
using UnityEngine;

// Edit-mode placement helper overlay. Visible only while PlacementController
// is in Edit mode with a held block. Three layers, all bbox-aware:
//
//   1. XYZ axis dashed lines through cursor cell (world axes, R/G/B)
//   2. 6 movement arrows hugging the block's outer faces (WASDQE labels)
//   3. 3 rotation rings encircling the block (1/2/3 labels)
//
// Block-shape aware:
//   • Arrow positions adapt to the rotated block bbox each frame, so they
//     sit just outside whichever face you're pushing the block toward.
//   • Rotation rings rebuild radius per frame to encompass the block.
//
// "Natural" touches:
//   • Subtle bob animation per arrow (different phase) so they feel alive
//   • Slow rotation on rings
//   • Soft fade-in when entering Edit mode
//   • Letter labels rendered with a light bracketed style
//
// Drop onto any persistent GameObject. No inspector wiring needed.
public class PlacementHintOverlay : MonoBehaviour
{
    [Header("XYZ axis dashed lines")]
    public bool showAxisLines = false;
    [Min(1f)] public float axisHalfLengthCells = 4f;
    [Range(2, 16)] public int axisDashesPerSide = 6;
    [Min(0.005f)] public float axisLineWidth = 0.025f;
    [Range(0f, 1f)] public float axisAlpha = 0.4f;
    public Color axisColorX = new(1.00f, 0.30f, 0.30f);
    public Color axisColorY = new(0.35f, 0.95f, 0.40f);
    public Color axisColorZ = new(0.35f, 0.55f, 1.00f);

    [Header("Movement arrows (WASDQE)")]
    public bool showMovementArrows = true;
    [Tooltip("If false, the WASDQE letter labels are hidden (arrows still draw if showMovementArrows is true).")]
    public bool showMovementLabels = false;
    [Tooltip("Gap between the arrow and the block's outer face, in cells.")]
    [Min(0.1f)] public float arrowGap   = 0.65f;
    [Min(0.05f)] public float arrowSize = 0.20f;
    public Color arrowColor       = new(0.95f, 0.95f, 0.95f, 0.65f);
    public Color arrowFlashColor  = new(1.00f, 0.85f, 0.30f, 1.00f);
    [Min(0.05f)] public float arrowFlashDuration = 0.35f;

    [Tooltip("Hide the W/S arrows (camera forward/back) — they project as 'depth' and tend to read poorly on screen. Recommended on, especially for iso views.")]
    public bool hideForwardBackArrows = true;

    [Header("Visibility toggles")]
    [Tooltip("Press to toggle the whole hint overlay on/off persistently.")]
    public KeyCode toggleKey = KeyCode.H;
    [Tooltip("While held, the overlay's visibility is temporarily inverted: visible → hidden, hidden → visible. Useful for a quick peek either way.")]
    public bool shiftHoldInverts = true;

    [Header("Arrow bob animation")]
    [Tooltip("Bob amplitude in cell-units. 0 = no bob.")]
    [Range(0f, 0.2f)] public float bobAmplitude = 0.05f;
    [Tooltip("Bob speed in Hz.")]
    [Range(0f, 4f)] public float bobSpeedHz = 0.7f;

    [Header("Rotation rings (1/2/3)")]
    public bool showRotationRings = true;
    [Tooltip("Extra padding outside the block bbox, in cells.")]
    [Min(0f)] public float ringPadding = 0.3f;
    [Range(8, 32)] public int ringSegments = 18;
    [Min(0.005f)] public float ringLineWidth = 0.022f;
    [Range(0f, 1f)] public float ringAlpha = 0.55f;
    [Tooltip("Slow spin of each ring in degrees / sec. Adds 'alive' feel.")]
    public float ringSpinSpeed = 8f;

    [Header("Fade-in")]
    [Tooltip("Seconds for the whole overlay to fade in when entering Edit mode.")]
    [Min(0f)] public float fadeInDuration = 0.25f;

    [Header("Labels (OnGUI)")]
    [Min(8)] public int labelFontSize = 13;
    [Min(0)] public float labelScreenYOffset = 0f;
    [Tooltip("Wrap the label letter in brackets, e.g. [W] — looks less rigid than bare W.")]
    public bool labelBrackets = true;

    [Tooltip("Color of the WASDQE letter labels. Separate from arrow tint so you can have white text on a colored background, etc.")]
    public Color labelColor = Color.white;

    [Header("Label background (optional, hand-drawn texture)")]
    [Tooltip("Drop a hand-drawn texture (e.g. a crayon circle) here. Drawn behind every label. Null = no background.")]
    public Texture2D labelBackground;

    [Tooltip("Tint multiplied on the background texture.")]
    public Color labelBackgroundColor = Color.white;

    [Tooltip("Size of the background relative to the label rect.")]
    [Range(0.5f, 4f)] public float labelBackgroundScale = 1.6f;

    [Tooltip("If true, ring (1/2/3) labels also get a background. If false, only WASDQE.")]
    public bool labelBackgroundForRings = true;

    // ── State ────────────────────────────────────────────────────────────

    sealed class Arrow
    {
        public GameObject obj;
        public Renderer   renderer;
        public Vector3    direction;        // unit world dir, recomputed each frame
        public bool       cameraRelative;   // WASD: true (rotates with camera). QE: false (world Y).
        public Vector3    worldDirIfFixed;  // used when cameraRelative=false
        public string     label;
        public KeyCode    key;
        public float      lastPressTime;
        public float      bobPhase;          // 0..2π for desync
    }

    [Header("Label screen separation")]
    [Tooltip("Extra pixels to push each label outward along its screen-direction from the cursor cell. Helps when arrows in 3D project to nearly the same point on screen.")]
    [Min(0f)] public float labelScreenPushPx = 18f;

    sealed class Ring
    {
        public GameObject obj;
        public List<LineRenderer> segments = new();
        // `axis` is the WORLD-space rotation axis this frame, recomputed
        // each Update as pc.CurrentRotation * localAxis so the ring tracks
        // the block's local axes (matching `_targetRotation *= ...` which
        // is Self-space rotation).
        public Vector3   axis;
        public Vector3   localAxis;     // fixed: (1,0,0), (0,1,0), or (0,0,1)
        public Color     baseColor;
        public Vector3   labelOffsetLocal;
        public string    label;
        public KeyCode   key;
        public float     lastPressTime;
    }

    bool _visible;
    bool _userToggleOff;        // persistent off via toggleKey

    // Layout cache — used by the dirty gate. Ring segment rebuilds (the
    // priciest per-frame work) skip when cursor cell + block rotation
    // haven't changed.
    Vector3Int _lastLayoutCell  = new(int.MinValue, int.MinValue, int.MinValue);
    Quaternion _lastLayoutRot   = Quaternion.identity;
    bool       _forceLayoutNext = true;
    GameObject _root;
    Arrow[] _arrows;
    Ring[]  _rings;
    Mesh    _coneMesh;
    Material _lineMat;
    GUIStyle _labelStyle;
    float   _buildTime;

    // Cached this-frame bbox (in cell units, relative to cursor cell center).
    Vector3 _bboxMin, _bboxMax;

    // ── Lifecycle ────────────────────────────────────────────────────────

    void Update()
    {
        var pc   = PlacementController.Instance;
        var grid = GridSystem.instance;

        // Toggle key — persistent on/off
        if (Input.GetKeyDown(toggleKey)) _userToggleOff = !_userToggleOff;

        // Only consider showing while in a valid placement context.
        bool inContext = pc != null && grid != null
                      && pc.mode == PlacementMode.Edit
                      && pc.currentBlock != null;

        bool persistentShown = !_userToggleOff;
        bool shiftHeld       = shiftHoldInverts
                            && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        // Shift inverts the current persistent state while held: shown → hide,
        // hidden → show. Only meaningful while in placement context.
        bool shouldShow = inContext && (shiftHeld ? !persistentShown : persistentShown);

        if (shouldShow && !_visible) Build();
        else if (!shouldShow && _visible) Teardown();
        if (!_visible) return;

        // 1) Anchor on the block's actual position (= cursor + WASDQE offset).
        //    Using SnappedGridPos would lag behind whenever the player nudged
        //    the block with WASDQE since that only updates manualOffset.
        var center = grid.GridToWorld(pc.CurrentGridPos);
        _root.transform.position = center;

        // 2) Compute rotated bbox of the current block (in cell units).
        ComputeRotatedBBox(pc.currentBlock, pc.CurrentRotation, out _bboxMin, out _bboxMax);
        float cell = grid.cellSize;

        // 3) Update arrow base positions (outside the bbox face) + bob.
        for (int i = 0; i < _arrows.Length; i++) RecordKey(_arrows[i].key, ref _arrows[i].lastPressTime);
        if (_rings != null) for (int i = 0; i < _rings.Length; i++) RecordKey(_rings[i].key, ref _rings[i].lastPressTime);

        float fadeAlpha = Mathf.Clamp01((Time.time - _buildTime) / Mathf.Max(0.001f, fadeInDuration));

        var mainCam = Camera.main;
        for (int i = 0; i < _arrows.Length; i++)
        {
            var a = _arrows[i];

            // Hide Q/E (camera-forward/back depth arrows) when configured.
            // Those project as "into/out of screen" and read poorly in iso.
            bool isDepth = a.key == KeyCode.Q || a.key == KeyCode.E;
            bool show    = !(hideForwardBackArrows && isDepth);
            if (a.obj.activeSelf != show) a.obj.SetActive(show);
            if (!show) continue;

            // 1) Live direction (camera-relative for WASD, world for Q/E).
            a.direction = LiveDirection(a, mainCam);

            // 2) Orient the cone to point along that direction.
            a.obj.transform.localRotation = Quaternion.FromToRotation(Vector3.up, a.direction);

            // 3) Base position: outer face of bbox in this direction + gap.
            var baseLocal = ArrowFaceOffset(a.direction, _bboxMin, _bboxMax, arrowGap) * cell;

            // 4) Subtle bob along the arrow direction.
            float bob = bobAmplitude > 0f && bobSpeedHz > 0f
                ? Mathf.Sin(Time.time * Mathf.PI * 2f * bobSpeedHz + a.bobPhase) * bobAmplitude * cell
                : 0f;

            a.obj.transform.localPosition = baseLocal + a.direction * bob;

            // 5) Color: lerp flash → base after press, then * fadeAlpha.
            float t = Mathf.Clamp01((Time.time - a.lastPressTime) / arrowFlashDuration);
            var c   = Color.Lerp(arrowFlashColor, arrowColor, t);
            c.a    *= fadeAlpha;
            if (a.renderer != null) MpbColor.Set(a.renderer, c);
        }

        // 4) Update ring center, axis, radius + slow spin.
        // Dirty gate: ring segment rebuilds (sin/cos per segment × 3 rings)
        // are the heaviest per-frame work. Skip when cursor cell + block
        // rotation haven't moved.
        bool layoutDirty = _forceLayoutNext
                        || pc.CurrentGridPos != _lastLayoutCell
                        || Quaternion.Angle(pc.CurrentRotation, _lastLayoutRot) > 0.1f;

        if (_rings != null)
        {
            Vector3 bcenterLocal = (_bboxMin + _bboxMax) * 0.5f * cell;
            for (int i = 0; i < _rings.Length; i++)
            {
                var r = _rings[i];

                if (layoutDirty)
                {
                    // Anchor ring on bbox centroid (matters for asymmetric pieces).
                    r.obj.transform.localPosition = bcenterLocal;
                    // World-space rotation → ring axis is fixed at build time.
                    r.axis = r.localAxis;
                    float radius = RingRadiusFor(r.axis, _bboxMin, _bboxMax) * cell + ringPadding * cell;
                    RebuildRingPositions(r, radius);
                }

                if (ringSpinSpeed != 0f)
                    r.obj.transform.localRotation *= Quaternion.AngleAxis(ringSpinSpeed * Time.deltaTime, r.axis);

                float t = Mathf.Clamp01((Time.time - r.lastPressTime) / arrowFlashDuration);
                var c   = Color.Lerp(arrowFlashColor, r.baseColor, t);
                c.a     = ringAlpha * fadeAlpha;
                for (int s = 0; s < r.segments.Count; s++)
                {
                    var lr = r.segments[s];
                    if (lr == null) continue;
                    lr.startColor = c;
                    lr.endColor   = c;
                }
            }
        }

        if (layoutDirty)
        {
            _lastLayoutCell  = pc.CurrentGridPos;
            _lastLayoutRot   = pc.CurrentRotation;
            _forceLayoutNext = false;
        }
    }

    static void RecordKey(KeyCode k, ref float t) { if (Input.GetKeyDown(k)) t = Time.time; }

    void OnDisable() => Teardown();
    void OnDestroy() => Teardown();

    // ── Bbox math ────────────────────────────────────────────────────────

    // Rotated-block bounding box in cell-units, relative to cursor cell center.
    static void ComputeRotatedBBox(BlockData data, Quaternion rot, out Vector3 min, out Vector3 max)
    {
        min = Vector3.positiveInfinity;
        max = Vector3.negativeInfinity;
        if (data == null || data.cells == null || data.cells.Length == 0)
        {
            min = max = Vector3.zero;
            return;
        }
        for (int i = 0; i < data.cells.Length; i++)
        {
            // Each cell is a unit cube centered at the integer coord; rotate the
            // center, then inflate by ±0.5 along each rotated axis. For 90°
            // rotations this stays axis-aligned in world space.
            Vector3 c = rot * (Vector3)data.cells[i];
            min = Vector3.Min(min, c - Vector3.one * 0.5f);
            max = Vector3.Max(max, c + Vector3.one * 0.5f);
        }
    }

    // Offset to the CENTER of the outer face of the bbox in the given axis
    // direction. Perpendicular axes use the bbox center (so an L4 block has
    // its W arrow sitting at the centroid of the +Z face, not at local zero).
    // dir is one of ±X / ±Y / ±Z.
    static Vector3 ArrowFaceOffset(Vector3 dir, Vector3 bmin, Vector3 bmax, float gap)
    {
        // Start from bbox centroid — gives correct in-plane positioning for
        // asymmetric blocks.
        Vector3 o = (bmin + bmax) * 0.5f;

        // Then override the coordinate ALONG the arrow direction to sit at
        // the outer face + gap.
        if (dir.x > 0.5f)       o.x = bmax.x + gap;
        else if (dir.x < -0.5f) o.x = bmin.x - gap;
        if (dir.y > 0.5f)       o.y = bmax.y + gap;
        else if (dir.y < -0.5f) o.y = bmin.y - gap;
        if (dir.z > 0.5f)       o.z = bmax.z + gap;
        else if (dir.z < -0.5f) o.z = bmin.z - gap;
        return o;
    }

    // Ring radius needs to clear the bbox in the ring's plane (perpendicular
    // to its rotation axis). Ring is anchored at bbox CENTER, so use the
    // bbox's half-extent in each in-plane axis, take the larger.
    static float RingRadiusFor(Vector3 axis, Vector3 bmin, Vector3 bmax)
    {
        Vector3 half = (bmax - bmin) * 0.5f;
        if (Mathf.Abs(axis.x) > 0.5f) return Mathf.Max(half.y, half.z);
        if (Mathf.Abs(axis.y) > 0.5f) return Mathf.Max(half.x, half.z);
        return Mathf.Max(half.x, half.y);
    }

    // ── Build ────────────────────────────────────────────────────────────

    void Build()
    {
        Teardown();
        _root = new GameObject("PlacementHints");
        _root.transform.SetParent(transform, false);

        _lineMat  = GetDefaultLineMaterial();
        _coneMesh = BuildConeMesh();
        _buildTime = Time.time;
        _forceLayoutNext = true;   // ensure first post-build frame rebuilds rings

        if (showAxisLines)         BuildAxisDashes();
        if (showMovementArrows)    BuildMovementArrows();
        else                       _arrows = System.Array.Empty<Arrow>();
        if (showRotationRings)     BuildRotationRings();

        _visible = true;
    }

    void Teardown()
    {
        if (_root != null) Destroy(_root);
        _root = null;
        _arrows = null;
        _rings  = null;
        _visible = false;
    }

    void BuildAxisDashes()
    {
        BuildAxisDashSet(Vector3.right,   axisColorX);
        BuildAxisDashSet(Vector3.up,      axisColorY);
        BuildAxisDashSet(Vector3.forward, axisColorZ);
    }

    void BuildAxisDashSet(Vector3 axis, Color col)
    {
        float cell  = GridSystem.instance.cellSize;
        float total = axisHalfLengthCells * cell;
        int   dashes = Mathf.Max(2, axisDashesPerSide);
        float slot   = total / dashes;
        float fill   = slot * 0.6f;

        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < dashes; i++)
            {
                float t0 = i * slot;
                float t1 = t0 + fill;
                float midDist = (t0 + t1) * 0.5f;
                float a = axisAlpha * (1f - Mathf.Clamp01(midDist / total));
                var c = col; c.a = a;
                BuildLineSegment(_root.transform, axis * (t0 * side), axis * (t1 * side), c, axisLineWidth);
            }
        }
    }

    void BuildLineSegment(Transform parent, Vector3 local0, Vector3 local1, Color col, float width)
    {
        var go = new GameObject("Dash");
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace   = false;
        lr.positionCount   = 2;
        lr.widthMultiplier = width;
        lr.material        = _lineMat;
        lr.startColor      = col;
        lr.endColor        = col;
        lr.SetPosition(0, local0);
        lr.SetPosition(1, local1);
    }

    void BuildMovementArrows()
    {
        // New WASDQE mapping:
        //   W / S = world UP / DOWN  (fixed)
        //   A / D = camera left / right  (camera-relative, snapped to world axis)
        //   Q / E = camera forward / back  (camera-relative)
        var defs = new (string lbl, KeyCode k, bool cam, Vector3 worldDir)[]
        {
            ("W", KeyCode.W, false, Vector3.up),
            ("S", KeyCode.S, false, Vector3.down),
            ("A", KeyCode.A, true,  Vector3.left),
            ("D", KeyCode.D, true,  Vector3.right),
            ("Q", KeyCode.Q, true,  Vector3.forward),
            ("E", KeyCode.E, true,  Vector3.back),
        };

        _arrows = new Arrow[defs.Length];
        for (int i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            var obj = new GameObject($"Arrow_{d.lbl}");
            obj.transform.SetParent(_root.transform, false);
            obj.transform.localScale = Vector3.one * arrowSize;

            var mf = obj.AddComponent<MeshFilter>();
            mf.sharedMesh = _coneMesh;
            var mr = obj.AddComponent<MeshRenderer>();
            mr.sharedMaterial = _lineMat;
            MpbColor.Set(mr, arrowColor);

            _arrows[i] = new Arrow
            {
                obj             = obj,
                renderer        = mr,
                direction       = d.worldDir,
                cameraRelative  = d.cam,
                worldDirIfFixed = d.worldDir,
                label           = d.lbl,
                key             = d.k,
                lastPressTime   = -999f,
                bobPhase        = i * 1.04f,
            };
        }
    }

    // Recompute the live direction for an arrow. WASD snaps to the camera's
    // horizontal forward/right, matching PlacementController's input behavior
    // (HandleKeyboardOffset). Q/E stays world ±Y.
    Vector3 LiveDirection(Arrow a, Camera cam)
    {
        if (!a.cameraRelative || cam == null) return a.worldDirIfFixed;

        Vector3 fwd   = SnapToHorizontalAxis(cam.transform.forward);
        Vector3 right = SnapToHorizontalAxis(cam.transform.right);
        return a.key switch
        {
            KeyCode.A => -right,
            KeyCode.D =>  right,
            KeyCode.Q =>  fwd,
            KeyCode.E => -fwd,
            _         => a.worldDirIfFixed,
        };
    }

    static Vector3 SnapToHorizontalAxis(Vector3 v)
    {
        v.y = 0;
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.z))
            return new Vector3(Mathf.Sign(v.x), 0, 0);
        else
            return new Vector3(0, 0, Mathf.Sign(v.z));
    }

    void BuildRotationRings()
    {
        var defs = new (string lbl, KeyCode k, Vector3 axis, Color col)[]
        {
            ("1", KeyCode.Alpha1, Vector3.right,   axisColorX),
            ("2", KeyCode.Alpha2, Vector3.up,      axisColorY),
            ("3", KeyCode.Alpha3, Vector3.forward, axisColorZ),
        };

        _rings = new Ring[defs.Length];
        for (int i = 0; i < defs.Length; i++) _rings[i] = BuildOneRing(defs[i].lbl, defs[i].k, defs[i].axis, defs[i].col);
    }

    Ring BuildOneRing(string label, KeyCode key, Vector3 axis, Color color)
    {
        var ring = new Ring
        {
            obj           = new GameObject($"RotRing_{label}"),
            label         = label,
            key           = key,
            axis          = axis,        // initial value; Update overwrites
            localAxis     = axis,        // fixed reference axis
            baseColor     = color,
            lastPressTime = -999f,
        };
        ring.obj.transform.SetParent(_root.transform, false);

        int n = Mathf.Max(8, ringSegments);
        for (int s = 0; s < n; s++)
        {
            var go = new GameObject("Seg");
            go.transform.SetParent(ring.obj.transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace   = false;
            lr.positionCount   = 2;
            lr.widthMultiplier = ringLineWidth;
            lr.material        = _lineMat;
            ring.segments.Add(lr);
        }
        return ring;
    }

    // Rebuild ring vertex positions for a given radius (called each frame as
    // bbox / rotation change).
    void RebuildRingPositions(Ring r, float radius)
    {
        // Two basis vectors in the plane perpendicular to the rotation axis.
        Vector3 u = Mathf.Abs(r.axis.y) < 0.9f
            ? Vector3.Cross(r.axis, Vector3.up).normalized
            : Vector3.Cross(r.axis, Vector3.right).normalized;
        Vector3 v = Vector3.Cross(r.axis, u).normalized;

        int n = r.segments.Count;
        float tau = Mathf.PI * 2f;
        for (int s = 0; s < n; s++)
        {
            float a0 = (s     / (float)n) * tau;
            float a1 = ((s+1) / (float)n) * tau;
            var p0 = radius * (u * Mathf.Cos(a0) + v * Mathf.Sin(a0));
            var p1 = radius * (u * Mathf.Cos(a1) + v * Mathf.Sin(a1));
            r.segments[s].SetPosition(0, p0);
            r.segments[s].SetPosition(1, p1);
        }
        // Anchor for the label: a point on the ring's NE octant.
        r.labelOffsetLocal = radius * (u * 0.707f + v * 0.707f);
    }

    // ── OnGUI labels (with brackets + fade) ──────────────────────────────

    // Per-label info collected before layout resolution.
    struct LabelInfo
    {
        public Vector2 center;       // screen-space (y-up)
        public string  text;
        public Color   baseColor;
        public float   lastPress;
        public bool    anchored;     // ring labels are anchored — others move around them
    }

    const float kLabelW = 44f;
    const float kLabelH = 20f;
    const float kLabelPad = 4f;

    readonly List<LabelInfo> _labelBuf = new();

    void OnGUI()
    {
        if (!_visible) return;
        var cam = Camera.main;
        if (cam == null) return;

        if (_labelStyle == null)
        {
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize  = labelFontSize,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleCenter,
            };
        }

        float fadeAlpha = Mathf.Clamp01((Time.time - _buildTime) / Mathf.Max(0.001f, fadeInDuration));

        // 1) Collect candidate labels at their "initial pushed" positions.
        _labelBuf.Clear();
        Vector2 cubeScreen = _root != null
            ? (Vector2)cam.WorldToScreenPoint(_root.transform.position)
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        if (showMovementLabels)
        {
            for (int i = 0; i < _arrows.Length; i++)
            {
                var a = _arrows[i];
                if (a.obj == null || !a.obj.activeSelf) continue;
                AddCandidateLabel(cam, a.obj.transform.position, cubeScreen,
                    FormatLabel(a.label), labelColor, a.lastPressTime, anchored: false);
            }
        }
        if (_rings != null)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                var r = _rings[i];
                if (r.obj == null) continue;
                var worldPos = r.obj.transform.position + r.obj.transform.TransformVector(r.labelOffsetLocal);
                AddCandidateLabel(cam, worldPos, cubeScreen,
                    FormatLabel(r.label), r.baseColor, r.lastPressTime, anchored: true);
            }
        }
        if (_labelBuf.Count == 0) return;

        // 2) Compute block screen rect (project 8 bbox corners → AABB).
        Rect blockRect = ComputeBlockScreenRect(cam);
        Vector2 blockCenter = blockRect.center;
        float   blockHalfDiag = 0.5f * new Vector2(blockRect.width, blockRect.height).magnitude;
        float   labelHalfDiag = 0.5f * new Vector2(kLabelW, kLabelH).magnitude;
        float   pushClearance = blockHalfDiag + labelHalfDiag + kLabelPad;
        float   pairwiseMin   = Mathf.Max(kLabelW, kLabelH) + kLabelPad;

        // 3) Iterative push-out — 4 passes hits convergence for typical counts.
        // Anchored labels (ring 1/2/3) never move; non-anchored ones (WASDQE)
        // dodge around them and the block.
        for (int iter = 0; iter < 4; iter++)
        {
            // 3a) Push non-anchored labels out of the block rect.
            for (int i = 0; i < _labelBuf.Count; i++)
            {
                var info = _labelBuf[i];
                if (info.anchored) continue;
                if (RectsOverlap(LabelRect(info.center), blockRect))
                {
                    var dir = info.center - blockCenter;
                    if (dir.sqrMagnitude < 0.01f) dir = Vector2.up;
                    else dir.Normalize();
                    info.center = blockCenter + dir * pushClearance;
                    _labelBuf[i] = info;
                }
            }

            // 3b) Push pairs apart.
            for (int i = 0; i < _labelBuf.Count; i++)
            {
                for (int j = i + 1; j < _labelBuf.Count; j++)
                {
                    var a = _labelBuf[i];
                    var b = _labelBuf[j];
                    if (a.anchored && b.anchored) continue;   // both fixed → skip

                    var diff = b.center - a.center;
                    float dist = diff.magnitude;
                    if (dist >= pairwiseMin) continue;

                    Vector2 push;
                    if (dist > 0.01f) push = diff.normalized * (pairwiseMin - dist);
                    else              push = Vector2.right   *  pairwiseMin;

                    if (a.anchored)
                    {
                        b.center += push;
                    }
                    else if (b.anchored)
                    {
                        a.center -= push;
                    }
                    else
                    {
                        a.center -= push * 0.5f;
                        b.center += push * 0.5f;
                    }
                    _labelBuf[i] = a;
                    _labelBuf[j] = b;
                }
            }
        }

        // 4) Draw resolved labels (background texture first, then text).
        for (int i = 0; i < _labelBuf.Count; i++)
        {
            var info = _labelBuf[i];
            float t = Mathf.Clamp01((Time.time - info.lastPress) / arrowFlashDuration);
            Color c = Color.Lerp(arrowFlashColor, info.baseColor, t);
            c.a = fadeAlpha;

            float x = info.center.x - kLabelW * 0.5f;
            float y = Screen.height - info.center.y - kLabelH * 0.5f + labelScreenYOffset;
            Rect labelRect = new(x, y, kLabelW, kLabelH);

            // Hand-drawn background (e.g. crayon circle).
            bool drawBg = labelBackground != null
                       && (!info.anchored || labelBackgroundForRings);
            if (drawBg)
            {
                float bgSide = Mathf.Max(kLabelW, kLabelH) * labelBackgroundScale;
                Rect bgRect = new(
                    info.center.x - bgSide * 0.5f,
                    Screen.height - info.center.y - bgSide * 0.5f + labelScreenYOffset,
                    bgSide, bgSide);
                Color saved = GUI.color;
                var bgC = labelBackgroundColor; bgC.a *= fadeAlpha;
                GUI.color = bgC;
                GUI.DrawTexture(bgRect, labelBackground, ScaleMode.ScaleToFit, alphaBlend: true);
                GUI.color = saved;
            }

            _labelStyle.normal.textColor = c;
            GUI.Label(labelRect, info.text, _labelStyle);
        }
    }

    string FormatLabel(string letter) => labelBrackets ? $"{letter}" : letter;

    void AddCandidateLabel(Camera cam, Vector3 worldPos, Vector2 cubeScreen,
                           string text, Color baseCol, float lastPress, bool anchored)
    {
        var screen = cam.WorldToScreenPoint(worldPos);
        if (screen.z < 0f) return;
        Vector2 pos = new(screen.x, screen.y);
        // Anchored labels (ring 123) sit exactly on the ring — don't shove
        // them further out. Only flexible labels (WASDQE) get the push.
        Vector2 center = pos;
        if (!anchored)
        {
            Vector2 dir = pos - cubeScreen;
            if (dir.sqrMagnitude > 1f) dir.Normalize();
            else dir = Vector2.up;
            center = pos + dir * labelScreenPushPx;
        }
        _labelBuf.Add(new LabelInfo
        {
            center    = center,
            text      = text,
            baseColor = baseCol,
            lastPress = lastPress,
            anchored  = anchored,
        });
    }

    // Rect centered on `c` with kLabelW × kLabelH dimensions, in screen y-up.
    static Rect LabelRect(Vector2 c)
        => new(c.x - kLabelW * 0.5f, c.y - kLabelH * 0.5f, kLabelW, kLabelH);

    static bool RectsOverlap(Rect a, Rect b)
        => !(a.xMax < b.xMin || b.xMax < a.xMin || a.yMax < b.yMin || b.yMax < a.yMin);

    // Project the block's 8 bbox corners and take the screen-space AABB.
    Rect ComputeBlockScreenRect(Camera cam)
    {
        if (_root == null) return new Rect(0, 0, 0, 0);

        float cs = GridSystem.instance != null ? GridSystem.instance.cellSize : 1f;
        Vector3 bmin = _bboxMin, bmax = _bboxMax;

        // 8 corners of the bbox in local cell space.
        Vector3[] corners =
        {
            new(bmin.x, bmin.y, bmin.z), new(bmax.x, bmin.y, bmin.z),
            new(bmin.x, bmax.y, bmin.z), new(bmax.x, bmax.y, bmin.z),
            new(bmin.x, bmin.y, bmax.z), new(bmax.x, bmin.y, bmax.z),
            new(bmin.x, bmax.y, bmax.z), new(bmax.x, bmax.y, bmax.z),
        };

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var world = _root.transform.TransformPoint(corners[i] * cs);
            var s = cam.WorldToScreenPoint(world);
            if (s.z < 0f) continue;
            if (s.x < minX) minX = s.x;
            if (s.x > maxX) maxX = s.x;
            if (s.y < minY) minY = s.y;
            if (s.y > maxY) maxY = s.y;
        }
        if (minX > maxX || minY > maxY)   // all corners behind camera; collapse
            return new Rect(Screen.width * 0.5f, Screen.height * 0.5f, 1f, 1f);

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    Material GetDefaultLineMaterial()
    {
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        return new Material(sh);
    }

    static Mesh BuildConeMesh()
    {
        var m = new Mesh { name = "HintCone" };
        Vector3[] verts =
        {
            new( 0.0f,  0.5f,  0.0f),
            new(-0.4f, -0.5f, -0.4f),
            new( 0.4f, -0.5f, -0.4f),
            new( 0.4f, -0.5f,  0.4f),
            new(-0.4f, -0.5f,  0.4f),
        };
        int[] tris =
        {
            0, 2, 1,
            0, 3, 2,
            0, 4, 3,
            0, 1, 4,
            1, 2, 3,
            1, 3, 4,
        };
        m.vertices  = verts;
        m.triangles = tris;
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
