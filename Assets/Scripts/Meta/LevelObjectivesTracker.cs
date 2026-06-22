using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Tracks a level's special objectives (LevelDefinition.objectives) and shows them
// top-left as a runtime UGUI checklist. Auto-spawns for Level mode when the level
// has objectives. Counters come from global hooks (enemy deaths/leaks, block
// placements) + polled state (waves, synergies, lives).
[DisallowMultipleComponent]
public class LevelObjectivesTracker : MonoBehaviour
{
    enum State { Pending, Done, Failed }

    static LevelObjectivesTracker _inst;
    // True when every non-optional objective is currently satisfied (gates the clear
    // if the level has requireAllObjectives).
    public static bool AllRequiredSatisfied
    {
        get
        {
            if (_inst == null) return true;
            foreach (var o in _inst._lv.objectives)
                if (o != null && !o.optional && _inst.Eval(o, out _, out _) != State.Done)
                    return false;
            return true;
        }
    }

    LevelDefinition _lv;
    int _kills, _leaks, _placed, _maxSynergies;

    Canvas    _canvas;
    TMP_Text[] _rows;

    // ── Spawn ───────────────────────────────────────────────────────────────────
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
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null) return;
        if (RunConfig.Level.objectives == null || RunConfig.Level.objectives.Count == 0) return;
        if (PlacementController.Instance == null) return;             // gameplay scene only
        if (FindFirstObjectByType<LevelObjectivesTracker>() != null) return;
        new GameObject("LevelObjectivesTracker").AddComponent<LevelObjectivesTracker>();
    }

    void Awake() { _inst = this; _lv = RunConfig.Level; }

    void OnEnable()
    {
        EnemySurfaceUnit.AnyDied       += OnKill;
        EnemySurfaceUnit.AnyReachedEnd += OnLeak;
        PlacementController.BlockPlaced += OnPlaced;
    }

    void OnDisable()
    {
        EnemySurfaceUnit.AnyDied       -= OnKill;
        EnemySurfaceUnit.AnyReachedEnd -= OnLeak;
        PlacementController.BlockPlaced -= OnPlaced;
        if (_inst == this) _inst = null;
    }

    void OnKill(EnemySurfaceUnit _)            => _kills++;
    void OnLeak(EnemySurfaceUnit _)            => _leaks++;
    void OnPlaced(BlockData _, Vector3Int[] __) => _placed++;

    void Start() { if (_lv != null && _lv.objectives.Count > 0) BuildUI(); }

    void Update()
    {
        var ev = SynergyEvaluator.Instance;
        if (ev != null) _maxSynergies = Mathf.Max(_maxSynergies, ev.Actives.Count);

        if (_canvas != null) _canvas.enabled = !IntroDirector.Playing;   // hold until the intro finishes
        if (_rows == null) return;
        for (int i = 0; i < _rows.Length; i++)
        {
            var o = _lv.objectives[i];
            var st = Eval(o, out int cur, out int tgt);
            Color c = st == State.Done   ? GeoPalette.Gold
                    : st == State.Failed ? GeoPalette.Signal
                    : GeoPalette.Ink;
            string mark = st == State.Done ? "✓" : st == State.Failed ? "✗" : "▢";
            string bonus = o.optional ? "  <size=70%>(bonus)</size>" : "";
            _rows[i].color = c;
            _rows[i].text  = $"{mark}  {o.Label}   <b>{cur}/{tgt}</b>{bonus}";
        }
    }

    // Returns the objective's state + current/target counts.
    State Eval(LevelObjective o, out int cur, out int tgt)
    {
        tgt = o.target;
        var gfm = GameFlowManager.Instance;
        var hp  = PlayerHealth.Instance;

        switch (o.type)
        {
            case ObjectiveType.ClearWaves:
                cur = gfm != null ? gfm.WavesCleared : 0;
                return cur >= tgt ? State.Done : State.Pending;
            case ObjectiveType.ReachWave:
                cur = gfm != null ? gfm.UpcomingWaveNumber : 1;
                return cur >= tgt ? State.Done : State.Pending;
            case ObjectiveType.KillEnemies:
                cur = _kills;  return cur >= tgt ? State.Done : State.Pending;
            case ObjectiveType.PlaceBlocks:
                cur = _placed; return cur >= tgt ? State.Done : State.Pending;
            case ObjectiveType.ActivateSynergies:
                cur = _maxSynergies; return cur >= tgt ? State.Done : State.Pending;
            case ObjectiveType.LimitLeaks:
                cur = _leaks; return cur > tgt ? State.Failed : State.Done;   // on-track while within
            case ObjectiveType.KeepLivesAtLeast:
                cur = hp != null ? hp.CurrentLives : 0;
                return cur >= tgt ? State.Done : State.Failed;
        }
        cur = 0; return State.Pending;
    }

    // ── UI (top-left) ────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var go = new GameObject("ObjectivesCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 92;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 1f;

        var panel = NewRect("Panel", go.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);   // top-left
        panel.anchoredPosition = new Vector2(16f, -200f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(0.949f, 0.937f, 0.902f, 0.92f);    // paper
        bg.sprite = UIRoundedRect.Get(24);
        bg.type   = Image.Type.Sliced;
        bg.raycastTarget = false;

        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(20, 24, 16, 16);
        vlg.spacing = 7f;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = false; vlg.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var title = NewText("Title", panel, 26f, GeoPalette.Ink, FontStyles.Bold);
        title.text = "OBJECTIVES";

        _rows = new TMP_Text[_lv.objectives.Count];
        for (int i = 0; i < _rows.Length; i++)
            _rows[i] = NewText("Obj", panel, 21f, GeoPalette.Ink, FontStyles.Normal);
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.MidlineLeft;
        t.raycastTarget = false;
        t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }
}
