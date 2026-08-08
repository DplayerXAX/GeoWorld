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

    // Shown once, the very first time the player ever lands on LevelSelect —
    // LevelMapController.firstVisitConversation. Set the instant it plays (not
    // after it finishes), same as every other one-time-beat gate in this class, so
    // it can never replay even if the player quits mid-conversation.
    public bool seenLevelSelectIntro;

    // MapInteractable.id values already interacted with once — lets an NPC have a
    // one-time introduction and a different line on every visit after.
    public List<string> seenInteractables = new();

    // Best score per minigame, keyed by MapInteractable.id.
    public List<MinigameScore> minigameScores = new();

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

    public bool HasSeenInteractable(string id) =>
        !string.IsNullOrEmpty(id) && seenInteractables.Contains(id);

    public void MarkInteractableSeen(string id)
    {
        if (!string.IsNullOrEmpty(id) && !seenInteractables.Contains(id))
            seenInteractables.Add(id);
    }

    public int GetMinigameBest(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        var e = minigameScores.Find(x => x.id == id);
        return e != null ? e.best : 0;
    }

    // Returns true when this run actually set a new record, so the caller can say so.
    public bool RecordMinigameScore(string id, int score)
    {
        if (string.IsNullOrEmpty(id)) return false;
        var e = minigameScores.Find(x => x.id == id);
        if (e == null) { minigameScores.Add(new MinigameScore { id = id, best = score }); return score > 0; }
        if (score <= e.best) return false;
        e.best = score;
        return true;
    }

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

    // Capped at 1 in stock — reward blocks are single-use bridge pieces, not a
    // stockpile. Two different levels can (and do) reward the same shape; the
    // second grant is simply a no-op if the first one hasn't been placed yet.
    public void GrantMapBlock(string blockAssetName, int count = 1)
    {
        if (string.IsNullOrEmpty(blockAssetName) || count <= 0) return;
        var g = mapBlockInventory.Find(x => x.blockAssetName == blockAssetName);
        if (g == null) { g = new MapBlockGrant { blockAssetName = blockAssetName }; mapBlockInventory.Add(g); }
        g.count = Mathf.Min(1, g.count + count);
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

    // Full board state of the run that most recently cleared this level — the
    // same GridSnapshot format the dev save/load tool already uses (see
    // SnapshotManager), reused as the player-facing "keepsake" instead of a
    // baked screenshot: cheap to store, and LevelBuildThumbnail can re-render it
    // (or a future full 3D viewer could replay it) any time art or lighting
    // changes, instead of a stale flat image. Overwritten on every clear, not
    // just the first, so it always reflects your latest winning build.
    public GridSnapshot buildSnapshot;

    // Which synergy theme was active (highest tier) at the moment this level was
    // last cleared — None if nothing was active. Drives the celebratory ring on
    // this level's LevelSelect badge (see LevelMapController.MapLevelMarker).
    public BlockColor clearSynergyColor = BlockColor.None;
}

[Serializable]
public class MinigameScore
{
    public string id;   // MapInteractable.id
    public int    best;
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
    public Quaternion   rotation = Quaternion.identity;   // full 3-axis — kept for reference; `cells` is already rotated & absolute
}
