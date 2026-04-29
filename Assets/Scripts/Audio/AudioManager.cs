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
}