using UnityEngine;
using UnityEngine.Events;

// Convenience starter: kicks off a conversation on Start, on a key, or when its
// Play() is called (wire to a Button, trigger volume, etc.). Optional UnityEvent on
// finish for game flow (open shop, give reward, …).
public class DialogueTrigger : MonoBehaviour
{
    public DialogueConversation conversation;
    public bool    playOnStart = false;
    public KeyCode playKey     = KeyCode.None;
    [Tooltip("Don't start again while a dialogue is already playing.")]
    public bool    blockIfPlaying = true;

    public UnityEvent onFinished;

    void Start() { if (playOnStart) Play(); }

    void Update()
    {
        if (playKey != KeyCode.None && Input.GetKeyDown(playKey)) Play();
    }

    public void Play()
    {
        if (conversation == null) return;
        if (blockIfPlaying && DialogueRunner.Instance.IsPlaying) return;

        void Done(DialogueConversation _)
        {
            DialogueRunner.Instance.OnFinished -= Done;
            onFinished?.Invoke();
        }
        DialogueRunner.Instance.OnFinished += Done;
        DialogueRunner.Instance.Play(conversation);
    }
}
