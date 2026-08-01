using UnityEngine;

// "Get this UI out of my way for a second." One shared rule for every overlay that
// can sit between the player and the world — the dialogue box (DialogueRunner), the
// tutorial hint (TutorialDirector), and the block detail panel (PlacementController.
// SelectionPanel, which established the convention and already advertises it in
// BlockInfoPanel's footnote: "Long press Left Shift to hide it temporarily").
//
// Deliberately hold-to-peek rather than a toggle: nothing to remember, nothing to
// leave in a bad state, and the UI is always back the instant the key is released —
// which matters most for the tutorial, where hiding the instructions permanently
// would be the worst possible failure mode.
public static class PeekWorld
{
    public static bool Held => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
}
