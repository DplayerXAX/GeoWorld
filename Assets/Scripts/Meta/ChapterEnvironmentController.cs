using System;
using System.Collections.Generic;
using UnityEngine;

// Scene-scoped, single-player rules. The main run RNG is never consumed here.
public partial class ChapterEnvironmentController : MonoBehaviour
{
    public static ChapterEnvironmentController Instance { get; private set; }
    public ChapterEnvironmentProfile Profile { get; private set; }
    public EnvironmentRunState State { get; private set; }
    public ChapterWeather Current => State?.effect ?? ChapterWeather.None;
    public bool InCombat { get; private set; }
    public int RemainingDrops => State == null ? 0 : Mathf.Max(0, State.drops.Count - _nextDrop);

    Xoshiro256StarStar _rng;
    readonly object _mistSource = new();
    readonly HashSet<TurretController> _mistTurrets = new();
    readonly Dictionary<Vector3Int, PlacedBlockInstance> _puddleOwners = new();
    readonly HashSet<Vector3Int> _activePuddles = new();
    readonly List<RainBlockPickup> _pickups = new();
    int _nextDrop;
    float _combatTime;
    bool _refreshNeeded;
    bool _effectsActive;

    public static void ReleaseObject(UnityEngine.Object obj)
    {
        if (obj == null) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }

    public static ChapterEnvironmentController Ensure(GameFlowManager flow)
    {
        if (flow == null || RunConfig.Mode != GameMode.Endless) return null;
        var profile = flow.endlessEnvironment;
        if (profile == null || NetBootstrap.Online
            || MultiplayerSession.ConnectedCount > 1) return null;
        var controller = flow.GetComponent<ChapterEnvironmentController>();
        if (controller == null) controller = flow.gameObject.AddComponent<ChapterEnvironmentController>();
        controller.Profile = profile;
        return controller;
    }

    void Awake() => Instance = this;

    public void BeginWave(int wave)
    {
        if (Profile == null || (State != null && State.wave == wave)) return;
        var previous = Current;
        ClearEffects();
        _rng ??= new Xoshiro256StarStar(BitConverter.ToUInt64(Guid.NewGuid().ToByteArray(), 0));
        State = new EnvironmentRunState { themeId = Profile.themeId, wave = wave, effect = Profile.Pick(_rng, previous) };
        var grid = GridSystem.instance;
        if (grid != null)
        {
            var cells = new List<Vector3Int>();
            foreach (var ins in grid.GetAllInstances())
                if (ins.data != null && ins.visualObject != null)
                    cells.AddRange(ins.occupiedCells);
            cells.Sort(CompareCell);
            if (Current == ChapterWeather.RainMist)
            {
                Shuffle(cells);
                int count = Mathf.Min(Mathf.Max(0, Profile.mistCount), cells.Count);
                for (int i = 0; i < count; i++) State.mistCenters.Add(grid.GridToWorld(cells[i]));
            }
            else if (Current == ChapterWeather.Puddles)
            {
                cells.RemoveAll(c => !EligiblePuddle(grid, c));
                Shuffle(cells);
                int count = Mathf.Min(Mathf.Max(0, Profile.puddleLimit), Mathf.CeilToInt(cells.Count * Mathf.Clamp01(Profile.puddleFraction)));
                for (int i = 0; i < count; i++) State.puddleCells.Add(cells[i]);
            }
            else if (Current == ChapterWeather.BlockRain)
            {
                var pc = PlacementController.Instance;
                var pool = new List<BlockData>();
                if (pc?.blocks != null)
                    foreach (var block in pc.blocks)
                        if (block != null && !TurretTypes.Is(block.blockType) && block.cells != null
                            && block.cells.Length > 0 && !pool.Contains(block)) pool.Add(block);
                pool.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
                for (int i = 0; i < Mathf.Max(0, Profile.dropCount) && pool.Count > 0; i++)
                    State.drops.Add(new EnvironmentDropPlan {
                        blockAssetName = pool[_rng.NextInt(pool.Count)].name,
                        color = GameFlowManager.Instance?.colorDistribution?.Pick(_rng) ?? BlockColor.None,
                        viewportX = Mathf.Lerp(0.25f, 0.75f, _rng.NextFloat())
                    });
            }
        }
        _effectsActive = true;
        SaveRandomState();
        BindPuddles();
        RebuildMarkers();
        RefreshBoard();
    }

    static int CompareCell(Vector3Int a, Vector3Int b)
    {
        int x = a.x.CompareTo(b.x), y = a.y.CompareTo(b.y);
        return x != 0 ? x : y != 0 ? y : a.z.CompareTo(b.z);
    }

    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static bool EligiblePuddle(GridSystem grid, Vector3Int cell)
    {
        var ins = grid?.GetInstanceAt(cell);
        if (ins?.data == null || TurretTypes.Is(ins.data.blockType)) return false;
        // Sparse unbounded grid: inspect occupied columns, never scan to an arbitrary ceiling.
        foreach (var entry in grid.GetGrid())
            if (entry.Value && entry.Key.x == cell.x && entry.Key.z == cell.z && entry.Key.y > cell.y) return false;
        return true;
    }

    void BindPuddles()
    {
        _puddleOwners.Clear();
        if (State == null || GridSystem.instance == null) return;
        foreach (var cell in State.puddleCells)
        {
            var owner = GridSystem.instance.GetInstanceAt(cell);
            if (owner != null) _puddleOwners[cell] = owner;
        }
    }

    public void BoardChanged() => _refreshNeeded = true;

    public void RefreshBoard()
    {
        _refreshNeeded = false;
        var grid = GridSystem.instance;
        if (grid == null || State == null || !_effectsActive) return;
        _activePuddles.Clear();
        for (int i = State.puddleCells.Count - 1; i >= 0; i--)
        {
            var cell = State.puddleCells[i];
            if (!_puddleOwners.TryGetValue(cell, out var owner) || grid.GetInstanceAt(cell) != owner)
            {
                State.puddleCells.RemoveAt(i);
                _puddleOwners.Remove(cell);
            }
            else if (EligiblePuddle(grid, cell)) _activePuddles.Add(cell);
        }
        var affected = new HashSet<TurretController>();
        if (Current == ChapterWeather.RainMist)
        {
            float radius = Mathf.Max(0.1f, Profile.mistRadius) * grid.cellSize;
            foreach (var ins in grid.GetAllInstances())
            {
                if (ins.visualObject == null || ins.occupiedCells.Count == 0) continue;
                var turret = ins.visualObject.GetComponentInChildren<TurretController>();
                if (turret == null) continue;
                Vector3 center = Vector3.zero;
                foreach (var cell in ins.occupiedCells) center += grid.GridToWorld(cell);
                center /= ins.occupiedCells.Count;
                foreach (var mist in State.mistCenters)
                    if ((center - mist).sqrMagnitude <= radius * radius) { affected.Add(turret); break; }
            }
        }
        foreach (var turret in _mistTurrets)
            if (turret != null && !affected.Contains(turret)) turret.Mods.RemoveSource(_mistSource);
        _mistTurrets.Clear();
        foreach (var turret in affected)
        {
            turret.Mods.Set(Stat.TurretRange, _mistSource, mul: Mathf.Clamp(Profile.mistRangeMultiplier, 0.1f, 1f));
            _mistTurrets.Add(turret);
        }
        RefreshPuddleMarkers();
    }

    public bool IsInMist(TurretController turret) => turret != null && _mistTurrets.Contains(turret);
    public float SpeedAt(Vector3Int cell, Vector3 normal) => _effectsActive && Current == ChapterWeather.Puddles
        && normal.y > 0.99f && _activePuddles.Contains(cell) ? Mathf.Clamp(Profile.puddleSpeedMultiplier, 0.1f, 1f) : 1f;

    public void BeginCombat()
    {
        if (State == null || InCombat) return;
        if (!_effectsActive) { _effectsActive = true; BindPuddles(); RebuildMarkers(); }
        RefreshBoard();
        InCombat = true;
        _combatTime = 0f;
        _nextDrop = 0;
    }

    // Idempotent; also called before settlement and destruction, not only normal wave completion.
    public void EndCombat() { RefreshBoard(); ClearEffects(); }

    void Update()
    {
        if (_refreshNeeded) RefreshBoard();
        if (!InCombat || Current != ChapterWeather.BlockRain || PauseMenu.Paused) return;
        _combatTime += Time.deltaTime;
        while (_nextDrop < State.drops.Count && _combatTime >= Mathf.Max(0, Profile.firstDropDelay)
               + _nextDrop * Mathf.Max(0.1f, Profile.dropInterval))
        {
            var plan = State.drops[_nextDrop++];
            var pc = PlacementController.Instance;
            var data = pc?.FindBlockDataByName(plan.blockAssetName);
            if (data != null && !TurretTypes.Is(data.blockType))
            {
                var pickup = RainBlockPickup.Create(this, data, plan.color, plan.viewportX, Profile.dropLifetime);
                _pickups.Add(pickup);
            }
        }
    }

    public bool TryPickAt(Vector2 screen)
    {
        if (!InCombat || PauseMenu.Paused || GameFlowManager.SettlementUp) return false;
        for (int i = _pickups.Count - 1; i >= 0; i--)
        {
            var pickup = _pickups[i];
            if (pickup == null || !pickup.Hit(screen)) continue;
            PlacementController.Instance?.TryGrabRainBlock(pickup);
            return true; // Consume the click even when another item is held.
        }
        return false;
    }

    void SaveRandomState()
    {
        var state = _rng.SaveState();
        State.randomState = Array.ConvertAll(state, n => n.ToString());
    }

    public EnvironmentRunState Capture()
    {
        if (State == null) return null;
        RefreshBoard();
        return JsonUtility.FromJson<EnvironmentRunState>(JsonUtility.ToJson(State));
    }

    public void Restore(EnvironmentRunState saved, int wave)
    {
        ClearEffects();
        State = null;
        if (saved != null && saved.themeId == Profile.themeId && saved.wave == wave
            && saved.randomState != null && saved.randomState.Length == 4)
        {
            var values = new ulong[4];
            bool valid = true;
            for (int i = 0; i < 4; i++) valid &= ulong.TryParse(saved.randomState[i], out values[i]);
            if (valid)
            {
                State = JsonUtility.FromJson<EnvironmentRunState>(JsonUtility.ToJson(saved));
                State.mistCenters ??= new(); State.puddleCells ??= new(); State.drops ??= new();
                _rng = new Xoshiro256StarStar(1);
                _rng.LoadState(values);
                _effectsActive = true;
                BindPuddles(); RebuildMarkers(); RefreshBoard();
                return;
            }
        }
        BeginWave(wave);
    }

    void ClearEffects()
    {
        InCombat = false;
        _effectsActive = false;
        _nextDrop = 0;
        PlacementController.Instance?.ClearHeldRainBlock();
        foreach (var pickup in _pickups) if (pickup != null) ReleaseObject(pickup.gameObject);
        _pickups.Clear();
        foreach (var turret in _mistTurrets) if (turret != null) turret.Mods.RemoveSource(_mistSource);
        _mistTurrets.Clear();
        _activePuddles.Clear(); _puddleOwners.Clear();
        ClearMarkers();
    }

    void OnDestroy()
    {
        ClearEffects();
        if (Instance == this) Instance = null;
    }
}
