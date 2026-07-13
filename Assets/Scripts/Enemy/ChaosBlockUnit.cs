using UnityEngine;
using UnityEngine.Rendering;

// Visual + identity component for one Chaos Block instance. Lives alongside an
// EnemySurfaceUnit that's never given a path (EnemySurfaceUnit.Update() no-ops
// without one — see EnemySurfaceUnit.cs), so it just sits still, stays
// damageable, and fires OnDied normally when its health hits 0. Spawned +
// owned by ChaosBlockController; this component drives the "corrupted" look —
// slow spin, colour pulsing harder toward red as it takes damage, and a
// continuous black bubbling FX rising off the top face.
[DisallowMultipleComponent]
[RequireComponent(typeof(EnemySurfaceUnit))]
public class ChaosBlockUnit : MonoBehaviour
{
    public float spinDegPerSec = 12f;
    public float pulseSpeed    = 2f;
    public Color baseColor     = new Color(0.03f, 0.03f, 0.05f, 1f);
    public Color dangerColor   = new Color(0.55f, 0.05f, 0.10f, 1f);

    [Header("Black bubble FX")]
    public bool  bubbleFxEnabled     = true;
    public Color bubbleColor         = new Color(0.06f, 0.03f, 0.09f, 0.88f);
    [Tooltip("Average seconds between bubbles (jittered).")]
    public float bubbleSpawnInterval = 0.32f;
    public float bubbleLifetime      = 1.3f;
    [Tooltip("Rise speed in world units/sec.")]
    public float bubbleRiseSpeed     = 0.55f;
    public float bubbleMinSize       = 0.06f;
    public float bubbleMaxSize       = 0.16f;
    public float bubbleWobbleAmount  = 0.05f;
    public float bubbleWobbleSpeed   = 2.2f;

    EnemySurfaceUnit _unit;
    Renderer[]       _rends;
    float            _bubbleTimer;

    void Awake()
    {
        _unit  = GetComponent<EnemySurfaceUnit>();
        _rends = GetComponentsInChildren<Renderer>();
    }

    void Update()
    {
        transform.Rotate(Vector3.up, spinDegPerSec * Time.deltaTime, Space.Self);

        if (_unit != null && _rends != null && _rends.Length > 0)
        {
            float dangerT = 1f - Mathf.Clamp01((float)_unit.CurrentHealth / Mathf.Max(1, _unit.maxHealth));
            float pulse   = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed);
            Color c       = Color.Lerp(baseColor, dangerColor, dangerT * (0.35f + 0.65f * pulse));

            for (int i = 0; i < _rends.Length; i++)
                if (_rends[i] != null) MpbColor.Set(_rends[i], c);
        }

        if (!bubbleFxEnabled) return;
        _bubbleTimer -= Time.deltaTime;
        if (_bubbleTimer <= 0f)
        {
            _bubbleTimer = bubbleSpawnInterval * Random.Range(0.7f, 1.3f);
            SpawnBubble();
        }
    }

    // Small dark sphere popping up from a random point on the block's top face —
    // "corrupted liquid bubbling" cue. Independent, self-destructing GameObject
    // (not parented) so it keeps rising/fading even if the block dies mid-life.
    //
    // Uses Renderer.localBounds (the mesh's OWN bounding box, in the renderer's
    // local space) rather than Renderer.bounds (world-space AABB). world bounds
    // are rotation-dependent — since the block spins continuously, its world AABB
    // swells at diagonal angles (a spinning square's axis-aligned box is up to
    // ~41% wider than the square itself), which was pushing bubbles visibly
    // outside the block ("sometimes far"). Local bounds don't have that problem,
    // and picking a face of the local box (instead of just the flat top) spreads
    // bubbles across the whole skin instead of one plane.
    void SpawnBubble()
    {
        if (_rends == null || _rends.Length == 0) return;
        var mr = _rends[0];
        Bounds lb = mr.localBounds;

        Vector3 localPoint = lb.center + RandomPointOnSkin(lb.extents);
        Vector3 origin = mr.transform.TransformPoint(localPoint);

        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "ChaosBubble";
        if (go.TryGetComponent<Collider>(out var col)) Destroy(col);

        go.transform.position   = origin;
        float size = Random.Range(bubbleMinSize, bubbleMaxSize);
        go.transform.localScale = Vector3.one * size;

        var bubbleMr = go.GetComponent<MeshRenderer>();
        bubbleMr.sharedMaterial       = GetBubbleMaterial();
        bubbleMr.shadowCastingMode    = ShadowCastingMode.Off;
        bubbleMr.receiveShadows       = false;
        bubbleMr.lightProbeUsage      = LightProbeUsage.Off;
        bubbleMr.reflectionProbeUsage = ReflectionProbeUsage.Off;

        go.AddComponent<ChaosBubble>().Init(bubbleLifetime, bubbleRiseSpeed,
                                            bubbleWobbleAmount, bubbleWobbleSpeed, bubbleColor);
    }

    // A random point on 5 of the local box's 6 faces (skips the bottom — bubbles
    // shouldn't spawn on the underside and rise up through the block). `half` =
    // the box's local half-extents (Bounds.extents).
    static Vector3 RandomPointOnSkin(Vector3 half)
    {
        int face = Random.Range(0, 5);   // 0=+X 1=-X 2=+Y(top) 3=+Z 4=-Z
        float u = Random.Range(-half.x, half.x);
        float v = Random.Range(-half.z, half.z);
        float w = Random.Range(-half.y, half.y);
        return face switch
        {
            0 => new Vector3(half.x, w, v),
            1 => new Vector3(-half.x, w, v),
            2 => new Vector3(u, half.y, v),
            3 => new Vector3(u, w, half.z),
            _ => new Vector3(u, w, -half.z),
        };
    }

    // Shared translucent unlit material (alpha-blended, NOT additive — additive
    // would make a near-black colour invisible). Sprites/Default is alpha-
    // blended by default, same fallback used across the project's other VFX.
    static Material _bubbleMat;
    static Material GetBubbleMaterial()
    {
        if (_bubbleMat != null) return _bubbleMat;
        var sh = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        _bubbleMat = new Material(sh) { hideFlags = HideFlags.DontSave };
        return _bubbleMat;
    }
}

// One bubble: rises with a slight side-to-side wobble, grows in, then pops
// (quick shrink) near the end of its life while fading out. Self-destructs.
public class ChaosBubble : MonoBehaviour
{
    static readonly int _ColorID     = Shader.PropertyToID("_Color");
    static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");

    Renderer _r;
    MaterialPropertyBlock _mpb;
    Color _color;
    float _life, _rise, _wobbleAmt, _wobbleSpeed;
    float _t, _phase, _baseScale;
    Vector3 _origin;

    public void Init(float life, float rise, float wobbleAmt, float wobbleSpeed, Color color)
    {
        _life        = Mathf.Max(0.05f, life);
        _rise        = rise;
        _wobbleAmt   = wobbleAmt;
        _wobbleSpeed = wobbleSpeed;
        _color       = color;
        _origin      = transform.position;
        _baseScale   = transform.localScale.x;
        _phase       = Random.value * 6.2832f;
        _r           = GetComponent<Renderer>();
        _mpb         = new MaterialPropertyBlock();
    }

    void Update()
    {
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / _life);

        float wob = Mathf.Sin(Time.time * _wobbleSpeed + _phase) * _wobbleAmt;
        transform.position = _origin + Vector3.up * (_rise * _t) + new Vector3(wob, 0f, wob * 0.6f);

        // Grow in over the first ~85% of life, then pop (quick shrink) at the end.
        float grow = Mathf.Lerp(0.7f, 1.15f, EaseOutCubic(Mathf.Clamp01(k / 0.85f)));
        float pop  = k > 0.85f ? Mathf.Lerp(1f, 0f, (k - 0.85f) / 0.15f) : 1f;
        transform.localScale = Vector3.one * (_baseScale * grow * pop);

        if (_r != null)
        {
            float fadeIn  = k < 0.1f ? k / 0.1f : 1f;
            float alpha   = _color.a * fadeIn * (1f - k);
            Color c       = new Color(_color.r, _color.g, _color.b, alpha);
            _r.GetPropertyBlock(_mpb);
            _mpb.SetColor(_ColorID, c);
            _mpb.SetColor(_BaseColorID, c);
            _r.SetPropertyBlock(_mpb);
        }

        if (_t >= _life) Destroy(gameObject);
    }

    static float EaseOutCubic(float x) { float xm = 1f - x; return 1f - xm * xm * xm; }
}
