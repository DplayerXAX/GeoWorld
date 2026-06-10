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
    float _damageTint;
    static readonly int DamageTintId =
    Shader.PropertyToID("_DamageTint");

    // ── Theme flash (synergy activation) — mirrors the damage flash but tinted
    //    to a caller-chosen colour instead of red.
    Color _flashColor = Color.white;
    float _flashAmount;
    static readonly int FlashColorId  = Shader.PropertyToID("_FlashColor");
    static readonly int FlashAmountId = Shader.PropertyToID("_FlashAmount");

    [Header("Theme Flash")]
    [Tooltip("How fast the synergy-activation skybox flash fades out.")]
    [Range(0.5f, 8f)] public float themeFlashDecay = 3f;

    [Header("Combat Mode")]
    [Tooltip("Press to toggle combat skybox for testing (auto-driven by GameFlowManager in play).")]
    public KeyCode combatTestKey        = KeyCode.G;
    [Tooltip("How fast the skybox transitions between calm and combat state.")]
    public float   combatTransitionSpeed = 2.0f;

    // ── Internal state ────────────────────────────────────────────────────────
    float _beatPulse;
    float _colorShift;
    float _targetIntensity;
    float _smoothIntensity;
    float _typeHue;
    float _targetTypeHue;
    float _pitchGlow;
    float _combatMode;        // current smoothed value (0=calm, 1=intense)
    float _targetCombatMode;  // 0 or 1

    static readonly int BeatPulseId  = Shader.PropertyToID("_BeatPulse");
    static readonly int IntensityId  = Shader.PropertyToID("_MusicIntensity");
    static readonly int ColorShiftId = Shader.PropertyToID("_ColorShift");
    static readonly int TypeHueId    = Shader.PropertyToID("_TypeHue");
    static readonly int PitchGlowId  = Shader.PropertyToID("_PitchGlow");
    static readonly int CombatModeId = Shader.PropertyToID("_CombatMode");

    void Awake()
    {
        Instance = this;

        // Clone the skybox asset so runtime SetFloat() writes go to a
        // throw-away instance and don't mark the project file dirty in git.
        var source = skyboxMaterial != null ? skyboxMaterial : RenderSettings.skybox;
        if (source != null)
        {
            skyboxMaterial = new Material(source) { name = source.name + " (runtime)" };
            RenderSettings.skybox = skyboxMaterial;
        }

        _targetIntensity = idleIntensity;
        _smoothIntensity = idleIntensity;
        _typeHue         = 0.62f;
        _targetTypeHue   = 0.62f;
    }

    void OnDestroy()
    {
        // Restore RenderSettings to the asset (if we replaced it) and free
        // the runtime clone. Avoids growing leaked materials across reloads.
        if (skyboxMaterial != null && skyboxMaterial.name.EndsWith(" (runtime)"))
            Destroy(skyboxMaterial);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        
        // ── Combat mode: test key toggle ─────────────────────────────────────
        // G toggles freely at any time.
        // For auto-drive, call SetCombatMode() from GameFlowManager on phase change.
        if (Input.GetKeyDown(combatTestKey))
            _targetCombatMode = _targetCombatMode > 0.5f ? 0f : 1f;

        _combatMode = Mathf.MoveTowards(_combatMode, _targetCombatMode,
                                        combatTransitionSpeed * dt);

        // In combat, music intensity rides higher for extra visual energy
        float targetInt = _targetCombatMode > 0.5f
                          ? Mathf.Max(_targetIntensity, 0.85f)
                          : _targetIntensity;

        _beatPulse       = Mathf.Lerp(_beatPulse,       0f,       1f - Mathf.Exp(-beatDecay      * dt));
        _pitchGlow       = Mathf.Lerp(_pitchGlow,       0f,       1f - Mathf.Exp(-pitchGlowDecay * dt));
        _smoothIntensity = Mathf.Lerp(_smoothIntensity, targetInt, dt * 2f);
     
        _typeHue    = Mathf.Lerp(_typeHue, _targetTypeHue, dt * typeHueLerpSpeed);
        _colorShift = (_colorShift + ambientColorDrift * dt) % 1f;
        _damageTint = Mathf.Lerp(
    _damageTint,
    0f,
    1f - Mathf.Exp(-4f * dt)
);
        _flashAmount = Mathf.Lerp(_flashAmount, 0f, 1f - Mathf.Exp(-themeFlashDecay * dt));
        if (skyboxMaterial == null) return;
        skyboxMaterial.SetFloat(BeatPulseId,  _beatPulse);
        skyboxMaterial.SetFloat(IntensityId,  _smoothIntensity); 
        skyboxMaterial.SetFloat(
    DamageTintId,
    _damageTint
);
        skyboxMaterial.SetColor(FlashColorId,  _flashColor);
        skyboxMaterial.SetFloat(FlashAmountId, _flashAmount);
        skyboxMaterial.SetFloat(ColorShiftId, _colorShift);
        skyboxMaterial.SetFloat(TypeHueId,    _typeHue);
        skyboxMaterial.SetFloat(PitchGlowId,  _pitchGlow);
        skyboxMaterial.SetFloat(CombatModeId, _combatMode);
    }

    /// <summary>
    /// Programmatically set combat mode (called by GameFlowManager or other systems).
    /// The auto-detection in Update() will also keep this in sync.
    /// </summary>
    public void SetCombatMode(bool active) =>
        _targetCombatMode = active ? 1f : 0f;

    // ── Simple call (scan notes, bass roots, backward-compat) ────────────────

    public void TriggerDamageFlash()
    {
        _damageTint = 1f;
    }

    // Flash the skybox toward an arbitrary theme colour, then fade out (mirrors
    // the damage flash, but the caller picks the colour). Called on synergy
    // activation so the background briefly washes toward the activated theme.
    public void TriggerThemeFlash(Color color, float strength = 0.6f)
    {
        _flashColor  = color;
        _flashAmount = Mathf.Max(_flashAmount, Mathf.Clamp01(strength));
    }
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
