using System.Collections;
using UnityEngine;

// ── Mid-level save ────────────────────────────────────────────────────────────
// A level can be left part-way and picked up again. The save (LevelRunSave, kept
// per level in the profile) is taken at three build-phase moments:
//   * just before each wave starts (Run) — so leaving mid-fight replays that
//     wave from its start rather than losing the build that led up to it;
//   * right after each wave ends, once the new turn is dealt (EndRunningPhase);
//   * on Save & Quit from the pause menu.
// It goes the moment the attempt is over — cleared, lost, or restarted.
//
// Resuming replaces the whole opening (inheritance, first endpoints, starting
// layout, the first StartTurn) with the saved board and turn: no fresh income, no
// fresh shop roll — the turn is exactly as it was left.
public partial class GameFlowManager
{
    /// <summary>True when leaving now keeps this level to come back to (the pause menu asks first).</summary>
    public bool CanResumeLater => RunSaveAllowed && !_levelDone && phase != GamePhase.GameOver;

    bool RunSaveAllowed =>
        RunConfig.Mode == GameMode.Level && RunConfig.Level != null
        && !(NetBootstrap.Online && MultiplayerSession.ConnectedCount > 1);   // one player's save can't hold a shared table

    /// <summary>
    /// Save the level as it stands. Build phase only (a fight in progress is never
    /// saved — the last save before it stands). Returns whether anything was written.
    /// </summary>
    public bool SaveRunNow()
    {
        if (!RunSaveAllowed || _levelDone || phase == GamePhase.GameOver || phase == GamePhase.Running) return false;

        var save = new LevelRunSave
        {
            levelId          = RunConfig.Level.levelId,
            environment      = ChapterEnvironmentController.Instance?.Capture(),
            savedAt          = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            board            = SnapshotManager.Capture(),
            challengeCell    = _challengeCell,
            challengeIsStart = _challengeIsStart,
            wavesCompleted   = _wavesCompleted,
            runSeed          = runSeed.ToString(),
            lives            = PlayerHealth.Instance != null ? PlayerHealth.Instance.CurrentLives : 0,
            refreshCost      = placement != null ? placement.RefreshCost : 0,
            tutorialStep     = TutorialDirector.CurrentStepIndex,
            statKills        = RunStats.Kills,
            statBlocks       = RunStats.BlocksPlaced,
            statElapsed      = RunStats.ElapsedTime,
        };

        var st = EnsureRng().SaveState();
        save.rngState = new string[st.Length];
        for (int i = 0; i < st.Length; i++) save.rngState[i] = st[i].ToString();

        var rm = ResourceManager.Instance;
        if (rm != null) { save.blockCurrency = rm.CaptureBlockWallets(); save.turretCurrency = rm.CaptureTurretWallets(); }
        if (ShopController.Instance != null)       save.shop        = ShopController.Instance.CaptureItems();
        if (UpgradeManager.Instance != null)       save.ownedCards  = UpgradeManager.Instance.OwnedNames();
        if (ChaosBlockController.Instance != null) save.chaosBlocks = ChaosBlockController.Instance.Capture();
        if (ShrineController.Instance != null)     save.shrines     = ShrineController.Instance.Capture();
        LevelObjectivesTracker.Capture(save);

        SaveSystem.Profile.SetRunSave(save);
        SaveSystem.Save();
        return true;
    }

    // The attempt is over — whatever was saved of it is no longer a place to return to.
    void DiscardRunSave()
    {
        var lv = RunConfig.Mode == GameMode.Level ? RunConfig.Level : null;
        if (lv == null) return;
        if (SaveSystem.Profile.ClearRunSave(lv.levelId)) SaveSystem.Save();
    }

    LevelRunSave PendingResume()
    {
        if (!RunSaveAllowed) return null;
        var s = SaveSystem.Profile.GetRunSave(RunConfig.Level.levelId);
        return s != null && s.board != null ? s : null;
    }

    // Board and turn back from a save — in place of SpawnInheritedLayout,
    // CreateFirstStage, SpawnStartingLayout and the first StartTurn.
    void ResumeRun(LevelRunSave s)
    {
        // The run stream: same seed, then exactly where it had got to, so the waves
        // and shops still to come are the ones this run would have had.
        if (ulong.TryParse(s.runSeed, out var seed) && seed != 0UL) { runSeed = seed; _rng = null; }
        var rng = EnsureRng();
        endpoints?.SetRng(rng);
        ConfigureEndpointBounds();
        // Before the stream is put back, so this draw is overwritten rather than shifting it.
        colorDistribution?.BeginRound(rng, RunConfig.Level.allowedColors);
        if (s.rngState != null && s.rngState.Length == 4)
        {
            var state = new ulong[4];
            bool ok = true;
            for (int i = 0; i < 4; i++) ok &= ulong.TryParse(s.rngState[i], out state[i]);
            if (ok) rng.LoadState(state);
        }

        // Board.
        var snap = s.board;
        if (snap.endpoints != null)
            foreach (var e in snap.endpoints)
            {
                if (e == null) continue;
                endpoints.SpawnEndpointAt(e.cell, e.isStart);
                (e.isStart ? allStarts : allEnds).Add(e.cell);
            }
        var placed = SnapshotManager.PlaceBlocks(snap, inherited: false, withUpgrades: true, keepFlags: true);
        foreach (var ins in placed)
        {
            ResourceManager.Instance?.OnBlockPlaced(ins.data.blockType);
            // Pops in with the endpoints after the intro, like any opening board.
            if (ins.visualObject != null) startingLayoutVisuals.Add(ins.visualObject);
        }
        roundIndex             = snap.roundIndex;
        _runsSinceLastEndpoint = snap.runsSinceLastEndpoint;
        _challengeCell         = s.challengeCell;
        _challengeIsStart      = s.challengeIsStart;
        _wavesCompleted        = s.wavesCompleted;

        // The player.
        ResourceManager.Instance?.RestoreWallets(s.blockCurrency, s.turretCurrency);
        if (PlayerHealth.Instance != null && s.lives > 0) PlayerHealth.Instance.RestoreLives(s.lives);
        RunStats.Restore(s.statKills, s.statBlocks, s.statElapsed);
        if (s.tutorialStep >= 0) TutorialDirector.ResumeFromStep = s.tutorialStep;

        // The turn, as StartTurn would have left it — minus the income and the shop
        // roll, which were already dealt when this turn was saved.
        MultiplayerSession.ClearReady();
        placement.currentBlock = null;
        placement.mode = PlacementMode.Select;
        placement.ClearTray();
        placement.MarkStartingShopApplied();
        placement.SetRefreshCost(s.refreshCost);
        ShopController.Instance?.RestoreItems(s.shop, placement.FindBlockDataByName, placement.cubePrefab, gridSystem);

        var cam = FindFirstObjectByType<OrbitCamera>();
        if (cam != null && snap.camera != null)
            cam.ApplyState(snap.camera.focusPoint, snap.camera.distance, snap.camera.yaw, snap.camera.pitch);
    }

    // What has to wait for every other Start(): the level mechanics and trackers
    // that are created alongside this manager and aren't guaranteed to exist yet.
    IEnumerator ResumeLate(LevelRunSave s)
    {
        yield return null;
        LevelObjectivesTracker.Restore(s);
        UpgradeManager.Instance?.RestoreOwned(s.ownedCards);
        ChaosBlockController.Instance?.Restore(s.chaosBlocks);
        ShrineController.Instance?.Restore(s.shrines);
        EvaluateGrid();   // the mechanics' cells are on the board now
        ChapterEnvironmentController.Instance?.Restore(s.environment, UpcomingWaveNumber);
    }
}
