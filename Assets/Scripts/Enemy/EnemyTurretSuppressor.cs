using UnityEngine;

// Disruptor enemy: turrets near it fire slower while it's alive. Add alongside
// an EnemySurfaceUnit (same pattern as EnemySplitOnAlive).
//
// Design intent: punishes stacking every turret into one nest — a suppressor
// walking past shuts the whole nest down at once, so spreading coverage (or
// killing it before it arrives) becomes the answer.
//
// Tick + linger mirrors OrderSlowEffect: re-applies a short debuff every tick,
// so it lingers `lingerDuration` after the suppressor leaves or dies rather than
// expiring mid-tick, and it needs no cleanup on death.
[RequireComponent(typeof(EnemySurfaceUnit))]
public class EnemyTurretSuppressor : MonoBehaviour
{
    [Header("Suppression")]
    [Tooltip("Radius in world units — turrets inside fire slower.")]
    [Min(0.1f)] public float radius = 3.5f;
    [Tooltip("Fire-rate multiplier applied to turrets in range. 0.5 = half attack speed.")]
    [Range(0.05f, 1f)] public float fireRateMultiplier = 0.5f;
    [Tooltip("How often it re-scans for turrets in range.")]
    [Min(0.02f)] public float tickInterval = 0.2f;
    [Tooltip("How long the debuff lingers after a turret leaves the aura. Keep >= a couple of ticks.")]
    [Min(0.05f)] public float lingerDuration = 0.5f;

    [Header("Visual")]
    public Color auraColor = new Color(0.85f, 0.35f, 0.95f);

    EnemySurfaceUnit _self;
    SynergyAura _aura;
    float _timer;

    void Awake() => _self = GetComponent<EnemySurfaceUnit>();

    void Update()
    {
        if (_self == null || _self.CurrentHealth <= 0) return;

        // See EnemyHealerAura: the shared per-target aura slot can be taken over
        // and destroyed by a temporary EnemySlowEffect — re-attach if that happens.
        if (_aura == null)
        {
            _aura = SynergyBuffFx.AttachAura(transform, auraColor, radius * 0.5f);
            _aura?.Persist();
        }

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = tickInterval;
        Tick();
    }

    void Tick()
    {
        var grid = GridSystem.instance;
        if (grid == null) return;

        float r2  = radius * radius;
        var   all = grid.GetAllInstances();
        for (int i = 0; i < all.Count; i++)
        {
            var ins = all[i];
            if (ins?.visualObject == null || ins.data == null) continue;
            if (!TurretTypes.Is(ins.data.blockType)) continue;
            if ((ins.visualObject.transform.position - transform.position).sqrMagnitude > r2) continue;

            var tc = ins.visualObject.GetComponentInChildren<TurretController>();
            if (tc != null) TurretSuppressionEffect.Apply(tc, lingerDuration, fireRateMultiplier);
        }
    }

    void OnDisable()
    {
        if (_aura != null) _aura.Remove();
        _aura = null;
    }
}
