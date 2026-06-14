using System.IO;
using UnityEngine;

// Save / load level-select maps as JSON (persistentDataPath/levelmaps), mirroring
// SnapshotManager. CaptureCurrent reads the live grid + LevelNodeTags, so you
// author a map with the real placement tool and just press Save.
public static class LevelMapIO
{
    static string Dir => Path.Combine(Application.persistentDataPath, "levelmaps");
    static string PathFor(string name) => Path.Combine(Dir, name + ".json");

    public static LevelMapData CaptureCurrent(string name = "map")
    {
        var data = new LevelMapData { name = name };
        var grid = GridSystem.instance;
        if (grid == null) return data;

        foreach (var ins in grid.GetAllInstances())
        {
            if (ins?.visualObject == null) continue;
            var rend = ins.visualObject.GetComponentInChildren<Renderer>();
            var tag  = ins.visualObject.GetComponent<LevelNodeTag>();
            data.nodes.Add(new LevelMapNode
            {
                cells         = ins.occupiedCells.ToArray(),
                color         = rend != null ? MpbColor.Get(rend) : Color.white,
                blockTypeName = ins.data != null ? ins.data.blockType.ToString() : "",
                levelId       = (tag != null && tag.level != null) ? tag.level.levelId : "",
                isStart       = tag != null && tag.isStart,
            });
        }
        return data;
    }

    public static string Save(LevelMapData data, string name = null)
    {
        if (data == null) return null;
        if (!string.IsNullOrEmpty(name)) data.name = name;
        Directory.CreateDirectory(Dir);
        var path = PathFor(data.name);
        File.WriteAllText(path, JsonUtility.ToJson(data, true));
        Debug.Log($"[LevelMap] saved → {path}");
        return path;
    }

    public static LevelMapData Load(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path)) { Debug.LogWarning($"[LevelMap] not found: {path}"); return null; }
        return JsonUtility.FromJson<LevelMapData>(File.ReadAllText(path));
    }
}
