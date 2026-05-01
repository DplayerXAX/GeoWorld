using UnityEngine;

public class AudioManager : MonoBehaviour
{
    public static AudioManager Instance;

    [Header("Wwise GameObject")]
    public GameObject audioEmitter;
    public AK.Wwise.Event BGM;

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
        // Establish a default Switch state before the BGM Switch Container fires,
        // otherwise Wwise logs "No default Switch value selected".
        SetChord(BlockType.Home);
        BGM.Post(this.gameObject);
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

    public void PlayArpNote(int degree, int octave, float velocity = 0.7f)
    {
        AkUnitySoundEngine.SetRTPCValue("NoteValue", degree, audioEmitter);
        AkUnitySoundEngine.SetRTPCValue("Oct", octave, audioEmitter);
        AkUnitySoundEngine.SetRTPCValue("Velocity", velocity * 100f, audioEmitter);

        Note.Post(audioEmitter);
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
        AkUnitySoundEngine.SetSwitch(chordSwitchGroup, type.ToString(), audioEmitter);
    }
}