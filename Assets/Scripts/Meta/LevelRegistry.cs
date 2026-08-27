using UnityEngine;

// The LevelDatabase, reachable from a scene that has no Inspector reference to it.
//
// Every scene that owns a database already assigns one (TitleFlow, LevelMapController)
// and those are wired in their scenes; this just remembers the last one seen so a
// scene built entirely in code — the multiplayer lobby — can resolve a levelId
// without a serialized field of its own to keep in sync.
//
// Static, and therefore process-wide: it survives scene loads for free, which is the
// whole point. The editor fallback covers pressing Play directly in a scene that was
// never reached through Title.
public static class LevelRegistry
{
    static LevelDatabase _db;

    /// <summary>Called by whoever owns a database, as they load.</summary>
    public static void Register(LevelDatabase db)
    {
        if (db != null) _db = db;
    }

    public static LevelDatabase Db
    {
        get
        {
            if (_db != null) return _db;

#if UNITY_EDITOR
            // Play-from-this-scene in the editor never passes through Title, so
            // without this the lobby would show an empty level list only in the
            // editor — the one place it gets tested most.
            foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:LevelDatabase"))
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                var db   = UnityEditor.AssetDatabase.LoadAssetAtPath<LevelDatabase>(path);
                if (db != null) { _db = db; break; }
            }
#endif
            return _db;
        }
    }

    /// <summary>Resolve a level id, or null for "endless".</summary>
    public static LevelDefinition Find(string id)
    {
        var db = Db;
        return db != null ? db.Find(id) : null;
    }
}
