using UnityEngine;

// Base for a device attached to a placed block. PlacementController adds the right
// subclass when a block whose BlockData carries a DeviceData is placed (see
// AttachDevice), and destroying the block's visual takes the device with it —
// which is why teardown lives in OnDestroy rather than in a removal path that
// every caller would have to remember.
public abstract class PlacedDevice : MonoBehaviour
{
    public DeviceData Data { get; private set; }
    public Vector3Int Cell { get; private set; }

    public void Init(DeviceData data, Vector3Int cell)
    {
        Data = data;
        Cell = cell;
        OnInit();
    }

    protected virtual void OnInit() { }

    protected virtual void OnDestroy()
    {
        DeviceRegistry.ReleaseAll(this);
    }

    // Factory — keeps the kind→component mapping in exactly one place.
    public static PlacedDevice Attach(GameObject host, DeviceData data, Vector3Int cell)
    {
        if (host == null || data == null) return null;

        PlacedDevice d = data.kind switch
        {
            DeviceKind.Oscillator => host.AddComponent<OscillatorDevice>(),
            DeviceKind.Portal     => host.AddComponent<PortalDevice>(),
            DeviceKind.Trap       => host.AddComponent<TrapDevice>(),
            _                     => null,
        };
        d?.Init(data, cell);
        return d;
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Oscillator — rides its block up and down.
// ═════════════════════════════════════════════════════════════════════════════
//
// Moves the VISUAL only. The block's grid cells stay where they were placed, so
// pathfinding, synergies and occupancy all keep seeing a stationary block — the
// alternative (re-registering cells every frame) would rebuild the surface graph
// continuously and make the enemy route thrash.
//
// What makes it matter to the player is the RESERVATION: the corridor it sweeps
// is unbuildable, so an oscillator costs you column space in exchange for whatever
// its motion is worth.
public class OscillatorDevice : PlacedDevice
{
    Vector3 _restPos;
    float   _t;

    protected override void OnInit()
    {
        _restPos = transform.position;

        // Reserve the cells above the placed one. Not the placed cell itself — the
        // block IS there, and marking it reserved would make the device unsellable
        // by its own rule.
        int n = Mathf.Max(1, Data.travelCells);
        var cells = new Vector3Int[n];
        for (int i = 0; i < n; i++) cells[i] = Cell + new Vector3Int(0, i + 1, 0);
        DeviceRegistry.Reserve(this, cells);
    }

    void Update()
    {
        float cs   = GridSystem.instance != null ? GridSystem.instance.cellSize : 1f;
        float span = Data.travelCells * cs;

        // Cycle = up leg + dwell + down leg + dwell, so the dwell is real time at
        // each end rather than a slow-down that never quite stops.
        float legs  = Mathf.Max(0.1f, Data.cycleSeconds);
        float dwell = Mathf.Max(0f, Data.dwellSeconds);
        float total = legs + dwell * 2f;

        _t = (_t + Time.deltaTime) % total;

        float k;
        if (_t < legs * 0.5f)                        k = _t / (legs * 0.5f);                       // rising
        else if (_t < legs * 0.5f + dwell)           k = 1f;                                        // held high
        else if (_t < legs + dwell)                  k = 1f - (_t - legs * 0.5f - dwell) / (legs * 0.5f);
        else                                         k = 0f;                                        // held low

        // Smoothstep so the ends ease rather than reversing with a jerk.
        k = k * k * (3f - 2f * k);
        transform.position = _restPos + Vector3.up * (span * k);
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Portal — half of a pair enemies will route through.
// ═════════════════════════════════════════════════════════════════════════════
public class PortalDevice : PlacedDevice
{
    public PortalDevice Partner { get; private set; }

    protected override void OnInit()
    {
        DeviceRegistry.RegisterPortal(this);

        // Pairs form on a first-come basis: the earliest unpaired portal with the
        // same key claims this one. A third portal on that key simply stays
        // unlinked and inert rather than silently re-pairing and breaking a route
        // the player had already built around.
        var lonely = DeviceRegistry.FindLonelyPortal(Data.pairKey);
        if (lonely != null && lonely != this)
        {
            Partner = lonely;
            lonely.Partner = this;
        }
    }

    protected override void OnDestroy()
    {
        if (Partner != null && Partner.Partner == this) Partner.Partner = null;
        Partner = null;
        DeviceRegistry.UnregisterPortal(this);
        base.OnDestroy();
    }

    /// <summary>Linked and therefore usable by pathfinding.</summary>
    public bool Linked => Partner != null;
}

// ═════════════════════════════════════════════════════════════════════════════
// Trap — spends itself on the first ordinary enemy to step on it.
// ═════════════════════════════════════════════════════════════════════════════
public class TrapDevice : PlacedDevice
{
    int _charges;

    protected override void OnInit()
    {
        _charges = Mathf.Max(1, Data.charges);
        // Registered on its OWN cell: an enemy walking across the top of this block
        // reports that block's cell (FaceNode.cell is the block, not the air above
        // it), so that's the key the step hook will look up.
        DeviceRegistry.RegisterTrap(Cell, this);
    }

    protected override void OnDestroy()
    {
        DeviceRegistry.UnregisterTrap(Cell, this);
        base.OnDestroy();
    }

    /// <summary>
    /// Returns true when the trap fired on this enemy. Called from the enemy's
    /// per-cell step hook (see EnemySurfaceUnit.StepToNode).
    /// </summary>
    public bool TryTrigger(EnemySurfaceUnit enemy)
    {
        if (_charges <= 0 || enemy == null || enemy.CurrentHealth <= 0) return false;
        if (enemy.maxHealth > Data.maxTargetHealth) return false;   // not "ordinary"

        _charges--;
        // Full max health, not a fixed number: this is a kill, and a trap that
        // merely dents a tougher-than-expected enemy reads as broken.
        enemy.TakeDamage(enemy.maxHealth);

        if (_charges <= 0)
        {
            DeviceRegistry.UnregisterTrap(Cell, this);
            Spend();
        }
        return true;
    }

    // Spent traps stay on the board as terrain — removing the block would tear a
    // hole in the path the player built, which is a far bigger consequence than
    // the trap was worth. It just greys out.
    void Spend()
    {
        foreach (var r in GetComponentsInChildren<Renderer>())
            MpbColor.Set(r, new Color(0.35f, 0.35f, 0.38f));
    }
}
