using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Zoomed-in preview of the next wave, triggered by clicking a start portal
// (PlacementController.TrySelectObject). Camera glides in close to the portal's core
// (EndpointPortalVisual.Core); inert preview copies of the upcoming wave's enemies
// orbit around it; a small panel lists wave/enemy info. Click again (or click empty
// space) exits — camera restores, previews despawn. No scene setup, auto-builds its UI.
public static class WavePreview
{
    public static bool Active => _active;
    static bool _active;

    static GameObject _root;                 // holds spawned preview enemies
    static GameObject _panelGo;
    static TMP_Text _panelText;

    static OrbitCamera _orbit;
    static Vector3 _savedFocus;
    static float   _savedZoom;
    static float   _savedDistance;

    // ── Tuning knobs ──────────────────────────────────────────────────────────
    // Pushing the camera INTO the core sphere clipped through the frame geometry
    // (near-plane cutting into the cube edges) and, worse, made the whole portal
    // shader disappear (backface-culled from inside) — losing the "3D" fresnel/
    // depth read entirely. Staying outside the frame keeps the shader looking like
    // an actual object, while still framing tight on the cube's inner core.
    const float ZoomFrac         = 1.5f;    // orthoSize, relative to the cube frame's half-extent
    const float DistanceFrac     = 2.2f;    // camera distance, relative to the cube frame's half-extent
    const float MinZoom          = 1.0f;
    const float MinDistance      = 1.6f;
    const float OrbitSpeed       = 35f;     // deg/sec
    const float BobAmplitude     = 0.12f;
    const float BobSpeed         = 1.6f;
    const float PreviewScale     = 0.55f;   // shrink the real enemy model down for this display-case view
    const int   MaxPerGroup      = 5;       // cap visible instances per enemy type

    public static void Toggle(GridEndpoint startPoint, GameFlowManager.WaveForecast forecast)
    {
        if (_active) { Exit(); return; }
        Enter(startPoint, forecast);
    }

    static void Enter(GridEndpoint startPoint, GameFlowManager.WaveForecast forecast)
    {
        if (startPoint == null) return;
        var portal = startPoint.GetComponent<EndpointPortalVisual>();
        var core = portal != null ? portal.Core : startPoint.transform;
        if (core == null) core = startPoint.transform;

        _orbit = Object.FindFirstObjectByType<OrbitCamera>();
        if (_orbit == null) return;

        // Half-extent of the cube FRAME (not the core sphere) — the "square portal"
        // the user means. Falls back to a core-sphere-based guess if the frame's own
        // scale/frameScale aren't available for some reason.
        float coreRadius = core.lossyScale.x * 0.5f;
        float frameHalfExtent = portal != null
            ? portal.transform.lossyScale.x * 0.5f * portal.frameScale
            : coreRadius * 1.4f;

        _savedFocus    = _orbit.FocusPoint;
        _savedZoom     = _orbit.useOrthographic ? _orbit.orthoSize : _orbit.distance;
        _savedDistance = _orbit.distance;

        _orbit.FocusOnPoint(core.position, snap: false);
        _orbit.SetZoom(Mathf.Max(MinZoom, frameHalfExtent * ZoomFrac));
        _orbit.SetPhysicalDistance(Mathf.Max(MinDistance, frameHalfExtent * DistanceFrac));
        OrbitCamera.InputLocked = true;   // only WavePreview.Exit() gives control back

        _root = new GameObject("WavePreviewRoot");
        SpawnEnemies(core, coreRadius, frameHalfExtent, forecast);
        BuildPanel(forecast);

        _active = true;
    }

    public static void Exit()
    {
        if (!_active) return;
        _active = false;

        if (_root != null) Object.Destroy(_root);
        _root = null;

        if (_panelGo != null) Object.Destroy(_panelGo);
        _panelGo = null;
        _panelText = null;

        OrbitCamera.InputLocked = false;
        if (_orbit != null)
        {
            _orbit.FocusOnPoint(_savedFocus, snap: false);
            _orbit.SetZoom(_savedZoom);
            _orbit.SetPhysicalDistance(_savedDistance);
        }
        _orbit = null;
    }

    // ── Enemy previews ───────────────────────────────────────────────────────
    static void SpawnEnemies(Transform core, float coreRadius, float frameHalfExtent, GameFlowManager.WaveForecast forecast)
    {
        // Orbit radius must clear the CORE SPHERE's own radius, not just be "some
        // fraction of the frame" — frameHalfExtent and coreRadius come from two
        // different scale knobs (frameScale vs coreScale), so a frame-relative
        // radius could end up smaller than the sphere itself, putting previews
        // inside/overlapping its mesh. That's why "in front" still looked occluded:
        // the preview was actually intersecting the sphere, not clearing it.
        float orbitRadius = Mathf.Min(coreRadius * 1.25f, frameHalfExtent * 0.85f);
        if (forecast.groups == null || forecast.groups.Count == 0) return;

        // Flatten to one entry per visible instance, spread evenly around the ring
        // regardless of which group it came from.
        var instances = new List<EnemySurfaceUnit>();
        foreach (var g in forecast.groups)
        {
            if (g.prefab == null) continue;
            int shown = Mathf.Min(g.count, MaxPerGroup);
            for (int i = 0; i < shown; i++) instances.Add(g.prefab);
        }
        if (instances.Count == 0) return;

        for (int i = 0; i < instances.Count; i++)
        {
            var prefab = instances[i];
            var go = Object.Instantiate(prefab.gameObject);
            go.SetActive(false);   // no Awake/OnEnable logic runs before we strip it

            foreach (var comp in go.GetComponentsInChildren<EnemySurfaceUnit>(true)) comp.enabled = false;
            foreach (var comp in go.GetComponentsInChildren<Collider>(true)) comp.enabled = false;
            foreach (var comp in go.GetComponentsInChildren<Rigidbody>(true)) comp.isKinematic = true;

            go.transform.SetParent(_root.transform, false);
            go.transform.localScale *= PreviewScale;
            go.SetActive(true);

            float angle = i * (360f / instances.Count);
            var orbiter = go.AddComponent<WavePreviewOrbiter>();
            orbiter.Init(core, angle, orbitRadius, OrbitSpeed, BobAmplitude, BobSpeed, i * 0.37f);
        }
    }

    // ── Panel ─────────────────────────────────────────────────────────────────
    static void BuildPanel(GameFlowManager.WaveForecast forecast)
    {
        var canvasGo = new GameObject("WavePreviewCanvas", typeof(Canvas), typeof(CanvasScaler));
        _panelGo = canvasGo;
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 90;   // with the HUD, below tutorial/shop overlays
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        var panel = NewRect("Panel", canvasGo.transform);
        panel.anchorMin = new Vector2(0.5f, 0f); panel.anchorMax = new Vector2(0.5f, 0f);
        panel.pivot = new Vector2(0.5f, 0f);
        panel.sizeDelta = new Vector2(420f, 140f);
        panel.anchoredPosition = new Vector2(0f, 40f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.949f, 0.937f, 0.902f, 0.94f);
        bg.sprite = UIRoundedRect.Get(20);
        bg.type = Image.Type.Sliced;

        var t = NewText("Body", panel, 22f, GeoPalette.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(20f, 14f); trt.offsetMax = new Vector2(-20f, -14f);
        _panelText = t;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<b>Wave {forecast.waveNumber}</b>   Enemies: {(forecast.valid ? forecast.totalCount.ToString() : "—")}");
        if (forecast.valid && forecast.groups != null && forecast.groups.Count > 0)
            foreach (var g in forecast.groups) sb.AppendLine($"{g.name}  ×{g.count}");
        else
            sb.Append("Composition unknown");
        _panelText.text = sb.ToString().TrimEnd();
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false; t.richText = true;
        return t;
    }
}

// Restless orbit + bob + jitter around a fixed centre — purely cosmetic, no gameplay
// hookup. Each instance gets its own random speed/jitter phase so the group doesn't
// move as one uniform ring — reads more like agitated things circling a portal than
// a clean mechanical orbit.
public class WavePreviewOrbiter : MonoBehaviour
{
    Transform _centre;
    float _angle, _radius, _speed, _bobAmp, _bobSpeed, _phase;
    float _speedJitterAmp, _speedJitterFreq;   // wobbles the orbit speed over time
    float _radiusJitterAmp, _radiusJitterFreq; // wobbles the orbit radius (skittish darting in/out)
    float _jitterSeed;

    public void Init(Transform centre, float startAngle, float radius, float speed, float bobAmp, float bobSpeed, float phase)
    {
        _centre = centre; _angle = startAngle; _radius = radius; _speed = speed;
        _bobAmp = bobAmp; _bobSpeed = bobSpeed; _phase = phase;

        // Per-instance randomness so the whole ring doesn't move like one rigid body.
        _speedJitterAmp   = _speed * Random.Range(0.35f, 0.65f);
        _speedJitterFreq  = Random.Range(0.8f, 1.8f);
        _radiusJitterAmp  = _radius * Random.Range(0.12f, 0.22f);
        _radiusJitterFreq = Random.Range(1.2f, 2.4f);
        _jitterSeed       = Random.Range(0f, 100f);
    }

    void Update()
    {
        if (_centre == null) { Destroy(gameObject); return; }

        float t = Time.unscaledTime;
        float speedNow = _speed + Mathf.Sin(t * _speedJitterFreq + _jitterSeed) * _speedJitterAmp;
        _angle += speedNow * Time.unscaledDeltaTime;

        float rad = _angle * Mathf.Deg2Rad;
        float radiusNow = _radius + Mathf.Sin(t * _radiusJitterFreq + _jitterSeed * 1.7f) * _radiusJitterAmp;
        float bob = Mathf.Sin(t * _bobSpeed + _phase) * _bobAmp
                  + Mathf.Sin(t * _bobSpeed * 2.3f + _jitterSeed) * (_bobAmp * 0.4f);   // extra flicker
        Vector3 ringOffset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radiusNow;
        transform.position = _centre.position + ringOffset + Vector3.up * bob;
        transform.rotation = Quaternion.LookRotation(-ringOffset.normalized, Vector3.up);   // face the core
    }
}
