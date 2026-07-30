using UnityEngine;
using UnityEngine.InputSystem;

// Central gamepad-reading layer. Everything else (PlacementController, OrbitCamera,
// DialogueRunner, ...) reads these static accessors instead of polling Gamepad.current
// itself. Mouse/keyboard scripts stay untouched — call sites just OR these in.
//
// Button layout (Xbox-style naming; PS/Switch equivalents map 1:1 via Gamepad.current):
//   A (South)      Confirm            B (East)        Cancel
//   X (West)       Delete             Y (North)       Toggle place/select mode
//   Right shoulder Rotate             Left shoulder   Undo
//   D-pad L/R      Cycle block type   D-pad U/D       Menu carousel move
//   Right stick click                 Toggle shop
//   Start                             Toggle pause
//   Left stick     Move / cursor      Right stick     Camera look
//   Triggers (R-L) Camera zoom
public static class GamepadInput
{
    public static bool Present => Gamepad.current != null;
    public static bool GamepadModeActive { get; private set; }

    public static bool ConfirmDown, CancelDown;
    public static bool RotateDown, DeleteDown, UndoDown;
    public static bool CycleBlockPrevDown, CycleBlockNextDown;
    public static bool ToggleModeDown, ToggleShopDown, TogglePauseDown;
    public static bool DPadUpDown, DPadDownDown;

    public static Vector2 Move;             // left stick
    public static Vector2 Look;             // right stick
    public static float   ZoomDelta;        // right trigger - left trigger
    public static Vector2 CursorMoveDelta;  // feeds VirtualCursor (== Move, kept distinct for clarity)

    const float Deadzone = 0.15f;

    public static void NoteGamepadActivity() => GamepadModeActive = true;
    public static void NoteMouseKeyboardActivity() => GamepadModeActive = false;

    // Called once/frame by GamepadInputDriver before anything else reads the fields above.
    internal static void Poll()
    {
        var pad = Gamepad.current;
        if (pad == null)
        {
            ConfirmDown = CancelDown = RotateDown = DeleteDown = UndoDown = false;
            CycleBlockPrevDown = CycleBlockNextDown = false;
            ToggleModeDown = ToggleShopDown = TogglePauseDown = false;
            DPadUpDown = DPadDownDown = false;
            Move = Look = Vector2.zero;
            ZoomDelta = 0f;
            CursorMoveDelta = Vector2.zero;
            return;
        }

        ConfirmDown         = pad.buttonSouth.wasPressedThisFrame;
        CancelDown          = pad.buttonEast.wasPressedThisFrame;
        DeleteDown          = pad.buttonWest.wasPressedThisFrame;
        ToggleModeDown      = pad.buttonNorth.wasPressedThisFrame;
        RotateDown          = pad.rightShoulder.wasPressedThisFrame;
        UndoDown            = pad.leftShoulder.wasPressedThisFrame;
        CycleBlockPrevDown  = pad.dpad.left.wasPressedThisFrame;
        CycleBlockNextDown  = pad.dpad.right.wasPressedThisFrame;
        DPadUpDown          = pad.dpad.up.wasPressedThisFrame;
        DPadDownDown        = pad.dpad.down.wasPressedThisFrame;
        ToggleShopDown      = pad.rightStickButton.wasPressedThisFrame;
        TogglePauseDown     = pad.startButton.wasPressedThisFrame;

        Vector2 move = pad.leftStick.ReadValue();
        Vector2 look = pad.rightStick.ReadValue();
        Move = move.sqrMagnitude > Deadzone * Deadzone ? move : Vector2.zero;
        Look = look.sqrMagnitude > Deadzone * Deadzone ? look : Vector2.zero;
        CursorMoveDelta = Move;
        ZoomDelta = pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();

        bool anyButton = ConfirmDown || CancelDown || DeleteDown || ToggleModeDown || RotateDown ||
                          UndoDown || CycleBlockPrevDown || CycleBlockNextDown || DPadUpDown ||
                          DPadDownDown || ToggleShopDown || TogglePauseDown;
        if (anyButton || Move != Vector2.zero || Look != Vector2.zero || Mathf.Abs(ZoomDelta) > 0.05f)
            NoteGamepadActivity();

        if (Input.anyKeyDown || Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1) ||
            Input.mouseScrollDelta.sqrMagnitude > 0f)
            NoteMouseKeyboardActivity();
    }

    // Maps a legacy per-step KeyCode (LevelDefinition.TutorialStep.inputKey, drawn from the same
    // hotkey set PlacementController/CarouselMenu use) to whichever gamepad action fired this
    // frame — lets data-driven tutorial Input-steps become gamepad-aware with no data migration.
    public static bool MatchesKeyCode(KeyCode key) => key switch
    {
        KeyCode.Space                                  => ConfirmDown,
        KeyCode.Return or KeyCode.KeypadEnter           => ConfirmDown,
        KeyCode.Escape                                  => CancelDown,
        KeyCode.Tab                                     => ToggleModeDown,
        KeyCode.Alpha1 or KeyCode.Alpha2 or KeyCode.Alpha3
                                                         => RotateDown,
        KeyCode.R                                       => RotateDown,
        KeyCode.Delete or KeyCode.Backspace              => DeleteDown,
        KeyCode.F                                        => ToggleShopDown,
        _                                                => false,
    };
}
