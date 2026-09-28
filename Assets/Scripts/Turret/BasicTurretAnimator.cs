using UnityEngine;

// Basic turret: the free tumble every turret had under TurretBeacon — a random
// axis and speed per turret, turning the whole model, plus the shared bob.
//
// Attack: a hard contraction the instant it fires — the whole model snaps small,
// is thrown back away from the target, and its tumble whips round faster — then
// springs back out through a slight overshoot. The model is a single mesh, so the
// whole root is what moves (RootOffset / SetRootScale).
public class BasicTurretAnimator : TurretAnimator
{
    public float minSpinSpeed = 80f;
    public float maxSpinSpeed = 140f;

    [Header("Attack")]
    [Tooltip("How far it contracts on a shot (0.3 = to 70% size).")]
    [Range(0f, 0.6f)] public float squeeze = 0.32f;
    [Tooltip("How far it is thrown back from the target, in world units.")]
    public float recoil = 0.12f;
    [Tooltip("Tumble speed multiplier at the moment of the shot, fading back to 1.")]
    public float spinBurst = 4f;
    [Tooltip("How fast the kick dies away, and how fast it rings.")]
    public float kickDecay = 9f, kickFreq = 20f;

    Vector3 _spin;
    bool    _squeezing;

    protected override void Setup()
    {
        _spin = new Vector3(Random.Range(-1f, 1f), Random.Range(-1f, 1f), Random.Range(-1f, 1f)).normalized
              * Random.Range(minSpinSpeed, maxSpinSpeed);
    }

    protected override void Animate(float dt)
    {
        float k = Spring(FireAge, kickDecay, kickFreq);

        // Tumble, whipped faster by the shot (the burst fades on its own curve, no ring).
        float burst = 1f + spinBurst * Mathf.Exp(-kickDecay * 0.6f * FireAge);
        transform.Rotate(_spin * (dt * burst), Space.Self);

        // Snap small, spring back out. Left alone entirely between shots, so the
        // root's scale is only ever touched while a kick is actually playing.
        if (k != 0f || _squeezing) SetRootScale(Vector3.one * (1f - squeeze * k));
        _squeezing = k != 0f;

        // Thrown back along the shot, horizontally.
        var back = FireDir; back.y = 0f;
        RootOffset = back.sqrMagnitude > 1e-6f ? ToParentSpace(-back.normalized * (recoil * k)) : Vector3.zero;
    }
}
