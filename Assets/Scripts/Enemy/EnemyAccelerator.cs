using UnityEngine;

// Chaser enemy: starts slow, keeps building speed the longer it's alive. Add
// alongside an EnemySurfaceUnit (same pattern as EnemySplitOnAlive).
//
// Design intent: the threat is TIME, not toughness. Ignore it while you deal
// with the wave and it arrives as a blur — it punishes leaving one enemy to
// "handle later", which is exactly the habit a static wave lets you form.
//
// Ramps baseSpeedMultiplier, NOT SetSpeedMultiplier: the latter is the temporary
// channel EnemySlowEffect owns (Order synergy / slow turrets), and writing it
// here would mean each system erased the other's value on restore. Enemy speed
// is base × temporary, so ramping the base composes — a slow turret still bites
// on an accelerating enemy, it just fights a bigger base each second.
[RequireComponent(typeof(EnemySurfaceUnit))]
public class EnemyAccelerator : MonoBehaviour
{
    [Header("Acceleration")]
    [Tooltip("Speed multiplier it spawns at, relative to its normal speed. <1 = starts sluggish.")]
    [Min(0.05f)] public float startMultiplier = 0.6f;
    [Tooltip("Speed multiplier it tops out at. 2 = double its normal speed.")]
    [Min(0.05f)] public float maxMultiplier = 2.4f;
    [Tooltip("Seconds of being alive to go from start to max.")]
    [Min(0.1f)] public float rampSeconds = 12f;
    [Tooltip("Delay before it starts winding up.")]
    [Min(0f)] public float rampDelay = 1f;

    [Header("Visual")]
    [Tooltip("Aura tint at full speed. Fades in as it winds up, so the danger is readable.")]
    public Color auraColor = new Color(1f, 0.55f, 0.15f);

    EnemySurfaceUnit _self;
    SynergyAura _aura;
    float _spawnBase = 1f;
    float _t;

    // 0..1 wind-up progress — handy for the wave-intel dossier / debugging.
    public float RampProgress => Mathf.Clamp01((_t - rampDelay) / Mathf.Max(0.01f, rampSeconds));

    void Awake()
    {
        _self = GetComponent<EnemySurfaceUnit>();
    }

    void OnEnable()
    {
        // Capture the base the spawner gave us (BalanceTable overrides it per
        // record AFTER Awake), then scale around it so a record's own speed
        // rating still matters.
        if (_self != null) _spawnBase = Mathf.Max(0.01f, _self.baseSpeedMultiplier);
        _t = 0f;
    }

    void Update()
    {
        if (_self == null || _self.CurrentHealth <= 0) return;

        _t += Time.deltaTime;
        float k = RampProgress;
        float mul = Mathf.Lerp(startMultiplier, maxMultiplier, k);
        _self.baseSpeedMultiplier = _spawnBase * mul;

        // Aura fades in with the wind-up so a fast one is obvious before it hits.
        if (k > 0.05f)
        {
            if (_aura == null)
            {
                float size = GridSystem.instance != null ? GridSystem.instance.cellSize * 0.85f : 0.85f;
                _aura = SynergyBuffFx.AttachAura(transform, auraColor, size);
                _aura?.Persist();
            }
            if (_aura != null) _aura.Set(new Color(auraColor.r, auraColor.g, auraColor.b, k),
                                        (GridSystem.instance != null ? GridSystem.instance.cellSize : 1f) * (0.6f + 0.5f * k));
        }
    }

    void OnDisable()
    {
        if (_aura != null) _aura.Remove();
        _aura = null;
    }
}
