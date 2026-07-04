using System.Collections.Generic;
using System.IO;
using UnityEngine;

// The single persistent player profile (progression / tech / endless best).
// Mirrors SnapshotManager's JsonUtility + persistentDataPath approach. Cached in
// memory; mutators persist immediately so a crash never loses a clear/purchase.
public static class SaveSystem
{
    static string FilePath => Path.Combine(Application.persistentDataPath, "profile.json");

    static ProfileData _cached;
    public static ProfileData Profile
    {
        get { if (_cached == null) Load(); return _cached; }
    }

    public static ProfileData Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var data = JsonUtility.FromJson<ProfileData>(File.ReadAllText(FilePath));
                if (data != null) return _cached = data;
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] load failed: {e.Message}"); }
        return _cached = new ProfileData();
    }

    public static void Save()
    {
        if (_cached == null) return;
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(_cached, prettyPrint: true)); }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSystem] save failed: {e.Message}"); }
    }

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
    [UnityEditor.MenuItem("GeoWorld/Save/Wipe Profile")]
    public static void WipeForTesting()
    {
        _cached = new ProfileData();
        Save();
        Debug.Log("[SaveSystem] profile wiped.");
    }
#endif
}
