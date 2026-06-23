using System;
using System.Collections.Generic;
using UnityEngine;

public enum PortraitSlot { Left, Right, Center }

// One spoken line. character == null → narration (no name box / portrait change).
[Serializable]
public class DialogueLine
{
    public DialogueCharacter character;
    [Tooltip("Portrait/expression key on the character (blank = default).")]
    public string portrait = "";
    public PortraitSlot slot = PortraitSlot.Left;
    [TextArea(2, 5)] public string text;
    [Tooltip("Override the speaker name (else uses character.displayName).")]
    public string speakerOverride = "";
    [Tooltip("Optional id broadcast (DialogueRunner.OnLineEvent) when this line shows — hook game events / SFX.")]
    public string eventId = "";

    public string SpeakerName =>
        !string.IsNullOrEmpty(speakerOverride) ? speakerOverride
        : (character != null ? character.displayName : "");
}

// A branch option shown after the last line.
[Serializable]
public class DialogueChoice
{
    [TextArea(1, 2)] public string text;
    [Tooltip("Conversation to jump to when chosen (null = end the dialogue).")]
    public DialogueConversation next;
    [Tooltip("Optional id broadcast when this choice is picked.")]
    public string eventId = "";
}

// A topic / conversation: a list of lines, then either choices, an auto-continue,
// or the end. Branch by linking conversations together. Editable per-asset in the
// Inspector. Create via Assets ▸ Create ▸ Dialogue ▸ Conversation.
[CreateAssetMenu(menuName = "Dialogue/Conversation", fileName = "Conversation")]
public class DialogueConversation : ScriptableObject
{
    [Tooltip("Topic / title (话题) shown in the box header.")]
    public string topic = "";

    public List<DialogueLine> lines = new();

    [Header("After the last line")]
    [Tooltip("Branch choices. Empty = no choices.")]
    public List<DialogueChoice> choices = new();
    [Tooltip("If there are no choices, automatically continue into this conversation.")]
    public DialogueConversation autoNext;
}
