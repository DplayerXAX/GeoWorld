using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

// Graphics presets, the way a finished game offers them: Low, Medium, High and
// Ultra. Ultra is the game exactly as it is drawn now; each step down gives
// some of it up for frame rate on weaker machines.
//
// A preset reaches three layers:
//   Pipeline  Which Unity quality level (the PC or Mobile URP asset), then
//             render scale, MSAA, HDR, shadow distance and cascades on it.
//             Applied live.
//   Scene     The sun's shadows (soft, hard or none) and post-processing on
//             the cameras. Applied live, and again on every scene load.
//   Content   How much the game builds and moves: particle rates, far-scenery
//             detail, the painted sky's stroke layers, fog raymarch steps,
//             decor animation and counts. Read as a scene builds, so these
//             take effect from the next area loaded (the sky updates live).
//
// Pipeline values are always derived from the asset's own, captured the first
// time it is touched, so Ultra puts back precisely what the asset says. In the
// editor those originals are restored on leaving Play, so playing never leaves
// the project's URP assets or quality level changed.
public static class GraphicsQuality
{
    public enum Tier { Low, Medium, High, Ultra }

    public static readonly string[] Names = { "Low", "Medium", "High", "Ultra" };
    public static readonly string[] Descriptions =
    {
        "Lower resolution, no shadows or post effects, still scenery and sparse effects. For weak devices.",
        "Slightly lower resolution, hard shadows, lighter scenery, fewer particles and simpler sky.",
        "Full resolution and soft shadows, a little less scenery and fewer particles.",
        "RECOMMENDED. Everything as designed.",
    };

    public static Tier Current { get; private set; } = Tier.Ultra;

    /// <summary>Raised after a preset is applied. Live systems (the sky) refresh from it.</summary>
    public static event Action Changed;

    // ── Content budgets ──────────────────────────────────────────────────────
    /// <summary>Multiplier on particle emission and caps.</summary>
    public static float Particles => Pick(0.25f, 0.5f, 0.75f, 1f);
    /// <summary>Multiplier on how much scenery and decor gets built.</summary>
    public static float Detail    => Pick(0.3f, 0.55f, 0.8f, 1f);
    /// <summary>Multiplier on volumetric fog raymarch steps.</summary>
    public static float FogSteps  => Pick(0.45f, 0.6f, 0.8f, 1f);
    /// <summary>Stroke layers in the painted sky.</summary>
    public static int   SkyLayers => Current >= Tier.High ? 2 : 1;
    /// <summary>Spacing of the far land's heightfield, in cells.</summary>
    public static float BackdropGrid => Pick(2.6f, 2.0f, 1.5f, 1.5f);
    /// <summary>Ambient motion: smoke, bobbing, swaying, flicker. Machinery keeps turning regardless.</summary>
    public static bool  AmbientMotion => Current != Tier.Low;
    /// <summary>Trees and plants cast shadows.</summary>
    public static bool  TreeShadows   => Current >= Tier.High;
    /// <summary>Blooming trees grow branch by branch (VineEffect) rather than popping in.</summary>
    public static bool  GrowingTrees  => Current != Tier.Low;

    /// <summary>`n` scaled by Detail, rounded, never below `min` (and 0 stays 0).</summary>
    public static int Scaled(int n, int min = 0) => n <= 0 ? n : Mathf.Max(min, Mathf.RoundToInt(n * Detail));

    static float Pick(float low, float medium, float high, float ultra) => Current switch
    {
        Tier.Low    => low,
        Tier.Medium => medium,
        Tier.High   => high,
        _           => ultra,
    };

    /// <summary>A starting preset for this machine, used until the player picks one.</summary>
    public static Tier Recommended()
    {
        if (Application.isMobilePlatform) return SystemInfo.systemMemorySize >= 6000 ? Tier.Medium : Tier.Low;
        int vram = SystemInfo.graphicsMemorySize;
        if (vram >= 4000) return Tier.Ultra;
        if (vram >= 2000) return Tier.High;
        return Tier.Medium;
    }

    public static void Set(Tier tier)
    {
        Current = tier;
        ApplyPipeline();
        ApplyScene();
        Changed?.Invoke();
    }

    // ── Pipeline ─────────────────────────────────────────────────────────────

    struct Orig { public float scale, shadowDistance; public int msaa, cascades; public bool hdr; }
    static readonly Dictionary<UniversalRenderPipelineAsset, Orig> _orig = new();
    static int _bootLevel = -1;

    // Low and Medium run on the Mobile URP asset, High and Ultra on the PC one.
    static int LevelFor(Tier tier)
    {
        string want = tier >= Tier.High ? "PC" : "Mobile";
        int i = Array.IndexOf(QualitySettings.names, want);
        return i >= 0 ? i : QualitySettings.GetQualityLevel();
    }

    static void ApplyPipeline()
    {
        if (_bootLevel < 0) _bootLevel = QualitySettings.GetQualityLevel();
        int level = LevelFor(Current);
        if (level != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(level, true);

        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urp == null) return;
        if (!_orig.TryGetValue(urp, out var o))
        {
            o = new Orig
            {
                scale = urp.renderScale, msaa = urp.msaaSampleCount, shadowDistance = urp.shadowDistance,
                cascades = urp.shadowCascadeCount, hdr = urp.supportsHDR,
            };
            _orig[urp] = o;
        }

        urp.renderScale        = o.scale * Pick(0.7f, 0.85f, 1f, 1f);
        urp.msaaSampleCount    = Current == Tier.Ultra ? o.msaa : Current == Tier.Low ? 1 : Mathf.Min(o.msaa, 2);
        urp.shadowDistance     = o.shadowDistance * Pick(0.5f, 0.7f, 0.85f, 1f);
        urp.shadowCascadeCount = Current >= Tier.High ? o.cascades : 1;
        urp.supportsHDR        = Current != Tier.Low && o.hdr;
    }

    // ── Scene ────────────────────────────────────────────────────────────────

    static readonly Dictionary<Light, LightShadows> _shadowOrig = new();
    static readonly Dictionary<UniversalAdditionalCameraData, bool> _postOrig = new();

    /// <summary>Sun shadows and camera post-processing for the preset. Safe to call any time.</summary>
    public static void ApplyScene()
    {
        Purge(_shadowOrig);
        Purge(_postOrig);

        foreach (var l in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (l.type != LightType.Directional) continue;
            if (!_shadowOrig.TryGetValue(l, out var s)) _shadowOrig[l] = s = l.shadows;
            l.shadows = Current switch
            {
                Tier.Low    => LightShadows.None,
                Tier.Medium => s == LightShadows.None ? LightShadows.None : LightShadows.Hard,
                _           => s,
            };
        }

        foreach (var cam in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
        {
            if (!cam.TryGetComponent<UniversalAdditionalCameraData>(out var data)) continue;
            if (!_postOrig.TryGetValue(data, out var post)) _postOrig[data] = post = data.renderPostProcessing;
            data.renderPostProcessing = Current != Tier.Low && post;
        }
    }

    // Objects from scenes already unloaded.
    static void Purge<T, V>(Dictionary<T, V> map) where T : UnityEngine.Object
    {
        List<T> dead = null;
        foreach (var k in map.Keys) if (k == null) (dead ??= new List<T>()).Add(k);
        if (dead != null) foreach (var k in dead) map.Remove(k);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        _orig.Clear();
        _shadowOrig.Clear();
        _postOrig.Clear();
        _bootLevel = -1;
        Changed = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeChanged;
#endif
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ApplyScene();

#if UNITY_EDITOR
    // Put the project back as it was: the URP assets' own values and the editor's
    // quality level.
    static void OnPlayModeChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
        foreach (var kv in _orig)
        {
            var urp = kv.Key;
            if (urp == null) continue;
            urp.renderScale        = kv.Value.scale;
            urp.msaaSampleCount    = kv.Value.msaa;
            urp.shadowDistance     = kv.Value.shadowDistance;
            urp.shadowCascadeCount = kv.Value.cascades;
            urp.supportsHDR        = kv.Value.hdr;
        }
        _orig.Clear();
        if (_bootLevel >= 0 && _bootLevel != QualitySettings.GetQualityLevel()) QualitySettings.SetQualityLevel(_bootLevel, true);
        _bootLevel = -1;
    }
#endif
}
