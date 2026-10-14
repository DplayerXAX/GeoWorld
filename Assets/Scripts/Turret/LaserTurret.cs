using UnityEngine;

// The Laser turret: a beam down ONE axis, the way the turret is turned, that
// pierces every enemy in it and is stopped by the first block it meets.
//
// It is the turret that asks the most of the BOARD. The others shoot at what is in
// range; this one only hits what is in its line, so where it sits, which way it
// faces and what stands in front of it all decide what it does:
//   * down a straight stretch of route it hits everything on it, at once;
//   * a block placed in its way shortens it (or switches it off entirely);
//   * the route has to run along its line for it to matter, so it shapes the route.
//
// The beam runs along the grid, cell by cell, rather than through the physics
// scene: "stopped by a block" then means exactly "the next cell is occupied", the
// same rule everything else on the board uses, and the placement ghost can show the
// answer before the turret is down.
public static class LaserBeam
{
    // The grid axis a forward vector points most along.
    public static Vector3Int Axis(Vector3 forward)
    {
        float ax = Mathf.Abs(forward.x), ay = Mathf.Abs(forward.y), az = Mathf.Abs(forward.z);
        if (ax >= ay && ax >= az) return new Vector3Int(forward.x >= 0f ? 1 : -1, 0, 0);
        if (ay >= az)             return new Vector3Int(0, forward.y >= 0f ? 1 : -1, 0);
        return new Vector3Int(0, 0, forward.z >= 0f ? 1 : -1);
    }

    // Free cells from `from` along `axis` (at most maxCells), and whether a block
    // stopped it there.
    public static int Trace(GridSystem grid, Vector3Int from, Vector3Int axis, int maxCells, out bool blocked)
    {
        blocked = false;
        for (int i = 1; i <= maxCells; i++)
        {
            if (grid.IsOccupied(from + axis * i)) { blocked = true; return i - 1; }
        }
        return maxCells;
    }

    // The beam as a world segment: from the middle of its own cell out to the face of
    // the block that stops it, or the far edge of its last free cell.
    public static void Segment(GridSystem grid, Vector3Int cell, Vector3Int axis, int maxCells,
                               out Vector3 a, out Vector3 b, out bool blocked)
    {
        int free = Trace(grid, cell, axis, maxCells, out blocked);
        float cs = grid.cellSize;
        a = grid.GridToWorld(cell);
        b = a + (Vector3)axis * (free * cs + cs * 0.5f);
    }

    public static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float len2 = ab.sqrMagnitude;
        if (len2 < 1e-6f) return Vector3.Distance(p, a);
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / len2);
        return Vector3.Distance(p, a + ab * t);
    }
}

// One beam on screen: a thin line, and a spark where a block stops it. Kept in the
// scene root, not under the turret — the turret's visual is scaled to nothing and
// back by the pop-in effects, and a beam must not go with it.
//
//   Aim    in the build phase: a faint line showing where it will fire, and what
//          stops it — the answer to "what does this turret see" before the wave.
//   Armed  during combat: brighter; flares on every pulse that hits something.
public class LaserBeamView : MonoBehaviour
{
    public enum State { Hidden, Aim, Armed }

    static Material _mat;

    LineRenderer _lr;
    Transform    _spark;
    MeshRenderer _sparkMr;
    Color        _color = Color.white;
    Vector3      _a, _b;
    bool         _blocked;
    float        _cs = 1f;
    State        _state = State.Hidden;
    float        _flare;

    public static LaserBeamView Create(Color color)
    {
        var go = new GameObject("LaserBeam");
        var v  = go.AddComponent<LaserBeamView>();
        v._color = color;

        v._lr = go.AddComponent<LineRenderer>();
        v._lr.useWorldSpace     = true;
        v._lr.positionCount     = 2;
        v._lr.numCapVertices    = 4;
        v._lr.sharedMaterial    = Mat();
        v._lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        v._lr.receiveShadows    = false;

        var sp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sp.name = "LaserSpark";
        Destroy(sp.GetComponent<Collider>());
        sp.transform.SetParent(go.transform, false);
        v._spark   = sp.transform;
        v._sparkMr = sp.GetComponent<MeshRenderer>();
        v._sparkMr.sharedMaterial    = Mat();
        v._sparkMr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        v._sparkMr.receiveShadows    = false;

        v.Apply();
        return v;
    }

    public void Show(Vector3 a, Vector3 b, bool blocked, State state, float cellSize)
    {
        _a = a; _b = b; _blocked = blocked; _state = state; _cs = cellSize;
        Apply();
    }

    public void Hide()
    {
        if (_state == State.Hidden) return;
        _state = State.Hidden;
        Apply();
    }

    // A pulse that hit something.
    public void Pulse() => _flare = 1f;

    void Update()
    {
        if (_flare <= 0f) return;
        _flare = Mathf.MoveTowards(_flare, 0f, Time.deltaTime * 6f);
        Apply();
    }

    void Apply()
    {
        if (_lr == null) return;
        if (_state == State.Hidden)
        {
            _lr.enabled = false;
            if (_spark != null) _spark.gameObject.SetActive(false);
            return;
        }

        _lr.enabled = true;
        _lr.SetPosition(0, _a);
        _lr.SetPosition(1, _b);

        bool  aim   = _state == State.Aim;
        float width = ((aim ? 0.035f : 0.06f) + 0.12f * _flare) * _cs;
        _lr.startWidth = width;
        _lr.endWidth   = width * 0.8f;

        float alpha = (aim ? 0.32f : 0.6f) + 0.4f * _flare;
        float glow  = 1f + _flare * 1.6f;
        var   c     = new Color(_color.r * glow, _color.g * glow, _color.b * glow, alpha);
        MpbColor.Set(_lr, c);

        _spark.gameObject.SetActive(_blocked);
        if (_blocked)
        {
            _spark.position   = _b;
            _spark.localScale = Vector3.one * ((aim ? 0.12f : 0.18f) + 0.26f * _flare) * _cs;
            MpbColor.Set(_sparkMr, c);
        }
    }

    // Additive: these are light, not paint (same as the other turret shot effects).
    static Material Mat()
    {
        if (_mat != null) return _mat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _mat = new Material(sh) { name = "LaserBeam" };
        if (_mat.HasProperty("_Surface"))
        {
            _mat.SetFloat("_Surface", 1f);
            _mat.SetFloat("_ZWrite", 0f);
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _mat;
    }
}

// The laser turret's model holds still: it has a FACING, and a tumbling model would
// lie about it. A short emitter nub on the side it fires from says which way it
// points. The nub rides the placed block's root (which carries the rotation the
// turret was placed with), so it turns with the turret and is removed with it.
public class LaserTurretAnimator : TurretAnimator
{
    Transform _nub;

    protected override void Setup()
    {
        var root = transform.parent != null ? transform.parent : transform;
        var grid = GridSystem.instance;
        float cs = grid != null ? grid.cellSize : 1f;

        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "LaserEmitter";
        Destroy(go.GetComponent<Collider>());
        _nub = go.transform;
        _nub.SetParent(root, false);
        _nub.localRotation = Quaternion.identity;
        _nub.localPosition = new Vector3(0f, 0f, 0.42f * cs);
        _nub.localScale    = new Vector3(0.16f, 0.16f, 0.34f) * cs;

        var mr = go.GetComponent<MeshRenderer>();
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        MpbColor.Set(mr, TurretTypes.DisplayColor(TurretController.Mode.Laser));
    }

    protected override void Animate(float dt)
    {
        // Held still on purpose — see above. (The base class still bobs the root.)

        // The combat ripple pops turret models in and out by scaling them; the nub sits
        // on the block's root, which that doesn't touch, so it follows the model by hand.
        if (_nub != null) _nub.gameObject.SetActive(transform.lossyScale.x > 0.05f);
    }
}
