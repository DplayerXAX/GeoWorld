using UnityEngine;

public static class TurretTypes
{
    public static bool Is(BlockType type) =>
        type == BlockType.Turret
        || type == BlockType.SlowTurret
        || type == BlockType.AoeTurret;

    public static TurretController.Mode Mode(BlockType type) => type switch
    {
        BlockType.SlowTurret => TurretController.Mode.Slow,
        BlockType.AoeTurret  => TurretController.Mode.Aoe,
        _                    => TurretController.Mode.Basic,
    };

    public static string DisplayName(BlockType type) => type switch
    {
        BlockType.SlowTurret => "Slow Turret",
        BlockType.AoeTurret  => "AOE Turret",
        BlockType.Turret     => "Basic Turret",
        _                    => type.ToString(),
    };

    public static Color DisplayColor(BlockType type) => DisplayColor(Mode(type));

    // One colour per turret type, kept far apart in BOTH hue and value so they
    // read at a glance on a busy board. Basic was previously a pale cyan, which
    // sat right next to Slow's blue — the two were near-impossible to tell apart,
    // so Basic is now neutral steel and only Slow owns the blue/ice end.
    //   Basic → steel white  (the plain workhorse)
    //   Slow  → ice blue     (frost)
    //   Aoe   → hot orange   (explosive)
    public static Color DisplayColor(TurretController.Mode mode) => mode switch
    {
        TurretController.Mode.Slow => new Color(0.30f, 0.68f, 1.00f),
        TurretController.Mode.Aoe  => new Color(1.00f, 0.42f, 0.18f),
        _                          => new Color(0.88f, 0.91f, 0.95f),
    };
}
