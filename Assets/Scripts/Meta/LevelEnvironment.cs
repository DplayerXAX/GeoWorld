using UnityEngine;

// What a level's world is like — weather, sky, fog, light, the scenery around it,
// and how its blocks age. One per level (LevelDefinition.environment); a chapter can
// share one and have levels point at variants. Applied at level start by
// LevelEnvironmentDriver; the blocks read it through GeoWorld/BlockWeather.
//
// Blocks age by DATA (PlacedBlockInstance.age): the level's own prebuilt furniture
// starts old, and what the player places now is new. This asset says what that age LOOKS like
// here — moss on a wet level, rust under rain, sand-scour in a desert.
//
// Everything past the block look is OFF by default (no sky override, no fog, no
// scenery, no motes), so a level without an environment looks as it always did.
[CreateAssetMenu(menuName = "GeoWorld/Level Environment", fileName = "Env_")]
public class LevelEnvironment : ScriptableObject
{
    // Appended only — assets store these as numbers.
    public enum Motes    { None, Dust, Pollen, Snow, Embers, Spores }
    public enum Backdrop { None, Cliffs, Ruins, FloatingIslands, BarrenFields }

    [Header("Block age")]
    [Tooltip("Age given to the level's prebuilt (startingLayout) blocks — they were here before the player.")]
    [Min(0)] public int prebuiltAge = 4;
    [Tooltip("Age at which a block looks as worn as it ever gets.")]
    [Min(1)] public int maxAge = 4;
    [Tooltip("Colour age gathers in: the patina on top faces and the stain round corrosion pits (moss green, rust, sand…).")]
    public Color wearTint = new(0.46f, 0.55f, 0.36f, 1f);
    [Tooltip("Overall strength of the age effects.")]
    [Range(0f, 1.5f)] public float wearStrength = 1f;
    [Range(0f, 1.5f)] public float crackStrength = 1f;

    [Header("Contact shadow")]
    [Tooltip("How dark a block face gets where another block stands against it.")]
    [Range(0f, 1f)] public float contactShadow = 0.38f;
    [Tooltip("How far the shade reaches across the face, as a fraction of a cell.")]
    [Range(0.05f, 0.6f)] public float contactRadius = 0.32f;

    [Header("Rain")]
    public bool rain = false;
    [Range(0f, 2f)] public float rainIntensity = 1f;
    public Color rainColor = new(0.78f, 0.84f, 0.92f, 0.35f);
    [Tooltip("Sideways drift of the rain (x, z), world units per second.")]
    public Vector2 rainWind = new(1.2f, 0.4f);
    [Tooltip("Drops bursting on the tops of blocks.")]
    public bool splashes = true;

    [Header("Wet surfaces")]
    [Tooltip("How wet the blocks get: darker, glossier, reflecting the sky.")]
    [Range(0f, 1f)] public float wetness = 0f;
    [Tooltip("How wet they already are when the level opens, as a fraction of the above.")]
    [Range(0f, 1f)] public float wetAtStart = 0.3f;
    [Tooltip("Seconds to soak through from there.")]
    [Min(0f)] public float wetUpSeconds = 30f;
    [Tooltip("How much standing water gathers once soaked: cracks fill first, then puddles on block tops open to the sky, rippling under the drops.")]
    [Range(0f, 1f)] public float puddles = 0f;
    [Tooltip("Seconds, after soaking through, for the water to rise to its full level.")]
    [Min(0f)] public float floodSeconds = 40f;
    [Tooltip("Dark streaks running down block sides.")]
    [Range(0f, 1f)] public float streaks = 0f;
    [Tooltip("Pitting eaten into the surface, stained in the wear tint.")]
    [Range(0f, 1f)] public float corrosion = 0f;

    [Header("Sky")]
    [Tooltip("Repaint the sky (Custom/ManifoldSkybox) in the colours below.")]
    public bool overrideSky = false;
    public Color skyZenith  = new(0.6f, 0.6f, 0.6f, 1f);
    public Color skyHorizon = new(0.23f, 0.58f, 0.62f, 1f);
    [Tooltip("The sky's own horizon haze band.")]
    public Color skyHaze    = new(1f, 0.98f, 0.83f, 1f);
    [Tooltip("Sky haze density × this.")]
    [Range(0f, 3f)] public float skyHazeDensity = 1f;
    [Tooltip("The sky's grid lines and floating cubes.")]
    public Color skyGrid = new(0.25f, 0.35f, 0.65f, 1f);
    [Tooltip("How far the sky's stained-glass haze turns hue. 1 = full rainbow spread; low keeps the sky in the colours above.")]
    [Range(0f, 1f)] public float skyHueRange = 1f;

    [Header("Fog — the same height fog and far haze as the level map")]
    [Tooltip("A sea of fog under the board: the bottoms of the lowest blocks sink into it; the board above stays clear.")]
    public bool heightFogEnabled = false;
    public HeightFog.Settings heightFog = new()
    {
        color = new Color(0.9f, 0.89f, 0.87f, 1f), density = 0.35f, topOffset = 0.2f, falloff = 0.8f,
        wave = 0.6f, maxDistance = 60f, skyBlend = 0.7f, scatter = 0.8f, anisotropy = 0.5f,
        mapClear = 1f, clearFrom = 0f, clearTo = 0.6f,
    };
    [Tooltip("Volumetric haze thickening with distance from the board, swallowing the scenery.")]
    public bool farHazeEnabled = false;
    public MistBank.Settings farHaze = new()
    {
        color = new Color(0.93f, 0.9f, 0.86f, 1f), strength = 0.95f, density = 0.45f, height = 7f, depth = 10f,
        offset = -3f, falloff = 0.7f, skyBlend = 0.85f, scatter = 1.6f, anisotropy = 0.5f, steps = 28,
        edge = 24f, clearance = 2.5f, edgeWarp = 3f, noiseScale = 0.1f,
    };
    [Tooltip("Cells of clear air kept round the board (and its endpoints) before the haze begins.")]
    [Min(0f)] public float farHazeClear = 8f;
    [Tooltip("How far past that the haze volume reaches, in cells.")]
    [Min(1f)] public float farHazeMargin = 60f;
    public MistBank.Sink farHazeSink = new() { start = 14f, rate = 0.35f, max = 12f, soft = 8f };

    [Header("Light")]
    [Tooltip("Main light intensity × this.")]
    [Range(0f, 2f)] public float sunIntensity = 1f;
    [Tooltip("Main light colour × this.")]
    public Color sunTint = Color.white;
    [Tooltip("Set where the sun is (time of day): low and warm for dawn/dusk, high and dim for an overcast day.")]
    public bool overrideSunDirection = false;
    [Tooltip("Height of the sun above the horizon, degrees.")]
    [Range(2f, 90f)] public float sunPitch = 50f;
    [Tooltip("Compass direction the light comes FROM, degrees.")]
    [Range(0f, 360f)] public float sunYaw = 30f;

    [Header("Drifting motes")]
    public Motes motes = Motes.None;
    [Range(0f, 3f)] public float moteDensity = 1f;
    [Tooltip("Motes' preset colour × this.")]
    public Color moteTint = Color.white;

    [Header("Scenery (outside the build area, never in the way)")]
    public Backdrop backdrop = Backdrop.None;
    public Color backdropColor = new(0.34f, 0.37f, 0.40f, 1f);
    [Tooltip("What the scenery fades into with distance — usually the fog colour.")]
    public Color backdropHaze  = new(0.72f, 0.76f, 0.80f, 1f);
    [Tooltip("How far into the haze the furthest scenery goes (1 = gone).")]
    [Range(0f, 1f)] public float backdropHazeMax = 0.8f;
    [Range(0, 60)] public int backdropCount = 18;
    [Tooltip("Ring the scenery stands in, in cells from the board's centre.")]
    public Vector2 backdropDistance = new(45f, 110f);
    [Tooltip("Barren fields: how far the ridges' summits rise above the board's floor, in cells — (inner ring, outer ring). 0 = no ridges.")]
    public Vector2 backdropPeakRise = new(4f, 15f);
    [Tooltip("Barren fields: height of each shelf's top relative to the board's floor, in cells (lowest, highest).")]
    public Vector2 backdropShelfHeight = new(-9f, -2f);
    [Tooltip("Barren fields: dress the land as farmland — field strips divided by dirt tracks, red barns, hay bales.")]
    public bool backdropFarm = false;
    [Tooltip("What grows over the land when it blooms (see backdropBloomOn) — grass, or a crop. Alpha 0 = green from backdropLeafColor.")]
    public Color backdropGrowth  = new(0f, 0f, 0f, 0f);
    [Tooltip("When the land blooms, grow that colour over the ground itself (the grass / crop skin). Off = the ground keeps its colour; only the plants come up.")]
    public bool  backdropGrowSkin = true;
    public Color backdropGrowth2 = new(0f, 0f, 0f, 0f);
    public int backdropSeed = 1;
    [Tooltip("The haze glows this colour where you look toward the sun — a low sun smears its light across the air behind the scenery. Black = off.")]
    [ColorUsage(false, true)] public Color backdropSunGlow = Color.black;
    [Tooltip("Edges of the scenery turned toward the sun catch this light. Black = off.")]
    [ColorUsage(false, true)] public Color backdropRim = Color.black;
    [Tooltip("Luminous accents on the scenery's up-facing surfaces — glowing moss, lichen, veins. Black / 0 = off.")]
    [ColorUsage(false, true)] public Color backdropAccent = Color.black;
    [Range(0f, 1f)] public float backdropAccentAmount = 0f;
    [Tooltip("Barren fields only: the first time this synergy is formed in the level, the dry tufts flower and the dead trees leaf out. None = never.")]
    public BlockColor backdropBloomOn = BlockColor.None;
    [Tooltip("Leaf colour for that bloom (the flowers take the synergy's own colours).")]
    public Color backdropLeafColor = new(0.42f, 0.66f, 0.30f, 1f);

    [Header("Sun glow (a soft bloom on the horizon where the light comes from)")]
    public bool sunGlow = false;
    [ColorUsage(false, true)] public Color sunGlowColor = new(1f, 0.55f, 0.3f, 1f);
    [Tooltip("Diameter, in cells.")]
    public float sunGlowSize = 120f;
    [Tooltip("How far out it hangs, in cells — keep it past the scenery so the scenery stands against it.")]
    public float sunGlowDistance = 170f;

    [Header("Destroyed blocks")]
    [Tooltip("Glow along the edge of a block dissolving away (BlockDissolveFx).")]
    public Color dissolveEdge = new(1f, 0.85f, 0.55f, 1f);

    [Header("Audio")]
    [Tooltip("Ambience loop for this environment (rain, wind…). Optional.")]
    public AK.Wwise.Event ambience;

    static LevelEnvironment _default;
    /// <summary>A plain, dry world — for levels (and endless) that don't set one.</summary>
    public static LevelEnvironment Default =>
        _default != null ? _default : (_default = CreateInstance<LevelEnvironment>());

    public float AgeToWear(int age) => Mathf.Clamp01(age / (float)Mathf.Max(1, maxAge));
}
