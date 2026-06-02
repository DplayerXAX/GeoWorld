using UnityEngine;

// Appends an outline material as an additional slot on every Renderer in
// this GameObject and its children. Use with a Material that uses the
// `GeoWorld/BlockOutline` shader — the extra pass renders the inverse-hull
// outline behind the original mesh.
//
// Two ways to use:
//   A. Attach to your cube prefab → every spawned cube gets the outline.
//   B. Attach to a parent root (e.g. the PlacedBlock root that
//      BlockRenderer creates) and it walks all descendant Renderers.
//
// Append vs replace: the outline is ADDED to the existing material list,
// not replacing slot 0. So the block keeps its silkscreen / synergy color
// underneath and the outline renders on top of the geometry.
[DisallowMultipleComponent]
public class BlockOutlineApplier : MonoBehaviour
{
    [Tooltip("Material using the BlockOutline shader. If null, this component does nothing.")]
    public Material outlineMaterial;

    [Tooltip("If true, runs once on Awake. If false, you can manually call Apply() — useful for prefabs spawned via code.")]
    public bool applyOnAwake = true;

    [Tooltip("Recurse into children. Off = only this GameObject's Renderer.")]
    public bool includeChildren = true;

    [Tooltip("Skip Renderers that already have this material in their list (avoids double-add on hot reload).")]
    public bool skipIfAlreadyPresent = true;

    void Awake()
    {
        if (applyOnAwake) Apply();
    }

    public void Apply()
    {
        if (outlineMaterial == null) return;

        var rends = includeChildren
            ? GetComponentsInChildren<Renderer>(includeInactive: true)
            : new[] { GetComponent<Renderer>() };

        for (int i = 0; i < rends.Length; i++)
        {
            var r = rends[i];
            if (r == null) continue;

            var current = r.sharedMaterials;

            // Bail if already there.
            if (skipIfAlreadyPresent)
            {
                bool found = false;
                for (int m = 0; m < current.Length; m++)
                    if (current[m] == outlineMaterial) { found = true; break; }
                if (found) continue;
            }

            var next = new Material[current.Length + 1];
            for (int m = 0; m < current.Length; m++) next[m] = current[m];
            next[current.Length] = outlineMaterial;
            r.sharedMaterials = next;
        }
    }
}
