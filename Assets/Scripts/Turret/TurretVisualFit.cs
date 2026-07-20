using UnityEngine;

// Shared "fit a turret prefab to one grid cell" helper. Turret prefabs have wildly
// different intrinsic mesh sizes (the AOE turret is much bigger than the basic one),
// so a single shared scale multiplier can only ever look right for one of them.
// Measuring each prefab's own renderer bounds and scaling so its largest dimension
// equals the target makes every turret — on the board AND in the shop — read at the
// same cube size.
public static class TurretVisualFit
{
    // Uniformly scales `visual` so its largest measured renderer dimension equals
    // `targetSize`. Assumes `visual` is at localScale 1 / localPosition 0 / identity
    // rotation when called (so the measured bounds ARE the prefab's intrinsic size).
    //
    // Returns true on success and outputs the pre-scale local bounds — `localCenter`
    // and `maxDim` — so a caller that wants a matching click collider can size a
    // BoxCollider (center = localCenter, size = Vector3.one * maxDim) that scales
    // with the visual to `targetSize` in world space. Returns false when there's
    // nothing measurable (no renderers / degenerate bounds); the caller should then
    // apply its own fallback scale.
    public static bool Fit(GameObject visual, float targetSize, out Vector3 localCenter, out float maxDim)
    {
        localCenter = Vector3.zero;
        maxDim      = 0f;
        if (visual == null) return false;

        var rends = visual.GetComponentsInChildren<Renderer>();
        if (rends.Length == 0) return false;

        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Length; i++) b.Encapsulate(rends[i].bounds);

        localCenter = visual.transform.InverseTransformPoint(b.center);
        maxDim      = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (maxDim <= 1e-5f) return false;

        visual.transform.localScale = Vector3.one * (targetSize / maxDim);
        return true;
    }
}
