using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Shared screen-space pointer for every raw Physics.Raycast-from-mouse interaction
// (PlacementController, LevelMapController, ShopController). Also keeps the pointer
// on the placement anchor during keyboard nudges and locked-mouse rotation.
[DefaultExecutionOrder(-90)]
public class VirtualCursor : MonoBehaviour
{
    public static VirtualCursor Instance;

    public float cursorSpeed = 1400f;   // px/sec at full stick deflection

    Vector2 _pos;
    RectTransform _icon;
    Canvas _canvas;
    bool _rotationLocked;
    Camera _anchorCamera;
    Vector3 _anchorWorld;
    CursorLockMode _previousLock;
    bool _previousVisible;
    int _ignoreWarpUntil = -1;

    public static Vector2 Position => Instance != null ? Instance._pos : (Vector2)Input.mousePosition;
    public static Vector2 MouseDelta { get; private set; }
    public static bool IgnoreMouseDelta => Instance != null && Time.frameCount <= Instance._ignoreWarpUntil;
    public static bool ConfirmPressedThisFrame => GamepadInput.ConfirmDown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<VirtualCursor>() != null) { EndRotation(); return; }
        var go = new GameObject("VirtualCursor");
        DontDestroyOnLoad(go);
        go.AddComponent<VirtualCursor>();
    }

    void Awake()
    {
        Instance = this;
        _pos = Input.mousePosition;
        BuildUI();
    }

    void Update()
    {
        if (_rotationLocked && (!Input.GetKey(KeyCode.LeftAlt) || !Application.isFocused || SettingsScreen.Open))
            EndRotation();

        MouseDelta = Time.frameCount <= _ignoreWarpUntil || Mouse.current == null
            ? Vector2.zero : Mouse.current.delta.ReadValue();
        bool gamepadMode = GamepadInput.GamepadModeActive;

        if (_rotationLocked)
        {
            _pos = _anchorCamera.WorldToScreenPoint(_anchorWorld);
        }
        else if (gamepadMode)
        {
            _pos += GamepadInput.CursorMoveDelta * cursorSpeed * Time.unscaledDeltaTime;
            _pos.x = Mathf.Clamp(_pos.x, 0f, Screen.width);
            _pos.y = Mathf.Clamp(_pos.y, 0f, Screen.height);
        }
        else if (Time.frameCount > _ignoreWarpUntil)
        {
            _pos = Input.mousePosition;   // snap back the instant the mouse takes over
        }

        if (_icon != null)
        {
            _icon.gameObject.SetActive(gamepadMode || _rotationLocked);
            _icon.position = _pos;
        }
    }

    // Warp updates our shared pointer immediately; the OS/input backend follows
    // on its next update. Ignore that synthetic delta so it cannot rotate a block.
    public static void Warp(Vector2 screenPosition)
    {
        if (Instance == null) return;
        screenPosition.x = Mathf.Clamp(screenPosition.x, 0f, Screen.width - 1f);
        screenPosition.y = Mathf.Clamp(screenPosition.y, 0f, Screen.height - 1f);
        Instance._pos = screenPosition;
        Instance._ignoreWarpUntil = Time.frameCount + 1;
        MouseDelta = Vector2.zero;
        if (!GamepadInput.GamepadModeActive && Application.isFocused)
            Mouse.current?.WarpCursorPosition(screenPosition);
    }

    public static void SetRotationAnchor(bool rotating, Camera camera, Vector3 worldPosition)
    {
        if (Instance == null) return;
        if (!rotating) { EndRotation(); return; }
        var cursor = Instance;
        cursor._anchorCamera = camera;
        cursor._anchorWorld = worldPosition;
        cursor._pos = camera.WorldToScreenPoint(worldPosition);
        if (cursor._rotationLocked) return;

        cursor._rotationLocked = true;
        cursor._previousLock = Cursor.lockState;
        cursor._previousVisible = Cursor.visible;
        GamepadInput.NoteMouseKeyboardActivity();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        cursor._ignoreWarpUntil = Time.frameCount + 1;
        MouseDelta = Vector2.zero;
        cursor._icon.position = cursor._pos;
        cursor._icon.gameObject.SetActive(true);
    }

    public static void EndRotation()
    {
        if (Instance == null || !Instance._rotationLocked) return;
        var cursor = Instance;
        cursor._rotationLocked = false;
        Cursor.lockState = cursor._previousLock;
        Cursor.visible = cursor._previousVisible;
        if (cursor._anchorCamera != null)
            Warp(cursor._anchorCamera.WorldToScreenPoint(cursor._anchorWorld));
        cursor._icon.gameObject.SetActive(GamepadInput.GamepadModeActive);
    }

    void OnApplicationFocus(bool focused) { if (!focused) EndRotation(); }
    void OnDisable() => EndRotation();

    void BuildUI()
    {
        var go = new GameObject("VirtualCursorCanvas", typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 130;   // above tutorial hint (120), below Pause/Settings/LevelClear/Intro
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        var iconGo = new GameObject("Reticle", typeof(RectTransform));
        iconGo.transform.SetParent(go.transform, false);
        _icon = (RectTransform)iconGo.transform;
        _icon.sizeDelta = new Vector2(28f, 28f);
        _icon.pivot = new Vector2(0.5f, 0.5f);

        var img = iconGo.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(14);
        img.type = Image.Type.Sliced;
        img.color = new Color(1f, 1f, 1f, 0.85f);
        img.raycastTarget = false;

        // Thin ink ring so the reticle reads over both light and dark backgrounds.
        var ringGo = new GameObject("Ring", typeof(RectTransform));
        ringGo.transform.SetParent(iconGo.transform, false);
        var ringRt = (RectTransform)ringGo.transform;
        ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
        ringRt.offsetMin = new Vector2(4f, 4f); ringRt.offsetMax = new Vector2(-4f, -4f);
        var ringImg = ringGo.AddComponent<Image>();
        ringImg.sprite = UIRoundedRect.Get(8);
        ringImg.type = Image.Type.Sliced;
        ringImg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
        ringImg.raycastTarget = false;

        _icon.gameObject.SetActive(false);
    }
}
