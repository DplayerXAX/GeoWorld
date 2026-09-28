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

    // The label, clear of the OUTER SURFACE of that plate — not of its centre.
    //
    // This was 0.508, which was right while the plates were zero-thickness quads. Once
    // they became 0.06-thick slabs their outer face moved to 0.531 and every category
    // name ended up sealed inside its own plate: the settings cube came up blank, with
    // nothing in the scene to suggest why.
    const float FaceInset   = 0.548f;

    // The game's own block, so the thing the screen folds into is literally the shape
    // the whole game is built out of rather than a stand-in that resembles it.
    //
    // Loaded by name from Resources: this runs in the title screen and the level
    // select, where there is no PlacementController and no block catalogue to ask.
    const string BodyAsset = "cube_be";

    // The plate has to stay inside the FLAT part of a face. cube_be is bevelled, so
    // its flat square is about 0.89 across, not 1 — a plate sized for a sharp-cornered
    // cube would ride up over the bevel and break the silhouette of the frame.
    const float PlateSize  = 0.84f;
    const float PlateThick = 0.06f;   // a slab, not a decal: you can see its edge

    // ── Coming apart ────────────────────────────────────────────────────────
    //
    // In the pause menu the cube opens: the six faces travel out along their own
    // normals and the menu's six options ride on them. That is what stops the menu
    // being a ring of words with an ornament in the middle — the options ARE the
    // solid, taken to pieces.
    //
    // The face plates are what move, not the body. The body is one mesh and cannot be
    // separated; the plates were already there to mark the selected category, and a
    // plate travelling out along its face's normal is exactly the exploded view an
    // assembly drawing uses.
    // Reach. Worked back from where the WORDS land, not from where the plates do: at
    // the widest, the top plate sits ~307px above the middle and CLOSE stands another
    // ~160px past it, which is 467 of the 540 a 16:9 canvas has. Any further and the
    // biggest option starts leaving the screen.
    // Isometric foreshortens every axis to 0.816, so the same 3D travel buys less
    // screen radius than the old off-axis view did; this makes it back up.
    const float ExplodeSpread = 2.20f;   // travel at full open, in cube units
    const float ExplodeZoom   = 3.6f;    // how much wider the view goes to fit it
    const float ExplodeSpeed  = 3.4f;
    const float PulledExtra   = 0.30f;   // the focused plate comes a little further
    // Small on purpose, and this is the reason: the words are pinned to the
    // backdrop's sector centres, while the plates are carried round by the lean. Every
    // degree of lean is a degree the plates sit off the wedge they belong to. Seven
    // was enough to see; two and a half still answers the pointer and keeps the
    // assembly inside its own sectors.
    const float ParallaxTilt  = 2.5f;    // degrees of lean toward the pointer
    const float ParallaxSpeed = 5f;
    const float BaseOrtho     = 1.12f;

    // The line holding each plate to the middle. It starts ON the cube's face rather
    // than clear of it, so the line reads as coming OUT of the solid rather than
    // floating near it.
    const float LinkStart = 0.5f;
    const float LinkWidth = 0.022f;

    // The burst's own stage, parked clear of the cube's so neither camera can see the
    // other's contents. Layers would do the same job and cost the project a layer for
    // a menu; two origins cost nothing.
    static readonly Vector3 BurstOrigin = new(0f, -7600f, 0f);
    const int BurstRtPx = 1800;   // already supersampled — the field is ~1260px on screen

    // UNIFORM, and it can be, because the view is isometric: all three axes project
    // at the same rate, so equal travel in the cube is equal travel on the screen. The
    // hand-tuned per-face compensation this replaces was correcting for a view that no
    // longer exists.
    //
    // The unevenness worth keeping lives in the DELAYS: the six still leave at their
    // own moments, so the burst reads as a sequence rather than a switch, and they
    // still arrive as a set, on one ring.
    static readonly float[] FaceDrift = { 1f, 1f, 1f, 1f, 1f, 1f };
    static readonly float[] FaceDelay = { 0.00f, 0.22f, 0.10f, 0.34f, 0.16f, 0.28f };

    const float BurstFadeSpeed = 11f;

    bool  _wantExplode;
    float _explode;          // 0 whole, 1 apart
    float _burstFade;        // the plates' own opacity, on a much shorter clock
    int   _pulled = -1;      // the face under the pointer, or -1

    // ISOMETRIC. Not "roughly corner-on" — the exact pose, and the exactness is the
    // whole point.
    //
    // Equal foreshortening on all three axes happens at yaw ±45° and pitch
    // asin(1/√3) = 35.264°, and nowhere else. Everything downstream falls out of it:
    //
    //  · the six face normals project 60° apart, at 90 / ±30 / ±150 / −90 — which are
    //    exactly the centres of the backdrop's six sectors, so every plate sits in the
    //    middle of its own wedge instead of straddling a boundary;
    //  · the silhouette is a REGULAR hexagon, so it agrees with the hexagonal distance
    //    the field is drawn from;
    //  · all six project at the same rate, so one drift puts all six plates on one
    //    ring with no per-face compensation to keep in step.
    //
    // At 17°/−25° none of that was true: the bearings came out 40°, 58° and 82° apart
    // and two plates landed on sector boundaries, which is what read as the sectors
    // being slightly out.
    const float ViewPitch = 35.264f;
    const float ViewYaw   = -45f;

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
    readonly List<Transform> _plateT = new();

    // One material for every plate and rim, with a MaterialPropertyBlock giving each
    // its own colour, mark and density. Shared rather than per-plate because twelve
    // material instances for twelve quads is twelve things to leak.
    static Material _plateMat;

    static Material PlateMaterial()
    {
        if (_plateMat != null) return _plateMat;
        var sh = Shader.Find("GeoWorld/PlatePaper");
        if (sh == null)
        {
            Debug.LogWarning("[SettingsCube] GeoWorld/PlatePaper missing — plates will be plain.");
            return null;
        }
        _plateMat = new Material(sh) { name = "PlatePaper" };
        return _plateMat;
    }

    // A very short ramp between the paper and one warm deep tone.
    //
    // Six plates, six steps. They are told apart by VALUE — the way this game's
    // backdrops tell anything apart — rather than by six different patterns or six
    // outlines. The whole ramp spans about a tenth of the paper's brightness, which
    // is all it takes: on a flat sheet a 3% step is a visible edge.
    static readonly Color[] PlateTone =
    {
        new(0.949f, 0.937f, 0.902f),
        new(0.928f, 0.913f, 0.874f),
        new(0.906f, 0.888f, 0.844f),
        new(0.884f, 0.864f, 0.818f),
        new(0.864f, 0.842f, 0.792f),
        new(0.845f, 0.820f, 0.767f),
    };

    // The band at the plate's own edge, and the slab behind it. Both are further
    // steps of the same ramp, not ink: the rim still reads as a border, but as the
    // shadowed side of a piece of card rather than as a drawn line.
    static readonly Color PlateEdge = new(0.800f, 0.770f, 0.710f);
    static readonly Color PlateRim  = new(0.762f, 0.728f, 0.662f);
    static readonly Color LinkTone  = new(0.660f, 0.625f, 0.556f);

    void DressPlate(Renderer r, int i, bool rim)
    {
        if (r == null) return;

        var mat = PlateMaterial();
        if (mat != null) r.sharedMaterial = mat;

        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(_EdgeToneId, PlateEdge);
        // The rim gets no edge band of its own. It IS the edge; shading it again at
        // its own border would be a second boundary a few pixels from the first.
        block.SetFloat(_EdgeDepthId, rim ? 0f : 0.55f);
        r.SetPropertyBlock(block);

        MpbColor.Set(r, rim ? PlateRim : PlateTone[Mathf.Clamp(i, 0, PlateTone.Length - 1)]);
    }

    static readonly int _EdgeToneId  = Shader.PropertyToID("_EdgeTone");
    static readonly int _EdgeDepthId = Shader.PropertyToID("_EdgeDepth");

    // ── the burst ───────────────────────────────────────────────────────────
    Transform     _burstStage, _burstCube;
    Camera        _burstCam;
    RenderTexture _burstRt;
    readonly List<Transform> _burstPlates = new();
    readonly List<Transform> _burstLines  = new();

    // Where each face WOULD be with nothing hovered.
    //
    // The menu hangs its words off these rather than off the plates themselves, and
    // that is not a detail — it is the fix for a real bug. The focused plate travels
    // an extra PulledExtra; if the word followed it, the word slid out from under the
    // pointer, the hover dropped, the plate came back, the pointer was over it again,
    // and the option shivered back and forth for as long as you held still near its
    // edge. A hover response must never move the thing you are hovering.
    readonly float[] _faceRest = new float[6];
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
        _cam.orthographicSize = BaseOrtho;
        _cam.nearClipPlane    = 0.1f;
        _cam.farClipPlane     = 20f;
        _cam.depth            = -50f;    // renders before the main camera, into its RT

        // The CAMERA is what swings round to the corner, not the cube. That way the
        // cube's resting pose is plain identity, the face table above means exactly
        // what it says, and nothing downstream has to know the view is off-axis.
        var view = Quaternion.Euler(ViewPitch, ViewYaw, 0f);
        _cam.transform.localRotation = view;
        _cam.transform.localPosition = view * new Vector3(0f, 0f, -6f);

        BuildBurstStage();
    }

    // The burst: six plates and the lines that hold them to the middle.
    //
    // A SECOND stage with its own camera, rather than more objects on the one we
    // already have. The core and the plates are drawn in completely different ways —
    // the core is a HOLE in the paper with the frozen game showing through it, the
    // plates are solid ink-and-paper laid ON the paper — and one photograph cannot be
    // both. Two stages at two origins is the cheapest way to get two photographs;
    // splitting by culling layer would spend one of the project's layers on a menu.
    //
    // The two register exactly because they share a pose, and because the field's
    // camera widens by exactly the factor the field's rect grows by. A plate one cube
    // unit from the middle lands the same distance from the core in pixels at every
    // stage of the burst.
    void BuildBurstStage()
    {
        var root = new GameObject("SettingsCubeBurstStage").transform;
        root.position   = BurstOrigin;
        root.rotation   = Quaternion.identity;
        root.localScale = Vector3.one;
        DontDestroyOnLoad(root.gameObject);
        _burstStage = root;

        _burstCube = new GameObject("Assembly").transform;
        _burstCube.SetParent(root, false);

        for (int i = 0; i < FaceNormal.Length; i++)
        {
            _burstLines.Add(BuildBurstLink(i));
            _burstPlates.Add(BuildBurstPlate(i));
        }

        BuildDepthProxy();

        var camGo = new GameObject("BurstCam");
        camGo.transform.SetParent(root, false);
        _burstCam = camGo.AddComponent<Camera>();
        _burstCam.clearFlags       = CameraClearFlags.SolidColor;
        _burstCam.backgroundColor  = new Color(0f, 0f, 0f, 0f);
        _burstCam.orthographic     = true;
        _burstCam.orthographicSize = BaseOrtho;
        _burstCam.nearClipPlane    = 0.1f;
        _burstCam.farClipPlane     = 60f;
        _burstCam.depth            = -49f;

        var bview = Quaternion.Euler(ViewPitch, ViewYaw, 0f);
        _burstCam.transform.localRotation = bview;
        _burstCam.transform.localPosition = bview * new Vector3(0f, 0f, -20f);

        _burstRt = new RenderTexture(BurstRtPx, BurstRtPx, 16,
                                     RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
        {
            antiAliasing = 1,               // see the class note — never raise this
            filterMode   = FilterMode.Bilinear,
            wrapMode     = TextureWrapMode.Clamp,
        };
        _burstCam.targetTexture = _burstRt;

        if (_burstImage != null)
        {
            _burstImage.texture = _burstRt;
            _burstImage.color   = Color.white;
        }
    }

    // A copy of the cube standing in the plates' stage, writing depth and no colour.
    //
    // Without it the two stages know nothing about each other and every line is drawn
    // over the cube, including the ones running to the far side of it — the assembly
    // reads as flat. With it, the far lines and plates are depth-rejected and the
    // thing has a front and a back.
    //
    // It paints nothing, so the picture stays empty where the cube is and the real
    // cube — photographed by the other camera, showing the frozen game — comes
    // through from underneath.
    void BuildDepthProxy()
    {
        var sh = Shader.Find("GeoWorld/DepthMask");
        if (sh == null)
        {
            Debug.LogWarning("[SettingsCube] GeoWorld/DepthMask missing — the burst's lines will not be occluded.");
            return;
        }

        BuildBody(_burstCube);
        var proxy = _burstCube.Find("Body");
        if (proxy == null) return;
        proxy.name = "DepthProxy";

        var mat = new Material(sh) { name = "DepthMask" };
        foreach (var r in proxy.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    Transform BuildBurstPlate(int i)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
        q.name = $"BurstPlate{i}";
        q.transform.SetParent(_burstCube, false);
        Destroy(q.GetComponent<Collider>());

        q.transform.localPosition = FaceNormal[i] * PlateInset;
        q.transform.localRotation = Quaternion.LookRotation(-FaceNormal[i], FaceUp[i]);
        q.transform.localScale    = new Vector3(PlateSize, PlateSize, PlateThick);
        DressPlate(q.GetComponent<Renderer>(), i, rim: false);

        var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rim.name = "Rim";
        rim.transform.SetParent(q.transform, false);
        Destroy(rim.GetComponent<Collider>());
        rim.transform.localScale = new Vector3(1.10f, 1.10f, 0.94f);
        DressPlate(rim.GetComponent<Renderer>(), i, rim: true);

        return q.transform;
    }

    // The line from the middle out to a plate. Sized and placed each frame — it is the
    // gap between the two, so it cannot be authored once.
    Transform BuildBurstLink(int i)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = $"Link{i}";
        bar.transform.SetParent(_burstCube, false);
        Destroy(bar.GetComponent<Collider>());
        // A cube's local +Z is its length, so aim +Z along the face's normal.
        bar.transform.localRotation = Quaternion.LookRotation(FaceNormal[i], FaceUp[i]);

        // A deep step of the plates' own ramp rather than ink. It still reads as the
        // thing holding the plate to the middle, but at the weight of a shadow instead
        // of the weight of a drawn rule. Same unlit material as the plates, so it does
        // not change brightness with whatever scene is underneath.
        var lr = bar.GetComponent<Renderer>();
        var lmat = PlateMaterial();
        if (lmat != null) lr.sharedMaterial = lmat;
        var lblock = new MaterialPropertyBlock();
        lr.GetPropertyBlock(lblock);
        lblock.SetFloat(_EdgeDepthId, 0f);
        lr.SetPropertyBlock(lblock);
        MpbColor.Set(lr, LinkTone);

        bar.SetActive(false);
        return bar.transform;
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
        // A SLAB, not a decal. A zero-thickness quad flying away from a cube reads as
        // clip art; a plate with an edge you can see reads as a part that was prised
        // off something.
        var q = GameObject.CreatePrimitive(PrimitiveType.Cube);
        q.name = $"Plate{i}";
        q.transform.SetParent(_cube, false);
        Destroy(q.GetComponent<Collider>());

        q.transform.localPosition = FaceNormal[i] * PlateInset;
        // Local -Z points outward, so the thin axis is Z and FaceUp orients the word.
        q.transform.localRotation = Quaternion.LookRotation(-FaceNormal[i], FaceUp[i]);
        q.transform.localScale    = new Vector3(PlateSize, PlateSize, PlateThick);

        var r = q.GetComponent<Renderer>();
        DressPlate(r, i, rim: false);

        // An ink backing a shade larger, so the paper face carries a rule round it and
        // its edge is inked too — the house style, and the only way a pale plate on
        // pale paper has an outline at all.
        var rim = GameObject.CreatePrimitive(PrimitiveType.Cube);
        rim.name = "Rim";
        rim.transform.SetParent(q.transform, false);
        Destroy(rim.GetComponent<Collider>());
        rim.transform.localPosition = Vector3.zero;
        rim.transform.localRotation = Quaternion.identity;
        rim.transform.localScale    = new Vector3(1.10f, 1.10f, 0.94f);
        DressPlate(rim.GetComponent<Renderer>(), i, rim: true);

        _plateT.Add(q.transform);
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
            // Unselected goes back to its OWN step of the ramp, not to flat paper —
            // otherwise picking a category quietly wipes the tone that told the six
            // plates apart in the first place. Selected stays ink: a filled face is a
            // flat field, which is in keeping, and it is the one place on this screen
            // that has to be unmissable.
            if (_plates[i] != null)
                MpbColor.Set(_plates[i], on ? GeoPalette.Ink
                                            : PlateTone[Mathf.Clamp(i, 0, PlateTone.Length - 1)]);
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
    // The CORE's texture is cut from this once. The core never grows — only the field
    // around it does — so this only has to cover the largest slot the cube itself is
    // ever drawn in, which is the pause menu's 300px.
    const float ShellBuildPx = 420f;

    static Canvas        _shellCanvas;
    static RectTransform _shellRect;   // the field
    static RectTransform _coreRect;    // the cube inside it
    static RawImage      _burstImage;
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

        // THE FIELD — the whole area the opened cube spreads across. It carries the
        // burst, and it is what grows when the cube comes apart.
        var field = new GameObject("Field", typeof(RectTransform)).GetComponent<RectTransform>();
        field.SetParent(go.transform, false);
        field.anchorMin = field.anchorMax = field.pivot = new Vector2(0.5f, 0.5f);
        field.sizeDelta = new Vector2(ShellBuildPx * ExplodeZoom, ShellBuildPx * ExplodeZoom);
        _shellRect = field;

        var burst = new GameObject("Burst", typeof(RectTransform)).GetComponent<RectTransform>();
        burst.SetParent(field, false);
        burst.anchorMin = Vector2.zero; burst.anchorMax = Vector2.one;
        burst.offsetMin = burst.offsetMax = Vector2.zero;
        _burstImage = burst.gameObject.AddComponent<RawImage>();
        _burstImage.raycastTarget = false;
        _burstImage.enabled = false;

        // THE CORE — the cube itself, at a size that never changes however far the
        // field spreads. This is the one the screen folds into and the one the
        // settings page turns.
        //
        // Sized to the largest slot it will ever occupy BEFORE Attach runs, because
        // Attach cuts its RenderTexture from this rect once and never again. Shrinking
        // the rect afterwards just filters the same texture down, which is sharp;
        // growing past it would not be.
        var core = new GameObject("Core", typeof(RectTransform)).GetComponent<RectTransform>();
        core.SetParent(field, false);
        core.anchorMin = core.anchorMax = core.pivot = new Vector2(0.5f, 0.5f);
        core.sizeDelta = new Vector2(ShellBuildPx, ShellBuildPx);
        _coreRect = core;

        var img = core.gameObject.AddComponent<RawImage>();
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
    public static void ShowAt(Vector2 anchoredPos, float size, string[] categories,
                             bool explode = false)
    {
        var c = EnsureShared();
        if (c == null) return;

        _requestFrame = Time.frameCount;
        _slotPos  = anchoredPos;
        _slotSize = size;

        bool interactive = categories != null && categories.Length > 0;

        // Never both at once: apart, the faces are pictures with the menu's words
        // beside them; together, they are the settings page's category picker. A cube
        // that is a control AND in pieces is neither.
        c._wantExplode = explode && !interactive;
        if (!c._wantExplode) c._pulled = -1;
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

        // The CORE's rect, not the field's. The cube is what the screen folds into and
        // what the frozen frame is cut to; the field is just the room the plates fly
        // about in, and masking the frame to that would open a window three times too
        // big with the plates painted over most of it.
        bool live = _shellCanvas != null && _shellCanvas.enabled && _coreRect != null;
        Vector2 pos  = live ? _shellRect.anchoredPosition : _slotPos;
        float   side = live ? _coreRect.sizeDelta.x       : _slotSize;

        float s = ShellScale();
        centrePx = new Vector2(Screen.width, Screen.height) * 0.5f + pos * s;
        sizePx   = side * s;
        return true;
    }

    /// <summary>
    /// Where face `face` is on the canvas, in the same coordinates the pause menu
    /// places its own children in.
    ///
    /// This is what lets the menu's words RIDE the faces instead of being laid out in
    /// a ring beside them. The words stay flat 2D type — crisp, in the game's own
    /// face, at full UI resolution — while their POSITIONS come from a solid turning
    /// in 3D. Drawing them into the cube's render texture instead would make them
    /// small, soft and unreadable, and would put them behind the frozen frame the
    /// wipe cuts to this same silhouette.
    /// </summary>
    public static bool TryProjectFace(int face, out Vector2 anchoredPos)
    {
        anchoredPos = default;
        var c = Shared;
        if (c == null || c._burstCam == null || c._burstCube == null || _shellRect == null) return false;
        if (face < 0 || face >= c._faceRest.Length) return false;
        // The RESTING position, not the plate's. See _faceRest.
        return c.Project(c._burstCube.TransformPoint(FaceNormal[face] * c._faceRest[face]), out anchoredPos);
    }

    /// <summary>Where the middle of the assembly is, for measuring outward from.</summary>
    public static bool TryProjectCentre(out Vector2 anchoredPos)
    {
        anchoredPos = _shellRect != null ? _shellRect.anchoredPosition : default;
        return Shared != null && _shellRect != null;
    }

    bool Project(Vector3 world, out Vector2 anchoredPos)
    {
        Vector3 vp = _burstCam.WorldToViewportPoint(world);
        Vector2 size = _shellRect.sizeDelta;
        anchoredPos = _shellRect.anchoredPosition
                    + new Vector2((vp.x - 0.5f) * size.x, (vp.y - 0.5f) * size.y);
        return true;
    }

    // Pixels per cube unit. Constant through the whole burst by construction: the
    // field's rect and the field's camera widen by exactly the same factor.
    static float PxPerUnit => _slotSize / (2f * BaseOrtho);

    /// <summary>
    /// Half a plate's reach on screen, in canvas pixels — what a label has to clear to
    /// stand beside one rather than on it.
    ///
    /// Measured to the RIM's CORNER, which is the furthest any part of a plate gets
    /// from its middle. Taking the plate's centre distance alone, as the menu did, put
    /// the words forty pixels past the middle of a plate that is a hundred and seventy
    /// across — which is to say, on top of it.
    /// </summary>
    public static float PlateHalfPx => PlateSize * 0.5f * 1.10f * 1.4142f * PxPerUnit;

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
        if (!visible)
        {
            // Off screen, it closes back up. Otherwise the next open would find it
            // already in pieces and the burst — the whole point of the gesture — would
            // simply not happen.
            _wasVisible  = false;
            _wantExplode = false;
            return;
        }

        if (!_wasVisible)
        {
            // First frame back on screen: SNAP. Sliding in from wherever it was left
            // last time would animate a journey the player never saw the start of —
            // and here it would also break the handoff from the wipe, which has just
            // spent half a second shrinking the silhouette onto this exact rect.
            _wasVisible = true;
            // CLOSED, whatever was asked for. The cube arrives whole from the fold and
            // opens from there — snapping to the asked-for state instead meant the
            // menu was simply already in pieces on its first frame, and the burst, the
            // stagger and the whole gesture never happened.
            _explode   = 0f;
            _burstFade = 0f;
            _shellRect.anchoredPosition = _slotPos;
            _shellRect.sizeDelta        = Vector2.one * WantSize();
            if (_coreRect != null) _coreRect.sizeDelta = Vector2.one * _slotSize;
            return;
        }

        float k = 1f - Mathf.Exp(-SlideSpeed * Time.unscaledDeltaTime);
        _shellRect.anchoredPosition = Vector2.Lerp(_shellRect.anchoredPosition, _slotPos, k);
        _shellRect.sizeDelta        = Vector2.Lerp(_shellRect.sizeDelta, Vector2.one * WantSize(), k);
        // The core does NOT take the explosion's factor. That is the whole point: the
        // cube stays the size it was and the field opens out around it.
        if (_coreRect != null)
            _coreRect.sizeDelta = Vector2.Lerp(_coreRect.sizeDelta, Vector2.one * _slotSize, k);
    }

    // Smoothstep. Used everywhere the explosion is read, so the faces ease out of
    // rest and into their stops rather than starting and stopping dead.
    static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

    /// <summary>How far open the whole assembly is, 0..1.</summary>
    public static float Opening => Shared != null ? Shared._explode : 0f;

    /// <summary>How far face `face` has travelled, 0..1 — its own share of the burst.</summary>
    public static float FaceOpen(int face)
    {
        var c = Shared;
        if (c == null || face < 0 || face >= FaceDelay.Length) return 0f;
        return c.FaceProgress(face);
    }

    float FaceProgress(int i)
    {
        float d = FaceDelay[i];
        return Smooth((_explode - d) / Mathf.Max(0.05f, 1f - d));
    }

    /// <summary>The face the pointer is on, so it can come a little further out.</summary>
    public static void SetPulled(int face)
    {
        if (Shared != null) Shared._pulled = face;
    }

    void DriveExplode()
    {
        float target = _wantExplode ? 1f : 0f;
        _explode = Mathf.Lerp(_explode, target, 1f - Mathf.Exp(-ExplodeSpeed * Time.unscaledDeltaTime));
        if (Mathf.Abs(_explode - target) < 0.002f) _explode = target;

        // THE CUBE ITSELF NEVER COMES APART. It stays whole in the middle, and stays
        // the hole the frozen game shows through. What flies out is a separate set of
        // plates on the other stage — which is also why the plates can be solid paper
        // while the middle is a window: they are two different photographs.
        if (_burstCube != null && _cube != null) _burstCube.localRotation = _cube.localRotation;

        for (int i = 0; i < _burstPlates.Count && i < FaceNormal.Length; i++)
        {
            float t     = FaceProgress(i);
            float pull  = i == _pulled ? PulledExtra : 0f;
            float rest  = PlateInset + ExplodeSpread * FaceDrift[i] * t;
            float reach = PlateInset + ExplodeSpread * (FaceDrift[i] + pull) * t;

            if (i < _faceRest.Length) _faceRest[i] = rest;
            if (_burstPlates[i] != null) _burstPlates[i].localPosition = FaceNormal[i] * reach;

            var line = _burstLines[i];
            if (line == null) continue;

            float len  = reach - PlateThick * 0.5f - LinkStart;
            bool  show = len > 0.02f;
            if (line.gameObject.activeSelf != show) line.gameObject.SetActive(show);
            if (show)
            {
                line.localScale    = new Vector3(LinkWidth, LinkWidth, len);
                line.localPosition = FaceNormal[i] * (LinkStart + len * 0.5f);
            }
        }

        // The field's camera widens exactly as fast as the field's rect does, so a
        // plate a given distance from the middle lands on the same pixel at every
        // stage of the burst — and stays registered with the core, which the other
        // camera photographs at a size that never changes.
        if (_burstCam != null)
            _burstCam.orthographicSize = BaseOrtho * Mathf.Lerp(1f, ExplodeZoom, Smooth(_explode));

        // The burst fades on its OWN clock, much faster than it travels.
        //
        // Tied to _explode it outstayed its welcome: opening the settings page left
        // six plates coasting across the page for the better part of two seconds,
        // sliding under the cube one at a time — which reads exactly like a layer
        // stuck behind something. The plates belong to the menu, so they leave with
        // it, and what is left to watch is the cube going where it is going.
        _burstFade = Mathf.Lerp(_burstFade, _wantExplode ? 1f : 0f,
                                1f - Mathf.Exp(-BurstFadeSpeed * Time.unscaledDeltaTime));

        if (_burstImage != null)
        {
            bool draw = _burstFade > 0.004f && _explode > 0.004f;
            if (_burstImage.enabled != draw) _burstImage.enabled = draw;
            if (draw) _burstImage.color = new Color(1f, 1f, 1f, _burstFade);
        }
    }

    // Leaning toward the pointer, with a slow breath under it.
    //
    // This is what stops an exploded assembly reading as a slide: a static arrangement
    // of labelled parts IS a diagram, and the only difference between that and an
    // object is that an object answers when you move.
    Quaternion ParallaxPose()
    {
        float nx = 0f, ny = 0f;
        if (Screen.width > 0 && Screen.height > 0)
        {
            var m = Input.mousePosition;
            nx = Mathf.Clamp((m.x / Screen.width  - 0.5f) * 2f, -1f, 1f);
            ny = Mathf.Clamp((m.y / Screen.height - 0.5f) * 2f, -1f, 1f);
        }
        float breath = Mathf.Sin(Time.unscaledTime * 0.55f);
        return Quaternion.Euler(-ny * ParallaxTilt + breath * 0.30f,
                                 nx * ParallaxTilt + breath * 0.55f, 0f);
    }

    // The rect the shell wants: the caller's slot, widened by exactly the factor the
    // camera widens by. The two must move together — see DriveExplode.
    float WantSize() => _slotSize * Mathf.Lerp(1f, ExplodeZoom, Smooth(_explode));

    void Update()
    {
        if (this == Shared && _shellRect != null) DriveShell();

        if (_cube == null) return;

        DriveExplode();

        if (_idle)
        {
            if (_explode > 0.02f)
            {
                // Apart, it is HELD rather than spun. A spinning exploded assembly is
                // noise — and the menu's six words are pinned to these faces, so a
                // spin would drag the whole menu round the screen.
                _cube.localRotation = Quaternion.Slerp(_cube.localRotation, ParallaxPose(),
                                                       1f - Mathf.Exp(-ParallaxSpeed * Time.unscaledDeltaTime));
                return;
            }

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
        if (this == Shared)
        {
            Shared = null; _shellCanvas = null; _shellRect = null;
            _coreRect = null; _burstImage = null; _wasVisible = false;
        }
        if (_cam      != null) _cam.targetTexture = null;
        if (_burstCam != null) _burstCam.targetTexture = null;
        if (_rt      != null) { _rt.Release(); Destroy(_rt); }
        if (_burstRt != null) { _burstRt.Release(); Destroy(_burstRt); }
        if (_burstStage != null) Destroy(_burstStage.gameObject);
        // The stage is not our child, so it has to be cleaned up by hand.
        if (_stage != null) Destroy(_stage.gameObject);
    }
}
