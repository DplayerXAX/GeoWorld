using System;
using System.Collections.Generic;
using UnityEngine;

// A level left part-way through, kept in the profile so it can be picked up again
// (GameFlowManager.RunSave). Always a BUILD-phase moment — the start of a turn,
// just before a wave, or a Save & Quit — never the middle of a fight: a fight is
// enemies in flight, bullets, timers, and not worth the risk of restoring badly
// when replaying the wave from its start is what the player would expect anyway.
[Serializable]
public class LevelRunSave
{
    public int    version = 2;
    public EnvironmentRunState environment;
    public string levelId;
    public string savedAt;

    // ── The board ────────────────────────────────────────────────────────────
    public GridSnapshot board;               // blocks (+ inherited / sealed), endpoints, round counters, camera
    public Vector3Int   challengeCell;
    public bool         challengeIsStart;

    // ── The run ──────────────────────────────────────────────────────────────
    public int      wavesCompleted;
    public string   runSeed;                 // ulong, as text — JsonUtility and 64-bit unsigned don't mix
    public string[] rngState;                // the run stream mid-run, so the next waves and shops come out as they would have
    public int[]    blockCurrency;
    public int[]    turretCurrency;
    public int      lives;

    // ── The shop as it stands ────────────────────────────────────────────────
    public List<ShopItemSave> shop = new();
    public int                refreshCost;

    // ── Progress trackers ────────────────────────────────────────────────────
    public int          tutorialStep = -1;   // -1 = not a tutorial / nothing to resume
    public int          objKills, objLeaks, objPlaced, objMaxSynergies, objMaxTurretLevel;
    public int          statKills, statBlocks;
    public float        statElapsed;
    public List<string> ownedCards = new();  // UpgradeCard asset names

    // ── Level mechanics that live on the board between waves ─────────────────
    public List<CellHealth>  chaosBlocks = new();
    public List<Vector3Int>  shrines     = new();
}

[Serializable]
public class ShopItemSave
{
    public string     blockAssetName;
    public BlockColor color;
    public int        price;
    public bool       isTurret;
}

[Serializable]
public class CellHealth
{
    public Vector3Int cell;
    public int        health;
}
