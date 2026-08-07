using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using TMPro;

// Modelling-software XYZ orientation gizmo. Tumbles with the camera; clicking a
// letter snaps to that axis' view (X=side, Y=top, Z=front) via yaw/pitch only —
// focus/distance/pan untouched, right-drag keeps working from wherever it lands.
// Auto-spawns in gameplay/LevelSelect (needs OrbitCamera + one of their controllers).
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
        // Not Title/Gallery — their scripted camera choreography would fight this.
        if (PlacementController.Instance == null && LevelMapController.Instance == null) return;
        if (FindFirstObjectByType<OrbitCamera>() == null) return;
        new GameObject("AxisGizmo").AddComponent<AxisGizmo>();
    }

    [Header("Placement")]
    [Tooltip("Distance from top-left, in 1920×1080 canvas units. Matches PauseMenu's top-right inset.")]
    public Vector2 margin = new Vector2(32f, 32f);
    public float radius = 34f;
    public float handleSize = 22f;
    public float lineThickness = 3f;

    [Header("Look")]
    public Color xColor = new Color(0.886f, 0.141f, 0.106f);
    public Color yColor = new Color(0.298f, 0.686f, 0.314f);
    public Color zColor = new Color(0.169f, 0.424f, 0.690f);
    [Tooltip("How much a far-side axis fades toward the background.")]
    [Range(0f, 1f)] public float backFade = 0.65f;

    [Header("Snap targets")]
    public float sideViewPitch = 0f;
    [Tooltip("Clamped by OrbitCamera to the same range right-drag allows.")]
    public float topViewPitch = 89f;

    // Read by LevelMapController so a gizmo click doesn't also raycast into the
    // map and walk the pawn (PlacementController already covers this generically).
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

    // Axis direction in camera space: x/y give screen position, z gives depth
    // (positive = pointing away from viewer).
    void LayoutAxis(ref Axis a)
    {
        Vector3 local = _cam.transform.InverseTransformDirection(a.dir);
        Vector2 screen = new Vector2(local.x, local.y) * radius;

        a.handle.anchoredPosition = screen;

        float len = screen.magnitude;
        a.arm.sizeDelta = new Vector2(len, lineThickness);
        a.arm.localRotation = len > 0.01f
            ? Quaternion.Euler(0f, 0f, Mathf.Atan2(screen.y, screen.x) * Mathf.Rad2Deg)
            : Quaternion.identity;

        float away  = Mathf.InverseLerp(-1f, 1f, local.z);
        float alpha = Mathf.Lerp(1f, 1f - backFade, away);

        a.armImg.color    = new Color(a.color.r, a.color.g, a.color.b, alpha);
        a.handleImg.color = new Color(a.color.r, a.color.g, a.color.b, alpha);
        a.label.color     = new Color(1f, 1f, 1f, alpha);

        a.handle.SetSiblingIndex(local.z > 0f ? 0 : _root.childCount - 1);   // near draws over far
    }

    // Click +X → camera ends up looking from +X back at the origin, so forward = -axis.
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
        _canvas.sortingOrder = 88;   // just under the HUD (90)

        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;
        EnsureEventSystem();

        _root = NewRect("Gizmo", canvasGo.transform);
        _root.anchorMin = _root.anchorMax = _root.pivot = new Vector2(0f, 1f);
        _root.anchoredPosition = new Vector2(margin.x, -margin.y);
        _root.sizeDelta = Vector2.one * (radius * 2f + handleSize);

        // Transparent hit area over the whole widget, so PointerOver covers gaps too.
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

        a.arm = NewRect($"Arm_{letter}", _root);   // pivoted at left edge so it grows outward from centre
        a.arm.anchorMin = a.arm.anchorMax = new Vector2(0.5f, 0.5f);
        a.arm.pivot = new Vector2(0f, 0.5f);
        a.arm.anchoredPosition = Vector2.zero;
        a.armImg = a.arm.gameObject.AddComponent<Image>();
        a.armImg.color = color;
        a.armImg.raycastTarget = false;

        a.handle = NewRect($"Handle_{letter}", _root);   // clickable letter disc at the arm's tip
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

    class PointerTracker : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public System.Action onEnter, onExit;
        public void OnPointerEnter(PointerEventData e) => onEnter?.Invoke();
        public void OnPointerExit(PointerEventData e)  => onExit?.Invoke();
    }
}
