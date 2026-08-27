using UnityEngine;

// Hold Shift to get UI out of the way — shared by DialogueRunner, TutorialDirector,
// and the block detail panel (PlacementController.SelectionPanel).
public static class PeekWorld
{
    public static bool Held => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
}
