using System.IO;
using UnityEditor;
using UnityEngine;

// Bakes the procedural decor / gear meshes into real .asset files under
// Resources, so the game loads them instead of rebuilding the vertex buffers on
// every LevelSelect load and every Order rig that spawns.
//
// Run it from  GeoWorld ▸ Bake Decor Meshes  after changing any mesh generator.
// If you forget, nothing breaks: the accessors fall back to generating in memory
// exactly as they did before, so a stale or missing bake costs performance, never
// correctness. The generators stay the source of truth; these are cached output.
public static class DecorMeshBaker
{
    const string Folder = "Assets/Resources/" + LevelMapController.BakedMeshFolder;

    [MenuItem("GeoWorld/Bake Decor Meshes")]
    public static void Bake()
    {
        Directory.CreateDirectory(Folder);

        int written = 0;
        foreach (var (name, mesh) in LevelMapController.BuildAllDecorMeshesForBake())
            written += Save(name, mesh) ? 1 : 0;
        foreach (var (name, mesh) in GearMeshFactory.BuildAllForBake())
            written += Save(name, mesh) ? 1 : 0;

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[DecorMeshBaker] Baked {written} meshes into {Folder}.");
    }

    static bool Save(string name, Mesh mesh)
    {
        if (mesh == null)
        {
            Debug.LogWarning($"[DecorMeshBaker] '{name}' generated nothing — skipped.");
            return false;
        }

        // The generators tag their meshes DontSave so the in-memory copies never
        // leak into a scene. CreateAsset refuses to write those, so clear it here
        // — on the copy we're about to hand to the AssetDatabase, which owns it
        // from this point on.
        mesh.hideFlags = HideFlags.None;
        mesh.name      = name;

        string path = $"{Folder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            // Overwrite in place rather than delete-and-recreate: anything already
            // referencing this asset by GUID keeps working.
            existing.Clear();
            existing.SetVertices(new System.Collections.Generic.List<Vector3>(mesh.vertices));
            existing.SetTriangles(mesh.triangles, 0);
            existing.SetNormals(new System.Collections.Generic.List<Vector3>(mesh.normals));
            existing.bounds = mesh.bounds;
            EditorUtility.SetDirty(existing);
        }
        else
        {
            AssetDatabase.CreateAsset(mesh, path);
        }
        return true;
    }
}
