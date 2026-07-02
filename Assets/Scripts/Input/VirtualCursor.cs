using UnityEngine;
using UnityEngine.UI;

// Shared screen-space pointer for every raw Physics.Raycast-from-mouse interaction
// (PlacementController, LevelMapController, ShopController). In mouse mode it's a pure
// pass-through to Input.mousePosition — mouse users see byte-identical behavior. In gamepad
// mode it integrates the left stick and draws a small reticle so the player can see where
// they're pointing.
[DefaultExecutionOrder(-90)]
public class VirtualCursor : MonoBehaviour
{
    public static VirtualCursor Instance;

    public float cursorSpeed = 1400f;   // px/sec at full stick deflection

    Vector2 _pos;
    RectTransform _icon;
    Canvas _canvas;

    public static Vector2 Position => Instance != null ? Instance._pos : (Vector2)Input.mousePosition;
    public static bool ConfirmPressedThisFrame => GamepadInput.ConfirmDown;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<VirtualCursor>() != null) return;
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
        bool gamepadMode = GamepadInput.GamepadModeActive;

        if (gamepadMode)
        {
            _pos += GamepadInput.CursorMoveDelta * cursorSpeed * Time.unscaledDeltaTime;
            _pos.x = Mathf.Clamp(_pos.x, 0f, Screen.width);
            _pos.y = Mathf.Clamp(_pos.y, 0f, Screen.height);
        }
        else
        {
            _pos = Input.mousePosition;   // snap back the instant the mouse takes over
        }

        if (_icon != null)
        {
            _icon.gameObject.SetActive(gamepadMode);
            _icon.position = _pos;
        }
    }

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
