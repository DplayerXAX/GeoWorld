using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Wwise GameObject")]
    public GameObject audioEmitter;
    public AK.Wwise.Event BGM;
    public AK.Wwise.Event BGM_fight;

    [Header("Temp")]
    public GameObject[] pianoKey;

    [Header("Sound effect")]
    public AK.Wwise.Event Note;
    public AK.Wwise.Event scroll;
    public AK.Wwise.Event rotate;

    [Header("Chord pad (Wwise Switch driving the BGM pad layer)")]
    // The Switch Group name in your Wwise project. Switch values must
    // match BlockType enum names (Home / Lift / Pull / Shadow).
    public string chordSwitchGroup = "BlockChord";
    void Awake()
    {
        Instance = this;

    }
    private void Start()
    {
        SetChordOnObject(BlockType.Home, this.gameObject);
        SetChordOnObject(BlockType.Home, audioEmitter);
        BGM.Post(this.gameObject);
    }

    void SetChordOnObject(BlockType type, GameObject target)
    {
        if (target == null) return;
        AkUnitySoundEngine.SetSwitch(chordSwitchGroup, type.ToString(), target);
    }

    public void switchBGM() 
    {
        
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