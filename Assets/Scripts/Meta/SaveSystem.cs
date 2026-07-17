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

    static string SlotPath(int slot) =>
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
        ActiveSlot  = slot;
        _cached     = null;
        _cachedSlot = -1;
    }

    public static ProfileData Load()
    {
        int slot = ActiveSlot;
        _cachedSlot = slot;
        try
        {
            var path = SlotPath(slot);
            // One-time migration: an old single-file profile.json becomes slot 0.
            if (!File.Exists(path) && slot == 0 && File.Exists(LegacyPath))
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

    // Read a slot's data WITHOUT selecting it or touching the active cache — for
    // the save-select UI. Returns null when the slot has no save yet (empty).
    public static ProfileData PeekSlot(int slot)
    {
        try
        {
            var path = SlotPath(slot);
            if (!File.Exists(path) && slot == 0 && File.Exists(LegacyPath)) path = LegacyPath;
            if (File.Exists(path))
                return JsonUtility.FromJson<ProfileData>(File.ReadAllText(path));
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] peek slot {slot} failed: {e.Message}"); }
        return null;
    }

    public static bool SlotHasData(int slot) =>
        File.Exists(SlotPath(slot)) || (slot == 0 && File.Exists(LegacyPath));

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

    public static void RecordClear(LevelDefinition level, int wavesReached, int score = 0)
    {
        if (level == null) return;
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
