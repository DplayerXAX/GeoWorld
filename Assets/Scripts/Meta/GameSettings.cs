using UnityEngine;

// Central, PlayerPrefs-backed game settings. SettingsScreen edits these; Apply*()
// pushes them to the engine + game systems. Display applies at boot; audio applies
// when AudioManager starts; input applies live (and when the settings screen opens).
public static class GameSettings
{
    // ── Audio (0..1) ──
    public static float MasterVolume = 0.9f;
    public static float MusicVolume  = 0.8f;
    public static float SfxVolume    = 0.9f;

    // ── Display ──
    public static bool Fullscreen = true;
    public static bool VSync      = true;
    public static int  QualityLevel;                 // index into QualitySettings.names
    public static int  FrameCap   = 0;               // 0 = uncapped

    // ── Controls ──
    public static float CameraPanSpeed  = 8f;        // → PlacementController.panSpeed
    public static float LookSensitivity = 120f;      // → OrbitCamera.speed
    // On: held-block ghosts glide between cells (both PlacementController in gameplay
    // and LevelMapController in LevelSelect). Off: both snap instantly, cell to cell.
    public static bool SmoothBlockEditing = true;

    public static readonly int[] FrameCaps = { 0, 30, 60, 120, 144 };

    static bool       _loaded;
    static OrbitCamera _orbit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() { Load(); ApplyDisplay(); }

    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;

        MasterVolume = PlayerPrefs.GetFloat("set.master", MasterVolume);
        MusicVolume  = PlayerPrefs.GetFloat("set.music",  MusicVolume);
        SfxVolume    = PlayerPrefs.GetFloat("set.sfx",    SfxVolume);

        Fullscreen   = PlayerPrefs.GetInt("set.fullscreen", Fullscreen ? 1 : 0) == 1;
        VSync        = PlayerPrefs.GetInt("set.vsync",      VSync ? 1 : 0) == 1;
        QualityLevel = PlayerPrefs.GetInt("set.quality",    QualitySettings.GetQualityLevel());
        FrameCap     = PlayerPrefs.GetInt("set.framecap",   FrameCap);

        CameraPanSpeed  = PlayerPrefs.GetFloat("set.panspeed", CameraPanSpeed);
        LookSensitivity = PlayerPrefs.GetFloat("set.looksens", LookSensitivity);
        SmoothBlockEditing = PlayerPrefs.GetInt("set.smoothedit", SmoothBlockEditing ? 1 : 0) == 1;
    }

    public static void Save()
    {
        PlayerPrefs.SetFloat("set.master", MasterVolume);
        PlayerPrefs.SetFloat("set.music",  MusicVolume);
        PlayerPrefs.SetFloat("set.sfx",    SfxVolume);
        PlayerPrefs.SetInt("set.fullscreen", Fullscreen ? 1 : 0);
        PlayerPrefs.SetInt("set.vsync",      VSync ? 1 : 0);
        PlayerPrefs.SetInt("set.quality",    QualityLevel);
        PlayerPrefs.SetInt("set.framecap",   FrameCap);
        PlayerPrefs.SetFloat("set.panspeed", CameraPanSpeed);
        PlayerPrefs.SetFloat("set.looksens", LookSensitivity);
        PlayerPrefs.SetInt("set.smoothedit", SmoothBlockEditing ? 1 : 0);
        PlayerPrefs.Save();
    }

    public static void ApplyAll() { ApplyDisplay(); ApplyAudio(); ApplyInput(); }

    public static void ApplyDisplay()
    {
        Screen.fullScreen          = Fullscreen;
        QualitySettings.vSyncCount = VSync ? 1 : 0;
        if (QualityLevel >= 0 && QualityLevel < QualitySettings.names.Length)
            QualitySettings.SetQualityLevel(QualityLevel, true);
        // With vSync on, targetFrameRate is ignored; only meaningful when vSync off.
        Application.targetFrameRate = FrameCap <= 0 ? -1 : FrameCap;
    }

    public static void ApplyAudio()
    {
        var am = AudioManager.Instance;
        if (am != null)
        {
            am.SetMasterVolume(MasterVolume);
            am.SetMusicVolume(MusicVolume);
            am.SetSfxVolume(SfxVolume);
            return;
        }
        // No AudioManager in this scene (e.g. LevelSelect, which plays its BGM
        // directly). The volume RTPCs are GLOBAL Wwise RTPCs, so set them straight
        // on the sound engine — the sliders then work everywhere. Names match
        // AudioManager's default masterVolumeRtpc / musicVolumeRtpc / sfxVolumeRtpc.
        AkUnitySoundEngine.SetRTPCValue("MasterVolume", Mathf.Clamp01(MasterVolume) * 100f);
        AkUnitySoundEngine.SetRTPCValue("MusicVolume",  Mathf.Clamp01(MusicVolume)  * 100f);
        AkUnitySoundEngine.SetRTPCValue("SFXVolume",    Mathf.Clamp01(SfxVolume)    * 100f);
    }

    public static void ApplyInput()
    {
        var pc = PlacementController.Instance;
        if (pc != null) pc.panSpeed = CameraPanSpeed;

        if (_orbit == null) _orbit = Object.FindFirstObjectByType<OrbitCamera>();
        if (_orbit != null) _orbit.speed = LookSensitivity;
    }

    public static void ResetDefaults()
    {
        MasterVolume = 0.9f; MusicVolume = 0.8f; SfxVolume = 0.9f;
        Fullscreen = true; VSync = true;
        QualityLevel = QualitySettings.GetQualityLevel(); FrameCap = 0;
        CameraPanSpeed = 8f; LookSensitivity = 120f;
        SmoothBlockEditing = true;
        Save(); ApplyAll();
    }
}
