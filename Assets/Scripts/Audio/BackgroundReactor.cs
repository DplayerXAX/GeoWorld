using UnityEngine;

// Drives ManifoldSkybox shader properties in response to music events.
// Auto-grabs RenderSettings.skybox if no material is assigned in Inspector.
public class BackgroundReactor : MonoBehaviour
{
    public static BackgroundReactor Instance;

    [Header("Target")]
    public Material skyboxMaterial;

    [Header("Beat")]
    [Range(0.5f, 8f)] public float beatDecay = 4f;
    [Range(0f, 1f)]   public float beatStrength = 0.85f;

    [Header("Color Evolution")]
    // Bigger jumps per note so a 4-note phrase visibly shifts the palette.
    [Range(0f, 0.1f)] public float colorShiftPerNote = 0.025f;
    // Always-on slow drift so the sky keeps evolving even between notes.
    [Range(0f, 0.05f)] public float ambientColorDrift = 0.01f;

    [Header("Intensity")]
    [Range(0f, 1f)] public float idleIntensity = 0.5f;

    private float _beatPulse;
    private float _colorShift;
    private float _targetIntensity = 0.5f;
    private float _smoothIntensity = 0.5f;

    private static readonly int BeatPulseId  = Shader.PropertyToID("_BeatPulse");
    private static readonly int IntensityId  = Shader.PropertyToID("_MusicIntensity");
    private static readonly int ColorShiftId = Shader.PropertyToID("_ColorShift");

    void Awake()
    {
        Instance = this;
        if (skyboxMaterial == null)
            skyboxMaterial = RenderSettings.skybox;

        _targetIntensity = idleIntensity;
        _smoothIntensity = idleIntensity;
    }

    void Update()
    {
        _beatPulse       = Mathf.Lerp(_beatPulse, 0f, 1f - Mathf.Exp(-beatDecay * Time.deltaTime));
        _smoothIntensity = Mathf.Lerp(_smoothIntensity, _targetIntensity, Time.deltaTime * 2f);

        // Continuous slow hue drift so the player always sees the sky breathing,
        // even if they haven't triggered any music yet.
        _colorShift = (_colorShift + ambientColorDrift * Time.deltaTime) % 1f;

        if (skyboxMaterial == null) return;
        skyboxMaterial.SetFloat(BeatPulseId,  _beatPulse);
        skyboxMaterial.SetFloat(IntensityId,  _smoothIntensity);
        skyboxMaterial.SetFloat(ColorShiftId, _colorShift);
    }

    // Called per arp note. Pulses grids and bumps color shift.
    public void OnNote(float strength = 1f)
    {
        _beatPulse  = Mathf.Min(1f, _beatPulse + strength * beatStrength);
        _colorShift = (_colorShift + colorShiftPerNote) % 1f;
    }

    public void SetMusicIntensity(float intensity)
    {
        _targetIntensity = Mathf.Clamp01(intensity);
    }

    public void ShiftColor(float delta)
    {
        _colorShift = (_colorShift + delta) % 1f;
    }
}
