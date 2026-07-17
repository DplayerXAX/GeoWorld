using UnityEngine;

// Tiny helper that swaps per-renderer color writes from `.material.color`
// (auto-instantiates a unique material) to a MaterialPropertyBlock override.
// All renderers keep sharing the same material asset, which lets URP's GPU
// instancing path fold them into a single draw call.
public static class MpbColor
{
    static readonly int _ColorID = Shader.PropertyToID("_BaseColor");

    // The custom prism shadergraph (used by the turret model) exposes
    // _Base_color / _Light_color instead of URP's stock _BaseColor, and
    // _Light_color is the one that actually drives its look. Writing only
    // _BaseColor silently did nothing on those renderers — every turret stayed
    // the colour baked into the material. Setting all three is harmless: an MPB
    // property the shader doesn't declare is simply ignored.
    static readonly int _BaseColorUnderscoreID = Shader.PropertyToID("_Base_color");
    static readonly int _LightColorID          = Shader.PropertyToID("_Light_color");

    static MaterialPropertyBlock _block;

    public static void Set(Renderer r, Color c)
    {
        if (r == null) return;
        if (_block == null) _block = new MaterialPropertyBlock();
        r.GetPropertyBlock(_block);
        _block.SetColor(_ColorID, c);
        if (HasProp(r, _BaseColorUnderscoreID)) _block.SetColor(_BaseColorUnderscoreID, c);
        if (HasProp(r, _LightColorID))          _block.SetColor(_LightColorID, c);
        r.SetPropertyBlock(_block);
    }

    static bool HasProp(Renderer r, int id)
    {
        var m = r.sharedMaterial;
        return m != null && m.HasProperty(id);
    }

    public static Color Get(Renderer r)
    {
        if (r == null) return Color.white;
        if (_block == null) _block = new MaterialPropertyBlock();
        r.GetPropertyBlock(_block);
        Color c = _block.GetColor(_ColorID);
        if (c.a == 0f && HasProp(r, _LightColorID)) c = _block.GetColor(_LightColorID);
        // Empty block → fall back to the shared material's authored colour
        // so picking up a freshly-spawned (never-Set) renderer still works.
        if (c.a == 0f && r.sharedMaterial != null)
        {
            if (r.sharedMaterial.HasProperty(_ColorID))              c = r.sharedMaterial.GetColor(_ColorID);
            else if (r.sharedMaterial.HasProperty(_LightColorID))     c = r.sharedMaterial.GetColor(_LightColorID);
        }
        return c;
    }
}
