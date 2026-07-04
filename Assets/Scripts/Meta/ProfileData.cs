using System;
using System.Collections.Generic;

// Persistent player profile (serialized to JSON by SaveSystem). Plain data only —
// no IO here. `version` is bumped if the layout ever changes so old saves can be
// migrated instead of silently breaking.
[Serializable]
public class ProfileData
{
    public int version = 1;

    // ── Level progression (keyed by LevelDefinition.levelId) ────────────────
    public List<string>      unlockedLevels = new();
    public List<LevelRecord> levelRecords   = new();

    // ── Endless ─────────────────────────────────────────────────────────────
    public int endlessBestWave;
    public int endlessBestScore;

    // ── Tech tree ───────────────────────────────────────────────────────────
    public int          techPoints;
    public List<string> ownedTech = new();

    // ── Queries ─────────────────────────────────────────────────────────────
    public bool IsUnlocked(string levelId) =>
        !string.IsNullOrEmpty(levelId) && unlockedLevels.Contains(levelId);

    public bool OwnsTech(string id) =>
        !string.IsNullOrEmpty(id) && ownedTech.Contains(id);

    public LevelRecord GetRecord(string levelId)
    {
        for (int i = 0; i < levelRecords.Count; i++)
            if (levelRecords[i].levelId == levelId) return levelRecords[i];
        return null;
    }

    // ── Mutators (SaveSystem persists after calling these) ──────────────────
    public void Unlock(string levelId)
    {
        if (!string.IsNullOrEmpty(levelId) && !unlockedLevels.Contains(levelId))
            unlockedLevels.Add(levelId);
    }

    public LevelRecord GetOrCreateRecord(string levelId)
    {
        var r = GetRecord(levelId);
        if (r == null) { r = new LevelRecord { levelId = levelId }; levelRecords.Add(r); }
        return r;
    }
}

[Serializable]
public class LevelRecord
{
    public string levelId;
    public bool   cleared;
    public int    bestWave;    // furthest wave reached across attempts
    public int    bestScore;   // highest RunStats.ComputeScore across attempts
}
