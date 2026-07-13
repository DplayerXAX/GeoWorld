using System;
using System.Collections.Generic;
using UnityEngine;

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

    // ── Overworld map-building (LevelMapController build mode) ──────────────
    // Blocks earned from level clears (LevelDefinition.mapBlockRewards) but not
    // yet placed on the LevelSelect map.
    public List<MapBlockGrant> mapBlockInventory = new();
    // Blocks the player has already placed on the LevelSelect map — replayed at
    // load so the extended walkable network persists across sessions.
    public List<PlacedMapBlock> placedMapBlocks = new();

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

    public void GrantMapBlock(string blockAssetName, int count = 1)
    {
        if (string.IsNullOrEmpty(blockAssetName) || count <= 0) return;
        var g = mapBlockInventory.Find(x => x.blockAssetName == blockAssetName);
        if (g == null) { g = new MapBlockGrant { blockAssetName = blockAssetName }; mapBlockInventory.Add(g); }
        g.count += count;
    }

    // Spends one from inventory. Returns false (no-op) if none available — callers
    // should already have checked, but this guards against a stale UI double-click.
    public bool ConsumeMapBlock(string blockAssetName)
    {
        var g = mapBlockInventory.Find(x => x.blockAssetName == blockAssetName);
        if (g == null || g.count <= 0) return false;
        g.count--;
        return true;
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

[Serializable]
public class MapBlockGrant
{
    public string blockAssetName;   // resolves via LevelMapController.buildableBlocks
    public int    count;
}

[Serializable]
public class PlacedMapBlock
{
    public Vector3Int[] cells;          // absolute grid cells (LevelSelect map's GridSystem space)
    public string       blockAssetName;
    public int          rotationY;      // 0..3, ×90° — kept for reference; `cells` is already rotated & absolute
}
