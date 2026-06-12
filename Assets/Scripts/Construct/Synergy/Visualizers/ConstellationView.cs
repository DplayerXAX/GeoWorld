using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Per-cluster "star-sphere" renderer.
//
// A SynergyVisualizer is a ScriptableObject and can't run per-frame logic, so
// EnlightenmentConstellationVisualizer spawns one of these on a fresh
// GameObject and hands it the star positions (sampled on a sphere AROUND the
// cube) + links via Build(). From then on THIS component owns the figure's
// frame life: it billboards + twinkles each glowing star, draws the connecting
// mesh, and slowly tumbles the whole sphere around the blocks. Destroying the
// GameObject tears everything down (OnDestroy → Clear).
//
// Rendering — the project's proven Sprites/Default path (URP-compatible,
// vertex-color, premultiplied alpha; identical to EnlightenmentCenterFX):
//   • A bright near-white star core reads as a glowing point; HDR star colors
//     (starBrightness > 1) bloom when a URP Bloom override is present.
//   • A shared spiked-star sprite (cross + diagonal flares) for every star;
//     ONE per-view line mesh (its verts are this figure's world points).
//   • MaterialPropertyBlock _BaseColor/_Color drives per-renderer color + alpha
//     (twinkle / fade) without instancing the shared materials.
//   • Huge mesh bounds so frustum culling never drops the figure at an angle.
//
// Stars + lines live in the root's local space (each at worldPoint - center).
// Tumbling the root orbits stars AND lines together around the cube center,
// keeping links attached. Each star then overrides its own world rotation every
// frame to face the camera (billboard) — that only affects facing, not its
// orbiting position, so the sphere reads as turning while every star stays
// face-on and bright.
[DisallowMultipleComponent]
public class ConstellationView : MonoBehaviour
{
    // ── Animation config (set by the visualizer before Build) ───────────────
    public float twinkleSpeed   = 3f;
    [Range(0f, 1f)] public float twinkleDepth = 0.5f;
    public float spinSpeed      = 24f;    // deg/sec around Y (orbit around the cube)
    public float tiltSpeed      = 6f;     // deg/sec around X (tumble, for a 3-D feel)
    public float bobAmplitude   = 0f;     // world units (usually 0 for an orbiting sphere)
    public float bobSpeed       = 1f;
    public float fadeInDuration = 0.6f;

    sealed class Star
    {
        public Transform             t;
        public MeshRenderer          mr;
        public MaterialPropertyBlock mpb;
        public float                 phase;   // twinkle phase offset
    }

    readonly List<Star> _stars = new();

    GameObject            _lineObj;
    MeshRenderer          _lineMr;
    MaterialPropertyBlock _lineMpb;
    Mesh                  _lineMesh;   // per-view (owns its verts) — destroyed on teardown

    Vector3 _center;
    Color   _starColor;
    Color   _lineColor;
    float   _starSize;
    float   _born;
    bool    _built;

    static readonly int _ColorID     = Shader.PropertyToID("_Color");      // Sprites/Default
    static readonly int _BaseColorID = Shader.PropertyToID("_BaseColor");  // URP/Unlit

    // Build (or rebuild) the figure. Star positions are world-space; `center`
    // is the cube center the sphere tumbles around. `starSizeWorld` is the quad
    // size in world units (the visualizer already multiplied by cell size).
    public void Build(Vector3 center, Vector3[] starsWorld, List<Vector2Int> edges,
                      Color starColor, Color lineColor, float starSizeWorld)
    {
        Clear();

        _center    = center;
        _starColor = starColor;
        _lineColor = lineColor;
        _starSize  = Mathf.Max(0.001f, starSizeWorld);
        _born      = Time.time;

        transform.position      = center;
        transform.localRotation = Quaternion.identity;

        if (starsWorld != null && starsWorld.Length > 0)
        {
            BuildLines(center, starsWorld, edges);
            BuildStars(center, starsWorld);
        }

        _built = true;
    }

    // ── Combat-ripple replay: re-fade-in in sync with the block sprout ───────
    void OnEnable()  { SynergyVisualFX.OnReplayGrowIn += HandleReplay; }
    void OnDisable() { SynergyVisualFX.OnReplayGrowIn -= HandleReplay; }

    void HandleReplay(System.Func<Vector3, float> delayFor)
    {
        if (!_built) return;
        float d = delayFor != null ? Mathf.Max(0f, delayFor(transform.position)) : 0f;
        _born = Time.time + d;   // alpha collapses, then fades back in when the ripple arrives
    }

    void Update()
    {
        if (!_built) return;

        float fade = fadeInDuration > 0.0001f
            ? Mathf.Clamp01((Time.time - _born) / fadeInDuration)
            : 1f;

        // Tumble (+ optional bob) the whole sphere around the cube center.
        float bob = bobAmplitude != 0f ? Mathf.Sin(Time.time * bobSpeed) * bobAmplitude : 0f;
        transform.position = _center + Vector3.up * bob;
        if (spinSpeed != 0f)
            transform.localRotation *= Quaternion.AngleAxis(spinSpeed * Time.deltaTime, Vector3.up);
        if (tiltSpeed != 0f)
            transform.localRotation *= Quaternion.AngleAxis(tiltSpeed * Time.deltaTime, Vector3.right);

        // Links: fade alpha via MPB (rgb carried by the color).
        if (_lineMr != null)
        {
            Color lc = _lineColor; lc.a *= fade;
            if (_lineMpb == null) _lineMpb = new MaterialPropertyBlock();
            _lineMr.GetPropertyBlock(_lineMpb);
            _lineMpb.SetColor(_ColorID, lc);
            _lineMpb.SetColor(_BaseColorID, lc);
            _lineMr.SetPropertyBlock(_lineMpb);
        }

        // Stars: billboard to the camera, then twinkle WITHOUT dimming to zero —
        // the modulation rides a high baseline so additive stars stay bright.
        var cam = Camera.main;
        Quaternion face = cam != null ? cam.transform.rotation : transform.rotation;
        for (int i = 0; i < _stars.Count; i++)
        {
            var s = _stars[i];
            if (s.t == null) continue;

            s.t.rotation = face;   // world-facing; overrides inherited tumble for facing only

            // tw ∈ [1 - twinkleDepth, 1]  → never fully dark.
            float wave = 0.5f * (1f + Mathf.Sin(Time.time * twinkleSpeed + s.phase));
            float tw   = 1f - twinkleDepth + twinkleDepth * wave;
            Color sc = _starColor; sc.a *= fade * tw;

            if (s.mpb == null) s.mpb = new MaterialPropertyBlock();
            s.mr.GetPropertyBlock(s.mpb);
            s.mpb.SetColor(_ColorID, sc);
            s.mpb.SetColor(_BaseColorID, sc);
            s.mr.SetPropertyBlock(s.mpb);
        }
    }

    // ── Build helpers ───────────────────────────────────────────────────────

    void BuildStars(Vector3 center, Vector3[] starsWorld)
    {
        Mesh     mesh = GetQuadMesh();
        Material mat  = GetStarMaterial();
        const float golden = 2.39996323f;   // golden angle (rad) — decorrelated twinkle phases

        for (int i = 0; i < starsWorld.Length; i++)
        {
            var go = new GameObject("Star");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = starsWorld[i] - center;
            go.transform.localScale    = Vector3.one * _starSize;

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            ConfigRenderer(mr);

            _stars.Add(new Star
            {
                t     = go.transform,
                mr    = mr,
                mpb   = new MaterialPropertyBlock(),
                phase = i * golden,
            });
        }
    }

    void BuildLines(Vector3 center, Vector3[] starsWorld, List<Vector2Int> edges)
    {
        if (edges == null || edges.Count == 0) return;

        int n = starsWorld.Length;
        var verts = new Vector3[n];
        var cols  = new Color[n];
        for (int i = 0; i < n; i++)
        {
            verts[i] = starsWorld[i] - center;   // local space (root sits at center)
            cols[i]  = Color.white;              // rgb carried by MPB color; keep verts neutral
        }

        var idx = new int[edges.Count * 2];
        int k = 0;
        for (int i = 0; i < edges.Count; i++)
        {
            int a = Mathf.Clamp(edges[i].x, 0, n - 1);
            int b = Mathf.Clamp(edges[i].y, 0, n - 1);
            idx[k++] = a; idx[k++] = b;
        }

        _lineMesh = new Mesh { name = "ConstellationLines" };
        _lineMesh.SetVertices(verts);
        _lineMesh.SetColors(cols);
        _lineMesh.SetIndices(idx, MeshTopology.Lines, 0);
        _lineMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

        _lineObj = new GameObject("Lines");
        _lineObj.transform.SetParent(transform, false);
        _lineObj.transform.localPosition = Vector3.zero;
        _lineObj.AddComponent<MeshFilter>().sharedMesh = _lineMesh;
        _lineMr = _lineObj.AddComponent<MeshRenderer>();
        _lineMr.sharedMaterial = GetLineMaterial();
        ConfigRenderer(_lineMr);
        _lineMpb = new MaterialPropertyBlock();
    }

    static void ConfigRenderer(MeshRenderer mr)
    {
        mr.shadowCastingMode    = ShadowCastingMode.Off;
        mr.receiveShadows       = false;
        mr.lightProbeUsage      = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
    }

    // ── Teardown ────────────────────────────────────────────────────────────

    void OnDestroy() => Clear();

    void Clear()
    {
        _built = false;

        for (int i = 0; i < _stars.Count; i++)
            if (_stars[i] != null && _stars[i].t != null) Destroy(_stars[i].t.gameObject);
        _stars.Clear();

        if (_lineObj != null) { Destroy(_lineObj); _lineObj = null; }
        _lineMr = null;
        if (_lineMesh != null) { Destroy(_lineMesh); _lineMesh = null; }
    }

    // ── Shared (static) GPU assets — built once, reused by every view ────────
    // hideFlags.DontSave keeps them out of the scene/asset DB; they live for the
    // process and are intentionally never destroyed per-view.

    static Mesh      _quadMesh;
    static Material  _starMat;
    static Material  _lineMat;
    static Texture2D _starTex;

    static Mesh GetQuadMesh()
    {
        if (_quadMesh != null) return _quadMesh;
        _quadMesh = new Mesh { name = "ConstellationStarQuad" };
        _quadMesh.SetVertices(new List<Vector3>
        {
            new(-0.5f, -0.5f, 0f), new(0.5f, -0.5f, 0f),
            new( 0.5f,  0.5f, 0f), new(-0.5f, 0.5f, 0f),
        });
        _quadMesh.SetUVs(0, new List<Vector2>
        {
            new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f),
        });
        _quadMesh.SetColors(new List<Color> { Color.white, Color.white, Color.white, Color.white });
        _quadMesh.SetTriangles(new int[] { 0, 1, 2, 0, 2, 3 }, 0);
        _quadMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return _quadMesh;
    }

    static Material GetStarMaterial()
    {
        if (_starMat != null) return _starMat;
        _starMat = MakeSpriteMat();
        _starMat.mainTexture = GetStarTexture();
        return _starMat;
    }

    static Material GetLineMaterial()
    {
        if (_lineMat != null) return _lineMat;
        _lineMat = MakeSpriteMat();   // no texture → solid 1px links
        return _lineMat;
    }

    // Sprites/Default: URP-compatible, vertex-color, premultiplied-alpha blend
    // (One OneMinusSrcAlpha) with Cull Off — the SAME shader EnlightenmentCenterFX
    // uses, so it's guaranteed to render in this pipeline. The bright near-white
    // star core reads as a glowing point; HDR star colors bloom IF a URP Bloom
    // override is present. Falls back to URP/Unlit, then Unlit/Transparent.
    static Material MakeSpriteMat()
    {
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        return new Material(sh) { hideFlags = HideFlags.DontSave };
    }

    // Spiked "tech star": a soft diamond body with a sharp 4-point cross flare
    // and fainter diagonal flares. (Built from the user's authored kernel.)
    static Texture2D GetStarTexture()
    {
        if (_starTex != null) return _starTex;

        const int N = 64;
        _starTex = new Texture2D(N, N, TextureFormat.RGBA32, false)
        {
            hideFlags = HideFlags.DontSave,
            wrapMode  = TextureWrapMode.Clamp,
        };

        var pixels = new Color[N * N];
        Vector2 center = new(N * 0.5f, N * 0.5f);
        float maxR = N * 0.5f;

        for (int y = 0; y < N; y++)
        {
            for (int x = 0; x < N; x++)
            {
                Vector2 p = new(x + 0.5f, y + 0.5f);
                float dx = Mathf.Abs(p.x - center.x);
                float dy = Mathf.Abs(p.y - center.y);

                float diamond = (dx + dy) / maxR;
                float body    = Mathf.Clamp01(1f - diamond);
                body *= body;

                float spikeX = Mathf.Pow(Mathf.Clamp01(1f - dx / (maxR * 0.12f)), 4f);
                float spikeY = Mathf.Pow(Mathf.Clamp01(1f - dy / (maxR * 0.12f)), 4f);

                float spikeDiagA = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(dx - dy)        / (maxR * 0.15f)), 6f);
                float spikeDiagB = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(dx + dy - maxR) / (maxR * 0.15f)), 6f);

                float glow = body * 0.7f
                           + spikeX * 0.6f + spikeY * 0.6f
                           + spikeDiagA * 0.3f + spikeDiagB * 0.3f;
                glow = Mathf.Clamp01(glow);

                pixels[y * N + x] = new Color(1f, 1f, 1f, glow);
            }
        }

        _starTex.SetPixels(pixels);
        _starTex.Apply();
        return _starTex;
    }
}
