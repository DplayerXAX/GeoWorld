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

    [Header("Turret fire")]
    [Tooltip("Posted each time a Basic turret fires (TurretController.Fire).")]
    public AK.Wwise.Event TurretFireBasic;
    [Tooltip("Posted each time a Slow turret fires its beam.")]
    public AK.Wwise.Event TurretFireSlow;
    [Tooltip("Posted each time an AOE turret lobs a blast.")]
    public AK.Wwise.Event TurretFireAoe;
    [Tooltip("Post from the turret itself instead of from this manager — only useful if the events are authored as 3D (positioned) in Wwise.")]
    public bool turretFireSpatial = false;
    [Tooltip("Shortest gap between two posts of the SAME turret type, in seconds. A full board has many turrets firing in the same frame; without a gap they stack into one loud clipped burst.")]
    [Min(0f)] public float turretFireMinGap = 0.04f;

    readonly float[] _lastTurretFire = { -1f, -1f, -1f };   // per TurretController.Mode
    [Header("Volume RTPCs (Wwise global, 0..100)")]
    [Tooltip("Global Wwise RTPC names bound to your bus volumes. SettingsScreen drives these 0..1 → 0..100. Set them up on the Master / Music / SFX buses in Wwise.")]
    public string masterVolumeRtpc = "MasterVolume";
    public string musicVolumeRtpc  = "MusicVolume";
    public string sfxVolumeRtpc    = "SFXVolume";

    [Header("Chord pad (Wwise Switch driving the BGM pad layer)")]
    // The Switch Group name in your Wwise project. Switch values must
    // match BlockType enum names (Home / Lift / Pull / Shadow).
    public string chordSwitchGroup = "BlockChord";

    [Header("Hurt pulse (RTPC for the music)")]
    // Jumps to 1 the moment the player loses a life, holds, then falls back to 0.
    // Bind it to anything in Wwise — a low-pass on the BGM bus, a pitch dip, a
    // dropout of the melodic layer, a swell on a tension track — so the MUSIC
    // flinches when you are hit, not just a one-shot SFX over the top of it.
    [Tooltip("Global Wwise RTPC name. Set its range to 0..1 in Wwise (or change hurtRtpcMax).")]
    public string hurtRtpc = "HurtPulse";
    [Tooltip("Value sent at the peak. 1 for a 0..1 RTPC, 100 for a 0..100 one.")]
    public float hurtRtpcMax = 1f;
    [Tooltip("Seconds held at the peak before it starts to fall.")]
    [Min(0f)] public float hurtHold = 0.12f;
    [Tooltip("Seconds to fall from the peak back to 0.")]
    [Min(0.01f)] public float hurtDecay = 2.2f;
    [Tooltip("Shape of the fall: x = time through the decay (0..1), y = value (1..0). Default eases out — a sharp drop just after the hit, a long tail after it.")]
    public AnimationCurve hurtShape = new AnimationCurve(
        new Keyframe(0f, 1f, 0f, -2.2f),
        new Keyframe(1f, 0f, 0f, 0f));

    // Live value, 0..1, for anything else that wants to react with the music
    // (screen tint, vignette) instead of keeping its own timer.
    public float HurtPulse => _hurt;

    float _hurt;
    float _hurtAge = -1f;       // seconds since the last hit; < 0 = idle
    float _hurtSent = -1f;      // last value actually sent to Wwise

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

    // Real time, not game time. The music does not slow down for a hit-stop or a
    // slowed timescale, so neither may the thing driving it — a decay tied to
    // Time.deltaTime would stretch out and hang at its peak for as long as the game
    // is slowed.
    void Update()
    {
        if (_hurtAge < 0f) return;

        _hurtAge += Time.unscaledDeltaTime;

        float v;
        if (_hurtAge <= hurtHold) v = 1f;
        else
        {
            float t = (_hurtAge - hurtHold) / Mathf.Max(0.01f, hurtDecay);
            v = t >= 1f ? 0f : Mathf.Clamp01(hurtShape.Evaluate(t));
            if (t >= 1f) _hurtAge = -1f;   // done — stop updating until the next hit
        }

        SendHurt(v);
    }

    // Retriggers on every hit: a second leak mid-decay jumps straight back to the
    // peak rather than adding to it, so a burst of leaks reads as one sustained
    // flinch instead of clipping past the RTPC's range.
    public void PulseHurt()
    {
        _hurtAge = 0f;
        SendHurt(1f);
    }

    void SendHurt(float v01)
    {
        _hurt = v01;
        // Only when it moves — at rest this would otherwise post the same 0 to the
        // sound engine every frame for the entire game.
        if (Mathf.Abs(v01 - _hurtSent) < 0.001f) return;
        _hurtSent = v01;
        if (!string.IsNullOrEmpty(hurtRtpc))
            AkUnitySoundEngine.SetRTPCValue(hurtRtpc, v01 * hurtRtpcMax);   // global scope: every emitter, BGM included
    }

    void OnDestroy()
    {
        // The RTPC is GLOBAL and outlives this object. Leave it anywhere but 0 on a
        // scene change and the next scene's music starts out already flinching.
        _hurtAge = -1f;
        SendHurt(0f);

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

    // One event per turret type, rate-limited per type (see turretFireMinGap).
    public void PlayTurretFire(TurretController.Mode mode, GameObject turret)
    {
        var e = mode switch
        {
            TurretController.Mode.Slow => TurretFireSlow,
            TurretController.Mode.Aoe  => TurretFireAoe,
            _                          => TurretFireBasic,
        };
        if (e == null || !e.IsValid()) return;

        int i = Mathf.Clamp((int)mode, 0, _lastTurretFire.Length - 1);
        float now = Time.unscaledTime;
        if (_lastTurretFire[i] >= 0f && now - _lastTurretFire[i] < turretFireMinGap) return;
        _lastTurretFire[i] = now;

        e.Post(turretFireSpatial && turret != null ? turret : this.gameObject);
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
        //
        // RESUMED FIRST, and that ordering is the whole point. Wwise's music engine
        // schedules interactive music on a segment sequencer, and stopping a segment
        // that is sitting paused tears it down while the sequencer still has it
        // flagged as playing — which is the AKASSERT !m_pSegment->IsMusicPlaying()
        // in AkMatrixSequencer. Resuming puts it back into a state the stop can
        // unwind cleanly. Resume on an already-playing instance is a no-op, so this
        // is safe even when nothing was paused.
        foreach (var id in _pausedBgm.Values)
        {
            AkUnitySoundEngine.ExecuteActionOnPlayingID(
                AkActionOnEventType.AkActionOnEventType_Resume, id, 0,
                AkCurveInterpolation.AkCurveInterpolation_Linear);
            AkUnitySoundEngine.StopPlayingID(id, fadeMs, AkCurveInterpolation.AkCurveInterpolation_Linear);
        }
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