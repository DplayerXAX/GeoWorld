using UnityEngine;

// Tiny helper that swaps per-renderer color writes from `.material.color`
// (auto-instantiates a unique material) to a MaterialPropertyBlock override.
// All renderers keep sharing the same material asset, which lets URP's GPU
// instancing path fold them into a single draw call.
public static class MpbColor
{
    static readonly int _ColorID = Shader.PropertyToID("_BaseColor");
    static MaterialPropertyBlock _block;

    public static void Set(Renderer r, Color c)
    {
        if (r == null) return;
        if (_block == null) _block = new MaterialPropertyBlock();
        r.GetPropertyBlock(_block);
        _block.SetColor(_ColorID, c);
        r.SetPropertyBlock(_block);
    }

    public static Color Get(Renderer r)
    {
        if (r == null) return Color.white;
        if (_block == null) _block = new MaterialPropertyBlock();
        r.GetPropertyBlock(_block);
        Color c = _block.GetColor(_ColorID);
        // Empty block → fall back to the shared material's authored colour
        // so picking up a freshly-spawned (never-Set) renderer still works.
        if (c.a == 0f && r.sharedMaterial != null && r.sharedMaterial.HasProperty(_ColorID))
            c = r.sharedMaterial.GetColor(_ColorID);
        return c;
    }
}
