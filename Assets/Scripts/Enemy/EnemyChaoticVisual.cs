using UnityEngine;

// Enemy visual — abstract geometric composite that floats.
//
// Reads as a small constellation of mismatched primitives (cubes, capsules,
// cylinders, spheres) clustered around a center, each tilted at a random
// angle, each color-tinted from a palette, each bobbing on its own phase
// and spinning on its own axis. The result feels weightless and "alive"
// without any rigged animation.
//
// Cartoon outline is added by BlockOutlineApplier (set up by EnemyBaseManager
// BEFORE this component runs) — we trigger Apply() once children are built
// so every shard gets the dark inverse-hull halo. The outline is what keeps
// the enemy from melting into the painterly skybox / block background.
//
// Each enemy is unique but deterministic: RNG seeded by GetInstanceID().
public class EnemyChaoticVisual : MonoBehaviour
{
    // ────────────────────────────────────────────────────────────────────
    // Composition
    // ────────────────────────────────────────────────────────────────────

    [Header("Composition")]
    [Tooltip("Number of main geometric shards forming the body.")]
    [Range(3, 10)] public int shardCount = 5;

    [Tooltip("Number of small bright accent 'sparkles' floating around the body.")]
    [Range(0, 6)] public int accentCount = 2;

    [Tooltip("Material applied to main shards. RECOMMENDED: URP Unlit (so enemy isn't tinted by scene shadows / dark lighting). If left null we auto-create a runtime URP Unlit material — leave empty for the default unlit look.")]
    public Material shardMaterial;

    [Tooltip("Optional bright material for accent sparkles. Falls back to shardMaterial (or the runtime unlit fallback) when null.")]
    public Material accentMaterial;

    [Tooltip("If true, IGNORE any assigned Lit material and force a runtime URP Unlit fallback. Use this to keep enemies visually constant under dark scene shadows without re-authoring asset materials.")]
    public bool forceUnlit = true;

    // ────────────────────────────────────────────────────────────────────
    // Layout
    // ────────────────────────────────────────────────────────────────────

    [Header("Layout")]
    [Tooltip("Max object-space distance shards float from center.")]
    [Range(0.05f, 0.8f)] public float layoutRadius = 0.30f;

    [Tooltip("[min, max] object-space size of the main shards.")]
    public Vector2 shardSizeRange = new Vector2(0.18f, 0.32f);

    [Tooltip("[min, max] object-space size of the accent sparkles.")]
    public Vector2 accentSizeRange = new Vector2(0.06f, 0.11f);

    // ────────────────────────────────────────────────────────────────────
    // Palette
    // ────────────────────────────────────────────────────────────────────

    [Header("Palette — main shards (muted)")]
    public Color[] shardPalette =
    {
        new(0.30f, 0.78f, 0.82f), // dusty cyan
        new(0.92f, 0.55f, 0.62f), // coral pink
        new(0.78f, 0.42f, 0.52f), // raspberry
        new(0.48f, 0.85f, 0.70f), // sea mint
        new(0.60f, 0.50f, 0.85f), // lavender
        new(0.95f, 0.78f, 0.42f), // warm gold
    };

    [Header("Palette — accents (bright)")]
    public Color[] accentPalette =
    {
        new(1.00f, 0.95f, 0.55f), // bright lemon
        new(1.00f, 0.55f, 0.75f), // hot pink
        new(0.55f, 1.00f, 1.00f), // ice cyan
    };

    // ────────────────────────────────────────────────────────────────────
    // Animation
    // ────────────────────────────────────────────────────────────────────

    [Header("Animation")]
    [Tooltip("Root yaw rotation (deg/sec). Slow spin of the whole composite.")]
    public float bodyRotSpeed = 10f;

    [Tooltip("Per-shard tumble speed (deg/sec). Each shard spins on its own axis.")]
    public float shardSpinSpeed = 22f;

    [Tooltip("Object-space amplitude of the bob (up-down floating motion).")]
    public float bobAmplitude = 0.05f;

    [Tooltip("Bob oscillation frequency in Hz.")]
    public float bobSpeed = 0.75f;

    [Tooltip("Optional drift around the orbit. 0 = purely bob, no orbital swirl.")]
    [Range(0, 60)] public float orbitDriftSpeed = 6f;

    [Header("Determinism")]
    [Tooltip("Each instance gets a small random body tilt so two enemies don't look identical.")]
    public bool randomizeRootTilt = true;

    // ────────────────────────────────────────────────────────────────────
    // Internals
    // ────────────────────────────────────────────────────────────────────

    Transform _body;
    Shard[]   _shards;

    struct Shard
    {
        public Transform t;
        public Vector3   basePos;     // home position before bob/drift
        public Vector3   spinAxis;    // tumble axis (unit vector)
        public float     bobPhase;    // per-shard phase offset
        public float     orbitPhase;  // per-shard orbital phase
        public float     orbitRadius; // sphere distance — used by drift
    }

    void Start()
    {
        var rng = new System.Random(GetInstanceID());

        _body = new GameObject("Body").transform;
        _body.SetParent(transform, false);

        if (randomizeRootTilt)
            _body.localRotation = Quaternion.Euler(
                (float)rng.NextDouble() * 60f - 30f,
                (float)rng.NextDouble() * 360f,
                (float)rng.NextDouble() * 60f - 30f);

        BuildShards(rng);

        // Outline goes on AFTER all children exist so it walks the full tree.
        var applier = GetComponent<BlockOutlineApplier>();
        if (applier != null) applier.Apply();
    }

    void Update()
    {
        if (_body != null)
            _body.Rotate(Vector3.up, bodyRotSpeed * Time.deltaTime, Space.Self);

        if (_shards == null) return;

        float t = Time.time;
        for (int i = 0; i < _shards.Length; i++)
        {
            var s = _shards[i];
            if (s.t == null) continue;

            // Bob — small vertical oscillation in local space.
            float bob = Mathf.Sin(t * bobSpeed * Mathf.PI * 2 + s.bobPhase) * bobAmplitude;

            // Optional gentle orbital drift around Y so shards seem to swirl.
            float driftAngle = t * orbitDriftSpeed * Mathf.Deg2Rad + s.orbitPhase;
            float c = Mathf.Cos(driftAngle), si = Mathf.Sin(driftAngle);
            Vector3 driftPos;
            driftPos.x = s.basePos.x * c - s.basePos.z * si;
            driftPos.z = s.basePos.x * si + s.basePos.z * c;
            driftPos.y = s.basePos.y;

            s.t.localPosition = driftPos + Vector3.up * bob;

            // Independent spin.
            s.t.Rotate(s.spinAxis, shardSpinSpeed * Time.deltaTime, Space.Self);
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // Build
    // ────────────────────────────────────────────────────────────────────

    void BuildShards(System.Random rng)
    {
        int total = shardCount + accentCount;
        _shards   = new Shard[total];

        for (int i = 0; i < total; i++)
        {
            bool isAccent = i >= shardCount;
            _shards[i] = BuildOneShard(rng, isAccent, i);
        }
    }

    Shard BuildOneShard(System.Random rng, bool isAccent, int idx)
    {
        // Pick primitive — accents are simpler (sphere/cube), mains can be
        // any of the elongated primitives too.
        PrimitiveType prim;
        if (isAccent)
        {
            prim = rng.NextDouble() > 0.5 ? PrimitiveType.Sphere : PrimitiveType.Cube;
        }
        else
        {
            float r = (float)rng.NextDouble();
            prim = r < 0.40f ? PrimitiveType.Cube
                 : r < 0.65f ? PrimitiveType.Capsule
                 : r < 0.85f ? PrimitiveType.Cylinder
                             : PrimitiveType.Sphere;
        }

        var go = GameObject.CreatePrimitive(prim);
        go.name = isAccent ? $"Accent_{idx}" : $"Shard_{idx}";

        var collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);

        go.transform.SetParent(_body, false);

        // ── Layout ────────────────────────────────────────────────────────
        // Mains: distributed within layoutRadius, weighted slightly inward.
        // Accents: further out (1.0–1.3 × layoutRadius) so they float around.
        float distMul = isAccent
            ? Mathf.Lerp(1.0f, 1.35f, (float)rng.NextDouble())
            : Mathf.Lerp(0.25f, 1.0f, (float)rng.NextDouble());

        Vector3 dir    = RandomDirection(rng);
        Vector3 offset = dir * layoutRadius * distMul;
        go.transform.localPosition = offset;

        // ── Tilt ──────────────────────────────────────────────────────────
        go.transform.localRotation = Quaternion.Euler(
            (float)rng.NextDouble() * 360f,
            (float)rng.NextDouble() * 360f,
            (float)rng.NextDouble() * 360f);

        // ── Scale ─────────────────────────────────────────────────────────
        Vector2 sizeRange = isAccent ? accentSizeRange : shardSizeRange;
        float size = Mathf.Lerp(sizeRange.x, sizeRange.y, (float)rng.NextDouble());
        Vector3 scale = Vector3.one * size;

        // 50% of main shards get an axis squished/stretched so the composite
        // doesn't look like a bag of equal cubes — more "shard"-like.
        if (!isAccent && rng.NextDouble() > 0.5)
        {
            int axis = rng.Next(3);
            scale[axis] *= 0.35f + (float)rng.NextDouble() * 0.50f;
        }
        go.transform.localScale = scale;

        // ── Material + color ─────────────────────────────────────────────
        var rend = go.GetComponent<Renderer>();

        // Resolve which material to use:
        //   • forceUnlit=true → always the runtime Unlit fallback (ignores
        //     whatever was assigned, even Lit materials, so shadows can't
        //     darken the enemy).
        //   • Otherwise prefer the explicit accent/shard slots; if both null
        //     fall back to the runtime Unlit material.
        Material mat;
        if (forceUnlit)
        {
            mat = GetRuntimeUnlit();
        }
        else
        {
            mat = (isAccent && accentMaterial != null) ? accentMaterial
                : (shardMaterial != null              ? shardMaterial
                                                      : GetRuntimeUnlit());
        }
        rend.sharedMaterial = mat;

        Color[] palette = isAccent ? accentPalette : shardPalette;
        Color color = (palette != null && palette.Length > 0)
            ? palette[rng.Next(palette.Length)]
            : (isAccent ? Color.white : new Color(0.5f, 0.5f, 0.6f));
        MpbColor.Set(rend, color);

        rend.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows       = false; // scene shadows are very dark — keep enemy visually constant
        rend.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        rend.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        return new Shard
        {
            t           = go.transform,
            basePos     = offset,
            bobPhase    = (float)rng.NextDouble() * Mathf.PI * 2f,
            orbitPhase  = (float)rng.NextDouble() * Mathf.PI * 2f,
            orbitRadius = offset.magnitude,
            spinAxis    = RandomDirection(rng),
        };
    }

    // Uniformly distributed point on the unit sphere.
    static Vector3 RandomDirection(System.Random rng)
    {
        float u     = (float)(rng.NextDouble() * 2.0 - 1.0);
        float theta = (float)(rng.NextDouble() * Mathf.PI * 2.0);
        float r     = Mathf.Sqrt(Mathf.Max(0f, 1f - u * u));
        return new Vector3(r * Mathf.Cos(theta), u, r * Mathf.Sin(theta));
    }

    // ────────────────────────────────────────────────────────────────────
    // Runtime URP Unlit fallback — shared across every enemy spawned in
    // this run so they batch into one draw call (MPB tints each shard).
    // ────────────────────────────────────────────────────────────────────
    static Material _runtimeUnlit;
    static Material GetRuntimeUnlit()
    {
        if (_runtimeUnlit != null) return _runtimeUnlit;
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null)
        {
            Debug.LogWarning("[EnemyChaoticVisual] URP Unlit shader not found — falling back to Standard (will still be affected by scene lighting).");
            sh = Shader.Find("Standard");
        }
        _runtimeUnlit = new Material(sh) { name = "EnemyShard_RuntimeUnlit" };
        _runtimeUnlit.SetColor("_BaseColor", Color.white);
        return _runtimeUnlit;
    }
}
