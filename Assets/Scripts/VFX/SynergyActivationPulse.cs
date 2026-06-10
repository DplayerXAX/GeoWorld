using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// One-shot "activation pulse": when ANY synergy rule activates (and optionally
// when it tiers up), spawn a semi-transparent GHOST copy of the activated
// blocks that inflates outward from the cluster center and fades away — a quick
// outward pulse that reads as "this synergy just fired".
//
// HOW IT TRIGGERS:
//   Hooks SynergyEvaluator.OnTierChanged, which fires (rule, 0, >0) on the
//   inactive→active transition and (rule, old, new) on a tier change, but NOT
//   on same-tier claim growth (the evaluator only raises it when the tier
//   actually moves). So we pulse on activation / tier-up and never on every
//   block added to an existing cluster.
//
// HOW IT LOOKS:
//   We clone each claimed block's MeshFilters (geometry only — no scripts,
//   colliders, or original materials), give them ONE shared transparent
//   material tinted to the synergy color, and parent them under a root placed
//   at the cluster center. Scaling that root up grows every ghost AND pushes it
//   outward from the center, while the shared material's alpha fades to 0.
//   Everything is destroyed when the pulse ends, so it leaves no trace.
//
// SETUP: add this component to any always-on scene object (e.g. the
// SynergyEvaluator GameObject). No asset wiring is required — if Pulse Material
// is left empty, a URP/Unlit transparent material is built at runtime.
public class SynergyActivationPulse : MonoBehaviour
{
    [Header("Trigger")]
    [Tooltip("Also pulse when an already-active synergy tiers UP (not just on first activation).")]
    public bool pulseOnTierUp = true;

    [Header("Look")]
    [Tooltip("Optional transparent material for the ghost. If null, a URP/Unlit transparent material is created at runtime.")]
    public Material pulseMaterial;

    [Tooltip("Tint the ghost with the synergy's theme color. If off, uses Pulse Color.")]
    public bool useThemeColor = true;

    [Tooltip("Ghost color used when 'Use Theme Color' is off.")]
    public Color pulseColor = Color.white;

    [Range(0f, 1f)]
    [Tooltip("Starting opacity of the ghost (it fades to 0 over the pulse).")]
    public float startAlpha = 0.5f;

    [Header("Motion")]
    [Tooltip("How big the ghost grows relative to the original (1 = no growth).")]
    public float expandScale = 1.8f;

    [Tooltip("Seconds for the pulse to inflate + fade out.")]
    public float duration = 0.5f;

    [Tooltip("Extra world-space lift while expanding (0 = none).")]
    public float riseHeight = 0f;

    [Header("Skybox Flash")]
    [Tooltip("Also wash the background skybox toward the synergy's theme color on activation (like the damage flash, but tinted to the theme).")]
    public bool flashSkybox = true;

    [Range(0f, 1f)]
    [Tooltip("Strength of the skybox theme flash (0 = off, 1 = full).")]
    public float skyboxFlashStrength = 0.6f;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorID     = Shader.PropertyToID("_Color");

    private bool _hooked;

    private void OnEnable() => TryHook();
    private void Start()    => TryHook();

    private void OnDisable()
    {
        if (SynergyEvaluator.Instance != null)
            SynergyEvaluator.Instance.OnTierChanged -= HandleTierChanged;
        _hooked = false;
    }

    private void TryHook()
    {
        if (_hooked || SynergyEvaluator.Instance == null) return;
        SynergyEvaluator.Instance.OnTierChanged -= HandleTierChanged;
        SynergyEvaluator.Instance.OnTierChanged += HandleTierChanged;
        _hooked = true;
    }

    private void HandleTierChanged(SynergyRule rule, int oldTier, int newTier)
    {
        if (rule == null) return;

        // Activation (0 → >0) always; tier-ups optionally. Never on revoke.
        bool activated = oldTier == 0 && newTier > 0;
        bool tieredUp  = pulseOnTierUp && oldTier > 0 && newTier > oldTier;
        if (!activated && !tieredUp) return;

        var active = FindActive(rule);
        if (active == null) return;

        SpawnPulse(active);

        // Wash the background skybox toward this theme's color (mirrors the
        // monster-damage flash, but tinted to the synergy instead of red).
        if (flashSkybox && BackgroundReactor.Instance != null)
            BackgroundReactor.Instance.TriggerThemeFlash(
                BlockColorPalette.Get(active.rule.color), skyboxFlashStrength);
    }

    private static ActiveSynergy FindActive(SynergyRule rule)
    {
        var ev = SynergyEvaluator.Instance;
        if (ev == null) return null;
        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
            if (actives[i] != null && actives[i].rule == rule) return actives[i];
        return null;
    }

    private void SpawnPulse(ActiveSynergy active)
    {
        var grid = GridSystem.instance;
        if (grid == null || active.claimedPieces == null) return;

        // Build the ghost from grid CELLS, not the live block meshes. The block
        // that COMPLETES a synergy is momentarily at localScale 0 when this
        // fires: PlacementController zeroes the new block, runs OnPiecePlaced
        // (which raises OnTierChanged → this pulse) synchronously, and only THEN
        // starts GrowIn to animate the scale up. Cloning that block's mesh would
        // capture lossyScale 0 → an invisible ghost for the final block (the
        // user's bug: "the last placed block doesn't pulse"). Grid cells are
        // always full size, so every claimed block pulses.
        var cells = new HashSet<Vector3Int>();
        Vector3 sum = Vector3.zero;
        foreach (var p in active.claimedPieces)
        {
            if (p?.cells == null) continue;
            for (int i = 0; i < p.cells.Length; i++)
                if (cells.Add(p.cells[i])) sum += grid.GridToWorld(p.cells[i]);
        }

        if (cells.Count == 0) return;
        Vector3 center = sum / cells.Count;

        var cubeMesh = GetCubeMesh();
        if (cubeMesh == null) return;

        // One shared transparent material for the whole pulse (fades as a group).
        Color tint = useThemeColor ? BlockColorPalette.Get(active.rule.color) : pulseColor;
        tint.a = startAlpha;
        Material mat = pulseMaterial != null ? new Material(pulseMaterial) : CreateRuntimeTransparent();
        SetColor(mat, tint);

        var root = new GameObject("SynergyActivationPulse");
        root.transform.position = center;

        float cube = grid.cellSize;
        foreach (var c in cells)
        {
            var ghost = new GameObject("PulseGhost");
            // Parent first (root scale is 1 here) so localScale == world scale.
            ghost.transform.SetParent(root.transform, false);
            ghost.transform.position   = grid.GridToWorld(c);
            ghost.transform.localScale = new Vector3(cube, cube, cube);

            ghost.AddComponent<MeshFilter>().sharedMesh = cubeMesh;
            var mr = ghost.AddComponent<MeshRenderer>();
            mr.sharedMaterial      = mat;
            mr.shadowCastingMode   = ShadowCastingMode.Off;
            mr.receiveShadows      = false;
            mr.lightProbeUsage     = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        StartCoroutine(Animate(root, mat, tint));
    }

    // Cache a shared unit cube mesh (1×1×1) for every ghost. Built by spawning a
    // throwaway primitive and keeping its sharedMesh (a built-in Unity asset that
    // survives the temp GameObject's destruction).
    private static Mesh _cubeMesh;
    private static Mesh GetCubeMesh()
    {
        if (_cubeMesh != null) return _cubeMesh;
        var temp = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mf = temp.GetComponent<MeshFilter>();
        _cubeMesh = mf != null ? mf.sharedMesh : null;
        if (Application.isPlaying) Destroy(temp); else DestroyImmediate(temp);
        return _cubeMesh;
    }

    private IEnumerator Animate(GameObject root, Material mat, Color tint)
    {
        float dur     = Mathf.Max(0.0001f, duration);
        float target  = Mathf.Max(1f, expandScale);
        Vector3 basePos = root.transform.position;

        float t = 0f;
        while (t < dur && root != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);

            // Ease-out so it bursts fast then settles.
            float ease = 1f - (1f - k) * (1f - k);
            float s    = Mathf.LerpUnclamped(1f, target, ease);
            root.transform.localScale = new Vector3(s, s, s);
            root.transform.position   = basePos + Vector3.up * (riseHeight * k);

            tint.a = startAlpha * (1f - k);
            SetColor(mat, tint);
            yield return null;
        }

        if (root != null) Destroy(root);
        if (mat  != null) Destroy(mat);
    }

    private static void SetColor(Material m, Color c)
    {
        if (m == null) return;
        if (m.HasProperty(BaseColorID)) m.SetColor(BaseColorID, c);
        if (m.HasProperty(ColorID))     m.SetColor(ColorID, c);
    }

    // Build a transparent (alpha-blended) URP/Unlit material at runtime so the
    // effect works with zero asset wiring. Override via Pulse Material for a
    // custom look (additive glow, fresnel, etc.).
    private static Material CreateRuntimeTransparent()
    {
        var sh = Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) sh = Shader.Find("Sprites/Default");
        var m = new Material(sh);

        if (m.HasProperty("_Surface"))  m.SetFloat("_Surface", 1f);   // 0 opaque, 1 transparent
        if (m.HasProperty("_Blend"))    m.SetFloat("_Blend", 0f);     // 0 alpha
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (m.HasProperty("_ZWrite"))   m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)RenderQueue.Transparent;
        return m;
    }
}
