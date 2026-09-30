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

    // Editor-only: redirects every read/write to ONE throwaway file instead of a
    // real slot, so LevelMapController.resetSaveOnStart can wipe freely. Deleted
    // when Play mode exits (below).
    public static bool DevTempActive;
    static string DevTempPath => Path.Combine(Application.persistentDataPath, "profile_devtemp.json");

    static string SlotPath(int slot) =>
        DevTempActive ? DevTempPath
                       : Path.Combine(Application.persistentDataPath, $"profile_{Mathf.Clamp(slot, 0, SlotCount - 1)}.json");

    // Bypasses DevTempActive — PeekSlot/SlotHasData (Title's save-select UI) must
    // always reflect the real slot files.
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

    // One-time migration of the pre-slot profile.json, done ONCE and for real: it is
    // copied into slot 0 (only if slot 0 has no file of its own) and then moved out
    // of the way as a backup.
    //
    // It used to be read in place instead — "slot 0 has no file, so use profile.json"
    // — and never retired. So deleting slot 0 to start over just brought that old
    // progress straight back: a "new" game started with every level it had cleared
    // (1-1 included, so the farm and the wood were already standing).
    static bool _legacyChecked;
    static void MigrateLegacyOnce()
    {
        if (_legacyChecked) return;
        _legacyChecked = true;
        try
        {
            if (!File.Exists(LegacyPath)) return;
            var slot0 = RealSlotPath(0);
            if (!File.Exists(slot0)) File.Copy(LegacyPath, slot0);

            var backup = Path.Combine(Application.persistentDataPath, "profile_legacy_backup.json");
            if (File.Exists(backup))
                backup = Path.Combine(Application.persistentDataPath,
                                      $"profile_legacy_backup_{System.DateTime.Now:yyyyMMdd_HHmmss}.json");
            File.Move(LegacyPath, backup);
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] legacy migration failed: {e.Message}"); }
    }

    public static ProfileData Load()
    {
        MigrateLegacyOnce();
        int slot = ActiveSlot;
        _cachedSlot = slot;
        try
        {
            var path = SlotPath(slot);

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

    // Wipes the active slot's save file (or dev-temp file) and resets the in-memory
    // cache to blank — for testing systems like LevelMapController.resetSaveOnStart.
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
        MigrateLegacyOnce();
        try
        {
            var path = RealSlotPath(slot);
            if (File.Exists(path))
                return JsonUtility.FromJson<ProfileData>(File.ReadAllText(path));
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] peek slot {slot} failed: {e.Message}"); }
        return null;
    }

    // A slot saved by an older build (ProfileData.CurrentVersion) — the title warns
    // before playing it.
    public static bool SlotIsOutdated(int slot)
    {
        if (!SlotHasData(slot)) return false;
        var p = PeekSlot(slot);
        return p != null && p.version < ProfileData.CurrentVersion;
    }

    // The player chose to keep playing an older slot: stamp it current so the
    // warning isn't repeated every time.
    public static void MarkActiveSlotCurrent()
    {
        var p = Profile;
        if (p == null || p.version >= ProfileData.CurrentVersion) return;
        p.version = ProfileData.CurrentVersion;
        Save();
    }

    public static bool SlotHasData(int slot)
    {
        MigrateLegacyOnce();
        return File.Exists(RealSlotPath(slot));
    }

    // Permanently deletes a slot (the Title's save-select Delete button). If it is
    // the active slot, the in-memory profile goes too, so nothing writes the old
    // progress back the next time anything calls Save().
    public static void DeleteSlot(int slot)
    {
        MigrateLegacyOnce();
        slot = Mathf.Clamp(slot, 0, SlotCount - 1);
        try { if (File.Exists(RealSlotPath(slot))) File.Delete(RealSlotPath(slot)); }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] delete slot {slot} failed: {e.Message}"); }

        if (slot == ActiveSlot && !DevTempActive)
        {
            _cached     = null;
            _cachedSlot = -1;
        }
    }

#if UNITY_EDITOR
    // Deletes the dev-temp file the instant Play mode exits.
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
