using UnityEngine;

// Support enemy: periodically heals the OTHER enemies around it. Add alongside
// an EnemySurfaceUnit (same pattern as EnemySplitOnAlive).
//
// Design intent: makes raw turret DPS stop being the only answer — a wave with a
// healer wants the player to focus it down (or out-burst the heal) instead of
// grinding the tank it keeps topping up.
[RequireComponent(typeof(EnemySurfaceUnit))]
public class EnemyHealerAura : MonoBehaviour
{
    [Header("Heal pulse")]
    [Tooltip("Seconds between heal pulses.")]
    [Min(0.05f)] public float healInterval = 2f;
    [Tooltip("Radius in world units — enemies within this range are healed.")]
    [Min(0.1f)] public float healRadius = 3f;
    [Tooltip("Health restored per pulse, per enemy in range.")]
    [Min(1)] public int healAmount = 1;
    [Tooltip("Heal itself too? Off (recommended) = it only supports others, so focusing it still works.")]
    public bool healSelf = false;
    [Tooltip("Max enemies healed per pulse. 0 = no cap.")]
    [Min(0)] public int maxTargetsPerPulse = 0;

    [Header("Visual")]
    public Color auraColor = new Color(0.35f, 1f, 0.45f);

    EnemySurfaceUnit _self;
    SynergyAura _aura;
    float _timer;

    void Awake() => _self = GetComponent<EnemySurfaceUnit>();

    void Update()
    {
        if (_self == null || _self.CurrentHealth <= 0) return;

        // SynergyBuffFx keeps ONE aura per target, so a temporary EnemySlowEffect
        // aura can take this slot over and destroy it on expiry — re-attach when
        // that happens instead of silently losing the healer's tell.
        if (_aura == null)
        {
            _aura = SynergyBuffFx.AttachAura(transform, auraColor, healRadius * 0.5f);
            _aura?.Persist();
        }

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = healInterval;
        Pulse();
    }

    void Pulse()
    {
        var mgr = EnemyBaseManager.Instance;
        if (mgr == null) return;

        var enemies = mgr.ActiveEnemies;
        float r2     = healRadius * healRadius;
        float moteSize = GridSystem.instance != null ? GridSystem.instance.cellSize * 0.5f : 0.5f;
        int healed = 0;

        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e == null || e.CurrentHealth <= 0) continue;
            if (e == _self && !healSelf) continue;
            if ((e.transform.position - transform.position).sqrMagnitude > r2) continue;

            if (e.Heal(healAmount) <= 0) continue;   // already full — no mote, doesn't use up a target slot
            SynergyBuffFx.Mote(e.transform.position, auraColor, moteSize);

            healed++;
            if (maxTargetsPerPulse > 0 && healed >= maxTargetsPerPulse) break;
        }
    }

    void OnDisable()
    {
        if (_aura != null) _aura.Remove();
        _aura = null;
    }
}
