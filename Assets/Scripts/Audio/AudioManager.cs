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
        //Note.Post(this.gameObject);
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
        AkSoundEngine.SetRTPCValue("NoteValue", note, pianoKey[note-1]);
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

    public void PlayArpNote(int semitone)
    {

        AkSoundEngine.SetRTPCValue("NoteValue", semitone, audioEmitter);
        Note.Post(audioEmitter);
        //AkSoundEngine.PostEvent("Play_Arp_Note", audioEmitter);
    }

    // ===== BGM CONTROL =====
    public void SetHarmony(string key)
    {
        AkSoundEngine.SetSwitch("Key", key, audioEmitter);
    }

    public void SetIntensity(float value)
    {
        AkSoundEngine.SetRTPCValue("Intensity", value, audioEmitter);
    }

    // Switches the chord pad to match the current block type.
    // In Wwise, set up:
    //   - a Switch Group named `chordSwitchGroup` (default "BlockChord")
    //   - Switch values "Home", "Lift", "Pull", "Shadow" matching BlockType
    //   - either a Switch Container (instant) or a Music Switch Container
    //     (beat-synced crossfade) under the BGM event
    public void SetChord(BlockType type)
    {
        AkSoundEngine.SetSwitch(chordSwitchGroup, type.ToString(), audioEmitter);
    }
}