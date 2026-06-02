using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// Scene-level effect that paints a single "radiate from cube center"
// animation when an EnlightenmentRule is active — regardless of how many
// cells / pieces make up the cube. Replaces the per-face shader animation
// (which read as 6 × N independent small animations) with one unified
// 3-D pulse anchored at the cube's geometric center.
//
// Architecture:
//   • Subscribes to SynergyEvaluator.OnClaimChanged
//   • Activates when an EnlightenmentRule fires; reads its CurrentCubeOrigin
//     and CurrentCubeSide each frame for live position + size
//   • Spawns N wireframe-cube "frames" that scale outward from 0 → maxScale
//     over `duration`, staggered by phase, looping
//   • Vertex color + MPB alpha drive a fade-out toward the end of each cycle
//
// Drop this MonoBehaviour onto any persistent GameObject in the scene.
// No inspector wiring needed.
public class EnlightenmentCenterFX : MonoBehaviour
{
    [Header("Frames")]
    [Tooltip("Number of overlapping pulse frames (staggered phases). 4–6 reads as continuous emission.")]
    [Range(1, 8)] public int frameCount = 5;

    [Header("Animation")]
    [Min(0.2f)] public float duration   = 2.4f;
    [Tooltip("Final scale as a multiple of the cube's actual side. 1.0 = matches cube; 1.4 = pulses beyond the cube outline.")]
    [Range(0.5f, 3f)] public float maxScaleMul = 1.35f;

    [Header("Look")]
    public Color frameColor       = new(0.30f, 0.65f, 1.00f, 1f);
    [Min(0.005f)] public float frameLineWidth = 0.06f;
    [Tooltip("Slow rotation of the whole pulse cluster around its Y axis.")]
    public float clusterSpinSpeed = 12f;

    [Tooltip("Frames fade to alpha=0 over the last `fadeOutTail` portion of their cycle (1.0 = whole cycle fading, 0.3 = only last 30%).")]
    [Range(0.05f, 1f)] public float fadeOutTail = 0.55f;

    // ── Runtime ─────────────────────────────────────────────────────────

    sealed class Frame
    {
        public GameObject obj;
        public MeshRenderer renderer;
        public MaterialPropertyBlock mpb;
        public float phase;
    }

    EnlightenmentRule _activeRule;
    GameObject        _root;
    readonly List<Frame> _frames = new();
    Mesh _sharedWireMesh;
    Material _sharedMat;

    static readonly int _ColorID = Shader.PropertyToID("_Color");

    // ── Lifecycle ───────────────────────────────────────────────────────

    void Awake() => TryHook();
    void Start() => TryHook();

    void OnDisable()
    {
        if (SynergyEvaluator.Instance != null)
            SynergyEvaluator.Instance.OnClaimChanged -= HandleClaimChanged;
        Teardown();
    }

    void OnDestroy() => Teardown();

    void TryHook()
    {
        if (SynergyEvaluator.Instance == null) return;
        SynergyEvaluator.Instance.OnClaimChanged -= HandleClaimChanged;
        SynergyEvaluator.Instance.OnClaimChanged += HandleClaimChanged;
    }

    void HandleClaimChanged(SynergyRule rule, ActiveSynergy active)
    {
        if (!(rule is EnlightenmentRule er)) return;

        if (active == null)
        {
            // Rule revoked — tear down FX.
            _activeRule = null;
            Teardown();
            return;
        }

        // Rule active (or absorb'd to a bigger cube). Rebuild lazily.
        _activeRule = er;
        if (_root == null) Build();
    }

    // ── Frame-by-frame ──────────────────────────────────────────────────

    void Update()
    {
        if (_activeRule == null || _root == null) return;
        var grid = GridSystem.instance;
        if (grid == null) return;

        int side = _activeRule.CurrentCubeSide;
        if (side <= 0) return;

        // Position root at cube geometric center.
        // For a side=N cube starting at cell origin O, the cells span
        // [O, O+N-1]. Cell world center for cell C is GridToWorld(C); cube
        // geometric center = GridToWorld(O) + (N-1)/2 × cellSize.
        Vector3 originW = grid.GridToWorld(_activeRule.CurrentCubeOrigin);
        Vector3 centerW = originW + Vector3.one * (side - 1) * 0.5f * grid.cellSize;
        _root.transform.position = centerW;

        // Slow cluster spin.
        if (clusterSpinSpeed != 0f)
            _root.transform.localRotation *= Quaternion.AngleAxis(clusterSpinSpeed * Time.deltaTime, Vector3.up);

        // Per-frame scale + alpha.
        float worldSide = side * grid.cellSize;
        for (int i = 0; i < _frames.Count; i++)
        {
            var f = _frames[i];
            if (f.obj == null) continue;

            float u = Mathf.Repeat(Time.time / Mathf.Max(0.01f, duration) + f.phase, 1f);

            // Scale ramps 0 → maxScaleMul × worldSide over the cycle.
            float scale = u * maxScaleMul * worldSide;
            f.obj.transform.localScale = Vector3.one * scale;

            // Fade-out: alpha = 1 in the early phase, ramps down to 0 in
            // the last `fadeOutTail` portion. Pre-fades start when cycle
            // remaining < fadeOutTail.
            float fadeStart = 1f - fadeOutTail;
            float a;
            if (u < fadeStart) a = 1f;
            else               a = Mathf.SmoothStep(1f, 0f, (u - fadeStart) / fadeOutTail);

            var c = frameColor; c.a *= a;
            f.mpb.SetColor(_ColorID, c);
            f.renderer.SetPropertyBlock(f.mpb);
        }
    }

    // ── Build / teardown ────────────────────────────────────────────────

    void Build()
    {
        Teardown();
        _root = new GameObject("EnlightenmentCenterFX");
        _root.transform.SetParent(transform, worldPositionStays: false);

        _sharedWireMesh = BuildUnitWireCube(frameColor);
        _sharedMat      = MakeLineMaterial();

        for (int i = 0; i < frameCount; i++)
        {
            var fo = new GameObject($"Pulse_{i}");
            fo.transform.SetParent(_root.transform, false);

            var mf = fo.AddComponent<MeshFilter>();
            mf.sharedMesh = _sharedWireMesh;
            var mr = fo.AddComponent<MeshRenderer>();
            mr.sharedMaterial      = _sharedMat;
            mr.shadowCastingMode   = ShadowCastingMode.Off;
            mr.receiveShadows      = false;
            mr.lightProbeUsage     = LightProbeUsage.Off;
            mr.reflectionProbeUsage= ReflectionProbeUsage.Off;

            _frames.Add(new Frame
            {
                obj      = fo,
                renderer = mr,
                mpb      = new MaterialPropertyBlock(),
                phase    = (float)i / frameCount,
            });
        }
    }

    void Teardown()
    {
        if (_root != null) Destroy(_root);
        _root = null;
        _frames.Clear();
        if (_sharedMat != null)     { Destroy(_sharedMat);     _sharedMat = null; }
        if (_sharedWireMesh != null){ Destroy(_sharedWireMesh); _sharedWireMesh = null; }
    }

    // 12-edge unit cube centered at origin with line topology.
    Mesh BuildUnitWireCube(Color col)
    {
        var m = new Mesh { name = "EnlightenmentWireCube" };
        var verts = new Vector3[8]
        {
            new(-0.5f, -0.5f, -0.5f), new( 0.5f, -0.5f, -0.5f),
            new( 0.5f,  0.5f, -0.5f), new(-0.5f,  0.5f, -0.5f),
            new(-0.5f, -0.5f,  0.5f), new( 0.5f, -0.5f,  0.5f),
            new( 0.5f,  0.5f,  0.5f), new(-0.5f,  0.5f,  0.5f),
        };
        var indices = new int[24]
        {
            0,1, 1,2, 2,3, 3,0,   // back face
            4,5, 5,6, 6,7, 7,4,   // front face
            0,4, 1,5, 2,6, 3,7,   // connecting edges
        };
        var colors = new Color[8];
        for (int i = 0; i < 8; i++) colors[i] = col;

        m.SetVertices(verts);
        m.SetColors(colors);
        m.SetIndices(indices, MeshTopology.Lines, 0);
        // Force huge bounds so frustum culling doesn't drop it at oblique angles.
        m.bounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
        return m;
    }

    Material MakeLineMaterial()
    {
        var sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Color");
        return new Material(sh) { hideFlags = HideFlags.DontSave };
    }
}
