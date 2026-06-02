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

    public static Color DisplayColor(TurretController.Mode mode) => mode switch
    {
        TurretController.Mode.Slow => new Color(0.35f, 0.55f, 1.00f),
        TurretController.Mode.Aoe  => new Color(1.00f, 0.42f, 0.18f),
        _                          => new Color(0.45f, 0.95f, 1.00f),
    };
}
