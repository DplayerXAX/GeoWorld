using System.Collections.Generic;
using UnityEngine;

// The motion of a turret's VISUAL — one subclass per turret type, attached to the
// fitted prefab instance by PlacementController.SpawnTurretVisual (see Attach).
//
//   Basic → BasicTurretAnimator : the free tumble every turret used to have
//   Slow  → SlowTurretAnimator  : its sub-meshes turn about Y one after another
//   Aoe   → AoeTurretAnimator   : its three parts move together in a slow harmony
//
// ── WHAT THE BASE GIVES EVERY TYPE ───────────────────────────────────────────
//
//   * A shared bob on the visual root (what TurretBeacon did).
//   * Its own CLOCK, advanced by Activity: when the turret loses support
//     (TurretController.Supported → Powered) the whole animation winds down to a
//     standstill and winds back up when it's held up again — no type has to handle
//     that itself.
//   * PARTS: the prefab's renderer-carrying children, each with its rest pose,
//     centre and the world-up axis in its own parent's space, so a subclass poses
//     parts from rest every frame (no drift, no accumulated rotation) whatever the
//     model's import rotation or the fit scale.
//   * Hooks from TurretController: OnTarget (whenever the target changes) and
//     OnFire (each shot). The base keeps FireAge (seconds since the last shot),
//     FireDir (world direction of that shot) and FireKick, and Spring() — the
//     shared attack envelope: at its peak the instant the shot leaves, then a
//     damped spring back through a small overshoot. Each type shapes its attack
//     from that.
//   * RootOffset / SetRootScale for attacks that move the whole model (a model
//     that is one mesh has no parts to move).
//
// The root's SCALE belongs to CombatRipple, which pops turret visuals in and out by
// scaling it. SetRootScale therefore only ever MULTIPLIES whatever scale is there,
// and treats any value it did not write itself as the new base — so the pop always
// wins, and a squash never bakes itself into the turret's size.
public abstract class TurretAnimator : MonoBehaviour
{
    [Header("Common")]
    [Tooltip("Up/down float of the whole turret, in its block's local units.")]
    public float bobHeight = 0.08f;
    public float bobSpeed  = 1.6f;
    [Tooltip("How quickly the animation winds down when the turret loses support, and back up when it regains it.")]
    public float powerEase = 3f;
    [Tooltip("How fast FireKick decays after a shot (per second, exponential).")]
    public float fireKickDecay = 6f;

    // Set by TurretController each frame from its Supported flag.
    public bool Powered { get; set; } = true;
    public TurretController Turret { get; set; }

    protected float    Clock;          // animation time; advances with Activity
    protected float    Activity = 1f;  // 1 = running, 0 = wound down (unsupported)
    protected float    FireKick;       // 1 on the frame of a shot, decaying to 0
    protected Vector3  AimPoint;       // world position of the last target / shot
    protected bool     HasTarget;
    protected float    FireAge = 999f; // seconds since the last shot (real time, not Activity-scaled)
    protected Vector3  FireDir = Vector3.forward;   // world direction of the last shot
    protected Vector3  RootOffset;     // added to the root's rest position (parent space), e.g. recoil

    // The shared attack envelope. 1 the instant the shot leaves, then a damped
    // spring back to 0 — through a small overshoot the other way, which is what
    // makes a kick read as a kick and not a fade. 0 before any shot.
    //   decay: how fast it dies away (per second)   freq: how fast it rings (rad/s)
    protected static float Spring(float age, float decay, float freq)
    {
        if (age < 0f || age > 3f) return 0f;
        return Mathf.Exp(-decay * age) * Mathf.Cos(freq * age);
    }

    // Root scale = whatever it is (CombatRipple's pop, or the fitted size) × factor.
    Vector3 _scaleBase, _scaleWritten;
    bool    _scaleHeld;
    protected void SetRootScale(Vector3 factor)
    {
        var cur = transform.localScale;
        // Anything we did not write ourselves is someone else's scale — adopt it.
        if (!_scaleHeld || (cur - _scaleWritten).sqrMagnitude > 1e-10f) _scaleBase = cur;
        _scaleHeld = true;

        var s = Vector3.Scale(_scaleBase, factor);
        transform.localScale = s;
        _scaleWritten = s;
    }

    // One animated piece of the model, with everything needed to pose it from rest.
    protected struct Part
    {
        public Transform  t;
        public Vector3    restPos;     // localPosition at rest
        public Quaternion restRot;     // localRotation at rest
        public Vector3    center;      // its bounds centre, in its parent's space
        public Vector3    up;          // world up, in its parent's space (unit)
        public float      unit;        // one world unit, in its parent's space

        // Pose = rest turned by `spin` about its own vertical axis, then moved by
        // `offset` (parent space).
        public void Pose(Quaternion spin, Vector3 offset)
        {
            t.localPosition = center + spin * (restPos - center) + offset;
            t.localRotation = spin * restRot;
        }
    }

    protected readonly List<Part> Parts = new();
    protected float Size;              // largest dimension of the whole model, world units

    Vector3 _basePos;
    float   _bobPhase;
    bool    _ready;

    public static TurretAnimator Attach(GameObject visual, TurretController.Mode mode)
    {
        if (visual == null) return null;
        return mode switch
        {
            TurretController.Mode.Slow => visual.AddComponent<SlowTurretAnimator>(),
            TurretController.Mode.Aoe  => visual.AddComponent<AoeTurretAnimator>(),
            _                          => visual.AddComponent<BasicTurretAnimator>(),
        };
    }

    // World centre of the model as it stands right now (its renderers' bounds) —
    // where a shot that should come out of the turret's HEART, not its muzzle, leaves
    // from (the Slow turret's beam). Read per shot, so it follows the animation.
    Renderer[] _rends;
    public Vector3 WorldCenter
    {
        get
        {
            if (_rends == null) _rends = GetComponentsInChildren<Renderer>();
            bool any = false; Bounds b = default;
            foreach (var r in _rends)
            {
                if (r == null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            return any ? b.center : transform.position;
        }
    }

    // ── Hooks ────────────────────────────────────────────────────────────────
    public virtual void OnFire(Vector3 targetPos)
    {
        FireKick = 1f;
        FireAge  = 0f;
        AimPoint = targetPos;
        var d = targetPos - transform.position;
        if (d.sqrMagnitude > 1e-6f) FireDir = d.normalized;
    }

    public virtual void OnTarget(EnemySurfaceUnit target)
    {
        HasTarget = target != null;
        if (HasTarget) AimPoint = target.transform.position;
    }

    // ── Subclass surface ─────────────────────────────────────────────────────
    // Setup runs once the visual has a real size (see TryReady); Animate every frame
    // after, with dt already scaled by Activity.
    protected virtual void Setup() { }
    protected abstract void Animate(float dt);

    void Start()
    {
        _basePos  = transform.localPosition;
        _bobPhase = Random.Range(0f, Mathf.PI * 2f);
        TryReady();
    }

    // Parts are measured from renderer bounds, which are empty while the root is
    // scaled to zero (CombatRipple hides turrets that way before popping them in) —
    // so wait until it has a size rather than baking NaNs into every rest pose.
    void TryReady()
    {
        if (_ready) return;
        var s = transform.lossyScale;
        if (Mathf.Abs(s.x) < 1e-6f || Mathf.Abs(s.y) < 1e-6f || Mathf.Abs(s.z) < 1e-6f) return;
        CollectParts();
        Setup();
        _ready = true;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Activity  = Mathf.Lerp(Activity, Powered ? 1f : 0f, 1f - Mathf.Exp(-powerEase * dt));
        FireKick *= Mathf.Exp(-fireKickDecay * dt);
        FireAge  += dt;

        float adt = dt * Activity;
        Clock += adt;

        if (!_ready) TryReady();
        if (_ready) Animate(adt);

        // After Animate, so this frame's RootOffset (recoil) is applied this frame.
        transform.localPosition = _basePos + RootOffset
                                + Vector3.up * (Mathf.Sin(Clock * bobSpeed + _bobPhase) * bobHeight);
    }

    // World direction → the root's parent space (where RootOffset lives), keeping
    // world length.
    protected Vector3 ToParentSpace(Vector3 worldVec)
    {
        var p = transform.parent;
        return p != null ? p.InverseTransformVector(worldVec) : worldVec;
    }

    // Renderer-carrying descendants of the root, minus any that sit under another
    // one (moving a parent already moves its children — posing both would double it).
    void CollectParts()
    {
        Parts.Clear();
        var rends = GetComponentsInChildren<Renderer>();
        var holders = new HashSet<Transform>();
        foreach (var r in rends) if (r.transform != transform) holders.Add(r.transform);

        Bounds all = default; bool any = false;
        foreach (var r in rends)
        {
            if (!any) { all = r.bounds; any = true; } else all.Encapsulate(r.bounds);
        }
        Size = any ? Mathf.Max(all.size.x, Mathf.Max(all.size.y, all.size.z)) : 1f;

        foreach (var r in rends)
        {
            var t = r.transform;
            if (t == transform || t.parent == null) continue;

            bool nested = false;
            for (var p = t.parent; p != null && p != transform; p = p.parent)
                if (holders.Contains(p)) { nested = true; break; }
            if (nested) continue;

            var parent = t.parent;
            Parts.Add(new Part
            {
                t       = t,
                restPos = t.localPosition,
                restRot = t.localRotation,
                center  = parent.InverseTransformPoint(r.bounds.center),
                up      = parent.InverseTransformDirection(Vector3.up).normalized,
                unit    = parent.InverseTransformVector(Vector3.up).magnitude,
            });
        }
    }

    // Ease-in-out cubic on [0,1].
    protected static float EaseInOut(float x)
    {
        x = Mathf.Clamp01(x);
        return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;
    }
}
