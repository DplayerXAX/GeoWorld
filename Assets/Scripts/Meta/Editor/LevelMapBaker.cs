#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

// Editor tool: bake a dev-time level-map JSON (saved by LevelMapAuthor into
// persistentDataPath/levelmaps) into a LevelMapAsset that ships inside the build.
//   GeoWorld ▸ Level Map ▸ Bake JSON → Asset
// Pick the .json, it writes / updates  Assets/LevelMaps/<name>.asset.
// Then assign that asset to LevelMapController.mapAsset.
public static class LevelMapBaker
{
    const string OutDir = "Assets/LevelMaps";

    [MenuItem("GeoWorld/Level Map/Bake JSON → Asset")]
    static void Bake()
    {
        string startDir = Path.Combine(Application.persistentDataPath, "levelmaps");
        string json = EditorUtility.OpenFilePanel("Pick a level-map JSON", startDir, "json");
        if (string.IsNullOrEmpty(json)) return;

        LevelMapData data;
        try { data = JsonUtility.FromJson<LevelMapData>(File.ReadAllText(json)); }
        catch (System.Exception e) { Debug.LogError($"[LevelMapBaker] parse failed: {e.Message}"); return; }
        if (data == null || data.nodes == null || data.nodes.Count == 0)
        {
            Debug.LogError("[LevelMapBaker] no nodes in " + json);
            return;
        }

        Directory.CreateDirectory(OutDir);
        string name      = Path.GetFileNameWithoutExtension(json);
        string assetPath = $"{OutDir}/{name}.asset";

        var asset = AssetDatabase.LoadAssetAtPath<LevelMapAsset>(assetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<LevelMapAsset>();
            asset.data = data;
            AssetDatabase.CreateAsset(asset, assetPath);
        }
        else
        {
            asset.data = data;
            EditorUtility.SetDirty(asset);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorGUIUtility.PingObject(asset);
        Debug.Log($"[LevelMapBaker] baked {data.nodes.Count} nodes: {json} → {assetPath}");
    }
}
#endif
