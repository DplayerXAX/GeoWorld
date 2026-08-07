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

    // Set by GameFlowManager on a level's first clear; consumed once by
    // LevelMapController.Start() back on LevelSelect, then cleared.
    public static DialogueConversation PendingRewardConversation;
    // Which level granted PendingRewardConversation — lets LevelMapController
    // focus the camera on that level's map marker before the dialogue starts.
    public static string PendingRewardLevelId;

    // Set alongside PendingRewardConversation. If it matches decorGateLevelId,
    // LevelMapController.Start() plays the decoration's grow-in cutscene first.
    public static string PendingMapGrowthLevelId;

    // levelId of the node the pawn stood on when it last entered a level. Sticky
    // (not cleared) so returning to LevelSelect resumes there instead of the home block.
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
