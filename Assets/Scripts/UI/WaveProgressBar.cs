using System.Collections.Generic;
using UnityEngine;
using TMPro;

// The level's waves as a row of nodes under the lives / wave panel: one hollow ring
// per wave, joined by a line that fills in as the fight goes on. A wave after which
// a new PORTAL opens is a solid red dot — a new spawn point rewrites the enemy
// route, so seeing it coming is the difference between preparing for it and being
// surprised by it. A wave that adds a new core has a gold ring.
//
// Node k stands for the END of wave k (that's when a new endpoint appears), with a
// start node before the first; the line into node k fills while wave k is fought,
// by enemies dealt with, not enemies spawned.
//
// The layout is the WaveBar prefab (GeoWorld ▸ UI ▸ Place Gameplay HUD): move the
// track, restyle the node and segment templates there. Nodes are cloned from the
// templates and spread evenly across the track's width.
[DisallowMultipleComponent]
public class WaveProgressBar : MonoBehaviour
{
    [Header("Layout (scene)")]
    public CanvasGroup     group;
    [Tooltip("The nodes are spread across this rect's width, centred vertically.")]
    public RectTransform   track;
    public WaveNodeView    nodeTemplate;
    public WaveSegmentView segmentTemplate;
    [Tooltip("Size of the start node relative to a wave node.")]
    public float startNodeScale = 0.55f;
    [Tooltip("Show the wave number under each node.")]
    public bool showNumbers = false;

    [Header("Tooltip")]
    public RectTransform tip;
    public TMP_Text      tipText;

    [Header("Colours")]
    public Color ringColor    = new Color(1f, 1f, 1f, 0.85f);
    public Color clearedColor = new Color(0.42f, 0.72f, 0.95f, 1f);
    public Color portalColor  = new Color(0.90f, 0.25f, 0.22f, 1f);
    public Color coreColor    = new Color(1f, 0.78f, 0.25f, 1f);
    public Color labelColor   = new Color(1f, 1f, 1f, 0.75f);

    const int EndlessSpan = 10;   // waves shown at a time when there's no fixed total
    const float TipGrabPx = 6f;   // extra hover reach round a node

    struct Node { public WaveNodeView v; public int wave; public bool portal, core; }
    readonly List<Node> _nodes = new();
    readonly List<WaveSegmentView> _segs = new();
    readonly List<float> _segShown = new();

    int   _builtFirst = -1, _builtLast = -1;
    float _alpha = 1f;

    void Awake()
    {
        if (nodeTemplate    != null) nodeTemplate.gameObject.SetActive(false);
        if (segmentTemplate != null) segmentTemplate.gameObject.SetActive(false);
        if (tip != null) tip.gameObject.SetActive(false);
    }

    void LateUpdate()
    {
        var flow = GameFlowManager.Instance;
        bool visible = flow != null && !GameFlowManager.SettlementUp && !PeekWorld.Held && !IntroDirector.Playing;
        _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, 6f * Time.unscaledDeltaTime);
        if (group != null) group.alpha = _alpha;
        if (flow == null || track == null || _alpha <= 0.001f) return;

        int per   = Mathf.Max(1, flow.runsPerEndpoint);
        int wave  = Mathf.Max(1, flow.UpcomingWaveNumber);
        int total = TotalWaves();

        // Fixed total → the whole level. Endless → a window sliding along in steps.
        int first = total > 0 ? 1     : (wave - 1) / EndlessSpan * EndlessSpan + 1;
        int last  = total > 0 ? total : first + EndlessSpan - 1;
        if (first != _builtFirst || last != _builtLast) Build(first, last, per);

        int cleared = flow.WavesCleared;
        float k = 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime);
        for (int i = 0; i < _segs.Count; i++)
        {
            int w = first + i;   // segment i leads into the node for wave w
            float target = w <= cleared ? 1f : (w == cleared + 1 ? WaveFraction() : 0f);
            _segShown[i] = Mathf.Lerp(_segShown[i], target, k);
            if (_segs[i].fill != null) _segs[i].fill.fillAmount = _segShown[i];
        }

        float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 4f);
        foreach (var n in _nodes)
        {
            bool isStart = n.wave < first;
            bool done    = n.wave <= cleared;
            bool next    = n.wave == cleared + 1;
            if (n.v.ring != null)
                n.v.ring.color = n.portal ? portalColor : n.core ? coreColor : done || isStart ? clearedColor : ringColor;
            float s = isStart ? startNodeScale : 1f;
            n.v.rect.localScale = Vector3.one * (next ? s * pulse : s);
        }

        UpdateTooltip(cleared, total);
    }

    // How far through the CURRENT wave the fight is, 0..1: enemies RESOLVED
    // (spawned minus still alive), not spawned — a spawn-driven bar reads "done"
    // while the board is still swarming. Only ever increases.
    static float WaveFraction()
    {
        var mgr = EnemyBaseManager.Instance;
        if (mgr == null || !mgr.WaveActive) return 0f;
        int target = mgr.TargetSpawnCount;
        if (target <= 0) return 0f;
        int resolved = mgr.SpawnedCount - mgr.ActiveEnemyCount;
        return Mathf.Clamp01(resolved / (float)target);
    }

    static int TotalWaves()
    {
        var lv = RunConfig.Mode == GameMode.Level ? RunConfig.Level : null;
        return lv != null && lv.wavesToClear > 0 ? lv.wavesToClear : 0;
    }

    // An authored schedule is asked first, so the marks say what GameFlowManager
    // will actually do. Cadence fallback: wave W opens an endpoint when it's a
    // multiple of runsPerEndpoint; the Nth such event adds a START (portal) for
    // odd N, a goal (core) for even N.
    static LevelDefinition Scheduled =>
        RunConfig.Mode == GameMode.Level && RunConfig.Level != null
        && RunConfig.Level.HasEndpointSchedule ? RunConfig.Level : null;

    static bool IsEndpointWave(int wave, int per)
    {
        var lv = Scheduled;
        if (lv != null) return lv.EndpointKindAfterWave(wave).HasValue;
        return wave > 0 && wave % per == 0;
    }

    static bool IsSpawnWave(int wave, int per)
    {
        var lv = Scheduled;
        if (lv != null) return lv.EndpointKindAfterWave(wave) == true;
        return IsEndpointWave(wave, per) && (wave / per) % 2 == 1;
    }

    void Build(int first, int last, int per)
    {
        foreach (var n in _nodes) if (n.v != null) Destroy(n.v.gameObject);
        foreach (var s in _segs)  if (s != null)   Destroy(s.gameObject);
        _nodes.Clear(); _segs.Clear(); _segShown.Clear();
        _builtFirst = first; _builtLast = last;
        if (nodeTemplate == null) return;

        int count = Mathf.Max(1, last - first + 1);
        float nodeW = nodeTemplate.rect.rect.width;

        // Segments first so the rings draw over the ends of the lines.
        if (segmentTemplate != null)
            for (int i = 0; i < count; i++)
            {
                var s = Instantiate(segmentTemplate, track);
                s.gameObject.SetActive(true);
                s.name = $"Segment{first + i}";
                float x0 = i / (float)count, x1 = (i + 1) / (float)count;
                s.rect.anchorMin = new Vector2(x0, 0.5f);
                s.rect.anchorMax = new Vector2(x1, 0.5f);
                s.rect.pivot = new Vector2(0.5f, 0.5f);
                s.rect.anchoredPosition = Vector2.zero;
                s.rect.sizeDelta = new Vector2(-nodeW, s.rect.sizeDelta.y);   // stop at the rings' edges
                if (s.fill != null) s.fill.fillAmount = 0f;
                _segs.Add(s);
                _segShown.Add(0f);
            }

        for (int i = 0; i <= count; i++)
        {
            int w = first - 1 + i;   // i = 0: the start node
            var v = Instantiate(nodeTemplate, track);
            v.gameObject.SetActive(true);
            v.name = i == 0 ? "Start" : $"Wave{w}";
            v.rect.anchorMin = v.rect.anchorMax = new Vector2(i / (float)count, 0.5f);
            v.rect.anchoredPosition = Vector2.zero;

            bool portal = i > 0 && IsSpawnWave(w, per);
            bool core   = i > 0 && !portal && IsEndpointWave(w, per);
            if (v.dot != null)
            {
                v.dot.enabled = portal || i == 0;
                v.dot.color   = portal ? portalColor : clearedColor;
            }
            if (v.label != null)
            {
                v.label.gameObject.SetActive(showNumbers && i > 0);
                v.label.text  = w.ToString();
                v.label.color = labelColor;
            }
            _nodes.Add(new Node { v = v, wave = w, portal = portal, core = core });
        }
    }

    // ── Tooltip ──────────────────────────────────────────────────────────────

    void UpdateTooltip(int cleared, int total)
    {
        if (tip == null || tipText == null) return;
        Vector2 mouse = VirtualCursor.Position;

        Node? hit = null;
        foreach (var n in _nodes)
        {
            if (n.v == null || n.wave < _builtFirst) continue;
            Vector3 c = n.v.rect.position;   // overlay canvas → screen space
            float r = n.v.rect.rect.width * 0.5f * n.v.rect.lossyScale.x + TipGrabPx;
            if ((mouse - (Vector2)c).sqrMagnitude > r * r) continue;
            hit = n;
            break;
        }
        if (hit == null) { tip.gameObject.SetActive(false); return; }

        var h = hit.Value;
        var flow = GameFlowManager.Instance;
        bool fighting = flow != null && flow.phase == GamePhase.Running;
        string head = total > 0 ? $"Wave {h.wave} of {total}" : $"Wave {h.wave}";
        string body;
        if (h.wave == cleared + 1)
            body = fighting ? "You are here — this wave is being fought now."
                            : "You are here — this wave starts when you press Space.";
        else if (h.wave <= cleared) body = "Cleared.";
        else body = "";
        if (h.portal) body += (body.Length > 0 ? "\n" : "") + "After it, a new portal opens — enemies start from one more place, and the route changes.";
        if (h.core)   body += (body.Length > 0 ? "\n" : "") + "After it, a new core appears — part of the horde will peel off toward it.";

        tipText.text = body.Length > 0 ? $"{head}\n<size=85%>{body}</size>" : head;
        tip.gameObject.SetActive(true);
        // Under the node — the bar sits at the top of the screen.
        tip.position = new Vector3(h.v.rect.position.x, h.v.rect.position.y - h.v.rect.rect.height * h.v.rect.lossyScale.y, 0f);
        var half = tip.rect.width * 0.5f * tip.lossyScale.x;
        tip.position = new Vector3(Mathf.Clamp(tip.position.x, half + 8f, Screen.width - half - 8f), tip.position.y, 0f);
    }
}
