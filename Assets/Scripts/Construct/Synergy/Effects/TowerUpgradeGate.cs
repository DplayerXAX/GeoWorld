using System;

// Gate for the turret-upgrade feature (启发 / Enlightenment unlock).
//
// The actual "upgrade a turret" UI/flow is NOT implemented yet — this is the
// hook it will read. Enlightenment's 2×2×2 cube flips this on via
// UnlockTowerUpgradeEffect; whatever builds the upgrade UI later should check
// TowerUpgradeGate.Unlocked and/or subscribe to OnChanged to show/enable it.
//
// Ref-counted so multiple unlock sources (or a tier-up that revokes+re-applies)
// compose without prematurely re-locking.
public static class TowerUpgradeGate
{
    static int _refs;

    public static bool Unlocked => _refs > 0;

    // (unlocked) — fires when the gate transitions locked↔unlocked.
    public static event Action<bool> OnChanged;

    public static void Acquire()
    {
        bool was = Unlocked;
        _refs++;
        if (!was && Unlocked) OnChanged?.Invoke(true);
    }

    public static void Release()
    {
        if (_refs <= 0) return;
        bool was = Unlocked;
        _refs--;
        if (was && !Unlocked) OnChanged?.Invoke(false);
    }

    // Hard reset (e.g. new run). Safe to call any time.
    public static void ResetAll()
    {
        bool was = Unlocked;
        _refs = 0;
        if (was) OnChanged?.Invoke(false);
    }
}
