using UnityEngine;

// Thin front door to OrbitCamera.Shake so callers don't each re-find the rig.
// The shake itself lives in OrbitCamera because that's what owns the camera
// transform (see the comment there).
public static class CameraShake
{
    static OrbitCamera _cam;

    public static void Shake(float amplitude, float duration)
    {
        if (_cam == null) _cam = Object.FindFirstObjectByType<OrbitCamera>();
        if (_cam != null) _cam.Shake(amplitude, duration);
    }

    // Losing a life. Sharp but small — this fires repeatedly during a bad wave,
    // so it has to stay readable rather than nauseating.
    public static void Damage() => Shake(0.16f, 0.22f);

    // Game over. The one place a genuinely violent hit is warranted.
    public static void Death() => Shake(0.85f, 0.55f);
}
