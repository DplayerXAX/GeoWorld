// How the next gameplay run is configured. Title / LevelSelect set this, then
// load the "gamePlay" scene; GameFlowManager.Start reads it to decide how to open.
//
// Static on purpose: a plain static class survives a scene load with zero
// serialization or DontDestroyOnLoad plumbing — it's just process memory.
public enum GameMode { Level, Endless }

public static class RunConfig
{
    public static GameMode        Mode  = GameMode.Endless;
    public static LevelDefinition Level;      // null in Endless mode
    public static ulong           Seed;       // 0 = randomize at run start

    // Set by GameFlowManager right after a level's FIRST clear (if it has one
    // authored); consumed once by LevelMapController.Start() back on LevelSelect,
    // then cleared so it never replays. Static for the same reason as the rest of
    // this class — needs to survive the gameplay → LevelSelect scene load.
    public static DialogueConversation PendingRewardConversation;
    // Which level granted PendingRewardConversation — set alongside it, consumed
    // alongside it. Lets LevelMapController focus the camera on THAT level's own
    // map marker right before the reward/build-tutorial dialogue starts ("here's
    // what you earned, and here's where you earned it"), without hard-coding any
    // specific level id.
    public static string PendingRewardLevelId;

    // Set alongside PendingRewardConversation on a level's FIRST clear — the
    // cleared level's id. LevelMapController.Start() consumes it once: if it
    // matches decorGateLevelId, it plays a camera-locked "grow in" cutscene for
    // that decoration BEFORE any first-visit/reward dialogue fires, instead of
    // the decoration just silently existing next time the map loads. Cleared
    // immediately after being read, same one-shot contract as the field above.
    public static string PendingMapGrowthLevelId;

    // levelId of the node the pawn was standing on when it last entered a level —
    // set right before the gameplay scene loads (LevelMapController.EnterLevel),
    // read (not cleared — sticky across repeated round trips) by
    // LevelMapController.Start() so returning to LevelSelect keeps the pawn where
    // it left off instead of snapping back to the home block every time.
    public static string LastLevelSelectNodeId;

    public static void SetLevel(LevelDefinition level)
    {
        Mode  = GameMode.Level;
        Level = level;
        Seed  = level != null ? level.runSeed : 0UL;
    }

    public static void SetEndless(ulong seed = 0UL)
    {
        Mode  = GameMode.Endless;
        Level = null;
        Seed  = seed;
    }
}
