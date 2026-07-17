using UnityEngine;

// Saboteur enemy: whatever synergy it's standing on goes DARK while it's there.
// Add alongside an EnemySurfaceUnit (same pattern as EnemySplitOnAlive).
//
// Design intent: it attacks the thing the player built, not the thing the player
// shoots with. A jammer walking your Abundance loop means the payout stops until
// you kill it — so the loop's own route suddenly matters, and "just build the
// synergy once and forget it" stops being free.
//
// The synergy keeps its claim (blocks stay locked, decorations stay put); only
// the EFFECT is revoked, via SynergyEvaluator.SetSuppressed, which ref-counts so
// several jammers on one loop don't un-jam each other on the way out.
[RequireComponent(typeof(EnemySurfaceUnit))]
public class EnemySynergyJammer : MonoBehaviour
{
    [Header("Visual")]
    public Color auraColor = new Color(0.25f, 0.25f, 0.3f);

    EnemySurfaceUnit _self;
    ActiveSynergy    _jammed;
    SynergyAura      _aura;

    void Awake() => _self = GetComponent<EnemySurfaceUnit>();

    void Update()
    {
        if (_self == null || _self.CurrentHealth <= 0) { Release(); return; }

        if (_aura == null)
        {
            float size = GridSystem.instance != null ? GridSystem.instance.cellSize * 0.9f : 0.9f;
            _aura = SynergyBuffFx.AttachAura(transform, auraColor, size);
            _aura?.Persist();
        }

        var ev   = SynergyEvaluator.Instance;
        var cell = _self.CurrentCell;
        var here = (ev != null && cell.HasValue) ? ev.FindActiveAtCell(cell.Value) : null;

        // Stepped onto a different synergy (or off the board): hand the old one back.
        if (!ReferenceEquals(here, _jammed))
        {
            Release();
            if (here != null)
            {
                ev.SetSuppressed(here, true);
                _jammed = here;
            }
        }
    }

    void Release()
    {
        if (_jammed == null) return;
        SynergyEvaluator.Instance?.SetSuppressed(_jammed, false);
        _jammed = null;
    }

    // Covers death, despawn and scene teardown — a jammer must never take a
    // synergy to the grave with it.
    void OnDisable()
    {
        Release();
        if (_aura != null) _aura.Remove();
        _aura = null;
    }
}
