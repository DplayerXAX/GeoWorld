using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Gameplay entrance animation. On entering a gameplay scene:
//   1. a white overlay fades in with the level name,
//   2. it fades out while the ManifoldSkybox lerps from a flat mono-colour (_IntroBlend)
//      to its full form,
//   3. the start/end endpoints pop in.
// Auto-spawns; no scene setup. Self-contained (builds its own canvas).
[DisallowMultipleComponent]
public class IntroDirector : MonoBehaviour
{
    [Header("Timing (seconds)")]
    public float fadeIn     = 0.5f;
    public float hold       = 1.0f;
    public float fadeOut    = 0.8f;
    public float revealTime = 2.2f;   // skybox mono → full
    public float popTime    = 0.5f;   // endpoints scale-in

    [Header("Look")]
    public Color whiteColor = Color.white;
    public Color nameColor  = new Color(0.086f, 0.086f, 0.086f);
    public float nameSize   = 72f;
    [Tooltip("Description shown near the bottom (uses the level's description).")]
    public Color descColor  = new Color(0.30f, 0.30f, 0.30f);
    public float descSize   = 30f;
    [Tooltip("Font for the name AND description.")]
    public TMP_FontAsset font;

    // Blocks placement/selection while the intro is on screen.
    public static bool Playing { get; private set; }

    Image     _white;
    TMP_Text  _name, _desc;
    Material  _sky;
    bool      _hasBlend;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (PlacementController.Instance == null) return;            // gameplay scene only
        if (FindFirstObjectByType<IntroDirector>() != null) return;
        new GameObject("IntroDirector").AddComponent<IntroDirector>();
    }

    void Start()
    {
        BuildUI();
        StartCoroutine(Play());
    }

    IEnumerator Play()
    {
        Playing = true;

        // Wait until the endpoints exist (GameFlowManager.Start creates them), then hide them.
        float tw = 0f;
        while (tw < 1.5f && (GameFlowManager.Instance == null
               || GameFlowManager.Instance.endpoints == null
               || GameFlowManager.Instance.endpoints.allEndpoints.Count == 0))
        { tw += Time.unscaledDeltaTime; yield return null; }

        var eps = new List<(Transform tr, Vector3 scale)>();
        var gen = GameFlowManager.Instance != null ? GameFlowManager.Instance.endpoints : null;
        if (gen != null)
            foreach (var go in gen.allEndpoints)
                if (go != null) { eps.Add((go.transform, go.transform.localScale)); go.transform.localScale = Vector3.zero; }

        // Pre-built starting-layout blocks (LevelDefinition.startingLayout) pop in
        // alongside the endpoints, same treatment — hidden through the intro, not
        // sitting there at full scale while the reveal plays.
        if (GameFlowManager.Instance != null)
            foreach (var go in GameFlowManager.Instance.startingLayoutVisuals)
                if (go != null) { eps.Add((go.transform, go.transform.localScale)); go.transform.localScale = Vector3.zero; }

        SetupSky();
        SetBlend(0f);

        // Phase A — fade in white + name.
        for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
        { float k = Mathf.Clamp01(t / fadeIn); SetWhite(k); SetName(k); yield return null; }
        SetWhite(1f); SetName(1f);
        for (float t = 0f; t < hold; t += Time.unscaledDeltaTime) yield return null;

        // Phase B — fade out white/name while the sky reveals from mono → full.
        // With the level's sky rise on, the camera starts low looking up into
        // that sky and rises to the opening view meanwhile.
        bool rise = BeginSkyRise();
        float dur = Mathf.Max(fadeOut, revealTime);
        if (rise) dur = Mathf.Max(dur, _riseSeconds);
        for (float t = 0f; t < dur; t += Time.unscaledDeltaTime)
        {
            float fo = 1f - Mathf.Clamp01(t / fadeOut);
            SetWhite(fo); SetName(fo);
            SetBlend(Mathf.Clamp01(t / revealTime));
            if (rise) StepSkyRise(t / _riseSeconds);
            yield return null;
        }
        SetWhite(0f); SetName(0f); SetBlend(1f);
        if (rise) EndSkyRise();

        // Phase C — pop the endpoints in.
        for (float t = 0f; t < popTime; t += Time.unscaledDeltaTime)
        {
            float k = EaseOutBack(Mathf.Clamp01(t / popTime));
            foreach (var e in eps) if (e.tr != null) e.tr.localScale = e.scale * k;
            yield return null;
        }
        foreach (var e in eps) if (e.tr != null) e.tr.localScale = e.scale;

        Playing = false;
        Destroy(gameObject);
    }

    // ── Sky rise (LevelEnvironment.introSkyRise) ─────────────────────────────────
    // The orbit rig is switched off for the move and the camera driven directly:
    // from low down (just over the lake — the rig's own floor — or a few cells
    // under the board), looking up into the sky off to one side, it rises and
    // turns to the pose the rig settled on under the white — so handing back is
    // seamless. The look lags the climb a little: it keeps its eyes on the sky
    // as it starts to rise, then comes round and down onto the board.
    OrbitCamera _riseOrbit;
    Transform   _riseCam;
    Vector3     _riseFocus, _riseEndPos;
    float       _riseSeconds, _riseStartYaw, _riseEndYaw, _riseStartDist, _riseEndDist;
    float       _riseStartH, _riseEndH;
    float       _riseStartLookYaw, _riseEndLookYaw, _riseStartPitch, _riseEndPitch;

    bool BeginSkyRise()
    {
        var env = RunConfig.Level != null ? RunConfig.Level.environment : null;
        if (env == null || !env.introSkyRise) return false;
        _riseOrbit = FindFirstObjectByType<OrbitCamera>();
        if (_riseOrbit == null) return false;
        _riseCam = _riseOrbit.transform;
        _riseSeconds = Mathf.Max(0.5f, env.introSkyRiseSeconds);

        var grid = FindFirstObjectByType<GridSystem>();
        float cs = grid != null ? grid.cellSize : 1f;

        // Where the rig has settled (under the white): the end of the move.
        _riseFocus  = _riseOrbit.FocusPoint;
        _riseEndPos = _riseCam.position;
        var e = _riseEndPos - _riseFocus;
        _riseEndYaw  = Mathf.Atan2(e.z, e.x) * Mathf.Rad2Deg;
        _riseEndDist = new Vector2(e.x, e.z).magnitude;
        _riseEndH    = e.y;
        var er = _riseCam.rotation.eulerAngles;
        _riseEndLookYaw = er.y;
        _riseEndPitch   = er.x > 180f ? er.x - 360f : er.x;

        // The start: a quarter-turn round, a little further out, low down.
        float low = float.IsNegativeInfinity(_riseOrbit.minCameraY)
                  ? _riseFocus.y - 5f * cs
                  : _riseOrbit.minCameraY + 0.5f * cs;
        _riseStartYaw  = _riseEndYaw - 55f;
        _riseStartDist = Mathf.Max(_riseEndDist * 1.25f, 4f * cs);
        _riseStartH    = Mathf.Min(low - _riseFocus.y, _riseEndH - 4f * cs);
        _riseStartLookYaw = _riseEndLookYaw - 55f + 40f;   // off past the board, into open sky
        _riseStartPitch   = -38f;                          // looking up

        OrbitCamera.InputLocked = true;
        _riseOrbit.enabled = false;
        StepSkyRise(0f);
        return true;
    }

    void StepSkyRise(float u)
    {
        if (_riseCam == null) return;
        u = Mathf.Clamp01(u);
        float kp = EaseInOut(u);                               // along the ground
        float kh = EaseInOut(Mathf.Clamp01(u / 0.9f));         // up
        float kl = EaseInOut(Mathf.Clamp01((u - 0.18f) / 0.82f));   // the look comes round last

        float yaw  = Mathf.LerpAngle(_riseStartYaw, _riseEndYaw, kp) * Mathf.Deg2Rad;
        float dist = Mathf.Lerp(_riseStartDist, _riseEndDist, kp);
        float h    = Mathf.Lerp(_riseStartH, _riseEndH, kh);
        _riseCam.position = _riseFocus + new Vector3(Mathf.Cos(yaw) * dist, h, Mathf.Sin(yaw) * dist);
        _riseCam.rotation = Quaternion.Euler(Mathf.Lerp(_riseStartPitch, _riseEndPitch, kl),
                                             Mathf.LerpAngle(_riseStartLookYaw, _riseEndLookYaw, kl), 0f);
    }

    void EndSkyRise()
    {
        if (_riseCam != null) _riseCam.SetPositionAndRotation(_riseEndPos, Quaternion.Euler(_riseEndPitch, _riseEndLookYaw, 0f));
        if (_riseOrbit != null) _riseOrbit.enabled = true;
        OrbitCamera.InputLocked = false;
        _riseOrbit = null; _riseCam = null;
    }

    static float EaseInOut(float x) => x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;

    // ── Skybox intro blend ───────────────────────────────────────────────────────
    // Drive _IntroBlend on the actual skybox material (no instancing / swap) so the
    // reveal ends seamlessly at 1 instead of snapping when a material is restored.
    void SetupSky()
    {
        _sky = RenderSettings.skybox;
        _hasBlend = _sky != null && _sky.HasProperty("_IntroBlend");
    }

    void SetBlend(float v)
    {
        if (_hasBlend) _sky.SetFloat("_IntroBlend", v);
    }

    void OnDestroy()
    {
        if (_riseOrbit != null) EndSkyRise();   // interrupted mid-rise: hand the camera back
        if (_hasBlend && _sky != null) _sky.SetFloat("_IntroBlend", 1f);   // ensure full sky if interrupted
        Playing = false;
    }

    // ── Overlay ────────────────────────────────────────────────────────────────────
    void SetWhite(float a) { var c = whiteColor; c.a = a; _white.color = c; }
    void SetName(float a)
    {
        var n = nameColor; n.a = a; _name.color = n;
        var d = descColor; d.a = a; _desc.color = d;
    }

    void BuildUI()
    {
        var go = new GameObject("IntroCanvas", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode  = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;   // above all HUD
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        var wrt = NewRect("White", go.transform);
        wrt.anchorMin = Vector2.zero; wrt.anchorMax = Vector2.one;
        wrt.offsetMin = wrt.offsetMax = Vector2.zero;
        _white = wrt.gameObject.AddComponent<Image>();
        _white.raycastTarget = true;                    // block clicks under the overlay
        SetWhite(0f);

        var nrt = NewRect("Name", go.transform);
        nrt.anchorMin = nrt.anchorMax = new Vector2(0.5f, 0.5f);
        nrt.pivot = new Vector2(0.5f, 0.5f);
        nrt.sizeDelta = new Vector2(1400f, 200f);
        _name = nrt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) _name.font = font;
        _name.fontSize = nameSize;
        _name.fontStyle = FontStyles.Bold;
        _name.alignment = TextAlignmentOptions.Center;
        _name.raycastTarget = false;
        _name.text = RunConfig.Mode == GameMode.Level && RunConfig.Level != null
                   ? (string.IsNullOrEmpty(RunConfig.Level.displayName) ? RunConfig.Level.levelId : RunConfig.Level.displayName)
                   : "ENDLESS";

        // Description near the bottom.
        var drt = NewRect("Desc", go.transform);
        drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0.18f);
        drt.pivot = new Vector2(0.5f, 0.5f);
        drt.sizeDelta = new Vector2(1200f, 220f);
        _desc = drt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) _desc.font = font;
        _desc.fontSize = descSize;
        _desc.alignment = TextAlignmentOptions.Center;
        _desc.textWrappingMode = TextWrappingModes.Normal;
        _desc.raycastTarget = false;
        _desc.text = RunConfig.Mode == GameMode.Level && RunConfig.Level != null
                   ? RunConfig.Level.description : "";

        SetName(0f);
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static float EaseOutBack(float x)
    {
        const float c1 = 1.70158f, c3 = c1 + 1f;
        float xm = x - 1f;
        return 1f + c3 * xm * xm * xm + c1 * xm * xm;
    }
}
