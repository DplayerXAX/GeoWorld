using UnityEngine;

// Debuff turret: it never fires, so its motion is its only tell. A slow, steady turn
// about the vertical, and a breathing pulse — which quickens and deepens while an
// enemy is inside its field (TurretController reports the nearest one through
// OnTarget), so at a glance you can see which of these is actually working.
public class DebuffTurretAnimator : TurretAnimator
{
    [Tooltip("Degrees per second of the steady turn.")]
    public float turnSpeed = 25f;
    [Tooltip("Breathing depth at rest / with an enemy in the field (fraction of size).")]
    public float breatheIdle = 0.04f, breatheActive = 0.11f;
    [Tooltip("Breaths per second at rest / with an enemy in the field.")]
    public float rateIdle = 0.5f, rateActive = 1.6f;
    [Tooltip("How quickly it moves between the two.")]
    public float engageEase = 4f;

    float _engaged;   // 0 = idle, 1 = an enemy in the field — eased
    float _phase;

    protected override void Animate(float dt)
    {
        _engaged = Mathf.Lerp(_engaged, HasTarget ? 1f : 0f, 1f - Mathf.Exp(-engageEase * dt));

        transform.Rotate(Vector3.up, turnSpeed * dt, Space.World);

        _phase += Mathf.Lerp(rateIdle, rateActive, _engaged) * Mathf.PI * 2f * dt;
        float depth = Mathf.Lerp(breatheIdle, breatheActive, _engaged);
        SetRootScale(Vector3.one * (1f + Mathf.Sin(_phase) * depth));
    }
}
