using System.Collections.Generic;
using UnityEngine;

// Author-facing config for the Gallery scene. Create via
//   Assets ▸ Create ▸ GeoWorld ▸ Gallery Config
// and place it at  Assets/Resources/GalleryConfig.asset  so GalleryScreen loads it
// with Resources.Load. Any section left empty falls back to GalleryScreen's built-in
// defaults (shaders from Resources/Gallery/Shaders, monsters from BalanceTable, and
// the Calm/Battle mood switcher for music).
[CreateAssetMenu(menuName = "GeoWorld/Gallery Config", fileName = "GalleryConfig")]
public class GalleryConfig : ScriptableObject
{
    [System.Serializable]
    public class MusicTrack
    {
        public string title = "Track";
        [TextArea] public string description;
        [Tooltip("Wwise event posted when this track is selected. The previous track is stopped first.")]
        public AK.Wwise.Event track;
    }

    [System.Serializable]
    public class ShaderEntry
    {
        public string title = "Shader";
        [TextArea] public string description;
        [Tooltip("Material shown on the shader cube for this entry.")]
        public Material material;
    }

    [System.Serializable]
    public class MonsterEntry
    {
        public string title;
        [TextArea] public string description;
        [Tooltip("Enemy prefab — instantiated live on the Gallery pedestal, normalised to GalleryScreen.monsterDisplaySize.")]
        public EnemySurfaceUnit prefab;
    }

    [Header("Music — the tracks the record plays (leave empty for the Calm/Battle switcher)")]
    public List<MusicTrack> music = new();

    [Header("Shaders — materials shown on the cube (leave empty to auto-load Resources/Gallery/Shaders)")]
    public List<ShaderEntry> shaders = new();

    [Header("Monsters — leave empty to auto-populate from BalanceTable")]
    public List<MonsterEntry> monsters = new();
}
