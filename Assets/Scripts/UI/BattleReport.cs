using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// What happened in the last wave, and why — the half of a tower-defence round that was
// missing: you build, press go, watch, and have to guess which of your choices made
// the difference. This collects it as the fight happens and turns it into a short
// report (WaveIntelHud shows it):
//
//   * who was killed, who got through, how healthy the ones that got through were;
//   * damage and kills per turret — and which turrets did NOTHING, and why;
//   * where along the route the damage actually landed (a heat map, drawn on the
//     board as glowing markers until the next wave starts);
//   * a few plain-language observations built from all of that.
//
// Nothing here changes how a wave plays. It listens to EnemySurfaceUnit's static
// events and watches the phase.
public static class BattleReport
{
    public sealed class TurretLine
    {
        public string     name;
        public Color      color;
        public Vector3Int cell;
        public float      damage;
        public int        kills;
        public bool       deals = true;   // false for the Prayer turret: it never shoots
        public bool       idle;           // should have fought, and dealt nothing
        public bool       unsupported;    // was not connected to the build
        public bool       laser;
    }

    // What an observation is about — the report panel draws an icon for each kind and
    // keeps the sentence for the hover.
    public enum InsightKind { Note, Clean, LeakDying, LeakHealthy, Idle, Disconnected, Carry, HotSpot, Gap }

    public sealed class Insight
    {
        public InsightKind kind;
        public string      text;
    }

    public sealed class Report
    {
        public int   wave, spawned, killed, leaked, livesLost, chaosKilled;
        public float seconds, totalDamage, areaDamage, leakHpFraction;
        public float coverageBefore; public int gapBefore; public bool coverageKnown;
        public readonly List<TurretLine> turrets = new();
        public readonly Dictionary<Vector3Int, float> heat = new();
        public readonly List<Insight> insights = new();
    }

    public static Report Last { get; private set; }
    public static int    Version { get; private set; }
    public static event Action Changed;

    public static void Publish(Report r)
    {
        Last = r;
        Version++;
        Changed?.Invoke();
    }

    public static string ModeName(TurretController.Mode m) => m switch
    {
        TurretController.Mode.Slow   => "Slow Turret",
        TurretController.Mode.Aoe    => "AOE Turret",
        TurretController.Mode.Debuff => "Prayer Turret",
        TurretController.Mode.Laser  => "Laser Turret",
        TurretController.Mode.Gravity => "Gravity Generator",
        _                            => "Basic Turret",
    };

    public static string Cell(Vector3Int c) => $"{c.x},{c.y},{c.z}";

    // ── Heat markers ─────────────────────────────────────────────────────────

    static readonly List<GameObject> _heat = new();
    static Material _heatMat;
    public static bool HeatVisible { get; private set; }

    // Glowing markers on the cells where the damage landed, bigger and redder the
    // more of it there was — the board itself showing where the fight was.
    public static void SetHeatVisible(bool on)
    {
        ClearHeat();
        HeatVisible = on && Last != null && Last.heat.Count > 0;
        if (!HeatVisible) return;

        var grid = GridSystem.instance;
        if (grid == null) { HeatVisible = false; return; }

        var list = new List<KeyValuePair<Vector3Int, float>>(Last.heat);
        list.Sort((a, b) => b.Value.CompareTo(a.Value));
        float max = Mathf.Max(0.0001f, list[0].Value);

        for (int i = 0; i < list.Count && i < 14; i++)
        {
            float k = Mathf.Clamp01(list[i].Value / max);
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "HeatMarker";
            UnityEngine.Object.Destroy(go.GetComponent<Collider>());
            go.transform.position   = grid.GridToWorld(list[i].Key) + Vector3.up * (grid.cellSize * 0.75f);
            go.transform.localScale = Vector3.one * grid.cellSize * Mathf.Lerp(0.22f, 0.7f, k);

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial    = HeatMat();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows    = false;
            var c = Color.Lerp(new Color(1f, 0.92f, 0.35f), new Color(1f, 0.22f, 0.14f), k);
            MpbColor.Set(mr, new Color(c.r, c.g, c.b, Mathf.Lerp(0.35f, 0.75f, k)));
            _heat.Add(go);
        }
    }

    static void ClearHeat()
    {
        foreach (var g in _heat) if (g != null) UnityEngine.Object.Destroy(g);
        _heat.Clear();
    }

    static Material HeatMat()
    {
        if (_heatMat != null) return _heatMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _heatMat = new Material(sh) { name = "BattleHeat" };
        if (_heatMat.HasProperty("_Surface"))
        {
            _heatMat.SetFloat("_Surface", 1f);
            _heatMat.SetFloat("_ZWrite", 0f);
            _heatMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _heatMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            _heatMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _heatMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _heatMat;
    }
}

// Watches the fight and builds the report. One per gameplay scene.
[DisallowMultipleComponent]
public class BattleReportDirector : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        TrySpawn();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (PlacementController.Instance == null) return;           // gameplay scene only (GameFlowManager.Instance is set in Start, later)
        if (FindFirstObjectByType<BattleReportDirector>() != null) return;
        new GameObject("BattleReportDirector").AddComponent<BattleReportDirector>();
    }

    BattleReport.Report _cur;
    bool  _active;
    float _t0;
    int   _lives0;
    float _leakHpSum;

    readonly Dictionary<TurretController, BattleReport.TurretLine> _lines = new();
    readonly Dictionary<EnemySurfaceUnit, TurretController> _lastHit = new();
    readonly HashSet<EnemySurfaceUnit> _seen = new();
    EnemyBaseManager _hooked;

    void OnEnable()
    {
        EnemySurfaceUnit.AnyDamaged    += OnDamaged;
        EnemySurfaceUnit.AnyDied       += OnDied;
        EnemySurfaceUnit.AnyReachedEnd += OnLeaked;
    }

    void OnDisable()
    {
        EnemySurfaceUnit.AnyDamaged    -= OnDamaged;
        EnemySurfaceUnit.AnyDied       -= OnDied;
        EnemySurfaceUnit.AnyReachedEnd -= OnLeaked;
        if (_hooked != null) _hooked.OnWaveCompleted -= OnWaveCompleted;
        _hooked = null;
    }

    void Update()
    {
        var flow = GameFlowManager.Instance;
        if (flow == null) return;

        var mgr = EnemyBaseManager.Instance;
        if (mgr != _hooked)
        {
            if (_hooked != null) _hooked.OnWaveCompleted -= OnWaveCompleted;
            _hooked = mgr;
            if (_hooked != null) _hooked.OnWaveCompleted += OnWaveCompleted;
        }

        bool running = flow.phase == GamePhase.Running;
        if (running && !_active) Begin(flow);
        else if (!running && _active) _active = false;   // left combat without finishing (route cut, quit): no report

        if (_active && mgr != null)
        {
            var list = mgr.ActiveEnemies;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && !IsChaos(list[i])) _seen.Add(list[i]);
        }
    }

    static bool IsChaos(EnemySurfaceUnit e) => e != null && e.GetComponent<ChaosBlockUnit>() != null;

    void Begin(GameFlowManager flow)
    {
        _active = true;
        _t0 = Time.time;
        _lives0 = PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentLives : 0;
        _leakHpSum = 0f;
        _lines.Clear(); _lastHit.Clear(); _seen.Clear();

        _cur = new BattleReport.Report { wave = flow.UpcomingWaveNumber };
        var cov = RouteCoverage.Last;
        _cur.coverageKnown  = cov.valid && cov.turrets > 0;
        _cur.coverageBefore = cov.Coverage;
        _cur.gapBefore      = cov.longestGap;
    }

    void OnDamaged(EnemySurfaceUnit e, int dealt, TurretController src)
    {
        if (!_active || _cur == null || e == null || dealt <= 0) return;
        _cur.totalDamage += dealt;
        _lastHit[e] = src;

        var grid = GridSystem.instance;
        Vector3Int cell = e.CurrentCell ?? (grid != null ? grid.WorldToGrid(e.transform.position) : default);
        _cur.heat[cell] = (_cur.heat.TryGetValue(cell, out float h) ? h : 0f) + dealt;

        if (src == null) { _cur.areaDamage += dealt; return; }
        if (!_lines.TryGetValue(src, out var line))
        {
            line = NewLine(src);
            _lines[src] = line;
        }
        line.damage += dealt;
    }

    void OnDied(EnemySurfaceUnit e)
    {
        if (!_active || _cur == null || e == null) return;
        if (IsChaos(e)) { _cur.chaosKilled++; return; }
        _cur.killed++;
        if (_lastHit.TryGetValue(e, out var t) && t != null && _lines.TryGetValue(t, out var line)) line.kills++;
    }

    void OnLeaked(EnemySurfaceUnit e)
    {
        if (!_active || _cur == null || e == null || IsChaos(e)) return;
        _cur.leaked++;
        _leakHpSum += e.maxHealth > 0 ? Mathf.Clamp01((float)e.CurrentHealth / e.maxHealth) : 0f;
    }

    void OnWaveCompleted()
    {
        if (!_active || _cur == null) return;
        _active = false;

        var r = _cur;
        r.seconds   = Time.time - _t0;
        r.spawned   = Mathf.Max(_seen.Count, r.killed + r.leaked);
        r.livesLost = Mathf.Max(0, _lives0 - (PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentLives : _lives0));
        r.leakHpFraction = r.leaked > 0 ? _leakHpSum / r.leaked : 0f;

        // Turrets that stood through the wave and dealt nothing.
        foreach (var t in FindObjectsByType<TurretController>(FindObjectsSortMode.None))
        {
            if (t == null || _lines.ContainsKey(t)) continue;
            var line = NewLine(t);
            line.idle = line.deals;
            _lines[t] = line;
        }
        foreach (var kv in _lines)
        {
            // A turret sold or lost during the wave leaves its line, built at its first hit.
            if (kv.Key == null && kv.Value.damage <= 0f) continue;
            r.turrets.Add(kv.Value);
        }
        r.turrets.Sort((a, b) => b.damage.CompareTo(a.damage));

        BuildInsights(r);
        BattleReport.Publish(r);
    }

    static BattleReport.TurretLine NewLine(TurretController t)
    {
        var grid = GridSystem.instance;
        var root = t.transform.parent != null ? t.transform.parent : t.transform;
        return new BattleReport.TurretLine
        {
            name        = BattleReport.ModeName(t.mode),
            color       = TurretTypes.DisplayColor(t.mode),
            cell        = grid != null ? grid.WorldToGrid(root.position) : default,
            deals       = t.mode != TurretController.Mode.Debuff,
            unsupported = !t.Supported,
            laser       = t.mode == TurretController.Mode.Laser,
        };
    }

    // A few plain-language reads of the wave, most useful first. The point is that a
    // player who lost something, or won by luck, can see WHY.
    static void BuildInsights(BattleReport.Report r)
    {
        var ins = r.insights;
        void Add(BattleReport.InsightKind k, string text) => ins.Add(new BattleReport.Insight { kind = k, text = text });

        if (r.leaked > 0)
        {
            int pct = Mathf.RoundToInt(r.leakHpFraction * 100f);
            if (pct <= 35)
                Add(BattleReport.InsightKind.LeakDying,
                    $"{r.leaked} got through but nearly dead ({pct}% HP left). A little more damage near the end of the route would have stopped them.");
            else
                Add(BattleReport.InsightKind.LeakHealthy,
                    $"{r.leaked} got through with {pct}% HP left — barely hurt. Add damage along the route, and earlier on it.");
        }

        int shown = 0;
        foreach (var t in r.turrets)
        {
            if (!t.idle || shown >= 2) continue;
            shown++;
            if (t.unsupported)
                Add(BattleReport.InsightKind.Disconnected,
                    $"{t.name} at {BattleReport.Cell(t.cell)} was not connected to the build, so it held fire.");
            else if (t.laser)
                Add(BattleReport.InsightKind.Idle,
                    $"{t.name} at {BattleReport.Cell(t.cell)} never fired: nothing crossed its beam. Aim it down the route, or clear what blocks it.");
            else
                Add(BattleReport.InsightKind.Idle,
                    $"{t.name} at {BattleReport.Cell(t.cell)} never fired: nothing came within its reach. Move it next to the route.");
        }

        float total = Mathf.Max(1f, r.totalDamage);
        int dealers = 0;
        foreach (var t in r.turrets) if (t.deals && t.damage > 0f) dealers++;
        if (dealers >= 2 && r.turrets.Count > 0 && r.turrets[0].damage / total > 0.55f)
            Add(BattleReport.InsightKind.Carry,
                $"{r.turrets[0].name} at {BattleReport.Cell(r.turrets[0].cell)} did {Mathf.RoundToInt(r.turrets[0].damage / total * 100f)}% of all the damage. The others added little.");

        if (r.heat.Count >= 3)
        {
            Vector3Int hot = default; float best = 0f, sum = 0f;
            foreach (var kv in r.heat) { sum += kv.Value; if (kv.Value > best) { best = kv.Value; hot = kv.Key; } }
            if (sum > 0f && best / sum >= 0.25f)
                Add(BattleReport.InsightKind.HotSpot,
                    $"{Mathf.RoundToInt(best / sum * 100f)}% of the damage landed around {BattleReport.Cell(hot)}: that is where enemies spent the most time under fire.");
        }

        if (r.coverageKnown && r.coverageBefore < 0.5f && r.gapBefore >= 3)
            Add(BattleReport.InsightKind.Gap,
                $"Only {Mathf.RoundToInt(r.coverageBefore * 100f)}% of the route was within reach of a turret, with a stretch of {r.gapBefore} blocks nothing could hit.");

        if (ins.Count == 0)
            Add(r.leaked == 0 ? BattleReport.InsightKind.Clean : BattleReport.InsightKind.Note,
                r.leaked == 0 ? "A clean wave: nothing got through, and every turret pulled its weight."
                              : "Nothing stood out.");

        if (ins.Count > 4) ins.RemoveRange(4, ins.Count - 4);
    }
}
