using UnityEngine;

// Registry of every level, so a saved map's levelId strings resolve back to
// LevelDefinition assets when the level-select scene rebuilds the map.
[CreateAssetMenu(menuName = "GeoWorld/Level Database", fileName = "LevelDatabase")]
public class LevelDatabase : ScriptableObject
{
    public LevelDefinition[] levels;

    public LevelDefinition Find(string id)
    {
        if (string.IsNullOrEmpty(id) || levels == null) return null;
        for (int i = 0; i < levels.Length; i++)
            if (levels[i] != null && levels[i].levelId == id) return levels[i];
        return null;
    }
}
