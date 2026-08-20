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
    // On (default): the raw mouse-projected cell is used directly, no snap search —
    // the block goes exactly where the cursor says. Off: the cell is pulled to the
    // nearest one actually touching the existing build (see
    // PlacementController.SnapToNearestSupported), with WASDQE/scroll nudging it
    // further from there.
    public static bool FreeMove = true;

    // Cycles the game speed — the same action as clicking the fast-forward chip.
    public static KeyCode FastForwardKey = KeyCode.C;

    // Buys a random affordable shop item and goes straight into placing it. Fixed
    // rather than rebindable for now; if it becomes rebindable, drop it out of
    // ReservedKeys below and give it a row in SettingsScreen next to FastForwardKey.
    public static KeyCode QuickBuyKey = KeyCode.O;

    // Keys the game already owns. A rebind that lands on one of these is refused:
    // the conflict wouldn't announce itself, it would just make two things happen
    // on one press and look like a bug.
    //
    // Listed explicitly rather than scraped from the code — the bindings live in a
    // dozen Update() methods as literals, and a scrape would silently go stale the
    // first time one moved. If you add a permanent binding, add it here too.
    public static readonly KeyCode[] ReservedKeys =
    {
        KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D, KeyCode.Q, KeyCode.E,   // move / raise / lower
        KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3,                     // rotate
        KeyCode.F,                                                          // shop
        KeyCode.R,                                                          // refresh / hold-restart
        KeyCode.Z,                                                          // undo (with Ctrl)
        KeyCode.G,                                                          // grid overlay
        KeyCode.P,                                                          // re-evaluate path
        KeyCode.O,                                                          // quick buy
        KeyCode.Space, KeyCode.Tab, KeyCode.Escape, KeyCode.Delete,
        KeyCode.LeftShift, KeyCode.RightShift,                              // peek
        KeyCode.LeftControl, KeyCode.RightControl,
        KeyCode.Return, KeyCode.KeypadEnter,
    };

    /// <summary>True when `key` is free to bind (not None, not already spoken for).</summary>
    public static bool IsKeyAvailable(KeyCode key) =>
        key != KeyCode.None && System.Array.IndexOf(ReservedKeys, key) < 0;

    public static readonly int[] FrameCaps = { 0, 30, 60, 120, 144 };

    static bool       _loaded;
    static OrbitCamera _orbit;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot() { Load(); ApplyDisplay(); }

    // Volume lives in global Wwise RTPCs, which loading a SoundBank resets to their
    // authored default (gamePlay's AudioManager carries an AkBank) — so re-push a
    // few frames after every scene load rather than once from some Start().
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void HookAudioReapply()
    {
        if (Object.FindFirstObjectByType<AudioSettingsReapplier>() != null) return;
        var go = new GameObject("GameSettingsAudioReapplier");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<AudioSettingsReapplier>();
    }

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
        FreeMove           = PlayerPrefs.GetInt("set.freemove",   FreeMove ? 1 : 0) == 1;

        // Guarded on load too, not just at rebind time: the reserved list can grow
        // after a player has already saved a binding that later became a conflict.
        var ff = (KeyCode)PlayerPrefs.GetInt("set.ffkey", (int)FastForwardKey);
        if (IsKeyAvailable(ff)) FastForwardKey = ff;
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
        PlayerPrefs.SetInt("set.freemove",   FreeMove ? 1 : 0);
        PlayerPrefs.SetInt("set.ffkey",      (int)FastForwardKey);
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
        FreeMove = true;
        FastForwardKey = KeyCode.C;
        Save(); ApplyAll();
    }
}

// Re-pushes the saved volumes into Wwise after every scene load. Auto-spawned +
// DontDestroyOnLoad — no scene wiring.
[DisallowMultipleComponent]
public class AudioSettingsReapplier : MonoBehaviour
{
    void OnEnable()  => UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
    void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;

    void Start() => StartCoroutine(ReapplySoon());

    void HandleSceneLoaded(UnityEngine.SceneManagement.Scene s, UnityEngine.SceneManagement.LoadSceneMode m)
        => StartCoroutine(ReapplySoon());

    // Waits a few frames so this lands after the new scene's AkBank finishes
    // resetting RTPCs to their authored defaults, not before.
    System.Collections.IEnumerator ReapplySoon()
    {
        for (int i = 0; i < 12; i++) yield return null;
        GameSettings.ApplyAudio();
    }
}
