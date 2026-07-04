using UnityEngine;

// Lightweight per-run stat tracker (kills, blocks placed, elapsed time) plus the
// shared score formula used by both level-clear (LevelClearScreen) and endless
// (GameFlowManager) settlement. Subscribes to existing hooks — EnemySurfaceUnit.AnyDied,
// PlacementController.BlockPlaced — once for the app's lifetime; BeginRun() just resets
// the counters at the start of each run.
public static class RunStats
{
    public static int Kills { get; private set; }
    public static int BlocksPlaced { get; private set; }
    static float _startTime;
    public static float ElapsedTime => Time.time - _startTime;

    static bool _subscribed;

    public static void BeginRun()
    {
        Kills = 0;
        BlocksPlaced = 0;
        _startTime = Time.time;

        if (_subscribed) return;
        _subscribed = true;
        EnemySurfaceUnit.AnyDied        += OnKill;
        PlacementController.BlockPlaced += OnBlockPlaced;
    }

    static void OnKill(EnemySurfaceUnit _) => Kills++;
    static void OnBlockPlaced(BlockData _, Vector3Int[] __) => BlocksPlaced++;

    // Stars: cleared = 1, + bonus objectives met, + flawless (no lives lost).
    public static int ComputeStars(bool objectivesMet, int lives, int maxLives)
    {
        int stars = 1;
        if (objectivesMet) stars++;
        if (maxLives > 0 && lives >= maxLives) stars++;
        return Mathf.Clamp(stars, 1, 3);
    }

    // Shared score formula. Waves/lives/stars carry the base, kills reward active
    // play, and a decaying time bonus rewards clearing quickly (caps out after 5
    // minutes, never goes negative). Tune the constants here — every caller (level
    // clear, endless) goes through this one formula.
    public static int ComputeScore(int wavesReached, int lives, int stars)
    {
        int timeBonus = Mathf.Max(0, 300 - Mathf.RoundToInt(ElapsedTime)) * 2;
        return Mathf.Max(0, wavesReached) * 150
             + Mathf.Max(0, lives) * 40
             + Mathf.Max(0, stars - 1) * 250
             + Kills * 8
             + timeBonus;
    }
}
