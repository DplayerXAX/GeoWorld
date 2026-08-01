using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Per-slot persistent player profiles. Three independent save slots, each its
// own profile_<n>.json. The ACTIVE slot (chosen on the Title save-select) is
// what gameplay reads/writes; PeekSlot reads any slot without selecting it, for
// the save-select UI. Cached in memory; mutators persist immediately so a crash
// never loses a clear/purchase.
public static class SaveSystem
{
    public const int SlotCount = 3;
    const string ActiveSlotKey = "geoworld_save_slot";

    // Which slot gameplay reads/writes. Persisted (PlayerPrefs) so it survives
    // scene loads and relaunches; the Title save-select sets it on pointer-down.
    public static int ActiveSlot
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(ActiveSlotKey, 0), 0, SlotCount - 1);
        private set { PlayerPrefs.SetInt(ActiveSlotKey, Mathf.Clamp(value, 0, SlotCount - 1)); PlayerPrefs.Save(); }
    }

    // Editor-only dev convenience: when true, every read/write below redirects to
    // ONE throwaway file instead of any of the real numbered slots — so a testing
    // session (LevelMapController.resetSaveOnStart) can wipe/rewrite freely without
    // ever touching real progress. Set once per Play session (see LevelMapController.
    // Start()); the file is deleted the instant Play mode exits (below), so it never
    // lingers into the next session. Plain bool rather than `#if UNITY_EDITOR` so
    // SlotPath doesn't need per-platform special-casing — nothing outside the
    // Editor ever sets this true.
    public static bool DevTempActive;
    static string DevTempPath => Path.Combine(Application.persistentDataPath, "profile_devtemp.json");

    static string SlotPath(int slot) =>
        DevTempActive ? DevTempPath
                       : Path.Combine(Application.persistentDataPath, $"profile_{Mathf.Clamp(slot, 0, SlotCount - 1)}.json");

    // The REAL numbered path, bypassing DevTempActive — used only by PeekSlot/
    // SlotHasData (the Title save-select UI), which must always reflect the actual
    // slot files regardless of whether a gameplay test session redirected
    // Profile/Load/Save/ResetProfile elsewhere.
    static string RealSlotPath(int slot) =>
        Path.Combine(Application.persistentDataPath, $"profile_{Mathf.Clamp(slot, 0, SlotCount - 1)}.json");

    // Pre-slot single-file save; migrated into slot 0 on first access.
    static string LegacyPath => Path.Combine(Application.persistentDataPath, "profile.json");

    static ProfileData _cached;
    static int _cachedSlot = -1;

    public static ProfileData Profile
    {
        get { if (_cached == null || _cachedSlot != ActiveSlot) Load(); return _cached; }
    }

    // Point gameplay at a slot. Next Profile access loads that slot's file.
    public static void SelectSlot(int slot)
    {
        ActiveSlot     = slot;
        DevTempActive  = false;   // a real slot pick always wins over any leftover dev-temp redirect
        _cached        = null;
        _cachedSlot    = -1;
    }

    public static ProfileData Load()
    {
        int slot = ActiveSlot;
        _cachedSlot = slot;
        try
        {
            var path = SlotPath(slot);
            // One-time migration: an old single-file profile.json becomes slot 0.
            // Never applies in dev-temp mode — that file isn't a real slot 0.
            if (!DevTempActive && !File.Exists(path) && slot == 0 && File.Exists(LegacyPath))
                path = LegacyPath;

            if (File.Exists(path))
            {
                var data = JsonUtility.FromJson<ProfileData>(File.ReadAllText(path));
                if (data != null) return _cached = data;
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] load slot {slot} failed: {e.Message}"); }
        return _cached = new ProfileData();
    }

    public static void Save()
    {
        if (_cached == null) return;
        int slot = _cachedSlot < 0 ? ActiveSlot : _cachedSlot;
        try { File.WriteAllText(SlotPath(slot), JsonUtility.ToJson(_cached, prettyPrint: true)); }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] save slot {slot} failed: {e.Message}"); }
    }

    // Wipes the ACTIVE slot's save file on disk (or the dev-temp file, if
    // DevTempActive) and resets the in-memory cache to a blank ProfileData — for
    // testing systems that want to start from a truly fresh profile (e.g.
    // LevelMapController.resetSaveOnStart) without hunting down profile_<n>.json
    // under Application.persistentDataPath by hand. Anything that runs afterward
    // and calls Save() will happily persist into the now-blank slot/temp file —
    // that's expected.
    public static void ResetProfile()
    {
        int slot = ActiveSlot;
        try { if (File.Exists(SlotPath(slot))) File.Delete(SlotPath(slot)); }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] reset slot {slot} failed: {e.Message}"); }
        if (!DevTempActive && slot == 0)
        {
            try { if (File.Exists(LegacyPath)) File.Delete(LegacyPath); }
            catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] reset legacy save failed: {e.Message}"); }
        }
        _cached     = new ProfileData();
        _cachedSlot = slot;
    }

    // Read a slot's data WITHOUT selecting it or touching the active cache — for
    // the save-select UI. Returns null when the slot has no save yet (empty).
    // Always reads the REAL file (RealSlotPath), never the dev-temp redirect.
    public static ProfileData PeekSlot(int slot)
    {
        try
        {
            var path = RealSlotPath(slot);
            if (!File.Exists(path) && slot == 0 && File.Exists(LegacyPath)) path = LegacyPath;
            if (File.Exists(path))
                return JsonUtility.FromJson<ProfileData>(File.ReadAllText(path));
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] peek slot {slot} failed: {e.Message}"); }
        return null;
    }

    public static bool SlotHasData(int slot) =>
        File.Exists(RealSlotPath(slot)) || (slot == 0 && File.Exists(LegacyPath));

#if UNITY_EDITOR
    // Deletes the dev-temp file the instant Play mode exits, so "reset on start"
    // testing never leaves a throwaway save lying around between sessions.
    // [InitializeOnLoadMethod] runs at editor load/recompile time (not Play), which
    // is exactly when we need to (re)register the callback.
    [UnityEditor.InitializeOnLoadMethod]
    static void RegisterDevTempCleanup()
    {
        UnityEditor.EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        UnityEditor.EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    static void HandlePlayModeStateChanged(UnityEditor.PlayModeStateChange change)
    {
        if (change != UnityEditor.PlayModeStateChange.ExitingPlayMode) return;
        DevTempActive = false;
        try { if (File.Exists(DevTempPath)) File.Delete(DevTempPath); }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] dev-temp cleanup failed: {e.Message}"); }
    }
#endif

    // ── Progression helpers ──────────────────────────────────────────────────

    // Make sure every "unlocked by default" level is unlocked (called by the map
    // on load so a fresh profile can start its first level).
    public static void EnsureDefaultsUnlocked(IReadOnlyList<LevelDefinition> levels)
    {
        if (levels == null) return;
        bool changed = false;
        var p = Profile;
        for (int i = 0; i < levels.Count; i++)
        {
            var l = levels[i];
            if (l != null && l.unlockedByDefault && !p.IsUnlocked(l.levelId))
            {
                p.Unlock(l.levelId);
                changed = true;
            }
        }
        if (changed) Save();
    }

    // Returns true iff this call was the level's FIRST clear — callers use that to
    // gate one-time follow-up beats (e.g. GameFlowManager queuing the level's
    // rewardConversation to play once back on LevelSelect).
    public static bool RecordClear(LevelDefinition level, int wavesReached, int score = 0)
    {
        if (level == null) return false;
        var p   = Profile;
        var rec = p.GetOrCreateRecord(level.levelId);
        bool firstClear = !rec.cleared;

        rec.cleared = true;
        if (wavesReached > rec.bestWave) rec.bestWave = wavesReached;
        if (score > rec.bestScore) rec.bestScore = score;

        if (firstClear)
        {
            p.techPoints += Mathf.Max(0, level.techReward);
            if (level.unlocks != null)
                foreach (var nxt in level.unlocks)
                    if (nxt != null) p.Unlock(nxt.levelId);
            if (level.mapBlockRewards != null)
                foreach (var b in level.mapBlockRewards)
                    if (b != null) p.GrantMapBlock(b.name, 1);
        }
        Save();
        return firstClear;
    }

    public static void RecordEndless(int wave, int score = 0)
    {
        var p = Profile;
        bool changed = false;
        if (wave  > p.endlessBestWave)  { p.endlessBestWave  = wave;  changed = true; }
        if (score > p.endlessBestScore) { p.endlessBestScore = score; changed = true; }
        if (changed) Save();
    }

    // ── Tech tree ─────────────────────────────────────────────────────────────
    public static bool BuyTech(string id, int cost)
    {
        var p = Profile;
        if (string.IsNullOrEmpty(id) || p.OwnsTech(id) || p.techPoints < cost) return false;
        p.techPoints -= cost;
        p.ownedTech.Add(id);
        Save();
        return true;
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("GeoWorld/Save/Wipe Active Slot")]
    public static void WipeForTesting()
    {
        _cached     = new ProfileData();
        _cachedSlot = ActiveSlot;
        Save();
        Debug.Log($"[SaveSystem] slot {ActiveSlot} wiped.");
    }
#endif
}
