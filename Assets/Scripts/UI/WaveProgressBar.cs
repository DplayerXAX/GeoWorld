using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// PvZ-style wave meter along the bottom-right: how far through the level you are,
// and — the part that actually changes decisions — WHICH waves will open a new
// endpoint. A new spawn point rewrites the enemy route, so seeing it two waves out
// is the difference between preparing for it and being surprised by it.
//
// Endpoints alternate: GameFlowManager.AddNextEndpoint adds a START on even
// roundIndex and an END on odd, once every `runsPerEndpoint` waves. Both are
// marked, in different colours, since "a new goal appears" matters too — just
// less than a new spawn.
[DisallowMultipleComponent]
public class WaveProgressBar : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static void TrySpawn()
    {
        if (FindFirstObjectByType<GameFlowManager>() == null) return;   // gameplay scene only
        if (FindFirstObjectByType<WaveProgressBar>() != null) return;
        new GameObject("WaveProgressBar").AddComponent<WaveProgressBar>();
    }

    // ── Layout (reference 1080p pixels) ──────────────────────────────────────
    const float TrackWidth   = 560f;
    const float TrackHeight  = 20f;
    const float MarginRight  = 28f;
    const float MarginBottom = 26f;
    const float PipWidth     = 2f;
    const float FlagWidth    = 5f;
    const float FlagOverhang = 9f;    // how far an endpoint marker sticks out top and bottom
    const int   MaxPips      = 24;    // above this, per-wave ticks turn into visual noise
    const int   EndlessSpan  = 10;    // waves shown at a time when there's no fixed total

    static readonly Color TrackColor = new(0.06f, 0.06f, 0.06f, 0.72f);
    static readonly Color FillColor  = new(0.42f, 0.72f, 0.95f, 0.85f);
    static readonly Color PipColor   = new(1f, 1f, 1f, 0.22f);

    Canvas        _canvas;
    CanvasGroup   _group;
    RectTransform _track;
    RectTransform _fill;
    RectTransform _head;
    TMP_Text      _label;

    // Marks keep the wave they stand for, so a hover can say what actually happens
    // there instead of the player having to decode a coloured tick.
    struct Mark { public GameObject go; public RectTransform rt; public int wave; public bool endpoint, spawn; }
    readonly List<Mark> _marks = new();

    RectTransform _tip;
    TMP_Text      _tipText;

    int   _builtFirst = -1, _builtLast = -1;
    float _shown;      // smoothed progress, so the head slides instead of jumping
    float _alpha = 1f;

    void Awake()
    {
        BuildUI();
    }

    void LateUpdate()
    {
        var flow = GameFlowManager.Instance;
        bool visible = flow != null && !GameFlowManager.SettlementUp && !PeekWorld.Held;

        _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, 6f * Time.unscaledDeltaTime);
        _group.alpha = _alpha;
        if (flow == null || _alpha <= 0.001f) return;

        int per   = Mathf.Max(1, flow.runsPerEndpoint);
        int wave  = Mathf.Max(1, flow.UpcomingWaveNumber);
        int total = TotalWaves();

        // Fixed total → the whole level fits on the bar. Endless → slide a window
        // along, aligned to EndlessSpan so the marks don't crawl every wave.
        int first = total > 0 ? 1     : (wave - 1) / EndlessSpan * EndlessSpan + 1;
        int last  = total > 0 ? total : first + EndlessSpan - 1;

        if (first != _builtFirst || last != _builtLast) BuildMarks(first, last, per);

        // Cleared waves fill solid; the wave in progress fills ACROSS its own slot,
        // driven by how much of that wave has actually been dealt with. Jumping to
        // the middle of the slot the moment combat starts made the bar useless
        // during the only stretch where the player wants to read it.
        int   cleared = Mathf.Clamp(flow.WavesCleared, first - 1, last);
        float slots   = Mathf.Max(1, last - first + 1);
        float done    = Mathf.Clamp01((cleared - (first - 1)) / slots);
        float target  = Mathf.Clamp01(done + WaveFraction() / slots);

        _shown = Mathf.Lerp(_shown, target, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
        _fill.anchorMax = new Vector2(_shown, 1f);
        _head.anchorMin = _head.anchorMax = new Vector2(_shown, 0.5f);

        _label.text = total > 0 ? $"WAVE {Mathf.Min(wave, total)} / {total}" : $"WAVE {wave}";

        UpdateTooltip();
    }

    // How far through the CURRENT wave the fight is, 0..1.
    //
    // Measured as enemies RESOLVED (spawned minus still alive), not enemies spawned:
    // a spawn-driven bar is full while the board is still swarming, which reads as
    // "you're done" at the exact moment you aren't. Resolved only ever increases —
    // a spawn bumps both terms, a death bumps only the subtrahend — so the fill
    // can't slide backwards mid-wave.
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

    // An authored schedule is asked first, so the flags say what GameFlowManager
    // will actually do. Falling back to the cadence formula while the level lists
    // its own waves would put markers on waves where nothing happens.
    //
    // Cadence fallback: wave W opens an endpoint when it's a multiple of
    // runsPerEndpoint. The Nth such event runs with roundIndex == N-1, and
    // AddNextEndpoint adds a START on even roundIndex — so odd N is a new spawn
    // point, even N a new goal.
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

    void BuildMarks(int first, int last, int per)
    {
        foreach (var m in _marks) Destroy(m.go);
        _marks.Clear();
        _builtFirst = first; _builtLast = last;

        int count = Mathf.Max(1, last - first + 1);
        bool pips = count <= MaxPips;

        for (int w = first; w <= last; w++)
        {
            bool endpoint = IsEndpointWave(w, per);
            if (!endpoint && !pips) continue;

            // Marks sit on wave BOUNDARIES, not centres: an endpoint appears when a
            // wave completes, so the flag belongs at the line the fill crosses when
            // that happens, not in the middle of the slot it belongs to.
            float x = (w - (first - 1)) / (float)count;

            var go = new GameObject(endpoint ? $"Flag{w}" : $"Pip{w}", typeof(RectTransform));
            go.transform.SetParent(_track, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(x, 0.5f);
            rt.pivot     = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = endpoint
                ? new Vector2(FlagWidth, TrackHeight + FlagOverhang * 2f)
                : new Vector2(PipWidth,  TrackHeight * 0.55f);

            var img = go.AddComponent<Image>();
            img.raycastTarget = false;
            img.color = !endpoint      ? PipColor
                      : IsSpawnWave(w, per) ? GeoPalette.Signal   // new SPAWN — the route changes
                                            : GeoPalette.Gold;    // new goal

            _marks.Add(new Mark
            {
                go = go, rt = rt, wave = w,
                endpoint = endpoint, spawn = endpoint && IsSpawnWave(w, per),
            });
        }
    }

    // ── Marker tooltip ───────────────────────────────────────────────────────

    // Hit-tested by DISTANCE rather than RectangleContainsScreenPoint: a flag is
    // 5px wide, which is far too small to hover deliberately. This gives it a
    // forgiving band without inflating the drawn mark or making it a raycast
    // target (which would start swallowing clicks over the bar).
    const float TipGrabPx = 14f;

    void UpdateTooltip()
    {
        if (_tip == null) return;

        Vector2 mouse = VirtualCursor.Position;

        // The head first — it sits ON the track and can overlap a flag it has just
        // passed, and "where am I" is the more useful answer of the two.
        if (_head != null && Mathf.Abs(mouse.x - _head.position.x) <= TipGrabPx
                          && Mathf.Abs(mouse.y - _head.position.y) <= _head.rect.height)
        {
            var flow  = GameFlowManager.Instance;
            int wave  = flow != null ? Mathf.Max(1, flow.UpcomingWaveNumber) : 1;
            int total = TotalWaves();
            bool fighting = flow != null && flow.phase == GamePhase.Running;

            _tipText.text = total > 0
                ? $"Wave {Mathf.Min(wave, total)} of {total}\n<size=85%>{(fighting ? "You are here — this wave is being fought now." : "You are here — this wave starts when you press Space.")}</size>"
                : $"Wave {wave}\n<size=85%>{(fighting ? "You are here — this wave is being fought now." : "You are here — this wave starts when you press Space.")}</size>";

            _tip.gameObject.SetActive(true);
            _tip.position = new Vector3(_head.position.x, _head.position.y + _head.rect.height, 0f);
            ClampTipOnScreen();
            return;
        }

        Mark? hit = null;

        // Only endpoint flags are worth explaining — a plain per-wave pip has
        // nothing to say that the label doesn't already.
        foreach (var m in _marks)
        {
            if (!m.endpoint || m.rt == null) continue;
            Vector3 sp = m.rt.position;                       // overlay canvas → already screen space
            if (Mathf.Abs(mouse.x - sp.x) > TipGrabPx) continue;
            if (Mathf.Abs(mouse.y - sp.y) > m.rt.rect.height) continue;
            hit = m;
            break;
        }

        if (hit == null) { _tip.gameObject.SetActive(false); return; }

        var mk = hit.Value;
        _tipText.text = mk.spawn
            ? $"Wave {mk.wave}\n<size=85%>A new portal opens here — enemies will start from one more place, and the route changes.</size>"
            : $"Wave {mk.wave}\n<size=85%>A new core appears here — part of the horde will peel off toward it.</size>";

        _tip.gameObject.SetActive(true);
        // Pinned above the flag, not to the cursor: the bar sits at the very bottom
        // of the screen, so a cursor-following tip would hang off it.
        _tip.position = new Vector3(mk.rt.position.x, mk.rt.position.y + mk.rt.rect.height, 0f);
        ClampTipOnScreen();
    }

    // Keeps the card fully on screen — the flags nearest the right edge would
    // otherwise push half of it out of frame.
    void ClampTipOnScreen()
    {
        var half = _tip.rect.width * 0.5f * _tip.lossyScale.x;
        float x = Mathf.Clamp(_tip.position.x, half + 8f, Screen.width - half - 8f);
        _tip.position = new Vector3(x, _tip.position.y, 0f);
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // Under the shop's letterbox bars (55) on purpose — the shop is a modal the
        // player opened, and it should be free to cover this.
        _canvas.sortingOrder = 45;

        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 0.5f;

        _group = canvasGo.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;

        // Bottom-RIGHT, not bottom-centre: PlacementHintBar already owns the centre
        // while a block is held, and the two would sit on top of each other.
        var trackGo = new GameObject("Track", typeof(RectTransform));
        trackGo.transform.SetParent(canvasGo.transform, false);
        _track = (RectTransform)trackGo.transform;
        _track.anchorMin = _track.anchorMax = _track.pivot = new Vector2(1f, 0f);
        _track.anchoredPosition = new Vector2(-MarginRight, MarginBottom);
        _track.sizeDelta = new Vector2(TrackWidth, TrackHeight);

        var trackImg = trackGo.AddComponent<Image>();
        trackImg.sprite        = UIRoundedRect.Get(6);
        trackImg.type          = Image.Type.Sliced;
        trackImg.color         = TrackColor;
        trackImg.raycastTarget = false;

        var fillGo = new GameObject("Fill", typeof(RectTransform));
        fillGo.transform.SetParent(_track, false);
        _fill = (RectTransform)fillGo.transform;
        _fill.anchorMin = Vector2.zero;
        _fill.anchorMax = new Vector2(0f, 1f);
        _fill.offsetMin = new Vector2(2f, 2f);
        _fill.offsetMax = new Vector2(-2f, -2f);
        var fillImg = fillGo.AddComponent<Image>();
        fillImg.sprite        = UIRoundedRect.Get(5);
        fillImg.type          = Image.Type.Sliced;
        fillImg.color         = FillColor;
        fillImg.raycastTarget = false;

        // Drawn after the marks would put it under them; it's added here and the
        // marks parent to _track later, so the head ends up behind the flags —
        // which is what we want, a flag must stay readable as the head passes it.
        var headGo = new GameObject("Head", typeof(RectTransform));
        headGo.transform.SetParent(_track, false);
        _head = (RectTransform)headGo.transform;
        _head.pivot      = new Vector2(0.5f, 0.5f);
        _head.sizeDelta  = new Vector2(10f, TrackHeight + 10f);
        var headImg = headGo.AddComponent<Image>();
        headImg.sprite        = UIRoundedRect.Get(4);
        headImg.type          = Image.Type.Sliced;
        headImg.color         = GeoPalette.Paper;
        headImg.raycastTarget = false;

        var labelGo = new GameObject("Label", typeof(RectTransform));
        labelGo.transform.SetParent(canvasGo.transform, false);
        var lrt = (RectTransform)labelGo.transform;
        lrt.anchorMin = lrt.anchorMax = lrt.pivot = new Vector2(1f, 0f);
        lrt.anchoredPosition = new Vector2(-MarginRight, MarginBottom + TrackHeight + 12f);
        lrt.sizeDelta = new Vector2(TrackWidth, 26f);
        _label = labelGo.AddComponent<TextMeshProUGUI>();
        _label.fontSize      = 18f;
        _label.color         = GeoPalette.Paper;
        _label.fontStyle     = FontStyles.Bold;
        _label.alignment     = TextAlignmentOptions.Right;
        _label.raycastTarget = false;

        BuildTooltip(canvasGo.transform);
    }

    void BuildTooltip(Transform parent)
    {
        var go = new GameObject("MarkTooltip", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        _tip = (RectTransform)go.transform;
        _tip.pivot     = new Vector2(0.5f, 0f);   // grows upward from the flag
        _tip.sizeDelta = new Vector2(420f, 88f);

        var bg = go.AddComponent<Image>();
        bg.sprite        = UIRoundedRect.Get(10);
        bg.type          = Image.Type.Sliced;
        bg.color         = new Color(0.06f, 0.06f, 0.06f, 0.92f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.transform.SetParent(_tip, false);
        var trt = (RectTransform)textGo.transform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(14f, 10f);
        trt.offsetMax = new Vector2(-14f, -10f);

        _tipText = textGo.AddComponent<TextMeshProUGUI>();
        _tipText.fontSize      = 17f;
        _tipText.color         = GeoPalette.Paper;
        _tipText.alignment     = TextAlignmentOptions.TopLeft;
        _tipText.raycastTarget = false;

        _tip.gameObject.SetActive(false);
    }
}
