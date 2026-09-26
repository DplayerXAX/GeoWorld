using System.Collections.Generic;
using UnityEngine;

// Scatters woodland around the board, in depth bands, without ever touching the
// board itself.
//
// ── THE ONE HARD RULE ────────────────────────────────────────────────────────
//
// Nothing spawned here gets a Collider. PlacementController picks its cell with
//     Physics.Raycast(ray, out RaycastHit hit)
// and NO layer mask — so it hits the first collider under the cursor, whatever it
// is. A single stray collider on a shrub would silently steal placement wherever it
// overlapped the board on screen, which is the kind of bug that gets blamed on the
// grid for a week. No colliders means the problem cannot exist, so that is the rule
// rather than a layer or a tag someone has to remember to set.
//
// Geometry is kept out of the playable footprint as well — derived from GridSystem
// so it follows the board instead of being hand-placed per scene — but that is for
// looks. The collider rule is the one that protects the game.
//
// ── DEPTH ────────────────────────────────────────────────────────────────────
//
// Three bands, and the far one is not simply the near one made smaller. Its colours
// are mixed toward DepthFog's own distance colour (_FogColor 0.70, 0.63, 0.56),
// because aerial perspective is a shift in HUE, not just a loss of contrast, and
// this game already declares that exact wash as how it draws distance.
//
// Worth being precise about, because the opposite rule holds elsewhere in this
// project: mixing a palette colour toward paper to "soften" it is wrong, since it
// drags the hue off the value the palette specifies. For distance the hue shift IS
// the effect. Baking it into the vertices also means the recession reads at any fog
// setting, and in the editor with fog off.
[DisallowMultipleComponent]
public class SceneDressing : MonoBehaviour
{
    public enum Kind { Tree, Shrub, Tuft }

    [System.Serializable]
    public class Band
    {
        public string name = "band";
        public bool   enabled = true;
        public Kind   kind = Kind.Tree;

        [Tooltip("How many to scatter.")]
        [Range(0, 200)] public int count = 18;

        [Tooltip("Distance out from the keep-out edge, in world units (min, max).")]
        public Vector2 distance = new(4f, 14f);

        [Tooltip("Uniform scale range.")]
        public Vector2 scale = new(0.8f, 1.5f);

        [Tooltip("How far toward the distance wash this band's colour is pulled. 0 = its own colour, 1 = pure fog.")]
        [Range(0f, 1f)] public float haze = 0f;

        [Tooltip("Distinct meshes built for this band. Instances differ by scale and rotation; SHAPE only differs between variants, because colour is baked per mesh and varying it per instance would need an MPB and kill SRP batching.")]
        [Range(1, 8)] public int variants = 4;

        [Tooltip("Far scenery is usually outside the shadow distance anyway, so casting is wasted work there.")]
        public bool castShadows = true;
    }

    [Header("Keep-out")]
    [Tooltip("Board bounds come from this. Leave empty to use GridSystem.instance, or none at all for manualSize.")]
    public GridSystem grid;

    [Tooltip("Used when there is no GridSystem in the scene (the level-select room, for instance): the playable footprint centred on this object, in world units.")]
    public Vector2 manualSize = new(14f, 14f);

    [Tooltip("Clear margin around the footprint. Scenery never comes closer than this.")]
    [Range(0f, 12f)] public float clearance = 2.5f;

    public float groundY = 0f;

    [Header("Framing")]
    [Tooltip("Two large plants at the board's camera-side corners, to close off the bottom of the frame. Which side that is comes from Camera.main, so it follows the scene's own setup.")]
    public bool frameForeground = true;
    [Range(0.5f, 5f)] public float frameScale = 2.2f;
    [Tooltip("How far out from the corners, in world units. Too small and they cover the board.")]
    [Range(1f, 20f)] public float frameOut = 5f;

    [Header("Bands")]
    public Band[] bands =
    {
        new() { name = "far wood",  kind = Kind.Tree,  count = 26, distance = new Vector2(16f, 34f), scale = new Vector2(1.3f, 2.4f), haze = 0.62f, castShadows = false },
        new() { name = "mid trees", kind = Kind.Tree,  count = 14, distance = new Vector2(5f, 15f),  scale = new Vector2(0.9f, 1.6f), haze = 0.22f },
        new() { name = "thicket",   kind = Kind.Shrub, count = 30, distance = new Vector2(0.5f, 7f), scale = new Vector2(0.8f, 1.5f), haze = 0.05f },
        new() { name = "scrub",     kind = Kind.Tuft,  count = 46, distance = new Vector2(0f, 4f),   scale = new Vector2(0.7f, 1.4f), haze = 0f },
    };

    [Header("Build")]
    public bool buildOnStart = true;
    public int  seed = 12345;

    // DepthFog's own distance colour. Kept in sync by hand rather than read from the
    // material, so this works in a scene that has no fog volume set up yet.
    static readonly Color Haze = new(0.70f, 0.63f, 0.56f);

    GameObject _root;
    readonly List<Mesh> _meshes = new();

    void Start() { if (buildOnStart) Build(); }
    void OnDestroy() { Clear(); }

    [ContextMenu("Build")]
    public void Build()
    {
        Clear();

        var mat = FoliageMaterial();
        if (mat == null) { Debug.LogWarning("[Dressing] No foliage material."); return; }

        _root = new GameObject("SceneDressing");
        _root.transform.SetParent(transform, false);

        GetFootprint(out Vector3 centre, out float halfX, out float halfZ);

        var rng = new System.Random(seed);
        int total = 0;

        for (int b = 0; b < bands.Length; b++)
        {
            var band = bands[b];
            if (band == null || !band.enabled || band.count <= 0) continue;

            var variants = new Mesh[Mathf.Clamp(band.variants, 1, 8)];
            for (int v = 0; v < variants.Length; v++)
            {
                var recipe = Hazed(Base(band.kind), band.haze);
                variants[v] = TreeMesh.Build(recipe, rng.Next());
                _meshes.Add(variants[v]);
            }

            for (int i = 0; i < band.count; i++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                Vector3 pos = OnRing(centre, halfX, halfZ, ang,
                                     clearance + Mathf.Lerp(band.distance.x, band.distance.y, (float)rng.NextDouble()));

                Place(variants[rng.Next(variants.Length)], mat, pos,
                      Mathf.Lerp(band.scale.x, band.scale.y, (float)rng.NextDouble()),
                      (float)rng.NextDouble() * 360f, band.castShadows, band.name);
                total++;
            }
        }

        if (frameForeground) total += BuildFrame(mat, centre, halfX, halfZ, rng);

        Debug.Log($"[Dressing] {total} props, {_meshes.Count} meshes, 1 material, 0 colliders.");
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        if (_root != null) DestroyImmediate(_root);
        _root = null;
        for (int i = 0; i < _meshes.Count; i++)
            if (_meshes[i] != null) DestroyImmediate(_meshes[i]);
        _meshes.Clear();
    }

    // Two big plants at the corners nearest the camera. They close the bottom of the
    // frame, which is what actually reads as "foreground" — a prop in the MIDDLE of
    // the near side would cover the board, so the corners are the only place a
    // foreground element can live in a game you have to see all of.
    int BuildFrame(Material mat, Vector3 centre, float halfX, float halfZ, System.Random rng)
    {
        var cam = Camera.main;
        if (cam == null) return 0;

        Vector3 toCam = cam.transform.position - centre;
        toCam.y = 0f;
        if (toCam.sqrMagnitude < 1e-4f) return 0;
        toCam.Normalize();

        Vector3 side = Vector3.Cross(toCam, Vector3.up).normalized;
        float reach = Mathf.Max(halfX, halfZ) + clearance + frameOut;

        var recipe = Base(Kind.Tree);
        recipe.leavesPerTip = Mathf.Max(3, recipe.leavesPerTip - 1);

        int n = 0;
        for (int s = -1; s <= 1; s += 2)
        {
            var mesh = TreeMesh.Build(recipe, rng.Next());
            _meshes.Add(mesh);

            Vector3 pos = centre + toCam * (Mathf.Max(halfX, halfZ) + clearance + frameOut * 0.5f)
                                 + side * (s * reach);
            Place(mesh, mat, new Vector3(pos.x, groundY, pos.z),
                  frameScale * Mathf.Lerp(0.9f, 1.15f, (float)rng.NextDouble()),
                  (float)rng.NextDouble() * 360f, true, "frame");
            n++;
        }
        return n;
    }

    void Place(Mesh mesh, Material mat, Vector3 pos, float scale, float yaw, bool shadows, string label)
    {
        var go = new GameObject(label);
        go.transform.SetParent(_root.transform, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(Random.Range(-3f, 3f), yaw, Random.Range(-3f, 3f)));
        go.transform.localScale = Vector3.one * scale;

        go.AddComponent<MeshFilter>().sharedMesh = mesh;

        // No collider. Ever. See the note at the top of the file.
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = shadows
            ? UnityEngine.Rendering.ShadowCastingMode.TwoSided
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows       = true;
        mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
    }

    // The playable footprint, in world units. GridToWorld puts cell (0,0,0) at half a
    // cell in, so the board spans [0, size*cell] on each axis.
    void GetFootprint(out Vector3 centre, out float halfX, out float halfZ)
    {
        var g = grid != null ? grid : GridSystem.instance;
        if (g != null)
        {
            halfX  = g.size.x * g.cellSize * 0.5f;
            halfZ  = g.size.z * g.cellSize * 0.5f;
            centre = new Vector3(halfX, groundY, halfZ);
            return;
        }

        halfX  = manualSize.x * 0.5f;
        halfZ  = manualSize.y * 0.5f;
        centre = new Vector3(transform.position.x, groundY, transform.position.z);
    }

    // A point `out` units beyond the footprint's RECTANGULAR edge along `ang`.
    // Measuring from the rectangle rather than from a circle around it matters on a
    // board that is not square: a circular keep-out either cuts the corners off the
    // play area or leaves a bald gap along the long sides.
    static Vector3 OnRing(Vector3 centre, float halfX, float halfZ, float ang, float outDist)
    {
        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);
        float tx = halfX / Mathf.Max(1e-4f, Mathf.Abs(ca));
        float tz = halfZ / Mathf.Max(1e-4f, Mathf.Abs(sa));
        float t  = Mathf.Min(tx, tz) + outDist;
        return centre + new Vector3(ca * t, 0f, sa * t);
    }

    static TreeMesh.Recipe Base(Kind k) => k switch
    {
        Kind.Shrub => TreeMesh.Recipe.Shrub(),
        Kind.Tuft  => TreeMesh.Recipe.Tuft(),
        _          => TreeMesh.Recipe.Tree(),
    };

    // Aerial perspective: pull the whole plant toward the distance wash, and flatten
    // its internal contrast at the same time. Distance does both — a far tree is not
    // just paler, it has also lost the difference between its bark and its leaves.
    static TreeMesh.Recipe Hazed(TreeMesh.Recipe r, float haze)
    {
        if (haze <= 0.001f) return r;
        r.bark  = Color.Lerp(r.bark,  Haze, haze);
        r.leafA = Color.Lerp(r.leafA, Haze, haze);
        r.leafB = Color.Lerp(r.leafB, Haze, haze * 1.15f);

        // Far plants also get cheaper: nobody can resolve a twig at that distance.
        if (haze > 0.4f) { r.levels = Mathf.Max(2, r.levels - 1); r.radial = 3; }
        return r;
    }

    static Material _mat;
    static Material FoliageMaterial()
    {
        if (_mat != null) return _mat;
        Shader sh = Shader.Find("GeoWorld/Foliage");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        _mat = new Material(sh) { name = "Dressing Foliage (runtime, shared)" };
        if (_mat.HasProperty("_Cull")) _mat.SetFloat("_Cull", 0f);
        return _mat;
    }
}
