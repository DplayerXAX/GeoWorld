using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Next-wave intel, triggered by clicking a start portal (PlacementController.
// TrySelectObject). Deliberately NON-modal: the camera does NOT move, so you can
// read what's coming while you keep placing blocks — the whole point of the
// forecast is to inform the build you're making right now.
//
// Shows one hologram per enemy TYPE (not per instance) floating above the portal
// in spawn order, each tagged with its real ×count, plus a card listing each
// type's count and what it actually does. One model + "×12" beats 5 clones of a
// 12-enemy group: it's honest about the number and stays readable.
//
// Click again (or click empty space) to dismiss. No scene setup; auto-builds.
public static class WavePreview
{
    public static bool Active => _active;
    static bool _active;

    static GameObject _root;

    // ── Tuning knobs ──────────────────────────────────────────────────────────
    const float HeightFrac   = 1.35f;   // row height above the portal core, × frame half-extent
    const float PreviewScale = 0.5f;    // shrink the real enemy model for the display row
    const float SpacingFrac  = 1.15f;   // gap between holograms, × cell size

    public static void Toggle(GridEndpoint startPoint, GameFlowManager.WaveForecast forecast)
    {
        if (_active) { Exit(); return; }
        Enter(startPoint, forecast);
    }

    static void Enter(GridEndpoint startPoint, GameFlowManager.WaveForecast forecast)
    {
        if (startPoint == null) return;
        if (!forecast.valid || forecast.groups == null || forecast.groups.Count == 0) return;

        var portal = startPoint.GetComponent<EndpointPortalVisual>();
        var core   = portal != null && portal.Core != null ? portal.Core : startPoint.transform;

        float frameHalfExtent = portal != null
            ? portal.transform.lossyScale.x * 0.5f * portal.frameScale
            : core.lossyScale.x * 0.7f;

        _root = new GameObject("WavePreviewRoot");
        var rig = _root.AddComponent<WavePreviewRig>();
        rig.Build(core, frameHalfExtent * HeightFrac, forecast, PreviewScale, SpacingFrac);

        _active = true;
    }

    public static void Exit()
    {
        if (!_active) return;
        _active = false;
        if (_root != null) Object.Destroy(_root);
        _root = null;
    }
}

// Owns the floating hologram row + its screen-space labels and info card. The
// row billboards to the camera every frame so it stays readable from any orbit
// angle, and the labels/card track their models' projected screen positions.
public class WavePreviewRig : MonoBehaviour
{
    class Entry
    {
        public Transform model;
        public TMP_Text  label;
    }

    readonly List<Entry> _entries = new();

    Transform     _core;
    float         _height;
    float         _spacing;
    Canvas        _canvas;
    RectTransform _card;
    float         _phase;

    public void Build(Transform core, float height, GameFlowManager.WaveForecast forecast,
                      float previewScale, float spacingFrac)
    {
        _core    = core;
        _height  = height;
        _spacing = (GridSystem.instance != null ? GridSystem.instance.cellSize : 1f) * spacingFrac;
        _phase   = Random.value * 6.2832f;

        BuildCanvas();

        var groups = forecast.groups;
        for (int i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            if (g.prefab == null) continue;

            var go = Instantiate(g.prefab.gameObject);
            go.SetActive(false);   // strip before any Awake/OnEnable can run

            // Inert display copy: no pathing, no physics, no gameplay behaviours.
            foreach (var c in go.GetComponentsInChildren<EnemySurfaceUnit>(true))      c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<EnemyHealerAura>(true))       c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<EnemyBlockSealer>(true))      c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<EnemyTurretSuppressor>(true)) c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<EnemySplitOnAlive>(true))     c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<EnemyAccelerator>(true))      c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<EnemySynergyJammer>(true))    c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Collider>(true))              c.enabled = false;
            foreach (var c in go.GetComponentsInChildren<Rigidbody>(true))             c.isKinematic = true;

            go.transform.SetParent(transform, false);
            go.transform.localScale *= previewScale;
            go.SetActive(true);

            var label = NewText("Count", _canvas.transform, 40f, GeoPalette.Paper,
                                FontStyles.Bold, TextAlignmentOptions.Center);
            label.text = $"×{g.count}";
            label.rectTransform.sizeDelta = new Vector2(200f, 60f);

            _entries.Add(new Entry { model = go.transform, label = label });
        }

        BuildCard(forecast);
        LateUpdate();   // place everything before the first frame renders
    }

    void BuildCanvas()
    {
        var go = new GameObject("WavePreviewCanvas", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 90;   // with the HUD, below tutorial/shop overlays
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 0.5f;
    }

    // The card rides beside the row (tracks it in 3-D) instead of pinning to a
    // screen corner, so the intel reads as part of the portal, not as HUD chrome.
    void BuildCard(GameFlowManager.WaveForecast forecast)
    {
        _card = NewRect("Card", _canvas.transform);
        _card.pivot     = new Vector2(0f, 0.5f);
        _card.sizeDelta = new Vector2(430f, 40f);   // height fits to content below

        var bg = _card.gameObject.AddComponent<Image>();
        bg.color         = new Color(0.949f, 0.937f, 0.902f, 0.94f);
        bg.sprite        = UIRoundedRect.Get(18);
        bg.type          = Image.Type.Sliced;
        bg.raycastTarget = false;

        var t = NewText("Body", _card, 21f, GeoPalette.Ink, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        t.textWrappingMode = TextWrappingModes.Normal;
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(18f, 14f); trt.offsetMax = new Vector2(-18f, -14f);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"<b>WAVE {forecast.waveNumber}</b>   <size=85%>{forecast.totalCount} enemies · in spawn order</size>");
        foreach (var g in forecast.groups)
        {
            if (g.prefab == null) continue;
            sb.AppendLine();
            sb.AppendLine($"<b>{g.name}</b>  ×{g.count}   <size=80%><color=#6A6A6A>{g.prefab.maxHealth} HP</color></size>");
            sb.Append($"<size=80%><color=#6A6A6A>{EnemyDossier.Mechanic(g.prefab)}</color></size>");
        }
        t.text = sb.ToString().TrimEnd();

        // Size the card to whatever the text actually needs.
        t.ForceMeshUpdate();
        _card.sizeDelta = new Vector2(_card.sizeDelta.x, Mathf.Max(60f, t.preferredHeight + 28f));
    }

    void LateUpdate()
    {
        var cam = Camera.main;
        if (_core == null || cam == null) { WavePreview.Exit(); return; }

        // Billboard the whole row: local +X always runs across the screen, so the
        // queue never gets viewed edge-on however the player orbits.
        Vector3 flatFwd = cam.transform.forward;
        flatFwd.y = 0f;
        if (flatFwd.sqrMagnitude < 0.0001f) flatFwd = Vector3.forward;
        transform.position = _core.position + Vector3.up * _height;
        transform.rotation = Quaternion.LookRotation(flatFwd.normalized, Vector3.up);

        int n = _entries.Count;
        float bob = Mathf.Sin(Time.unscaledTime * 1.6f + _phase) * 0.06f;

        for (int i = 0; i < n; i++)
        {
            var e = _entries[i];
            if (e.model == null) continue;

            float x = (i - (n - 1) * 0.5f) * _spacing;
            float perBob = Mathf.Sin(Time.unscaledTime * 1.9f + i * 0.6f) * 0.05f;
            e.model.localPosition = new Vector3(x, bob + perBob, 0f);
            e.model.localRotation = Quaternion.Euler(0f, Time.unscaledTime * 22f + i * 40f, 0f);

            // "×N" tag pinned under each hologram.
            Vector3 sp = cam.WorldToScreenPoint(e.model.position - transform.up * (_spacing * 0.42f));
            bool visible = sp.z > 0f;
            e.label.gameObject.SetActive(visible);
            if (visible) e.label.rectTransform.position = new Vector3(sp.x, sp.y, 0f);
        }

        // Card sits just past the right end of the row.
        if (_card != null && n > 0)
        {
            float edge = ((n - 1) * 0.5f) * _spacing + _spacing * 0.8f;
            Vector3 cardWorld = transform.TransformPoint(new Vector3(edge, 0f, 0f));
            Vector3 csp = cam.WorldToScreenPoint(cardWorld);
            bool visible = csp.z > 0f;
            _card.gameObject.SetActive(visible);
            if (visible) _card.position = new Vector3(csp.x, csp.y, 0f);
        }
    }

    // ── UI primitives ────────────────────────────────────────────────────────
    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color,
                            FontStyles style, TextAlignmentOptions align)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false; t.richText = true;
        return t;
    }
}

// Derives an enemy's "what does this thing actually do" line straight from the
// components on its prefab — so a new enemy type shows up in the wave intel the
// moment it exists, with no parallel description table to keep in sync.
public static class EnemyDossier
{
    public static string Mechanic(EnemySurfaceUnit prefab)
    {
        if (prefab == null) return "";
        var go    = prefab.gameObject;
        var lines = new List<string>();

        var healer = go.GetComponent<EnemyHealerAura>();
        if (healer != null)
            lines.Add($"Heals nearby enemies +{healer.healAmount} every {healer.healInterval:0.#}s");

        if (go.GetComponent<EnemyBlockSealer>() != null)
            lines.Add("Seals the first block it crosses (no moving it — sell only)");

        var supp = go.GetComponent<EnemyTurretSuppressor>();
        if (supp != null)
            lines.Add($"Nearby turrets fire {Mathf.RoundToInt((1f - supp.fireRateMultiplier) * 100f)}% slower");

        var split = go.GetComponent<EnemySplitOnAlive>();
        if (split != null)
            lines.Add($"Splits into {split.childCount} when it advances");

        var accel = go.GetComponent<EnemyAccelerator>();
        if (accel != null)
            lines.Add($"Speeds up over {accel.rampSeconds:0.#}s — up to ×{accel.maxMultiplier:0.#}");

        if (go.GetComponent<EnemySynergyJammer>() != null)
            lines.Add("Shuts down whatever synergy it stands on");

        if (prefab.targetPriority > 0)                 lines.Add("Taunts turrets");
        if (prefab.baseSpeedMultiplier >= 1.15f)       lines.Add("Fast mover");
        else if (prefab.baseSpeedMultiplier <= 0.85f)  lines.Add("Slow mover");

        return lines.Count > 0 ? string.Join("\n", lines) : "No special ability";
    }
}
