using UnityEngine;

// Wwise event references for minigames, loaded from Resources rather than
// dragged onto AudioManager — BlockTetris3D is a pure-code overlay with no scene
// GameObject to configure via Inspector, and it has to work when launched from
// LevelSelect, which carries no AudioManager at all (see GameSettings.ApplyAudio's
// no-AudioManager fallback for the same constraint on the volume RTPCs). Posting
// a Wwise event only needs SOME GameObject as the emitter, not an AudioManager,
// so this sidesteps the dependency entirely.
[CreateAssetMenu(menuName = "Game/Minigame Audio")]
public class MinigameAudio : ScriptableObject
{
    [Tooltip("Posted once when the Stack Well minigame opens, stopped (with fade) when it closes. Assign the Wwise event via the picker.")]
    public AK.Wwise.Event stackWellMusic;
    [Tooltip("Fade-out on the music above when the minigame closes, in ms.")]
    [Min(0)] public int stackWellMusicFadeOutMs = 500;

    static MinigameAudio _cached;
    public static MinigameAudio Get()
    {
        if (_cached == null) _cached = Resources.Load<MinigameAudio>("GeoWorldAudioConfig/MinigameAudio");
        return _cached;
    }
}
