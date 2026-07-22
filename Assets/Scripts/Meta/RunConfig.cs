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
