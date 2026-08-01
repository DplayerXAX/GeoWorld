using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

// Modelling-software style XYZ orientation gizmo, top-right. The three axes are
// drawn projected through the live camera rotation (so it tumbles as you orbit,
// reading as an orientation READOUT and not just three buttons), and clicking a
// letter snaps the camera to that axis' canonical view:
//
//     X → side view      Y → top view      Z → front view
//
// Snapping only rewrites yaw/pitch (OrbitCamera.SetWorldYawPitch) — focus point,
// distance and pan are untouched, and right-drag keeps working exactly as before
// from wherever the snap left off. Nothing here is modal.
//
// Auto-spawns into any scene that has BOTH an OrbitCamera and one of the two
// controllers that actually want it (gameplay's PlacementController, LevelSelect's
// LevelMapController) — same RuntimeInitializeOnLoadMethod + sceneLoaded pattern
// the rest of the runtime-built UI uses, so neither scene needs wiring.
[DisallowMultipleComponent]
public class AxisGizmo : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static void TrySpawn()
    {
        if (FindFirstObjectByType<AxisGizmo>() != null) return;
        // Gameplay OR LevelSelect only — deliberately not Title/Gallery, which run
        // their own scripted camera choreography an orientation gizmo would fight.
        if (PlacementController.Instance == null && LevelMapController.Instance == null) return;
        if (FindFirstObjectByType<OrbitCamera>() == null) return;
        new GameObject("AxisGizmo").AddComponent<AxisGizmo>();
    }

    [Header("Placement")]
    [Tooltip("Distance from the top-left screen corner, in 1920×1080 canvas units. Defaults match PauseMenu's top-right chip row's own (16,16) inset, so the gizmo sits level with it on the opposite corner.")]
    public Vector2 margin = new Vector2(32f, 32f);
    [Tooltip("Length of each axis arm, in canvas units — also the gizmo's radius.")]
    public float radius = 34f;
    [Tooltip("Diameter of the clickable letter discs at each arm's tip.")]
    public float handleSize = 22f;
    public float lineThickness = 3f;

    [Header("Look")]
    public Color xColor = new Color(0.886f, 0.141f, 0.106f);   // GeoPalette.Signal
    public Color yColor = new Color(0.298f, 0.686f, 0.314f);
    public Color zColor = new Color(0.169f, 0.424f, 0.690f);   // GeoPalette.Blue
    [Tooltip("How far an axis pointing directly AWAY from the camera fades toward the background — near arms stay fully opaque, far ones recede.")]
    [Range(0f, 1f)] public float backFade = 0.65f;

    [Header("Snap targets")]
    [Tooltip("Pitch used by the X / Z (side / front) views. 0 = perfectly level with the horizon.")]
    public float sideViewPitch = 0f;
    [Tooltip("Pitch used by the Y (top) view. OrbitCamera clamps this to the same range right-drag allows (80° in ortho), so a snap can never land somewhere the next drag would jump away from.")]
    public float topViewPitch = 89f;

    // True while the pointer is over any part of the gizmo. LevelMapController
    // reads this so a click meant for the gizmo doesn't ALSO raycast into the map
    // and walk the pawn somewhere (that guard is rect-scoped to the info panel, so
    // it doesn't cover this on its own). Gameplay's PlacementController already
    // uses a global EventSystem.IsPointerOverGameObject() check, which covers this
    // canvas for free.
    public static bool PointerOver { get; private set; }

    Camera        _cam;
    OrbitCamera   _orbit;
    Canvas        _canvas;
    RectTransform _root;

    // Per-axis: the world direction it represents, its arm, and its letter disc.
    struct Axis
    {
        public Vector3       dir;
        public RectTransform arm;
        public Image         armImg;
        public RectTransform handle;
        public Image         handleImg;
        public TMP_Text      label;
        public Color         color;
    }
    Axis[] _axes;

    void Awake()
    {
        BuildUI();
    }

    void OnDisable() => PointerOver = false;

    void Update()
    {
        if (_orbit == null)
        {
            _orbit = FindFirstObjectByType<OrbitCamera>();
            if (_orbit == null) return;
        }
        if (_cam == null) _cam = _orbit.myCam != null ? _orbit.myCam : Camera.main;
        if (_cam == null) return;

        for (int i = 0; i < _axes.Length; i++) LayoutAxis(ref _axes[i]);
    }

    // Projects one world axis into the gizmo's 2D face using the camera's own
    // basis: the axis' x/y in CAMERA space are its screen direction, and its z is
    // how much it points toward (negative) or away from (positive) the viewer.
    void LayoutAxis(ref Axis a)
    {
        Vector3 local = _cam.transform.InverseTransformDirection(a.dir);
        Vector2 screen = new Vector2(local.x, local.y) * radius;

        a.handle.anchoredPosition = screen;

        // Arm: a thin rect pivoted at the gizmo's centre, rotated to point at the
        // handle and stretched to exactly reach it. Foreshortens to nothing when
        // the axis points straight at the camera, which is what sells the tumble.
        float len = screen.magnitude;
        a.arm.sizeDelta = new Vector2(len, lineThickness);
        a.arm.localRotation = len > 0.01f
            ? Quaternion.Euler(0f, 0f, Mathf.Atan2(screen.y, screen.x) * Mathf.Rad2Deg)
            : Quaternion.identity;

        // Depth fade. local.z > 0 = pointing away from the viewer (Unity cameras
        // look down their own +z, so a positive camera-space z is BEHIND the
        // gizmo's face from the viewer's side).
        float away  = Mathf.InverseLerp(-1f, 1f, local.z);
        float alpha = Mathf.Lerp(1f, 1f - backFade, away);

        a.armImg.color    = new Color(a.color.r, a.color.g, a.color.b, alpha);
        a.handleImg.color = new Color(a.color.r, a.color.g, a.color.b, alpha);
        a.label.color     = new Color(1f, 1f, 1f, alpha);

        // Sort by depth so near arms draw over far ones, same as a real gizmo.
        a.handle.SetSiblingIndex(local.z > 0f ? 0 : _root.childCount - 1);
    }

    // ── Snap ─────────────────────────────────────────────────────────────────
    // Camera sits at focus + rot*(0,0,-distance), so its offset direction from the
    // focus point is -forward. Placing the camera on an axis' POSITIVE side (the
    // convention every modelling package uses — click +X, end up looking from +X
    // back at the origin) therefore means forward = -axis:
    //
    //   +X side → forward (-1,0,0) → worldYaw -90
    //   +Z side → forward (0,0,-1) → worldYaw 180
    //   +Y side → look straight down → pitch ~90, yaw kept as-is
    void SnapTo(int axisIndex)
    {
        if (_orbit == null) return;
        switch (axisIndex)
        {
            case 0: _orbit.SetWorldYawPitch(-90f, sideViewPitch); break;                    // X — side
            case 1: _orbit.SetWorldYawPitch(_orbit.WorldYaw, topViewPitch); break;          // Y — top (heading unchanged)
            default: _orbit.SetWorldYawPitch(180f, sideViewPitch); break;                   // Z — front
        }
    }

    // ── Build ────────────────────────────────────────────────────────────────
    void BuildUI()
    {
        var canvasGo = new GameObject("AxisGizmoCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 88;   // just under the HUD (90) — never over a panel or dialogue

        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();

        _root = NewRect("Gizmo", canvasGo.transform);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(margin.x, -margin.y);
        _root.sizeDelta = Vector2.one * (radius * 2f + handleSize);

        // Hover tracking lives on a transparent disc covering the whole gizmo, so
        // PointerOver is true across the entire widget (arms and gaps included),
        // not just when a letter happens to be under the cursor.
        var hit = NewRect("HitArea", _root);
        hit.anchorMin = Vector2.zero; hit.anchorMax = Vector2.one;
        hit.offsetMin = hit.offsetMax = Vector2.zero;
        var hitImg = hit.gameObject.AddComponent<Image>();
        hitImg.color = new Color(0f, 0f, 0f, 0f);
        hitImg.raycastTarget = true;
        var tracker = hit.gameObject.AddComponent<PointerTracker>();
        tracker.onEnter = () => PointerOver = true;
        tracker.onExit  = () => PointerOver = false;

        _axes = new[]
        {
            MakeAxis("X", Vector3.right,   xColor, 0),
            MakeAxis("Y", Vector3.up,      yColor, 1),
            MakeAxis("Z", Vector3.forward, zColor, 2),
        };
    }

    Axis MakeAxis(string letter, Vector3 dir, Color color, int index)
    {
        var a = new Axis { dir = dir, color = color };

        // Arm — pivoted at its LEFT edge so it grows outward from the gizmo centre
        // and rotates around it, which is what LayoutAxis' rotate+stretch assumes.
        a.arm = NewRect($"Arm_{letter}", _root);
        a.arm.anchorMin = a.arm.anchorMax = new Vector2(0.5f, 0.5f);
        a.arm.pivot = new Vector2(0f, 0.5f);
        a.arm.anchoredPosition = Vector2.zero;
        a.armImg = a.arm.gameObject.AddComponent<Image>();
        a.armImg.color = color;
        a.armImg.raycastTarget = false;

        // Handle — the clickable letter disc at the arm's tip.
        a.handle = NewRect($"Handle_{letter}", _root);
        a.handle.anchorMin = a.handle.anchorMax = a.handle.pivot = new Vector2(0.5f, 0.5f);
        a.handle.sizeDelta = Vector2.one * handleSize;
        a.handleImg = a.handle.gameObject.AddComponent<Image>();
        a.handleImg.sprite = UIRoundedRect.Get(Mathf.RoundToInt(handleSize * 0.5f));
        a.handleImg.type = Image.Type.Simple;
        a.handleImg.color = color;

        var btn = a.handle.gameObject.AddComponent<Button>();
        btn.targetGraphic = a.handleImg;
        var colors = btn.colors;
        colors.highlightedColor = GeoPalette.Gold;
        colors.pressedColor = GeoPalette.Paper;
        btn.colors = colors;
        int captured = index;
        btn.onClick.AddListener(() => SnapTo(captured));

        a.label = NewText($"Label_{letter}", a.handle, handleSize * 0.62f);
        a.label.text = letter;
        var lrt = a.label.rectTransform;
        lrt.anchorMin = Vector2.zero; lrt.anchorMax = Vector2.one;
        lrt.offsetMin = lrt.offsetMax = Vector2.zero;

        return a;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────
    static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;
        new GameObject("EventSystem",
            typeof(EventSystem),
            typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    // Tiny enter/exit relay — the gizmo needs hover state, and a UGUI Button's own
    // colour transitions don't expose one.
    class PointerTracker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action onEnter, onExit;
        public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
        public void OnPointerExit(PointerEventData e)  => onExit?.Invoke();
    }
}
