using System;

// What the room is set to play. The host owns it; clients receive it.
//
// Separate from RunConfig because the two answer different questions. RunConfig is
// "what is this machine loading", which every mode needs; RoomConfig is "what have
// the four of us agreed on", which only exists between opening a room and starting
// the match. Folding them together would mean a client's local RunConfig could be
// edited into disagreeing with the host's, and the desync would only surface once
// the level was already loading.
public static class RoomConfig
{
    /// <summary>Empty = endless.</summary>
    public static string LevelId = "";
    public static ulong  Seed;

    /// <summary>The scene a started match loads on every machine.</summary>
    public static string GameplayScene = "gamePlay_MP";

    public static event Action Changed;

    public static void Set(string levelId, ulong seed)
    {
        LevelId = levelId ?? "";
        Seed    = seed;
        Changed?.Invoke();
    }

    /// <summary>
    /// Hand the agreed settings to RunConfig, which is what the gameplay scene reads.
    /// Called on every machine just before the load, from the same broadcast — so all
    /// four resolve the level from the same id rather than from their own UI state.
    /// </summary>
    public static void PushToRunConfig(LevelDatabase db)
    {
        var level = db != null ? db.Find(LevelId) : null;
        if (level != null) RunConfig.SetLevel(level);
        else               RunConfig.SetEndless(Seed);
    }

    public static void Reset()
    {
        LevelId = "";
        Seed    = 0UL;
        Changed?.Invoke();
    }
}
