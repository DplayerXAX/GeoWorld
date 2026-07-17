using UnityEngine;

// Temporary fire-rate debuff on a turret, applied + refreshed each tick by any
// EnemyTurretSuppressor in range. Mirrors EnemySlowEffect: the strongest
// multiplier wins while several suppressors overlap, the timer refreshes so the
// debuff lingers a moment after the last suppressor leaves (or dies), then it
// restores the turret and removes itself.
//
// Writes the DEBUFF channel (not SetSynergyFireRateMultiplier) so Harmony's buff
// and this can both be active without erasing each other.
public class TurretSuppressionEffect : MonoBehaviour
{
    TurretController _turret;
    float _timer;
    float _mult = 1f;

    public static void Apply(TurretController turret, float duration, float multiplier)
    {
        if (turret == null || duration <= 0f) return;

        var effect = turret.GetComponent<TurretSuppressionEffect>();
        if (effect == null) effect = turret.gameObject.AddComponent<TurretSuppressionEffect>();

        effect._turret = turret;
        effect._timer  = Mathf.Max(effect._timer, duration);
        // Strongest (lowest) multiplier wins while multiple suppressors overlap.
        effect._mult   = Mathf.Min(effect._mult, Mathf.Clamp(multiplier, 0.01f, 1f));
        turret.SetDebuffFireRateMultiplier(effect._mult);
    }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        if (_turret != null) _turret.SetDebuffFireRateMultiplier(1f);
        Destroy(this);
    }
}
