using UnityEngine;

// Drives ManifoldSkybox shader properties in response to music events.
// Auto-grabs RenderSettings.skybox if no material is assigned in Inspector.
public class BackgroundReactor : MonoBehaviour
{
    public static BackgroundReactor Instance;

    [Header("Target")]
    public Material skyboxMaterial;

    [Header("Beat")]
    [Range(0.5f, 8f)] public float beatDecay     = 4f;
    [Range(0f, 1f)]   public float beatStrength  = 0.85f;

    [Header("Color")]
    [Range(0f, 0.1f)]  public float colorShiftPerNote = 0.018f;
    [Range(0f, 0.05f)] public float ambientColorDrift  = 0.008f;
    // How quickly the palette lerps toward the current block type's hue.
    [Range(0f, 2f)]    public float typeHueLerpSpeed   = 0.9f;

    [Header("Pitch Glow")]
    // High notes briefly brighten the zenith.
    [Range(0f, 1f)]  public float pitchGlowStrength = 0.7f;
    [Range(1f, 8f)]  public float pitchGlowDecay    = 3.5f;

    [Header("Tension")]
    // Tension pushes extra hue shift — dissonant leaps look different from steps.
    [Range(0f, 0.15f)] public float tensionColorKick = 0.06f;

    [Header("Intensity")]
    [Range(0f, 1f)] public float idleIntensity = 0.5f;

    // ── Internal state ────────────────────────────────────────────────────────
    float _beatPulse;
    float _colorShift;
    float _targetIntensity;
    float _smoothIntensity;
    float _typeHue;          // current smoothed type-hue (0-1)
    float _targetTypeHue;   // target type-hue for current block
    float _pitchGlow;       // decays each frame

    static readonly int BeatPulseId  = Shader.PropertyToID("_BeatPulse");
    static readonly int IntensityId  = Shader.PropertyToID("_MusicIntensity");
    static readonly int ColorShiftId = Shader.PropertyToID("_ColorShift");
    static readonly int TypeHueId    = Shader.PropertyToID("_TypeHue");
    static readonly int PitchGlowId  = Shader.PropertyToID("_PitchGlow");

    void Awake()
    {
        Instance = this;
        if (skyboxMaterial == null)
            skyboxMaterial = RenderSettings.skybox;

        _targetIntensity = idleIntensity;
        _smoothIntensity = idleIntensity;
        _typeHue         = 0.62f;
        _targetTypeHue   = 0.62f;
    }

    void Update()
    {
        float dt = Time.deltaTime;

        _beatPulse       = Mathf.Lerp(_beatPulse,       0f,              1f - Mathf.Exp(-beatDecay      * dt));
        _pitchGlow       = Mathf.Lerp(_pitchGlow,       0f,              1f - Mathf.Exp(-pitchGlowDecay * dt));
        _smoothIntensity = Mathf.Lerp(_smoothIntensity, _targetIntensity, dt * 2f);

        // Smooth palette shift toward the active block type's characteristic hue
        _typeHue = Mathf.Lerp(_typeHue, _targetTypeHue, dt * typeHueLerpSpeed);

        // Continuous ambient hue drift
        _colorShift = (_colorShift + ambientColorDrift * dt) % 1f;

        if (skyboxMaterial == null) return;
        skyboxMaterial.SetFloat(BeatPulseId,  _beatPulse);
        skyboxMaterial.SetFloat(IntensityId,  _smoothIntensity);
        skyboxMaterial.SetFloat(ColorShiftId, _colorShift);
        skyboxMaterial.SetFloat(TypeHueId,    _typeHue);
        skyboxMaterial.SetFloat(PitchGlowId,  _pitchGlow);
    }

    // ── Simple call (scan notes, bass roots, backward-compat) ────────────────
    public void OnNote(float strength = 1f)
    {
        _beatPulse  = Mathf.Min(1f, _beatPulse + strength * beatStrength);
        _colorShift = (_colorShift + colorShiftPerNote * strength) % 1f;
    }

    // ── Rich call (melody notes) — differentiates pitch, block type, tension ─
    // degree  1-7, octave -1..2 → normalized pitch 0-1 (low to high)
    // velocity  0-1, tension 0-1 (from ArpeggiatorManager.NoteEvent)
    // blockType → characteristic hue target for the current chord
    public void OnNoteDetailed(int degree, int octave, float velocity, float tension, BlockType blockType)
    {
        // 28 possible scalar values (deg 1-7, oct -1..2) → 0-1
        float pitch = Mathf.Clamp01(((degree - 1) + (octave + 1) * 7) / 27f);

        // Beat pulse — stronger notes hit harder
        _beatPulse = Mathf.Min(1f, _beatPulse + velocity * beatStrength);

        // Color shift — base bump + extra from tension (tense leaps = bigger shift)
        _colorShift = (_colorShift + colorShiftPerNote + tension * tensionColorKick) % 1f;

        // Pitch glow — high notes briefly brighten the zenith
        _pitchGlow = Mathf.Min(1f, _pitchGlow + pitch * pitchGlowStrength * velocity);

        // Lock palette toward the block type's characteristic hue
        _targetTypeHue = BlockTypeHue(blockType);
    }

    // Called when the chord pad switches (SurfaceUnit block-type change).
    // Immediately snaps the hue target so the color clearly tracks harmony.
    public void OnChordChange(BlockType blockType)
    {
        _targetTypeHue = BlockTypeHue(blockType);
    }

    public void SetMusicIntensity(float intensity)
    {
        _targetIntensity = Mathf.Clamp01(intensity);
    }

    public void ShiftColor(float delta)
    {
        _colorShift = (_colorShift + delta) % 1f;
    }

    // ── Hue targets per block — spread across the full hue wheel ─────────────
    // Home  Cm  → cool blue       (~220°)
    // Lift  F   → warm amber/gold (~45°)
    // Pull  Gm  → teal-cyan       (~165°)
    // Shadow Bb → deep violet     (~280°)
    static float BlockTypeHue(BlockType t) => t switch
    {
        BlockType.Home   => 0.61f,
        BlockType.Lift   => 0.12f,
        BlockType.Pull   => 0.46f,
        BlockType.Shadow => 0.78f,
        _                => 0.5f,
    };
}
