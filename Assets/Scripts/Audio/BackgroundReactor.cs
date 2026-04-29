using UnityEngine;

// Drives ManifoldSkybox shader properties in response to music events.
// Assign the skybox material in the Inspector, then call OnBeat / OnNote
// from ArpeggiatorManager and AudioManager.
public class BackgroundReactor : MonoBehaviour
{
    public static BackgroundReactor Instance;

    [Header("Target")]
    public Material skyboxMaterial;

    [Header("Beat")]
    [Range(0.5f, 8f)] public float beatDecay = 4f;

    [Header("Color Evolution")]
    [Range(0f, 0.05f)] public float colorShiftPerNote = 0.008f;

    private float _beatPulse;
    private float _colorShift;
    private float _targetIntensity;
    private float _smoothIntensity = 0.5f;

    private static readonly int BeatPulseId    = Shader.PropertyToID("_BeatPulse");
    private static readonly int IntensityId    = Shader.PropertyToID("_MusicIntensity");
    private static readonly int ColorShiftId   = Shader.PropertyToID("_ColorShift");

    void Awake()
    {
        Instance = this;
        // Auto-grab the active skybox if none assigned in Inspector
        if (skyboxMaterial == null)
            skyboxMaterial = RenderSettings.skybox;
    }

    void Update()
    {
        _beatPulse       = Mathf.Lerp(_beatPulse, 0f, 1f - Mathf.Exp(-beatDecay * Time.deltaTime));
        _smoothIntensity = Mathf.Lerp(_smoothIntensity, _targetIntensity, Time.deltaTime * 2f);

        if (skyboxMaterial == null) return;
        skyboxMaterial.SetFloat(BeatPulseId,  _beatPulse);
        skyboxMaterial.SetFloat(IntensityId,  _smoothIntensity);
        skyboxMaterial.SetFloat(ColorShiftId, _colorShift);
    }

    // Called by ArpeggiatorManager on every note played.
    public void OnNote(float strength = 1f)
    {
        _beatPulse = Mathf.Min(1f, _beatPulse + strength * 0.6f);
        _colorShift = (_colorShift + colorShiftPerNote) % 1f;
    }

    // Called when music intensity changes (e.g. from AudioManager.SetIntensity).
    public void SetMusicIntensity(float intensity)
    {
        _targetIntensity = Mathf.Clamp01(intensity);
    }

    // Allows external code to nudge color shift by a fixed amount.
    public void ShiftColor(float delta)
    {
        _colorShift = (_colorShift + delta) % 1f;
    }
}
