using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Wwise GameObject")]
    public GameObject audioEmitter;
    public AK.Wwise.Event BGM;
    public AK.Wwise.Event BGM_fight;

    [Header("Battle music — Wwise State path (preferred)")]
    [Tooltip("Optional. If your Wwise project drives the music via a State Group (e.g. Music_Mode → Calm/Battle), drag the BATTLE state value here. When set, Enter/ExitBattleBGM uses SetValue() — the container handles transitions per the Wwise authoring (beat-aligned, crossfade, etc.) and skips Post/Stop.")]
    public AK.Wwise.State BGM_StateBattle;
    public AK.Wwise.State BGM_StateCalm;

    [Header("Battle music — Event-swap fallback")]
    [Tooltip("Fade-out duration (ms) when stopping the previous BGM event in event-swap mode.")]
    [Min(0)] public int   bgmFadeOutMs = 500;

    [Header("Temp")]
    public GameObject[] pianoKey;

    [Header("Sound effect")]
    public AK.Wwise.Event Note;
    public AK.Wwise.Event scroll;
    public AK.Wwise.Event rotate;
    public AK.Wwise.Event fight_start;
    public AK.Wwise.Event fight_end;

    [Header("Volume RTPCs (Wwise global, 0..100)")]
    [Tooltip("Global Wwise RTPC names bound to your bus volumes. SettingsScreen drives these 0..1 → 0..100. Set them up on the Master / Music / SFX buses in Wwise.")]
    public string masterVolumeRtpc = "MasterVolume";
    public string musicVolumeRtpc  = "MusicVolume";
    public string sfxVolumeRtpc    = "SFXVolume";

    [Header("Chord pad (Wwise Switch driving the BGM pad layer)")]
    // The Switch Group name in your Wwise project. Switch values must
    // match BlockType enum names (Home / Lift / Pull / Shadow).
    public string chordSwitchGroup = "BlockChord";

    // Tracked so we can stop the right playing instance when swapping BGMs
    // (event-swap path). 0 = nothing playing.
    uint _currentBgmPlayingId;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        // Stop our BGM when this AudioManager goes away (e.g. scene reload on Restart),
        // so the reloaded scene's AudioManager doesn't stack a second BGM on top.
        if (_currentBgmPlayingId != 0)
        {
            AkUnitySoundEngine.StopPlayingID(
                _currentBgmPlayingId, bgmFadeOutMs,
                AkCurveInterpolation.AkCurveInterpolation_Linear);
            _currentBgmPlayingId = 0;
        }
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        SetChordOnObject(BlockType.Home, this.gameObject);
        SetChordOnObject(BlockType.Home, audioEmitter);

        // Force the initial music state to Calm BEFORE posting the BGM event.
        // Wwise doesn't always honor a "default state" config in the authoring
        // tool reliably, so we set it explicitly here.
        if (BGM_StateCalm != null && BGM_StateCalm.IsValid())
            BGM_StateCalm.SetValue();

        if (BGM != null && BGM.IsValid())
            _currentBgmPlayingId = BGM.Post(this.gameObject);

        GameSettings.ApplyAudio();   // push saved volumes once the engine is up
    }

    void SetChordOnObject(BlockType type, GameObject target)
    {
        if (target == null) return;
        AkUnitySoundEngine.SetSwitch(chordSwitchGroup, type.ToString(), target);
    }

    // ===== BATTLE MUSIC TRANSITIONS =====
    //
    // Two paths, picked automatically:
    //
    // 1. STATE PATH (preferred) — drag BGM_StateBattle / BGM_StateCalm in
    //    the inspector. Wwise's Music Switch Container reacts to the state
    //    change with whatever transition you authored (beat-aligned, fade,
    //    silent gap, etc.). Nothing posted from C#; clean and smooth.
    //
    // 2. EVENT-SWAP FALLBACK — when state values aren't set, the previous
    //    BGM event is stopped with a `bgmFadeOutMs` fade and the new event
    //    is posted. Simpler to set up but transitions are abrupt.
    //
    // Call EnterBattleBGM() when combat starts and ExitBattleBGM() when it
    // ends. Hooked from GameFlowManager.Run / EndRunningPhase / HandleGameOver.

    public void EnterBattleBGM()
    {
        if(fight_start!=null)
            fight_start.Post(this.gameObject);
        if (BGM_StateBattle != null && BGM_StateBattle.IsValid())
        {
            BGM_StateBattle.SetValue();
            return;
        }
        SwapBgmEvent(BGM_fight);
    }

    public void ExitBattleBGM()
    {

        if (fight_end != null)
            fight_end.Post(this.gameObject);
        if (BGM_StateCalm != null && BGM_StateCalm.IsValid())
        {
            BGM_StateCalm.SetValue();
            return;
        }
        SwapBgmEvent(BGM);
    }

    void SwapBgmEvent(AK.Wwise.Event next)
    {
        if (next == null || !next.IsValid()) return;
        var host = audioEmitter != null ? audioEmitter : gameObject;

        // Fade out previous BGM, if any.
        if (_currentBgmPlayingId != 0)
        {
            AkUnitySoundEngine.StopPlayingID(
                (uint)_currentBgmPlayingId,
                bgmFadeOutMs,
                AkCurveInterpolation.AkCurveInterpolation_Linear);
            _currentBgmPlayingId = 0;
        }

        _currentBgmPlayingId = next.Post(host);
    }

    // ===== VOLUME (Wwise global RTPCs) =====
    public void SetMasterVolume(float v01) => SetVolRtpc(masterVolumeRtpc, v01);
    public void SetMusicVolume (float v01) => SetVolRtpc(musicVolumeRtpc,  v01);
    public void SetSfxVolume   (float v01) => SetVolRtpc(sfxVolumeRtpc,    v01);

    void SetVolRtpc(string rtpc, float v01)
    {
        if (string.IsNullOrEmpty(rtpc)) return;
        AkUnitySoundEngine.SetRTPCValue(rtpc, Mathf.Clamp01(v01) * 100f);
    }

    public void PlayRotate()
    {
        rotate.Post(this.gameObject);
    }

    public void PlayScroll() 
    {
        scroll.Post(this.gameObject);
    }

    // ===== NOTE =====
    public void PlayNote(int note)
    {
        AkUnitySoundEngine.SetRTPCValue("NoteValue", note, pianoKey[note-1]);
        Note.Post(pianoKey[note-1]);
    }

    // ===== CHORD =====
    public void PlayChord(ChordData chord)
    {
        if (chord == null || chord.notes == null) 
        {
            Debug.Log("No music!");
            return;
        }

        foreach (int note in chord.notes)
        {
            PlayNote(note);
        }
    }

    // emitter: world-positioned object to emit from (enables Wwise distance attenuation).
    // Falls back to audioEmitter when null (BGM-level sounds with no world position).
    public void PlayArpNote(int degree, int octave, float velocity = 0.7f, GameObject emitter = null)
    {
        var e = emitter != null ? emitter : audioEmitter;
        AkUnitySoundEngine.SetRTPCValue("NoteValue", degree, e);
        AkUnitySoundEngine.SetRTPCValue("Oct",       octave, e);
        AkUnitySoundEngine.SetRTPCValue("Velocity",  velocity * 100f, e);
        Note.Post(e);
    }

    // ===== BGM CONTROL =====
    public void SetHarmony(string key)
    {
        AkUnitySoundEngine.SetSwitch("Key", key, audioEmitter);
    }

    public void SetIntensity(float value)
    {
        AkUnitySoundEngine.SetRTPCValue("Intensity", value, audioEmitter);
    }

    // Switches the chord pad to match the current block type.
    // In Wwise, set up:
    //   - a Switch Group named `chordSwitchGroup` (default "BlockChord")
    //   - Switch values "Home", "Lift", "Pull", "Shadow" matching BlockType
    //   - either a Switch Container (instant) or a Music Switch Container
    //     (beat-synced crossfade) under the BGM event
    public void SetChord(BlockType type)
    {
        SetChordOnObject(type, audioEmitter);
        SetChordOnObject(type, this.gameObject);   // BGM Switch Container 也需要
    }
}