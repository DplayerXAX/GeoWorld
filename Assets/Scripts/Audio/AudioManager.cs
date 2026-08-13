using System.Collections;
using System.Collections.Generic;
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
    public AK.Wwise.Event UISound;
    [Tooltip("Posted while a dialogue/tutorial-hint typewriter reveals new characters.")]
    public AK.Wwise.Event TextBlip;
    [Tooltip("Posted when a placed block / turret / endpoint is newly click-selected (PlacementController.UpdateHighlight's new-target branch).")]
    public AK.Wwise.Event SelectObject;
    [Tooltip("Posted when the shop rift opens.")]
    public AK.Wwise.Event ShopExpand;
    [Tooltip("Posted when the shop rift closes.")]
    public AK.Wwise.Event ShopCollapse;
    [Tooltip("Posted when a level is cleared (GameFlowManager.DoLevelClear) — the moment a run is actually won.")]
    public AK.Wwise.Event Victory;
    [Tooltip("Posted when the player runs out of lives (GameFlowManager.HandleGameOver).")]
    public AK.Wwise.Event Defeat;
    [Tooltip("Posted every time a life is lost (PlayerHealth.TakeDamage) — including the killing hit, which plays alongside Defeat.")]
    public AK.Wwise.Event Damage;
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

    // Tracked so Restart / returning to the map can cut these short — like BGM, a
    // posted Wwise event keeps ringing past this GameObject's destruction on a
    // scene change.
    uint _defeatPlayingId, _victoryPlayingId;

    // TextBlip is authored as ONE continuous segment (not a per-character one-shot),
    // so it's started once when a typewriter begins and stopped once it finishes/skips
    // — never re-posted per character (that would restart/overlap the clip).
    uint _currentBlipPlayingId;

    
    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        // Stop our BGM when this AudioManager goes away (e.g. scene reload on Restart),
        // so the reloaded scene's AudioManager doesn't stack a second BGM on top.
        // StopBGM covers the paused instances too — a paused event survives the
        // scene load exactly as a playing one does.
        StopBGM(bgmFadeOutMs);
        StopTextBlip();
        StopDefeat();
        StopVictory();
        if (Instance == this) Instance = null;
    }


    public void PlayUISound()
    {
        UISound.Post(this.gameObject);
    }

    public void PlaySelect()
    {
        if (SelectObject != null && SelectObject.IsValid()) SelectObject.Post(this.gameObject);
    }

    public void PlayShopToggle(bool expanded)
    {
        var e = expanded ? ShopExpand : ShopCollapse;
        if (e != null && e.IsValid()) e.Post(this.gameObject);
    }

    public void PlayVictory()
    {
        if (Victory != null && Victory.IsValid()) _victoryPlayingId = Victory.Post(this.gameObject);
    }

    // Cuts the Victory stinger short. Same reason as StopDefeat: a posted Wwise
    // event outlives the GameObject that posted it, so returning to the map or
    // restarting left the victory loop ringing over the next scene.
    public void StopVictory(int fadeMs = 0)
    {
        if (_victoryPlayingId == 0) return;
        AkUnitySoundEngine.StopPlayingID(_victoryPlayingId, fadeMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _victoryPlayingId = 0;
    }

    // Wave-over stinger on its own. ExitBattleBGM also posts this, but it SWAPS to
    // the calm loop at the same time — which is wrong at level clear, where the
    // Victory track has to play alone.
    public void PlayFightEnd()
    {
        if (fight_end != null && fight_end.IsValid()) fight_end.Post(this.gameObject);
    }

    public void PlayDefeat()
    {
        if (Defeat != null && Defeat.IsValid()) _defeatPlayingId = Defeat.Post(this.gameObject);
    }

    // Cuts the Defeat stinger short — used on Restart, where the old scene's
    // AudioManager is destroyed but the already-posted event would otherwise
    // keep ringing over the freshly reloaded scene.
    public void StopDefeat(int fadeMs = 0)
    {
        if (_defeatPlayingId == 0) return;
        AkUnitySoundEngine.StopPlayingID(_defeatPlayingId, fadeMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _defeatPlayingId = 0;
    }

    public void PlayDamage()
    {
        if (Damage != null && Damage.IsValid()) Damage.Post(this.gameObject);
    }

    // Call once when a typewriter starts revealing a new line/hint.
    public void StartTextBlip()
    {
        StopTextBlip();   // guard: don't stack a second instance if a prior one is still ringing
        if (TextBlip != null && TextBlip.IsValid())
            _currentBlipPlayingId = TextBlip.Post(this.gameObject);
    }

    // Call once when the typewriter finishes (naturally or skipped).
    public void StopTextBlip()
    {
        if (_currentBlipPlayingId == 0) return;
        AkUnitySoundEngine.StopPlayingID(_currentBlipPlayingId, 150,
            AkCurveInterpolation.AkCurveInterpolation_Linear);
        _currentBlipPlayingId = 0;
    }
    private void Start()
    {
        SetChordOnObject(BlockType.Home, this.gameObject);
        SetChordOnObject(BlockType.Home, audioEmitter);

        StartCoroutine(PostBgmAfterIntro());

        // Best-effort early push so there's no audible full-volume frame. It may
        // race this object's own AkBank load (which resets RTPCs to their authored
        // defaults) — AudioSettingsReapplier re-pushes a few frames later and is
        // the actual guarantee. See GameSettings.HookAudioReapply.
        GameSettings.ApplyAudio();
    }

    // In the gameplay scene, IntroDirector plays a short reveal before the player can
    // act — don't start the BGM until that's done (it isn't present in other scenes,
    // e.g. Title/LevelSelect, so this just posts next-frame there, no behavior change).
    IEnumerator PostBgmAfterIntro()
    {
        yield return null;   // let IntroDirector spawn + set Playing=true first, if this scene has one
        while (IntroDirector.Playing) yield return null;

        // Force the initial music state to Calm BEFORE posting the BGM event.
        // Wwise doesn't always honor a "default state" config in the authoring
        // tool reliably, so we set it explicitly here.
        if (BGM_StateCalm != null && BGM_StateCalm.IsValid())
            BGM_StateCalm.SetValue();

        if (BGM != null && BGM.IsValid())
        {
            _currentBgmPlayingId = BGM.Post(this.gameObject);
            _currentBgm = BGM;   // so the first battle swap knows what to pause
        }
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

    // Stops the event-swap BGM outright — unlike ExitBattleBGM, which hands off
    // to the calm loop, this leaves nothing playing. Used for Defeat, where the
    // stinger has to play alone, not layered under music that kept going.
    public void StopBGM(int fadeMs = 0)
    {
        // Stop the paused tracks too. They're real, still-alive instances holding a
        // playhead — leaving them behind means the next swap would RESUME music the
        // caller just asked to silence.
        foreach (var id in _pausedBgm.Values)
            AkUnitySoundEngine.StopPlayingID(id, fadeMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _pausedBgm.Clear();
        _currentBgm = null;

        if (_currentBgmPlayingId == 0) return;
        AkUnitySoundEngine.StopPlayingID(_currentBgmPlayingId, fadeMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        _currentBgmPlayingId = 0;
    }

    // The BGM event currently audible, and every event we've PAUSED (rather than
    // stopped) mapped to the playing instance that's waiting to be resumed.
    //
    // Swapping used to stop the outgoing track outright, so coming back to it posted
    // a brand-new instance — which is why the battle music started from bar one on
    // every single wave. Pausing keeps the instance alive at its playhead.
    AK.Wwise.Event _currentBgm;
    readonly Dictionary<uint, uint> _pausedBgm = new();   // event Id → paused playing id

    void SwapBgmEvent(AK.Wwise.Event next)
    {
        if (next == null || !next.IsValid()) return;
        var host = audioEmitter != null ? audioEmitter : gameObject;

        if (_currentBgm != null && _currentBgm.IsValid() && _currentBgmPlayingId != 0
            && _currentBgm.Id != next.Id)
        {
            BgmAction(_currentBgm, AkActionOnEventType.AkActionOnEventType_Pause);
            _pausedBgm[_currentBgm.Id] = _currentBgmPlayingId;
        }

        if (_pausedBgm.TryGetValue(next.Id, out uint resumeId) && resumeId != 0)
        {
            BgmAction(next, AkActionOnEventType.AkActionOnEventType_Resume);
            _pausedBgm.Remove(next.Id);
            _currentBgmPlayingId = resumeId;
        }
        else if (_currentBgm == null || _currentBgm.Id != next.Id)
        {
            _currentBgmPlayingId = next.Post(host);
        }

        _currentBgm = next;
    }

    // Targeted by EVENT with AK_INVALID_GAME_OBJECT — "wherever this is playing" —
    // rather than by playing id, which ExecuteActionOnEvent doesn't take.
    void BgmAction(AK.Wwise.Event evt, AkActionOnEventType action)
    {
        if (evt == null || !evt.IsValid()) return;
        AkUnitySoundEngine.ExecuteActionOnEvent(
            evt.Id, action, AkUnitySoundEngine.AK_INVALID_GAME_OBJECT,
            bgmFadeOutMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
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
        SetChordOnObject(type, this.gameObject);   // BGM Switch Container also needs it
    }
}