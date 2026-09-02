using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// The settings categories, as the faces of a cube.
//
// Replaces the tab column. A tab strip is a list that happens to be vertical; a cube
// is the game's own mark, and reading a category off one of its faces is the same
// gesture the player already makes to look at the board.
//
// THERE IS ONLY ONE OF THESE. The pause menu's centrepiece, the settings page's
// category picker and the silhouette the screen folds into are all the same object,
// because a shape that vanishes in one place and reappears in another reads as two
// shapes no matter how it is animated. That is what the "shared cube" section below
// is for, and it is why the screen wipe masks its captured frame to THIS cube's
// render rather than to a diamond of its own.
//
// The cube is held at a FIXED three-quarter pose rather than turned face-on to a
// chosen category. Square on, a cube is a square: it shows one category and gives no
// hint that there are others behind it, which throws away the only thing it has over
// a tab strip. From a corner you see three faces at once, so all three categories are
// legible and clickable without turning anything.
//
// Rendered through a RenderTexture rather than a world-space canvas so it can sit in
// a layout like any other UI element, and so the hit test is a plain rect check
// instead of a world raycast that has to dodge the rest of the scene.
//
// NOTE the RT is SUPERSAMPLED, not multisampled. antiAliasing > 1 on a hand-made
// RenderTexture produces "Missing resolve surface for attachment 0" under URP's
// render graph and the camera's output never lands — the shop lost its entire
// contents to exactly this. Rendering at 2x and letting the sampler filter down is
// the compatible way to get the same edges.
[DisallowMultipleComponent]
public class SettingsCube : MonoBehaviour, IPointerClickHandler, IDragHandler, IBeginDragHandler
{
    // Parked far from anything so the cube's own camera sees nothing but the cube.
    static readonly Vector3 StageOrigin = new(0f, -7000f, 0f);

    const int   Supersample = 2;
    const float SnapSpeed   = 9f;
    const float DragSpeed   = 0.42f;   // degrees per pixel
    const float PlateInset  = 0.501f;  // the tintable face, just clear of the cube
    const float FaceInset   = 0.508f;  // the label, just clear of the face

    // The game's own block, so the thing the screen folds into is literally the shape
    // the whole game is built out of rather than a stand-in that resembles it.
    //
    // Loaded by name from Resources: this runs in the title screen and the level
    // select, where there is no PlacementController and no block catalogue to ask.
    const string BodyAsset = "cube_be";

    // The plate has to stay inside the FLAT part of a face. cube_be is bevelled, so
    // its flat square is about 0.89 across, not 1 — a plate sized for a sharp-cornered
    // cube would ride up over the bevel and break the silhouette of the frame.
    const float PlateSize = 0.84f;

    // Looked at from a CORNER, tipped down. These two numbers decide which three
    // faces are visible, and everything below is ordered to match them.
    const float ViewPitch = 17f;
    const float ViewYaw   = -25f;

    // Face order is VIEWER ORDER, not axis order: index 0 is the face the camera sees
    // on top, 1 the one square on to it, 2 the one to its right. The settings screen
    // hands over its categories in its own order and they land on those three, so
    // AUDIO / DISPLAY / CONTROLS are all readable at once without turning anything.
    //
    // The last three are the faces you cannot see from here. They are spare slots for
    // categories that do not exist yet, and they stay blank rather than showing a
    // placeholder — the cube never advertises a tab that is not there.
    static readonly Vector3[] FaceNormal =
    {
        Vector3.up,       // 0 — the top
        Vector3.back,     // 1 — square on to the camera
        Vector3.right,    // 2 — the right-hand face
        Vector3.forward,  // 3 ┐
        Vector3.left,     // 4 │ hidden from this view
        Vector3.down,     // 5 ┘
    };

    // Which way is up on each face. The top and bottom need their own answer: "up"
    // is the face's own normal there, which says nothing about how to lay the word
    // out. They take +Z, so the word on the lid runs away from the viewer and reads
    // the right way up rather than back to front.
    static readonly Vector3[] FaceUp =
    {
        Vector3.forward, Vector3.up, Vector3.up, Vector3.up, Vector3.up, Vector3.forward,
    };

    Camera        _cam;
    Transform     _stage;
    Transform     _cube;
    RenderTexture _rt;
    RawImage      _target;
    Quaternion    _want = Quaternion.identity;
    bool          _dragging;

    readonly List<TMP_Text>  _labels = new();
    readonly List<Renderer>  _plates = new();
    string[] _names = Array.Empty<string>();

    /// <summary>Raised when the player picks a face.</summary>
    public event Action<int> FaceChosen;

    public int Current { get; private set; }

    /// <summary>
    /// Attaches a cube to `host` (a RawImage) showing `names` on its faces, at most
    /// one per face.
    /// </summary>
    public static SettingsCube Attach(RawImage host, string[] names)
    {
        if (host == null) return null;
        var c = host.gameObject.AddComponent<SettingsCube>();
        c.Init(host, names);
        return c;
    }

    void Init(RawImage host, string[] names)
    {
        _target = host;
        _names  = names ?? Array.Empty<string>();

        BuildStage();
        BuildTargetTexture();
        Select(0, instant: true);
    }

    void BuildStage()
    {
        // NOT parented to this component. This object is a RectTransform under a
        // ScreenSpaceOverlay canvas, and a canvas carries a large scale plus its own
        // placement — a 3D cube and a camera inheriting that are not a scene, they
        // are UI elements pretending to be one, at whatever size the canvas says.
        // The stage stands on its own and is torn down with us.
        //
        // It is also always ACTIVE, even when the cube is not on screen. The screen
        // wipe masks itself to this render, and it needs the picture before anything
        // has switched the cube's canvas on.
        var root = new GameObject("SettingsCubeStage").transform;
        root.position = StageOrigin;
        root.rotation = Quaternion.identity;
        root.localScale = Vector3.one;
        // Survives scene loads, because the cube does.
        //
        // Left in the loaded scene it is destroyed on the way to the title screen and
        // everything downstream fails at once: the render texture stops being drawn,
        // so the wipe masks itself to a frozen picture, and the face labels become
        // dead references that throw the moment a category is set. That is the whole
        // of the "cube transition broke after going to Title" bug.
        DontDestroyOnLoad(root.gameObject);
        _stage = root;

        // The pivot. The body hangs off this rather than BEING it, so the model can be
        // any size or shape and everything measured against the cube — plates, labels,
        // the face table — still works in clean ±0.5 units.
        _cube = new GameObject("Cube").transform;
        _cube.SetParent(root, false);
        BuildBody(_cube);

        for (int i = 0; i < FaceNormal.Length; i++)
        {
            _plates.Add(BuildFacePlate(i));
            _labels.Add(BuildFaceLabel(i < _names.Length ? _names[i] : ""));
        }

        var camGo = new GameObject("SettingsCubeCam");
        camGo.transform.SetParent(root, false);
        _cam = camGo.AddComponent<Camera>();
        _cam.clearFlags      = CameraClearFlags.SolidColor;
        // Transparent, so the paper shows through around it — and so the ALPHA of
        // this render is the cube's silhouette, which is what the screen wipe cuts
        // the captured frame to.
        _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
        _cam.orthographic     = true;
        // Seen corner-first the cube's silhouette is its diagonal, so the same box
        // needs more room than a face-on view — but not so much that it swims.
        _cam.orthographicSize = 1.12f;
        _cam.nearClipPlane    = 0.1f;
        _cam.farClipPlane     = 20f;
        _cam.depth            = -50f;    // renders before the main camera, into its RT

        // The CAMERA is what swings round to the corner, not the cube. That way the
        // cube's resting pose is plain identity, the face table above means exactly
        // what it says, and nothing downstream has to know the view is off-axis.
        var view = Quaternion.Euler(ViewPitch, ViewYaw, 0f);
        _cam.transform.localRotation = view;
        _cam.transform.localPosition = view * new Vector3(0f, 0f, -6f);
    }

    // The solid itself: the game's own block if it is there, a primitive if it is not.
    //
    // INK, not paper. The faces are the paper — separate plates laid on them — so the
    // body shows only as the frame around each one. That is the house style, and it is
    // also the only version that does not depend on the model's geometry: the twelve
    // hand-drawn edge bars this replaces had to be TOLD where the edges were, and a
    // bevelled cube's are not where a primitive's are, so they stood off the corners
    // in mid-air.
    void BuildBody(Transform pivot)
    {
        GameObject body = null;

        var src = Resources.Load<GameObject>(BodyAsset);
        if (src != null) body = Instantiate(src);
        if (body == null) body = GameObject.CreatePrimitive(PrimitiveType.Cube);

        body.name = "Body";
        var t = body.transform;
        t.SetParent(pivot, false);
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale    = Vector3.one;

        foreach (var c in body.GetComponentsInChildren<Collider>()) Destroy(c);

        FitToUnitCube(t);

        foreach (var r in body.GetComponentsInChildren<Renderer>())
            MpbColor.Set(r, GeoPalette.Ink);
    }

    // Scale and centre the body so its longest side is exactly 1 and its middle sits
    // on the pivot.
    //
    // cube_be already is that, to the millimetre. This is here for the mesh someone
    // swaps in later: a body that is silently 2.4 units across swallows every plate
    // and label whole, and the only symptom is a blank cube.
    static void FitToUnitCube(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0 || t.parent == null) return;

        var b = rs[0].bounds;                       // world space
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);

        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        if (longest < 1e-4f) return;

        // The stage and the pivot are both unrotated and unscaled, so a world
        // measurement converts to the pivot's space by plain subtraction.
        float k = 1f / longest;
        Vector3 offset = b.center - t.parent.position;

        t.localScale    = Vector3.one * k;
        t.localPosition = -offset * k;
    }

    // A tintable panel laid on each face.
    //
    // The cube is one mesh, so the selected category cannot be marked by colouring
    // "that face" of it. A separate plate a thousandth of a unit proud of the surface
    // can be, and being paper-on-paper it is invisible until it is the one that is
    // inked — which is exactly the behaviour wanted: no chrome until there is
    // something to say.
    Renderer BuildFacePlate(int i)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
        q.name = $"Plate{i}";
        q.transform.SetParent(_cube, false);
        Destroy(q.GetComponent<Collider>());

        q.transform.localPosition = FaceNormal[i] * PlateInset;
        // A Unity quad faces its own -Z, so pointing -Z outward means aiming +Z in.
        q.transform.localRotation = Quaternion.LookRotation(-FaceNormal[i], FaceUp[i]);
        q.transform.localScale    = Vector3.one * PlateSize;

        var r = q.GetComponent<Renderer>();
        MpbColor.Set(r, GeoPalette.Paper);
        return r;
    }

    TMP_Text BuildFaceLabel(string text)
    {
        int i = _labels.Count;

        var holder = new GameObject($"Face{i}", typeof(Canvas));
        holder.transform.SetParent(_cube, false);
        holder.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;

        var rt = (RectTransform)holder.transform;
        rt.sizeDelta      = new Vector2(200f, 100f);
        rt.localScale     = Vector3.one * 0.0044f;
        rt.localPosition  = FaceNormal[i] * FaceInset;
        // Turned to lie ON its face, read from outside.
        rt.localRotation  = Quaternion.LookRotation(-FaceNormal[i], FaceUp[i]);

        var t = new GameObject("T", typeof(RectTransform)).AddComponent<TextMeshProUGUI>();
        t.transform.SetParent(holder.transform, false);
        var trt = t.rectTransform;
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;

        t.fontSize      = 30f;
        t.alignment     = TextAlignmentOptions.Center;
        t.fontStyle     = FontStyles.Bold;
        t.color         = GeoPalette.Ink;
        t.raycastTarget = false;
        t.text          = text;

        holder.SetActive(!string.IsNullOrEmpty(text));
        return t;
    }

    void BuildTargetTexture()
    {
        var rect = _target.rectTransform.rect;
        int w = Mathf.Max(64, Mathf.RoundToInt(rect.width))  * Supersample;
        int h = Mathf.Max(64, Mathf.RoundToInt(rect.height)) * Supersample;

        _rt = new RenderTexture(w, h, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = 1,               // see the class note — never raise this
            filterMode   = FilterMode.Bilinear,
            // Clamped, not repeated. The wipe samples this well outside 0..1 while
            // the silhouette is bigger than the screen, and a repeating texture would
            // tile copies of the cube across the whole frame.
            wrapMode     = TextureWrapMode.Clamp,
        };
        _cam.targetTexture = _rt;
        _target.texture    = _rt;
        _target.color      = Color.white;
    }

    // ── Selection ────────────────────────────────────────────────────────────

    /// <summary>
    /// Pick a category. The cube does NOT turn to face it — all three are visible at
    /// once, so turning would only hide two of them to point at the third. The
    /// selection is shown by inking that face.
    /// </summary>
    public void Select(int face, bool instant = false)
    {
        if (face < 0 || face >= FaceNormal.Length) return;
        Current = face;
        _want   = Quaternion.identity;
        if (instant && _cube != null) _cube.localRotation = _want;
        PaintFaces();
        FaceChosen?.Invoke(face);
    }

    void PaintFaces()
    {
        for (int i = 0; i < _plates.Count; i++)
        {
            bool on = i == Current && !_idle && i < _names.Length && !string.IsNullOrEmpty(_names[i]);
            if (_plates[i] != null) MpbColor.Set(_plates[i], on ? GeoPalette.Ink : GeoPalette.Paper);
            if (_labels[i] != null) _labels[i].color = on ? GeoPalette.Paper : GeoPalette.Ink;
        }
    }

    /// <summary>
    /// Turn slowly on its own and ignore input — for the cube that is standing in for
    /// the game rather than acting as a control.
    /// </summary>
    public SettingsCube SetIdleSpin(bool on)
    {
        _idle = on;
        PaintFaces();
        return this;
    }

    bool _idle;

    /// <summary>
    /// Hand the cube its categories and wake it up as a control — or strip them and
    /// put it back to sleep as scenery.
    ///
    /// One call rather than three setters, because "spinning silhouette" and
    /// "category picker" are not independent properties that happen to be set
    /// together; they are two states of one object. Splitting them is how you end up
    /// with a cube that is labelled and inert, or bare and clickable.
    /// </summary>
    public void SetCategories(string[] names, bool interactive)
    {
        _names = names ?? Array.Empty<string>();

        for (int i = 0; i < _labels.Count; i++)
        {
            string text = i < _names.Length ? _names[i] : "";
            _labels[i].text = text;
            // The label's own parent is the world-space Canvas built for that face.
            var holder = _labels[i].transform.parent;
            if (holder != null) holder.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        _idle = !interactive;
        if (_target != null)
        {
            // Scenery must not eat clicks. A full-size invisible raycast target
            // sitting in the middle of the pause menu is exactly the sort of thing
            // that swallows a button press with nothing on screen to blame.
            _target.raycastTarget = interactive;

            // And scenery is not DRAWN at all.
            //
            // In the pause menu the cube is the hole the game is still showing
            // through — the wipe has cut the frozen frame to this very silhouette and
            // is drawing it underneath. Painting the solid over the top would replace
            // the picture with a blank white box, which is precisely the thing the
            // fold was supposed to avoid. The render texture keeps being drawn either
            // way, because the camera is not on this object; only the RawImage stops.
            _target.enabled = interactive;
        }

        if (interactive) Select(Mathf.Clamp(Current, 0, Mathf.Max(0, _names.Length - 1)));
        else             PaintFaces();
    }

    // ── The shared cube ──────────────────────────────────────────────────────
    //
    // It lives on a canvas of its own rather than inside either screen's, because it
    // belongs to neither and has to draw above both: the settings page is an opaque
    // sheet at 850, and a cube parented under the pause menu (800) would slide
    // straight underneath it half-way through the move.

    /// <summary>The one cube, or null before anything has asked for it.</summary>
    public static SettingsCube Shared { get; private set; }

    const float SlideSpeed   = 9f;     // house easing rate, same family as SnapSpeed
    const float ShellBuildPx = 420f;   // the RT is cut from this, once

    static Canvas        _shellCanvas;
    static RectTransform _shellRect;
    static Vector2       _slotPos;
    static float         _slotSize = 240f;
    static bool          _slotInteractive;
    static string[]      _slotNames;
    static int           _requestFrame = -99;
    static bool          _wasVisible;

    public static SettingsCube EnsureShared()
    {
        if (Shared != null) return Shared;

        var go = new GameObject("SharedSettingsCube",
                                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        DontDestroyOnLoad(go);

        _shellCanvas = go.GetComponent<Canvas>();
        _shellCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        // Above the settings sheet (850) and the pause menu (800), below the level
        // clear screen (900) and the intro (1000).
        _shellCanvas.sortingOrder = 860;
        _shellCanvas.enabled      = false;

        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(RefW, RefH);
        sc.matchWidthOrHeight  = 0.5f;

        var rt = new GameObject("Cube", typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(go.transform, false);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        // Sized to the LARGEST slot it will ever occupy before Attach runs, because
        // Attach cuts the RenderTexture from this rect once and never again. Shrinking
        // the rect afterwards just filters the same texture down, which is sharp;
        // growing past it would not be.
        rt.sizeDelta = new Vector2(ShellBuildPx, ShellBuildPx);
        _shellRect = rt;

        var img = rt.gameObject.AddComponent<RawImage>();
        Shared = Attach(img, Array.Empty<string>());
        Shared?.SetCategories(Array.Empty<string>(), false);
        return Shared;
    }

    /// <summary>
    /// Ask for the cube: put it here, this big, showing these categories (null or
    /// empty = scenery, no labels, no input).
    ///
    /// Called EVERY FRAME by whoever currently wants it. A one-shot "move there" would
    /// leave it parked on a page after that page was gone, because nothing would ever
    /// tell it otherwise; asking continuously means it disappears the moment nobody is
    /// asking, which is what every caller actually wants.
    /// </summary>
    public static void ShowAt(Vector2 anchoredPos, float size, string[] categories)
    {
        var c = EnsureShared();
        if (c == null) return;

        _requestFrame = Time.frameCount;
        _slotPos  = anchoredPos;
        _slotSize = size;

        bool interactive = categories != null && categories.Length > 0;
        if (interactive != _slotInteractive || !ReferenceEquals(categories, _slotNames))
        {
            _slotInteractive = interactive;
            _slotNames       = categories;
            c.SetCategories(categories, interactive);
        }
    }

    /// <summary>
    /// Say where the cube WILL go, without asking for it to be shown yet.
    ///
    /// The screen wipe needs the destination before the menu it belongs to exists: it
    /// spends the whole fold shrinking the silhouette down onto a slot that nothing
    /// has occupied so far. Without this it would have to guess, and a guess that is
    /// even slightly off shows up as the solid cube jumping the instant it appears.
    /// </summary>
    public static void PrepareSlot(Vector2 anchoredPos, float size)
    {
        if (EnsureShared() == null) return;
        _slotPos  = anchoredPos;
        _slotSize = size;
    }

    /// <summary>
    /// The cube's own render: paper faces, ink edges, transparent everywhere else.
    /// Its ALPHA is the cube's silhouette — which is what the screen wipe cuts the
    /// captured frame to, so that the shape the game folds into is this cube and not
    /// some other shape that merely resembles it.
    /// </summary>
    public static Texture SilhouetteTexture => Shared != null ? Shared._rt : null;

    /// <summary>
    /// Where the cube is on screen, in PIXELS: the centre, and the side of the square
    /// its render is drawn in (the silhouette sits inside that square, the same way
    /// it sits inside the texture).
    ///
    /// Reports the LIVE rect while the cube is up and the destination while it is
    /// not. That single rule is what makes the wipe correct in both halves of its
    /// life: during the fold there is no cube yet so the target is all there is, and
    /// once there is one the window has to travel with it — otherwise walking to the
    /// settings page would leave a hole in the paper where the cube used to be.
    /// </summary>
    public static bool TryCubeScreenRect(out Vector2 centrePx, out float sizePx)
    {
        centrePx = default; sizePx = 0f;
        if (Shared == null || _shellRect == null) return false;

        bool live = _shellCanvas != null && _shellCanvas.enabled;
        Vector2 pos  = live ? _shellRect.anchoredPosition : _slotPos;
        float   side = live ? _shellRect.sizeDelta.x      : _slotSize;

        float s = ShellScale();
        centrePx = new Vector2(Screen.width, Screen.height) * 0.5f + pos * s;
        sizePx   = side * s;
        return true;
    }

    const float RefW = 1920f, RefH = 1080f;

    // The CanvasScaler's own arithmetic, worked out rather than read off it.
    //
    // Canvas.scaleFactor is only right once the scaler has run, and the wipe asks for
    // this on the very frame the menu opens — one frame too early, when the answer
    // would still be 1 and the silhouette would land at the wrong size.
    static float ShellScale()
    {
        float lw = Mathf.Log(Mathf.Max(1f, Screen.width)  / RefW, 2f);
        float lh = Mathf.Log(Mathf.Max(1f, Screen.height) / RefH, 2f);
        return Mathf.Pow(2f, Mathf.Lerp(lw, lh, 0.5f));
    }

    // Moves the shell toward whatever slot was last asked for, and hides it when the
    // asking stops.
    //
    // Eased rather than timed. A fixed-duration tween has to be cancelled and
    // restarted whenever the target changes mid-flight, and this target changes
    // constantly — settings is one click away from the menu and one click back.
    void DriveShell()
    {
        // One frame of slack: this component's Update may run before or after the
        // screen that asks, and the order between two MonoBehaviours is not defined.
        bool visible = Time.frameCount - _requestFrame <= 1;

        if (_shellCanvas.enabled != visible) _shellCanvas.enabled = visible;
        if (!visible) { _wasVisible = false; return; }

        if (!_wasVisible)
        {
            // First frame back on screen: SNAP. Sliding in from wherever it was left
            // last time would animate a journey the player never saw the start of —
            // and here it would also break the handoff from the wipe, which has just
            // spent half a second shrinking the silhouette onto this exact rect.
            _wasVisible = true;
            _shellRect.anchoredPosition = _slotPos;
            _shellRect.sizeDelta        = Vector2.one * _slotSize;
            return;
        }

        float k = 1f - Mathf.Exp(-SlideSpeed * Time.unscaledDeltaTime);
        _shellRect.anchoredPosition = Vector2.Lerp(_shellRect.anchoredPosition, _slotPos, k);
        _shellRect.sizeDelta        = Vector2.Lerp(_shellRect.sizeDelta, Vector2.one * _slotSize, k);
    }

    void Update()
    {
        if (this == Shared && _shellRect != null) DriveShell();

        if (_cube == null) return;

        if (_idle)
        {
            // Two axes at unrelated rates, so it never settles into a loop the eye can
            // predict — a decorative spin that repeats reads as a screensaver.
            _cube.localRotation *= Quaternion.Euler(11f * Time.unscaledDeltaTime,
                                                    17f * Time.unscaledDeltaTime, 0f);
            return;
        }

        // Eased back toward the resting pose the whole time, including mid-drag, so
        // letting go of a drag has nothing to hand over and the cube never jumps at
        // the moment the player releases it.
        if (!_dragging)
            _cube.localRotation = Quaternion.Slerp(_cube.localRotation, _want,
                                                   1f - Mathf.Exp(-SnapSpeed * Time.unscaledDeltaTime));
    }

    // ── Input ────────────────────────────────────────────────────────────────

    public void OnBeginDrag(PointerEventData e) { if (!_idle) _dragging = true; }

    public void OnDrag(PointerEventData e)
    {
        if (_idle) return;
        // Turned about the WORLD axes, not the cube's own. Spinning about local axes
        // compounds: after two drags in different directions the cube is at some
        // orientation the player cannot undo by dragging back.
        _cube.localRotation = Quaternion.AngleAxis(-e.delta.x * DragSpeed, Vector3.up)
                            * Quaternion.AngleAxis( e.delta.y * DragSpeed, Vector3.right)
                            * _cube.localRotation;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (_idle) return;

        if (_dragging)
        {
            // A drag ends with a click event too. It is a look, not a choice: let go
            // and the cube springs back to its pose with the same face still picked.
            // Selecting whatever happened to be facing front on release would change
            // the player's category every time they turned the thing to look at it.
            _dragging = false;
            return;
        }

        int face = FaceAtPointer(e);
        if (face >= 0) Select(face);
        else           Select(NextPopulated(Current));
    }

    // Which face was clicked.
    //
    // Nearest projected face CENTRE rather than a ray cast into the mesh. A cube seen
    // corner-on projects to a hexagon made of three identical rhombi, one per visible
    // face, and the nearest-centre test partitions that hexagon along exactly the
    // seams the player can see. It also needs no collider, no physics layer and no
    // ray into a scene parked seven thousand units below the world.
    int FaceAtPointer(PointerEventData e)
    {
        if (_cam == null || _target == null) return -1;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _target.rectTransform, e.position, e.pressEventCamera, out var local))
            return -1;

        var r = _target.rectTransform.rect;
        if (r.width <= 0f || r.height <= 0f) return -1;
        var vp = new Vector2((local.x - r.xMin) / r.width, (local.y - r.yMin) / r.height);

        Vector3 toCam = -_cam.transform.forward;
        int   best = -1;
        float bestD = float.MaxValue;

        for (int i = 0; i < FaceNormal.Length; i++)
        {
            if (i >= _names.Length || string.IsNullOrEmpty(_names[i])) continue;
            // Facing away — its centre still projects somewhere sensible, so without
            // this the back of the cube would be clickable through the front.
            if (Vector3.Dot(_cube.rotation * FaceNormal[i], toCam) <= 0.05f) continue;

            Vector3 v = _cam.WorldToViewportPoint(_cube.TransformPoint(FaceNormal[i] * 0.5f));
            float d = ((Vector2)v - vp).sqrMagnitude;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    int NextPopulated(int from)
    {
        for (int step = 1; step <= FaceNormal.Length; step++)
        {
            int i = (from + step) % FaceNormal.Length;
            if (i < _names.Length && !string.IsNullOrEmpty(_names[i])) return i;
        }
        return from;
    }

    void OnDestroy()
    {
        if (this == Shared) { Shared = null; _shellCanvas = null; _shellRect = null; _wasVisible = false; }
        if (_cam != null) _cam.targetTexture = null;
        if (_rt  != null) { _rt.Release(); Destroy(_rt); }
        // The stage is not our child, so it has to be cleaned up by hand.
        if (_stage != null) Destroy(_stage.gameObject);
    }
}
