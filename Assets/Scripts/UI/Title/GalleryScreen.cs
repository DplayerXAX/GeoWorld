using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using TMPro;

// The Gallery scene. Two views:
//
//   OVERVIEW — three objects sit on a desk: a spinning vinyl record (MUSIC), a
//   monster (MONSTER), and a shader cube (SHADERS). Click any to zoom in.
//
//   DETAIL — the camera glides in on the chosen object (framed screen-right); a
//   menu of that category's items lists down the left, ‹ / › arrows step through
//   them, the current item's name sits up top and its description along the
//   bottom. BACK returns to the overview.
//
// Its own scene (TitleFlow.GoToGallery → LoadingScreen.Go("Gallery")); the scene
// itself only ships a camera, a light and an EventSystem — everything else self-
// builds here. Shader materials + BalanceTable load from Resources/Gallery so this
// cosmetic screen never wires scene references onto the live gameplay assets.
[DisallowMultipleComponent]
public class GalleryScreen : MonoBehaviour
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
        if (SceneManager.GetActiveScene().name != "Gallery") return;
        if (FindFirstObjectByType<GalleryScreen>() != null) return;
        new GameObject("GalleryScreen").AddComponent<GalleryScreen>();
    }

    public string titleScene = "Title";

    [Header("Font (leave null for TMP default)")]
    public TMP_FontAsset font;

    [Header("Desk object placement (world space)")]
    // The pieces form a shallow triangle: the cube comes forward as the hero,
    // while music and monsters sit slightly farther back on either side.
    public Vector3 recordPos  = new Vector3(-2.8f, 0.85f, 0.62f);
    public Vector3 cubePos    = new Vector3(0f, 2.2f, -0.62f);
    public Vector3 monsterPos = new Vector3(2.8f, 1.48f, 0.52f);

    [Header("Exhibit elevation")]
    [Tooltip("Keep the hero cube visibly raised above its plinth without moving the plinth.")]
    public float cubePlinthDrop = 0.86f;
    [Tooltip("Keep the monster card visibly raised above its plinth without moving the plinth.")]
    public float monsterPlinthDrop = 0.68f;
    [Tooltip("World-space size every monster model is normalised to, so a tiny prefab and a huge one both read the same on the pedestal.")]
    public float monsterDisplaySize = 1f;

    [Header("Atelier materials")]
    [Tooltip("A cool graphite blue makes the desk recess while the exhibit accents stay vivid.")]
    public Color deskColor = new Color(0.055f, 0.085f, 0.12f);

    [Header("Camera poses")]
    public Vector3 overviewCamPos  = new Vector3(0.78f, 4.25f, -5.7f);
    public Vector3 overviewLookAt  = new Vector3(0f, 1.5f, 0.28f);
    [Tooltip("Fallback detail camera position = focused object + this. Used only for a section with no Transform assigned below.")]
    public Vector3 detailCamOffset = new Vector3(0.1f, 2.95f, -2.5f);
    [Tooltip("Fallback detail camera look-at = focused object + this. Only consulted when a section has no Transform AND aimFromSubject is off — a world-space offset is correct at exactly one aspect ratio.")]
    public Vector3 detailLookOffset = new Vector3(-1.9f, -0.85f, 2f);

    [Header("Detail camera poses (per section)")]
    [Tooltip("Hand-position this Transform in the Scene view to set where the lens STANDS for Music. Its rotation is used as the aim only if you actually rotated it (see aimFromSubject).")]
    public Transform musicDetailCam;
    [Tooltip("Hand-position this Transform for the Shaders zoom-in camera.")]
    public Transform shaderDetailCam;
    [Tooltip("Hand-position this Transform for the Monster zoom-in camera.")]
    public Transform monsterDetailCam;
    [Tooltip("An un-rotated mark was dragged into place but never aimed, so its rotation points flat down world +Z instead of at the exhibit. On (recommended): solve the aim from where the exhibit actually is. Off: use the mark's raw rotation, whatever it is.")]
    public bool aimFromSubject = true;

    [Header("Camera transition")]
    [Tooltip("Seconds for a view change to essentially land. The move is a critically damped spring — ~91% there at 2x this, ~98% at 3x — scaled per move by travel distance and turn angle.")]
    [Min(0.05f)] public float camSettleTime = 0.34f;
    [Tooltip("Detail-to-overview moves run this much faster than the way in. The player already knows the wide, so lingering on the way out is dead air.")]
    [Range(0.5f, 1f)] public float returnSpeedUp = 0.85f;
    [Tooltip("How far the aim runs ahead of the dolly. Above 1 the framing locks before the camera stops — an operator's head arrives before his body. Also raises peak turn rate, so keep it small.")]
    [Range(1f, 1.4f)] public float aimLead = 1.12f;
    [Tooltip("0 = the camera travels in a straight line, 1 = it swings around the exhibit on an arc that can't dive through the desk.")]
    [Range(0f, 1f)] public float camArc = 1f;
    [Tooltip("Camera positions are never allowed below this Y. The plinth top sits at 0 and the floor at -0.38.")]
    public float deskClearance = 0.55f;
    [Tooltip("Degrees the lens narrows when zoomed in. The camera backs off by exactly the amount that holds the subject the same size, so only the compression changes — the hand-tuned framing is never re-cropped.")]
    public float detailFovDrop = 8f;
    [Tooltip("Where the exhibit sits on screen, 0-1 from the bottom-left. Leave at zero to derive it from the live menu strip / caption plaque / title band, which is the only version that survives an ultrawide.")]
    public Vector2 detailFraming = Vector2.zero;
    [Tooltip("Move progress (0-1) at which the detail panel begins to fade in. Late on purpose: a panel that arrives before the camera drains the destination of its reveal.")]
    [Range(0f, 1f)] public float panelCueIn = 0.55f;
    [Tooltip("Move progress at which the overview panel begins to fade in. Early, so coming back is never a bare frame while the camera retreats.")]
    [Range(0f, 1f)] public float panelCueOut = 0.22f;
    [Tooltip("Accessibility: zeroes the path arc and the lens change, and stops the aim leading the body. The move still eases and still lands — it just stops being vestibular.")]
    public bool reducedCameraMotion = false;

    [Header("Atelier presentation")]
    [Tooltip("Subtle movement applied to the complete exhibit composition as the pointer explores the screen.")]
    public float exhibitParallax = 0.11f;
    [Tooltip("How quickly the composition catches up to the pointer.")]
    public float parallaxGlide = 5f;
    [Tooltip("Multiplier on the composition's pointer TILT while zoomed in. The rig's translation is followed exactly by the live camera target and costs the subject nothing, but a rotation about the world origin isn't cancelled that way.")]
    [Range(0f, 1f)] public float detailParallaxTilt = 0.35f;
    [Tooltip("Degrees per second the record turns while it's being inspected. The overview's 35 is a turntable seen across a room — far too fast to actually look at.")]
    public float detailRecordSpin = 12f;
    [Tooltip("Swell held by a hovered exhibit, and by the focused one for the whole flight in — otherwise it deflates the instant you click and reads as the subject retreating from you.")]
    [Range(1f, 1.3f)] public float focusHoldScale = 1.12f;
    [Tooltip("Seconds used by the overview/detail information crossfade.")]
    [Min(0.05f)] public float panelFadeTime = 0.32f;

    enum View { Overview, Detail }
    enum Cat  { Music, Monster, Shaders }

    View _view = View.Overview;
    Cat  _cat;
    int  _index;

    Camera     _cam;
    float      _baseFov = 60f;   // adopted from the scene camera, never imposed
    Transform  _galleryRig;
    Vector2    _parallax;
    readonly List<Transform> _orbitAccents = new();

    // The whole camera move is this: one normalised progress scalar plus its
    // velocity, and the pose we launched from. Everything else is derived.
    float   _s = 1f, _sVel, _smoothTime = 0.34f;
    Vector3 _posFrom, _aimFrom, _pivot, _camVel;
    float   _fovFrom;

    // The incoming panel is armed rather than shown, and released by the shot clock.
    CanvasGroup _panelArmed;
    float _panelCue = 2f;   // > 1 = nothing armed

    // Cached screen-space framing point, re-derived whenever the window changes.
    Vector2 _framing = new Vector2(0.6f, 0.53f);
    int   _framingW, _framingH;
    float _framingScale = -1f;

    // Desk objects
    GameObject        _recordObj;
    GameObject        _cubeObj;
    TitleCubeShowcase _showcase;
    GameObject        _monsterObj;        // pedestal / click target — never swapped
    GameObject        _monsterInstance;   // the live enemy prefab standing on it
    readonly List<TextMeshPro> _worldLabels = new();

    // Hover state (overview): the hovered pick swells slightly and its label gilds.
    GalleryPick _hovered;
    readonly Dictionary<GalleryPick, Vector3> _baseScales = new();
    readonly Dictionary<GalleryPick, TextMeshPro> _pickLabels = new();

    // Built-in shader fallback (used only when GalleryConfig has no shaders).
    static readonly (string file, string label, string desc)[] ShadersFallback =
    {
        ("Gallery_M_Order",           "Order — Grid",       "The lawful lattice. Same-color pieces in a line debuff every enemy that crosses the path."),
        ("Gallery_M_HarmonyWood",     "Harmony — Flow",     "All same-color pieces joined as one — a standing buff to every turret touching the weave."),
        ("Gallery_Explorat",          "Exploration — Core", "A straight run of one color. Reach outward; the line rewards distance."),
        ("Gallery_M_EnlightmentWave", "Enlightenment",      "The N×N×N cube. Fold blue into a solid cube to earn reversible turret upgrades."),
        ("Gallery_start_M",           "Black Hole Core",    "The devouring heart — where the enemy path begins."),
        ("Gallery_start_MM",          "Halo Core",          "A ringed sentinel of light marking a spawn."),
        ("Gallery_end_m",            "Halo Core — End",     "The core you defend. Let nothing reach it."),
        ("Gallery_glass",            "Glass Box",           "Refractive casing — the game's translucent block finish."),
    };

    // Runtime data, populated from GalleryConfig (Resources/GalleryConfig) or the
    // built-in fallbacks in LoadConfig().
    GalleryConfig _config;
    struct ShaderItem  { public string title; public string desc; public Material mat; }
    struct MusicItem   { public string title; public string desc; public AK.Wwise.Event evt; }
    struct MonsterItem { public string name;  public string desc; public EnemySurfaceUnit prefab; }
    readonly List<ShaderItem>  _shaderItems = new();
    readonly List<MusicItem>   _musicItems  = new();
    readonly List<MonsterItem> _monsters    = new();
    bool _configMusic;      // true = play Wwise tracks; false = Calm/Battle mood fallback
    uint _musicPlayingId;   // currently-playing config track (0 = none)
    int  _musicIndex;       // the track actually playing — persists across overview/detail nav

    // Detail UI
    Canvas   _canvas;
    RectTransform _detailRoot;
    RectTransform _overviewRoot;
    CanvasGroup _detailGroup;
    CanvasGroup _overviewGroup;
    bool _detailVisible;
    bool _overviewVisible;
    RectTransform _menuList;
    // The detail framing point is derived from these two rather than hard-coded, so
    // the composition survives an ultrawide (the 380px strip is a fifth of a 1920
    // frame and only an eighth of a 3440 one).
    RectTransform _menuStrip;
    RectTransform _plaqueRect;
    TMP_Text _titleText, _descText, _menuHeading, _detailIndexText, _overviewStatus;
    readonly List<Button> _menuButtons = new();

    void Awake()
    {
        _cam = Camera.main;
        if (_cam != null)
        {
            // Adopt whatever focal length the scene ships with rather than imposing
            // one: overviewCamPos was framed against it, and a serialized default
            // here would silently re-crop the establishing wide.
            _baseFov = _cam.fieldOfView;
            if (_cam.clearFlags != CameraClearFlags.Skybox || RenderSettings.skybox == null)
            {
                // Default to a paper-white room, while respecting a Gallery scene that
                // intentionally supplies its own atelier skybox material.
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = GeoPalette.Paper;
            }
        }
        LoadConfig();
        BuildDesk();
        BuildObjects();
        BuildUI();
        GoOverview(instant: true);
        PlayMusic(_musicIndex);   // starts the instant the Gallery loads; keeps running across nav
    }

    void Update()
    {
        // Order matters, and it's the reverse of what shipped. The detail target is
        // re-solved every frame from FocusPos(), which reads a child of _galleryRig,
        // so the rig has to move FIRST — otherwise the camera is permanently framed
        // against last frame's parallax.
        UpdateAtelierMotion();
        UpdateCamera();

        // The incoming panel was armed by GoOverview/EnterDetail; the shot clock
        // releases it. Slaving the crossfade to the camera is what stops the caption
        // card arriving three quarters of a second before the picture does.
        if (_panelArmed != null && _s >= _panelCue)
        {
            SetPanelVisible(_panelArmed, true, false);
            _panelArmed = null;
        }
        UpdatePanelTransitions();

        // The three world titles dissolve on the camera's own curve. SetActive was
        // the last hard cut left anywhere in the transition.
        float labelAlpha = _view == View.Overview ? _s : 1f - _s;
        foreach (var kv in _pickLabels)
            if (kv.Value != null) kv.Value.alpha = labelAlpha;

        // Idle spin for the record — much slower while it's actually being inspected.
        if (_recordObj != null)
            _recordObj.transform.Rotate(0f,
                (_view == View.Detail ? detailRecordSpin : 35f) * Time.deltaTime, 0f, Space.Self);
        // The monster is a real model now — it spins on its own (TurretBeacon /
        // EnemyChaoticVisual) instead of being billboarded like the old sprite card.
        for (int i = 0; i < _worldLabels.Count; i++) BillboardToCamera(_worldLabels[i] != null ? _worldLabels[i].transform : null);

        // Overview hover + click-to-zoom
        if (_view == View.Overview && _cam != null)
        {
            GalleryPick pick = null;
            if (!PointerOverUI())
            {
                var ray = _cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out var hit, 100f))
                    pick = hit.collider.GetComponentInParent<GalleryPick>();
            }
            SetHovered(pick);
            if (pick != null && Input.GetMouseButtonDown(0)) EnterDetail((Cat)pick.cat);
        }
        else SetHovered(null);
        UpdateHoverVisuals();

        if (_view == View.Detail)
        {
            if (Input.GetKeyDown(KeyCode.Escape)) GoOverview(false);
            // Keyboard browsing — same as the on-screen ‹ › arrows.
            if (Input.GetKeyDown(KeyCode.LeftArrow)  || Input.GetKeyDown(KeyCode.A)) Step(-1);
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) Step(+1);
        }
    }

    // The room should feel like a physical model under glass, not a static menu.
    // We move the complete composition only a few centimetres; focus poses use the
    // live object positions below, so detail framing stays locked to the selected work.
    void UpdateAtelierMotion()
    {
        if (_galleryRig != null)
        {
            Vector2 pointer = new Vector2(
                Input.mousePosition.x / Mathf.Max(1f, Screen.width),
                Input.mousePosition.y / Mathf.Max(1f, Screen.height)) * 2f - Vector2.one;
            // Unscaled, to match the camera clock. The detail target is resolved from
            // a rig child every frame, so if the two ever advanced on different time
            // sources the camera would chase a rig that had moved by a different
            // amount. 1-exp(-k*dt) is frame-rate independent either way.
            _parallax = Vector2.Lerp(_parallax, pointer, 1f - Mathf.Exp(-parallaxGlide * Time.unscaledDeltaTime));
            _galleryRig.localPosition = new Vector3(_parallax.x, _parallax.y * 0.45f, 0f) * exhibitParallax;
            // Only the TILT is scaled back in detail. The rig's translation is followed
            // exactly by the live camera target, so it costs the subject nothing and
            // buys real differential parallax against the neighbouring plinths — but a
            // rotation about the world origin is NOT cancelled that way: at the
            // record's ~2.9m radius the full yaw swings it across the frame while the
            // player is trying to read the caption.
            float tilt = _view == View.Detail ? detailParallaxTilt : 1f;
            _galleryRig.localRotation = Quaternion.Euler(-_parallax.y * 1.3f * tilt, _parallax.x * 1.8f * tilt, 0f);
        }

        for (int i = 0; i < _orbitAccents.Count; i++)
        {
            var orbit = _orbitAccents[i];
            if (orbit == null) continue;
            float direction = i % 2 == 0 ? 1f : -1f;
            orbit.Rotate(0f, 18f * direction * Time.deltaTime, 0f, Space.Self);
        }
    }

    void UpdatePanelTransitions()
    {
        float speed = 1f / Mathf.Max(0.01f, panelFadeTime);
        UpdatePanelTransition(_overviewGroup, _overviewVisible, speed);
        UpdatePanelTransition(_detailGroup, _detailVisible, speed);
    }

    static void UpdatePanelTransition(CanvasGroup group, bool visible, float speed)
    {
        if (group == null) return;
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        group.alpha = Mathf.Lerp(group.alpha, visible ? 1f : 0f, k);
        // The outgoing panel remains visible while it fades but never catches a click.
        // 0.96 against an exponential fade was a bug: ln(25)/(1/0.32) = 1.03s in which
        // NEITHER panel accepted input, so BACK was dead for a full second after every
        // view change. Half opacity is reached in 0.22s and is still far too solid for
        // an invisible panel to swallow anything.
        bool interactive = visible && group.alpha > 0.5f;
        group.interactable = interactive;
        group.blocksRaycasts = interactive;
    }

    void SetPanelVisible(CanvasGroup group, bool visible, bool instant)
    {
        if (group == null) return;
        if (group == _overviewGroup) _overviewVisible = visible;
        if (group == _detailGroup) _detailVisible = visible;
        if (!instant) return;

        group.alpha = visible ? 1f : 0f;
        group.interactable = visible;
        group.blocksRaycasts = visible;
    }

    void BillboardToCamera(Transform t)
    {
        if (t == null || _cam == null) return;
        t.rotation = Quaternion.LookRotation(t.position - _cam.transform.position, Vector3.up);
    }

    void SetHovered(GalleryPick pick)
    {
        if (_hovered == pick) return;
        _hovered = pick;
        if (pick != null) AudioManager.Instance?.PlayUISound();
        if (_overviewStatus != null)
        {
            _overviewStatus.text = pick == null
                ? "THREE STUDIES  /  SELECT AN EXHIBIT"
                : $"{(Cat)pick.cat switch { Cat.Music => "01  /  MUSIC", Cat.Shaders => "02  /  SHADERS", _ => "03  /  MONSTERS" }}  /  CLICK TO INSPECT";
        }
    }

    // Eases every pick toward rest/swollen scale, and gilds the hovered label.
    void UpdateHoverVisuals()
    {
        float k = 1f - Mathf.Exp(-10f * Time.deltaTime);
        foreach (var kv in _baseScales)
        {
            var pick = kv.Key;
            if (pick == null) continue;
            // In shader-detail the showcase's own swap "punch" owns the cube's scale.
            if (_view == View.Detail && pick.gameObject == _cubeObj) continue;
            // The clicked exhibit HOLDS its swell for the whole flight. Easing it back
            // the instant _hovered cleared — the same frame EnterDetail fires — made
            // the object visibly shrink while the camera flew toward it, which read as
            // the subject retreating from the player.
            bool held = _view == View.Detail && pick.gameObject == FocusObject(_cat);
            Vector3 target = kv.Value * (pick == _hovered || held ? focusHoldScale : 1f);
            pick.transform.localScale = Vector3.Lerp(pick.transform.localScale, target, k);

            if (_pickLabels.TryGetValue(pick, out var label) && label != null)
            {
                // Lerp the hue only — the overview/detail dissolve owns the alpha now.
                Color c = Color.Lerp(label.color, pick == _hovered ? GeoPalette.Gold : GeoPalette.Paper, k);
                c.a = label.color.a;
                label.color = c;
            }
        }
    }

    static bool PointerOverUI()
        => UnityEngine.EventSystems.EventSystem.current != null
        && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

    // ── Scene objects ────────────────────────────────────────────────────────

    void BuildDesk()
    {
        _galleryRig = new GameObject("GalleryComposition").transform;

        // A shallow plinth rather than a literal desk: it grounds the three pieces
        // while keeping the room graphic and deliberately abstract.
        var desk = GameObject.CreatePrimitive(PrimitiveType.Cube);
        desk.name = "GalleryPlinth";
        desk.transform.SetParent(_galleryRig, false);
        desk.transform.position   = new Vector3(0f, -0.15f, 0.4f);
        desk.transform.localScale = new Vector3(9f, 0.3f, 4f);
        var col = desk.GetComponent<Collider>(); if (col != null) Destroy(col);
        var deskRend = desk.GetComponent<Renderer>();
        deskRend.sharedMaterial = SolidMaterial();
        MpbColor.Set(deskRend, deskColor);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "GalleryFloor";
        floor.transform.SetParent(_galleryRig, false);
        floor.transform.position = new Vector3(0f, -0.38f, 1.4f);
        floor.transform.localScale = new Vector3(15f, 0.08f, 9f);
        var floorCol = floor.GetComponent<Collider>(); if (floorCol != null) Destroy(floorCol);
        var floorRend = floor.GetComponent<Renderer>();
        floorRend.sharedMaterial = SolidMaterial();
        MpbColor.Set(floorRend, GeoPalette.WithAlpha(GeoPalette.Ink, 0.96f));

        // A thin horizon beam gives the studio a depth cue without needing a backdrop texture.
        var horizon = CreatePrimitive("HorizonBeam", PrimitiveType.Cube, _galleryRig,
            new Vector3(0f, 1.85f, 2.3f), new Vector3(11.5f, 0.025f, 0.025f), GeoPalette.Gold);
        horizon.transform.localRotation = Quaternion.Euler(0f, -2f, 0f);
    }

    void BuildObjects()
    {
        // Record (Music) — a flat cylinder disc with a bright centre label.
        _recordObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _recordObj.name = "Record";
        _recordObj.transform.SetParent(_galleryRig, false);
        _recordObj.transform.position   = recordPos;
        _recordObj.transform.localScale = new Vector3(1.5f, 0.06f, 1.5f);
        _recordObj.transform.rotation   = Quaternion.Euler(0f, 0f, 0f);
        var recRend = _recordObj.GetComponent<Renderer>();
        recRend.sharedMaterial = SolidMaterial();
        MpbColor.Set(recRend, new Color(0.09f, 0.09f, 0.1f));
        var label = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        label.transform.SetParent(_recordObj.transform, false);
        label.transform.localScale = new Vector3(0.34f, 1.05f, 0.34f);
        var lcol = label.GetComponent<Collider>(); if (lcol != null) Destroy(lcol);
        var labelRend = label.GetComponent<Renderer>();
        labelRend.sharedMaterial = SolidMaterial();
        MpbColor.Set(labelRend, GeoPalette.Signal);
        var recordPick = MakePickable(_recordObj, Cat.Music);
        // The record sits much lower (y ~0.85) than the cube/monster, so the same
        // -0.9 drop the other two use would land the label inside the desk plinth
        // (top surface at y=0) — a smaller drop keeps it clear above the desk.
        _pickLabels[recordPick] = AddWorldLabel(_galleryRig, recordPos + Vector3.down * 0.45f + Vector3.back * 0.7f, "MUSIC");
        BuildExhibitBase(recordPos, GeoPalette.Signal, "01");

        // Cube (Shaders) — the TitleCubeShowcase.
        _cubeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cubeObj.name = "ShaderCube";
        _cubeObj.transform.SetParent(_galleryRig, false);
        _cubeObj.transform.position   = cubePos;
        _cubeObj.transform.localScale = Vector3.one * 1.3f;
        var mats = new List<Material>();
        foreach (var s in _shaderItems) if (s.mat != null) mats.Add(s.mat);
        _showcase = _cubeObj.AddComponent<TitleCubeShowcase>();
        _showcase.materials = mats;
        _showcase.autoAdvance = false;   // never auto-cycles — only the detail ‹ / › arrows change it
        _showcase.ShowIndex(0);
        var cubePick = MakePickable(_cubeObj, Cat.Shaders);
        _pickLabels[cubePick] = AddWorldLabel(_galleryRig, cubePos + Vector3.down * 1.2f + Vector3.back * 0.7f, "SHADERS");
        BuildExhibitBase(cubePos + Vector3.down * cubePlinthDrop, GeoPalette.Blue, "02");

        // Monster — the real prefab on a pedestal, not a photographed card, so the
        // model reads in 3D like the other two exhibits. `_monsterObj` is just the
        // mount: it keeps the click collider and the pick/label bookkeeping stable
        // while the actual creature underneath is swapped per menu selection.
        _monsterObj = new GameObject("Monster");
        _monsterObj.transform.SetParent(_galleryRig, false);
        _monsterObj.transform.position = monsterPos;
        var box = _monsterObj.AddComponent<BoxCollider>();
        box.size = new Vector3(1.4f, 1.4f, 1.4f);
        if (_monsters.Count > 0) ShowMonster(_monsters[0].prefab);
        var monsterPick = MakePickable(_monsterObj, Cat.Monster);
        _pickLabels[monsterPick] = AddWorldLabel(_galleryRig, monsterPos + Vector3.down * 1.0f + Vector3.back * 0.7f, "MONSTER");
        BuildExhibitBase(monsterPos + Vector3.down * monsterPlinthDrop, GeoPalette.Gold, "03");
    }

    void BuildExhibitBase(Vector3 position, Color accent, string number)
    {
        // The plinth, halo and off-axis frame make each asset read as a curated object.
        var baseDisc = CreatePrimitive($"Plinth_{number}", PrimitiveType.Cylinder, _galleryRig,
            position + new Vector3(0f, -0.25f, 0f), new Vector3(1.15f, 0.055f, 1.15f), GeoPalette.Paper);
        baseDisc.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);

        var inset = CreatePrimitive($"Inset_{number}", PrimitiveType.Cylinder, _galleryRig,
            position + new Vector3(0f, -0.19f, 0f), new Vector3(0.88f, 0.025f, 0.88f), accent);
        inset.transform.localRotation = Quaternion.Euler(0f, 18f, 0f);

        var orbit = new GameObject($"Orbit_{number}").transform;
        orbit.SetParent(_galleryRig, false);
        orbit.position = position + new Vector3(0f, 0.18f, 0f);
        _orbitAccents.Add(orbit);
        for (int i = 0; i < 3; i++)
        {
            float angle = i * 120f;
            Vector3 local = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 1.04f;
            var tick = CreatePrimitive($"OrbitTick_{number}_{i}", PrimitiveType.Cube, orbit,
                local, new Vector3(0.09f, 0.018f, 0.38f), i == 0 ? accent : GeoPalette.Paper);
            tick.localRotation = Quaternion.Euler(0f, angle, 0f);
        }

        var frame = new GameObject($"Frame_{number}").transform;
        frame.SetParent(_galleryRig, false);
        frame.position = position + new Vector3(0f, 1.15f, 1.0f);
        frame.localRotation = Quaternion.Euler(0f, 0f, number == "02" ? 0f : (number == "01" ? -7f : 7f));
        const float width = 1.7f;
        const float height = 2.35f;
        const float line = 0.035f;
        CreatePrimitive("Top", PrimitiveType.Cube, frame, new Vector3(0f, height * 0.5f, 0f), new Vector3(width, line, line), accent);
        CreatePrimitive("Bottom", PrimitiveType.Cube, frame, new Vector3(0f, -height * 0.5f, 0f), new Vector3(width, line, line), GeoPalette.Ink);
        CreatePrimitive("Left", PrimitiveType.Cube, frame, new Vector3(-width * 0.5f, 0f, 0f), new Vector3(line, height, line), GeoPalette.Ink);
        CreatePrimitive("Right", PrimitiveType.Cube, frame, new Vector3(width * 0.5f, 0f, 0f), new Vector3(line, height, line), accent);

        var numberLabel = AddWorldLabel(frame, frame.position + Vector3.up * (height * 0.58f), number);
        numberLabel.fontSize = 2.2f;
        numberLabel.color = accent;
    }

    Transform CreatePrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localScale = scale;
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        var rend = go.GetComponent<Renderer>();
        rend.sharedMaterial = SolidMaterial();   // build-safe — see SolidMaterial()
        MpbColor.Set(rend, color);
        return go.transform;
    }

    // Runtime CreatePrimitive cubes get the built-in default material, whose shader
    // is stripped from standalone builds (renders magenta). Assign an explicit URP
    // Lit material instead — always present in a URP build. MpbColor drives colour.
    static Material _solidMat;
    static Material SolidMaterial()
    {
        if (_solidMat != null) return _solidMat;
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        _solidMat = new Material(sh) { name = "GallerySolid" };
        return _solidMat;
    }

    // Swaps the creature standing on the monster pedestal for a live instance of
    // `prefab`. Gameplay behaviours are stripped the same way EnemyThumbnail's
    // photo booth strips them — this is a display case, nothing should path,
    // collide, heal, seal or split in here. EnemyChaoticVisual is deliberately
    // LEFT ON: the mesh-less enemies only exist once it has built their shards.
    void ShowMonster(EnemySurfaceUnit prefab)
    {
        if (_monsterObj == null) return;

        if (_monsterInstance != null) Destroy(_monsterInstance);
        _monsterInstance = null;
        if (prefab == null) return;

        _monsterInstance = Instantiate(prefab.gameObject, _monsterObj.transform);
        _monsterInstance.name = "MonsterModel";
        _monsterInstance.transform.localPosition = Vector3.zero;
        _monsterInstance.transform.localRotation = Quaternion.identity;

        foreach (var c in _monsterInstance.GetComponentsInChildren<EnemySurfaceUnit>(true))      c.enabled = false;
        foreach (var c in _monsterInstance.GetComponentsInChildren<EnemyHealerAura>(true))       c.enabled = false;
        foreach (var c in _monsterInstance.GetComponentsInChildren<EnemyBlockSealer>(true))      c.enabled = false;
        foreach (var c in _monsterInstance.GetComponentsInChildren<EnemyTurretSuppressor>(true)) c.enabled = false;
        foreach (var c in _monsterInstance.GetComponentsInChildren<EnemySplitOnAlive>(true))     c.enabled = false;
        // The pedestal's own BoxCollider is what the click raycast wants to hit —
        // the model's colliders would sit in front of it and swallow the click.
        foreach (var c in _monsterInstance.GetComponentsInChildren<Collider>(true))              c.enabled = false;
        foreach (var c in _monsterInstance.GetComponentsInChildren<Rigidbody>(true))             c.isKinematic = true;

        StartCoroutine(FitMonsterNextFrame(_monsterInstance));
    }

    // Prefabs vary wildly in authored size (one ships a 34× scaled mesh child),
    // and the procedural ones have no renderers at all until EnemyChaoticVisual
    // has run — so normalise to a fixed display height a frame later, once the
    // visual actually exists and its bounds can be measured.
    System.Collections.IEnumerator FitMonsterNextFrame(GameObject model)
    {
        yield return null;
        yield return null;
        if (model == null || _monsterObj == null) yield break;

        var bounds = new Bounds(model.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in model.GetComponentsInChildren<Renderer>())
        {
            if (r == null) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        if (!any) yield break;

        float height = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
        if (height < 0.001f) yield break;

        model.transform.localScale = Vector3.one * (monsterDisplaySize / height);
        // Re-centre on the pedestal: the measured centre is rarely the pivot.
        Vector3 localCentre = model.transform.parent.InverseTransformPoint(bounds.center);
        model.transform.localPosition = -localCentre * (monsterDisplaySize / height);
    }

    GalleryPick MakePickable(GameObject go, Cat cat)
    {
        if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
        var pick = go.AddComponent<GalleryPick>();
        pick.cat = (int)cat;
        _baseScales[pick] = go.transform.localScale;
        return pick;
    }

    TextMeshPro AddWorldLabel(Transform parent, Vector3 worldPos, string text)
    {
        var go = new GameObject($"Label_{text}");
        go.transform.position = worldPos;
        // Labels belong to the same rig as their piece, so the miniature room keeps
        // its composition while responding to the subtle pointer parallax.
        if (parent != null) go.transform.SetParent(parent, true);
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = 4f;
        tmp.color = GeoPalette.Paper;   // white, reads over the dark atelier sky
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        _worldLabels.Add(tmp);
        return tmp;
    }

    // Populate every section from GalleryConfig (Resources/GalleryConfig) when
    // present, otherwise from the built-in fallbacks. Each section falls back
    // independently, so you can author just the music and leave the rest default.
    void LoadConfig()
    {
        _config = Resources.Load<GalleryConfig>("GalleryConfig");

        // ── Shaders ──
        _shaderItems.Clear();
        if (_config != null && _config.shaders != null && _config.shaders.Count > 0)
        {
            foreach (var s in _config.shaders)
                if (s != null && s.material != null)
                    _shaderItems.Add(new ShaderItem { title = s.title, desc = s.description, mat = s.material });
        }
        if (_shaderItems.Count == 0)
        {
            foreach (var s in ShadersFallback)
            {
                var m = Resources.Load<Material>($"Gallery/Shaders/{s.file}");
                if (m != null) _shaderItems.Add(new ShaderItem { title = s.label, desc = s.desc, mat = m });
            }
        }

        // ── Music ──
        _musicItems.Clear();
        if (_config != null && _config.music != null && _config.music.Count > 0)
        {
            _configMusic = true;
            foreach (var t in _config.music)
                if (t != null) _musicItems.Add(new MusicItem { title = t.title, desc = t.description, evt = t.track });
        }
        else
        {
            _configMusic = false;   // no authored tracks → the old Calm/Battle switcher
            _musicItems.Add(new MusicItem { title = "Calm",   desc = "The build phase. Quiet, patient, room to think between the tides of chaos." });
            _musicItems.Add(new MusicItem { title = "Battle", desc = "The wave. The score sharpens as the horde marches on your Core." });
        }

        // ── Monsters ──
        _monsters.Clear();
        if (_config != null && _config.monsters != null && _config.monsters.Count > 0)
        {
            foreach (var m in _config.monsters)
                if (m != null && m.prefab != null)
                    _monsters.Add(new MonsterItem { name = m.title, prefab = m.prefab, desc = m.description });
        }
        if (_monsters.Count == 0)
        {
            var balance = Resources.Load<BalanceTable>("Gallery/Gallery_BalanceTable");
            if (balance?.enemies != null)
                foreach (var rec in balance.enemies)
                {
                    if (rec == null || rec.prefab == null) continue;
                    string desc = $"Health {rec.maxHealth}   ·   Speed {rec.speedMultiplier:0.##}×   ·   Bounty {rec.rewardOnKill}\nFirst appears round {rec.minRound + 1}.";
                    _monsters.Add(new MonsterItem { name = rec.name, prefab = rec.prefab, desc = desc });
                }
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    void GoOverview(bool instant)
    {
        _view = View.Overview;
        // Music keeps playing across overview/detail — only OnDestroy (leaving the
        // Gallery) actually stops it.
        SetPanelVisible(_detailGroup, false, instant);
        if (instant)
        {
            SetPanelVisible(_overviewGroup, true, true);
            _panelArmed = null;
        }
        else
        {
            // Early on the way out: a bare frame while the camera retreats reads as a
            // hitch, whereas going in the reveal wants the screen to itself.
            _panelArmed = _overviewGroup;
            _panelCue   = panelCueOut;
        }
        if (_overviewStatus != null) _overviewStatus.text = "THREE STUDIES  /  SELECT AN EXHIBIT";

        RetargetCamera(instant);
    }

    void EnterDetail(Cat cat)
    {
        AudioManager.Instance?.PlayUISound();
        _view = View.Detail;
        _cat  = cat;
        // Music resumes on whichever track is already playing rather than restarting
        // from the top; the other categories always start browsing from the first item.
        _index = cat == Cat.Music ? _musicIndex : 0;
        SetPanelVisible(_overviewGroup, false, false);
        // Armed, not shown. UpdateCamera releases it at panelCueIn so the plaque lands
        // a beat before the frame stops instead of well ahead of it.
        _panelArmed = _detailGroup;
        _panelCue   = panelCueIn;

        RetargetCamera(instant: false);

        RebuildMenu();
        ApplyItem();
    }

    // A hand-positioned Transform per section takes priority; falls back to the
    // shared offset-from-object math when a section has no Transform assigned.
    Transform DetailCamFor(Cat cat) => cat switch
    {
        Cat.Music   => musicDetailCam,
        Cat.Monster => monsterDetailCam,
        _           => shaderDetailCam,
    };

    Vector3 FocusPos(Cat cat) => cat switch
    {
        Cat.Music   => _recordObj  != null ? _recordObj.transform.position  : recordPos,
        Cat.Monster => _monsterObj != null ? _monsterObj.transform.position : monsterPos,
        _           => _cubeObj    != null ? _cubeObj.transform.position    : cubePos,
    };

    // The clickable object behind a section — used to hold its hover swell while the
    // camera flies toward it.
    GameObject FocusObject(Cat cat) => cat switch
    {
        Cat.Music   => _recordObj,
        Cat.Monster => _monsterObj,
        _           => _cubeObj,
    };

    int ItemCount() => _cat switch
    {
        Cat.Music   => _musicItems.Count,
        Cat.Monster => _monsters.Count,
        _           => _shaderItems.Count,
    };

    void Step(int dir)
    {
        int n = ItemCount();
        if (n == 0) return;
        _index = (_index + dir + n) % n;
        AudioManager.Instance?.PlayUISound();
        ApplyItem();
        HighlightMenu();
    }

    void ApplyItem()
    {
        int n = ItemCount();
        if (n == 0 && _detailIndexText != null) _detailIndexText.text = "00  /  00";
        if (n == 0) { _titleText.text = "—"; _descText.text = ""; return; }
        _index = Mathf.Clamp(_index, 0, n - 1);
        if (_detailIndexText != null) _detailIndexText.text = $"{_index + 1:00}  /  {n:00}";

        switch (_cat)
        {
            case Cat.Music:
                _titleText.text = _musicItems[_index].title;
                _descText.text  = _musicItems[_index].desc;
                // Only switch the playing track when browsing actually picked a
                // different one — entering the Music detail view (or arriving on
                // the currently-playing item) must not restart it.
                if (_index != _musicIndex) { _musicIndex = _index; PlayMusic(_musicIndex); }
                break;
            case Cat.Monster:
                var m = _monsters[_index];
                _titleText.text = m.name;
                _descText.text  = m.desc;
                ShowMonster(m.prefab);
                break;
            default:
                _titleText.text = _shaderItems[_index].title;
                _descText.text  = _shaderItems[_index].desc;
                if (_showcase != null) _showcase.ShowIndex(_index);
                break;
        }
    }

    bool _battleMood;

    // Config path: post the selected Wwise track (stopping the previous). Fallback
    // path (no authored tracks): the two entries drive AudioManager's Calm/Battle mood.
    void PlayMusic(int index)
    {
        if (_configMusic)
        {
            StopMusic();
            var evt = index >= 0 && index < _musicItems.Count ? _musicItems[index].evt : null;
            if (evt != null && evt.IsValid()) _musicPlayingId = evt.Post(gameObject);
        }
        else
        {
            SetMood(index == 1);
        }
    }

    void StopMusic()
    {
        if (_musicPlayingId != 0)
        {
            AkUnitySoundEngine.StopPlayingID(_musicPlayingId, 300,
                AkCurveInterpolation.AkCurveInterpolation_Linear);
            _musicPlayingId = 0;
        }
        if (!_configMusic && _battleMood) SetMood(false);
    }

    void OnDestroy() => StopMusic();   // don't leave a track ringing after leaving the Gallery

    void SetMood(bool battle)
    {
        _battleMood = battle;
        if (battle) AudioManager.Instance?.EnterBattleBGM();
        else        AudioManager.Instance?.ExitBattleBGM();
    }

    // ── UI ───────────────────────────────────────────────────────────────────

    void BuildUI()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        _canvas = canvasGo.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var sc = canvasGo.GetComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight = 0.5f;

        BuildOverviewUI((RectTransform)canvasGo.transform);
        BuildDetailUI((RectTransform)canvasGo.transform);
    }

    void BuildOverviewUI(RectTransform parent)
    {
        _overviewRoot = NewRect("OverviewUI", parent);
        Stretch(_overviewRoot);
        _overviewGroup = _overviewRoot.gameObject.AddComponent<CanvasGroup>();

        var back = BuildTextButton(_overviewRoot, "‹ BACK", new Vector2(0f, 1f), new Vector2(40f, -36f),
            () => { AudioManager.Instance?.PlayUISound(); LoadingScreen.Go(titleScene); });
        back.fontSize = 26f;
        back.color = GeoPalette.Ink;

        var archive = NewText("ArchiveCaption", _overviewRoot, 18f, GeoPalette.Signal, FontStyles.Bold);
        var art = archive.rectTransform;
        art.anchorMin = art.anchorMax = new Vector2(1f, 1f); art.pivot = new Vector2(1f, 1f);
        art.anchoredPosition = new Vector2(-42f, -40f); art.sizeDelta = new Vector2(430f, 32f);
        archive.alignment = TextAlignmentOptions.Right;
        archive.characterSpacing = 3f;
        archive.text = "GEOWORLD  /  MATERIAL ARCHIVE  /  VOL. 01";

        // Wordmark — big ink GALLERY over a gold rule, Title-poster style.
        var mark = NewText("Wordmark", _overviewRoot, 64f, GeoPalette.Ink, FontStyles.Bold);
        var mrt = mark.rectTransform;
        mrt.anchorMin = new Vector2(0.5f, 1f); mrt.anchorMax = new Vector2(0.5f, 1f); mrt.pivot = new Vector2(0.5f, 1f);
        mrt.anchoredPosition = new Vector2(0f, -34f); mrt.sizeDelta = new Vector2(700f, 80f);
        mark.alignment = TextAlignmentOptions.Top;
        mark.characterSpacing = 18f;
        mark.text = "GALLERY";

        var rule = NewRect("Rule", _overviewRoot);
        rule.anchorMin = new Vector2(0.5f, 1f); rule.anchorMax = new Vector2(0.5f, 1f); rule.pivot = new Vector2(0.5f, 1f);
        rule.anchoredPosition = new Vector2(0f, -112f); rule.sizeDelta = new Vector2(260f, 6f);
        var ruleImg = rule.gameObject.AddComponent<Image>();
        ruleImg.color = GeoPalette.Gold;
        ruleImg.raycastTarget = false;

        var hint = NewText("Hint", _overviewRoot, 22f, GeoPalette.WithAlpha(GeoPalette.Ink, 0.65f), FontStyles.Normal);
        hint.alignment = TextAlignmentOptions.Bottom;
        var hrt = hint.rectTransform;
        hrt.anchorMin = new Vector2(0.5f, 0f); hrt.anchorMax = new Vector2(0.5f, 0f); hrt.pivot = new Vector2(0.5f, 0f);
        hrt.anchoredPosition = new Vector2(0f, 48f); hrt.sizeDelta = new Vector2(900f, 40f);
        hint.text = "Click the record, the cube, or the creature to inspect it.";

        _overviewStatus = NewText("Status", _overviewRoot, 16f, GeoPalette.WithAlpha(GeoPalette.Ink, 0.58f), FontStyles.Bold);
        var srt = _overviewStatus.rectTransform;
        srt.anchorMin = srt.anchorMax = new Vector2(0.5f, 0f); srt.pivot = new Vector2(0.5f, 0f);
        srt.anchoredPosition = new Vector2(0f, 86f); srt.sizeDelta = new Vector2(760f, 26f);
        _overviewStatus.alignment = TextAlignmentOptions.Center;
        _overviewStatus.characterSpacing = 3f;
        _overviewStatus.text = "THREE STUDIES  /  SELECT AN EXHIBIT";

        // A compact index makes the overview legible before the player begins to hover.
        BuildOverviewShortcut(_overviewRoot, "01  MUSIC", new Vector2(-290f, 0f), GeoPalette.Signal, Cat.Music);
        BuildOverviewShortcut(_overviewRoot, "02  SHADERS", new Vector2(0f, 0f), GeoPalette.Blue, Cat.Shaders);
        BuildOverviewShortcut(_overviewRoot, "03  MONSTERS", new Vector2(290f, 0f), GeoPalette.Gold, Cat.Monster);

        BuildCornerMark(_overviewRoot, new Vector2(26f, -144f), new Vector2(64f, 4f), GeoPalette.Signal);
        BuildCornerMark(_overviewRoot, new Vector2(-26f, -144f), new Vector2(64f, 4f), GeoPalette.Blue, right: true);
    }

    void BuildOverviewShortcut(RectTransform parent, string label, Vector2 offset, Color accent, Cat cat)
    {
        var rt = NewRect($"Shortcut_{cat}", parent);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(offset.x, 118f);
        rt.sizeDelta = new Vector2(250f, 38f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(10);
        img.type = Image.Type.Sliced;
        img.color = GeoPalette.WithAlpha(GeoPalette.Paper, 0.75f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => EnterDetail(cat));

        var text = NewText("Label", rt, 15f, GeoPalette.Ink, FontStyles.Bold);
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = new Vector2(14f, 0f); text.rectTransform.offsetMax = new Vector2(-14f, 0f);
        text.alignment = TextAlignmentOptions.Center;
        text.characterSpacing = 2f;
        text.text = label;

        var bar = NewRect("Accent", rt);
        bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(0f, 1f);
        bar.pivot = new Vector2(0f, 0.5f); bar.sizeDelta = new Vector2(5f, 0f);
        var barImage = bar.gameObject.AddComponent<Image>();
        barImage.color = accent; barImage.raycastTarget = false;
        rt.gameObject.AddComponent<GalleryHoverScale>().accent = accent;
    }

    void BuildCornerMark(RectTransform parent, Vector2 offset, Vector2 size, Color color, bool right = false)
    {
        var mark = NewRect("RegistrationMark", parent);
        mark.anchorMin = mark.anchorMax = new Vector2(right ? 1f : 0f, 1f);
        mark.pivot = new Vector2(right ? 1f : 0f, 1f);
        mark.anchoredPosition = offset;
        mark.sizeDelta = size;
        var image = mark.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
    }

    void BuildDetailUI(RectTransform parent)
    {
        _detailRoot = NewRect("DetailUI", parent);
        Stretch(_detailRoot);
        _detailGroup = _detailRoot.gameObject.AddComponent<CanvasGroup>();

        // Left menu strip.
        var strip = NewRect("MenuStrip", _detailRoot);
        _menuStrip = strip;    // its live width is one edge of the detail framing box
        strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(0f, 1f);
        strip.pivot = new Vector2(0f, 0.5f);
        strip.sizeDelta = new Vector2(380f, 0f);
        strip.anchoredPosition = Vector2.zero;
        var stripBg = strip.gameObject.AddComponent<Image>();
        stripBg.color = GeoPalette.WithAlpha(GeoPalette.Ink, 0.82f);
        stripBg.raycastTarget = true;

        var back = BuildTextButton(strip, "‹ BACK", new Vector2(0f, 1f), new Vector2(30f, -34f),
            () => { AudioManager.Instance?.PlayUISound(); GoOverview(false); });
        back.fontSize = 24f;

        // Category heading + gold rule at the top of the strip.
        _menuHeading = NewText("CatHeading", strip, 30f, GeoPalette.Gold, FontStyles.Bold);
        var chrt = _menuHeading.rectTransform;
        chrt.anchorMin = new Vector2(0f, 1f); chrt.anchorMax = new Vector2(1f, 1f); chrt.pivot = new Vector2(0.5f, 1f);
        chrt.anchoredPosition = new Vector2(0f, -84f); chrt.sizeDelta = new Vector2(-60f, 40f);
        _menuHeading.alignment = TextAlignmentOptions.Left;
        _menuHeading.characterSpacing = 10f;

        var stripRule = NewRect("Rule", strip);
        stripRule.anchorMin = new Vector2(0f, 1f); stripRule.anchorMax = new Vector2(0f, 1f); stripRule.pivot = new Vector2(0f, 1f);
        stripRule.anchoredPosition = new Vector2(30f, -128f); stripRule.sizeDelta = new Vector2(120f, 4f);
        var stripRuleImg = stripRule.gameObject.AddComponent<Image>();
        stripRuleImg.color = GeoPalette.Gold;
        stripRuleImg.raycastTarget = false;

        _menuList = NewRect("List", strip);
        _menuList.anchorMin = new Vector2(0f, 1f); _menuList.anchorMax = new Vector2(1f, 1f);
        _menuList.pivot = new Vector2(0.5f, 1f);
        _menuList.anchoredPosition = new Vector2(0f, -152f);
        _menuList.sizeDelta = new Vector2(-40f, 0f);
        var vlg = _menuList.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 8f; vlg.padding = new RectOffset(20, 20, 0, 0);
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true; vlg.childForceExpandHeight = false;
        _menuList.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        // Title (top centre, over the object) — ink on the pale sky, gold rule under.
        _titleText = NewText("Title", _detailRoot, 56f, GeoPalette.Ink, FontStyles.Bold);
        var trt = _titleText.rectTransform;
        trt.anchorMin = new Vector2(0.35f, 1f); trt.anchorMax = new Vector2(1f, 1f); trt.pivot = new Vector2(0.5f, 1f);
        trt.anchoredPosition = new Vector2(0f, -52f); trt.sizeDelta = new Vector2(-80f, 90f);
        _titleText.alignment = TextAlignmentOptions.Top;
        _titleText.characterSpacing = 6f;

        var titleRule = NewRect("TitleRule", _detailRoot);
        titleRule.anchorMin = new Vector2(0.35f, 1f); titleRule.anchorMax = new Vector2(1f, 1f); titleRule.pivot = new Vector2(0.5f, 1f);
        titleRule.anchoredPosition = new Vector2(0f, -126f); titleRule.sizeDelta = new Vector2(-460f, 5f);
        var titleRuleImg = titleRule.gameObject.AddComponent<Image>();
        titleRuleImg.color = GeoPalette.Gold;
        titleRuleImg.raycastTarget = false;

        _detailIndexText = NewText("Index", _detailRoot, 15f, GeoPalette.WithAlpha(GeoPalette.Ink, 0.68f), FontStyles.Bold);
        var irt = _detailIndexText.rectTransform;
        irt.anchorMin = irt.anchorMax = new Vector2(0.98f, 1f); irt.pivot = new Vector2(1f, 1f);
        irt.anchoredPosition = new Vector2(-40f, -38f); irt.sizeDelta = new Vector2(220f, 28f);
        _detailIndexText.alignment = TextAlignmentOptions.Right;
        _detailIndexText.characterSpacing = 2f;

        var navigationHint = NewText("NavigationHint", _detailRoot, 15f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.65f), FontStyles.Bold);
        var nrt = navigationHint.rectTransform;
        nrt.anchorMin = nrt.anchorMax = new Vector2(0.98f, 0f); nrt.pivot = new Vector2(1f, 0f);
        nrt.anchoredPosition = new Vector2(-40f, 16f); nrt.sizeDelta = new Vector2(370f, 26f);
        navigationHint.alignment = TextAlignmentOptions.Right;
        navigationHint.characterSpacing = 2f;
        navigationHint.text = "[ A / D ]   BROWSE   /   [ ESC ]   RETURN";

        // Description (bottom centre) — paper text on a translucent ink plaque,
        // like a caption card beside a hung work.
        var plaque = NewRect("Plaque", _detailRoot);
        _plaqueRect = plaque;  // and the caption card's height is the lower edge
        plaque.anchorMin = new Vector2(0.34f, 0f); plaque.anchorMax = new Vector2(0.98f, 0f); plaque.pivot = new Vector2(0.5f, 0f);
        plaque.anchoredPosition = new Vector2(0f, 44f); plaque.sizeDelta = new Vector2(0f, 170f);
        var plaqueImg = plaque.gameObject.AddComponent<Image>();
        plaqueImg.sprite = UIRoundedRect.Get(18);
        plaqueImg.type = Image.Type.Sliced;
        plaqueImg.color = GeoPalette.WithAlpha(GeoPalette.Ink, 0.78f);
        plaqueImg.raycastTarget = false;

        _descText = NewText("Desc", plaque, 24f, GeoPalette.Paper, FontStyles.Normal);
        var drt = _descText.rectTransform;
        drt.anchorMin = Vector2.zero; drt.anchorMax = Vector2.one;
        drt.offsetMin = new Vector2(28f, 18f); drt.offsetMax = new Vector2(-28f, -18f);
        _descText.alignment = TextAlignmentOptions.TopLeft;
        _descText.textWrappingMode = TextWrappingModes.Normal;

        // Prev / Next arrows.
        BuildArrow(_detailRoot, "‹", new Vector2(0.42f, 0.5f), () => Step(-1));
        BuildArrow(_detailRoot, "›", new Vector2(0.96f, 0.5f), () => Step(+1));
    }

    void RebuildMenu()
    {
        if (_menuHeading != null)
            _menuHeading.text = _cat switch
            {
                Cat.Music   => "MUSIC",
                Cat.Monster => "MONSTERS",
                _           => "SHADERS",
            };

        for (int i = 0; i < _menuButtons.Count; i++)
            if (_menuButtons[i] != null) Destroy(_menuButtons[i].gameObject);
        _menuButtons.Clear();

        int n = ItemCount();
        for (int i = 0; i < n; i++)
        {
            string name = _cat switch
            {
                Cat.Music   => _musicItems[i].title,
                Cat.Monster => _monsters[i].name,
                _           => _shaderItems[i].title,
            };
            int captured = i;
            var btn = BuildMenuRow(_menuList, name, () => { _index = captured; AudioManager.Instance?.PlayUISound(); ApplyItem(); HighlightMenu(); });
            _menuButtons.Add(btn);
        }
        HighlightMenu();
    }

    void HighlightMenu()
    {
        for (int i = 0; i < _menuButtons.Count; i++)
        {
            var img = _menuButtons[i] != null ? _menuButtons[i].GetComponent<Image>() : null;
            if (img != null) img.color = (i == _index) ? GeoPalette.WithAlpha(GeoPalette.Gold, 0.9f)
                                                       : GeoPalette.WithAlpha(GeoPalette.Paper, 0.12f);
            var t = _menuButtons[i] != null ? _menuButtons[i].GetComponentInChildren<TMP_Text>() : null;
            if (t != null) t.color = (i == _index) ? GeoPalette.Ink : GeoPalette.Paper;
        }
    }

    Button BuildMenuRow(RectTransform parent, string label, System.Action onClick)
    {
        var rt = NewRect(label, parent);
        rt.gameObject.AddComponent<LayoutElement>().minHeight = 52f;
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(12);
        img.type = Image.Type.Sliced;
        img.color = GeoPalette.WithAlpha(GeoPalette.Paper, 0.12f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<GalleryHoverScale>().accent = GeoPalette.Gold;

        var t = NewText("Label", rt, 22f, GeoPalette.Paper, FontStyles.Bold);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = new Vector2(16f, 0f); t.rectTransform.offsetMax = new Vector2(-16f, 0f);
        t.alignment = TextAlignmentOptions.Left;
        return btn;
    }

    void BuildArrow(RectTransform parent, string glyph, Vector2 anchor, System.Action onClick)
    {
        var rt = NewRect($"Arrow_{glyph}", parent);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(80f, 80f);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = UIRoundedRect.Get(40);
        img.type = Image.Type.Sliced;
        img.color = GeoPalette.WithAlpha(GeoPalette.Ink, 0.55f);
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<GalleryHoverScale>().accent = GeoPalette.Gold;
        var t = NewText("Glyph", rt, 48f, GeoPalette.Paper, FontStyles.Bold);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        t.alignment = TextAlignmentOptions.Center;
        t.text = glyph;
    }

    TMP_Text BuildTextButton(RectTransform parent, string label, Vector2 anchor, Vector2 pos, System.Action onClick)
    {
        var rt = NewRect($"Btn_{label}", parent);
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = new Vector2(180f, 48f);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = 24f; t.color = GeoPalette.Paper; t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Left; t.text = label;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = t;
        btn.onClick.AddListener(() => onClick());
        rt.gameObject.AddComponent<GalleryHoverScale>().accent = GeoPalette.Gold;
        return t;
    }

    // ── Camera ───────────────────────────────────────────────────────────────

    // One critically damped scalar drives the whole move, and the camera's ROTATION
    // is never authored or interpolated — it's derived every frame from where the
    // subject physically is. That pair of decisions is the entire fix:
    //
    //   · a spring eases IN as well as out. The old 1-exp(-camGlide*dt) chase left
    //     at maximum speed on frame one and then crawled, never quite arriving —
    //     which is also why nothing else on screen had anything to synchronise to;
    //   · a spring survives interruption as a PROPERTY, because it carries velocity.
    //     BACK pressed mid-push-in decelerates, turns and leaves;
    //   · a derived aim keeps the subject framed for every frame of the move.
    //     Slerping two quaternions walks the geodesic through orientation space,
    //     which has no relationship to where the exhibit actually is, so the subject
    //     swims out of frame mid-move and drifts back;
    //   · and because the target is re-solved every frame instead of baked at click
    //     time, the pointer parallax under _galleryRig stops fighting the camera:
    //     the subject stays pinned while its neighbours slide past it. That's the
    //     answer to "dead once it arrives", and it beats authored idle noise because
    //     the player causes it.

    // Reference travel and turn for the pacing law. Distance scales as a SQRT, so a
    // 6m move is quicker than a 3m one but not twice as quick; ANGLE scales linearly,
    // which pins peak turn rate regardless of how far apart the two poses are. Turn
    // rate — not linear speed — is what decides whether a move is comfortable.
    const float RefDist  = 4.5f;
    const float RefAngle = 18f;

    // Starts a new move. Safe to call at any point during another one.
    void RetargetCamera(bool instant)
    {
        if (_cam == null) return;
        ResolveTarget(out Vector3 eyeTo, out Vector3 aimTo, out float fovTo);

        // ALWAYS re-launch from where the lens actually is. Restarting from the pose
        // we happened to be aiming at is what teleports a camera on an interrupt.
        _posFrom = _cam.transform.position;
        _fovFrom = _cam.fieldOfView;
        _pivot   = aimTo;   // frozen for the duration: the point the arc swings around

        // Deriving the outgoing aim from the LIVE rotation rather than caching the
        // last one makes the hand-off exactly continuous even if something else moved
        // the camera, and costs one distance.
        float aimDepth = Mathf.Max(0.5f, Vector3.Distance(_posFrom, aimTo));
        _aimFrom = _posFrom + _cam.transform.forward * aimDepth;

        Vector3 seg = eyeTo - _posFrom;
        float segLen = Mathf.Max(0.05f, seg.magnitude);
        float angle  = Quaternion.Angle(_cam.transform.rotation, LookSafe(aimTo - eyeTo));

        float pace = Mathf.Clamp(Mathf.Max(Mathf.Sqrt(segLen / RefDist), angle / RefAngle), 0.8f, 1.75f);
        _smoothTime = Mathf.Max(0.05f, camSettleTime * pace
                    * (_view == View.Overview ? returnSpeedUp : 1f));

        // Keep only the part of the current motion that still points at the new
        // target — carrying raw speed through a reversal would be an instant velocity
        // flip. The cap is 2/smoothTime, exactly the initial velocity at which a
        // critically damped step stops overshooting: an interrupt (or a frame hitch's
        // velocity spike) can bend the move, but can never make it fly past its mark.
        float carried = Vector3.Dot(_camVel, seg / segLen) / segLen;
        _sVel = Mathf.Clamp(carried, 0f, 2f / _smoothTime);
        _s = 0f;

        if (instant)
        {
            // Awake calls this before the first Update, so it must leave no scrap of
            // in-flight state behind.
            _s = 1f; _sVel = 0f; _camVel = Vector3.zero;
            _posFrom = eyeTo; _aimFrom = aimTo; _fovFrom = fovTo;
            Vector3 look = aimTo - eyeTo;
            if (look.sqrMagnitude > 1e-8f) _cam.transform.SetPositionAndRotation(eyeTo, LookSafe(look));
            else                           _cam.transform.position = eyeTo;
            _cam.fieldOfView = fovTo;
        }
    }

    void UpdateCamera()
    {
        if (_cam == null) return;

        // Unscaled throughout. The panel crossfade already is, and a menu camera has
        // no business freezing because something elsewhere left timeScale at 0.
        // Mathf.SmoothDamp integrates the spring analytically per step, so this is
        // frame-rate independent with no fixed-step bookkeeping.
        float dt = Time.unscaledDeltaTime;

        ResolveTarget(out Vector3 eyeTo, out Vector3 aimTo, out float fovTo);

        _s = Mathf.Clamp01(Mathf.SmoothDamp(_s, 1f, ref _sVel, _smoothTime, Mathf.Infinity, dt));

        // The aim runs ahead of the body and locks before the dolly stops — an
        // operator frames first and finishes moving second. Clamping the lead BEFORE
        // the warp means the framing is genuinely locked for the last stretch while
        // the camera coasts in, and (1-x)² still has zero slope at x=1 so it settles
        // rather than snapping.
        float lead = Mathf.Clamp01(_s * (reducedCameraMotion ? 1f : Mathf.Max(1f, aimLead)));
        Vector3 aim = Vector3.Lerp(_aimFrom, aimTo, 1f - (1f - lead) * (1f - lead));

        Vector3 eye = ArcLerp(_posFrom, eyeTo, _pivot, _s, reducedCameraMotion ? 0f : camArc);

        // Measured from the commanded delta, so it's exact and free. RetargetCamera
        // projects it onto the next segment; that projection is the whole interrupt story.
        _camVel = dt > 1e-5f ? (eye - _cam.transform.position) / dt : Vector3.zero;

        Vector3 look = aim - eye;
        if (look.sqrMagnitude > 1e-8f) _cam.transform.SetPositionAndRotation(eye, LookSafe(look));
        else                           _cam.transform.position = eye;
        // Degrees, on the same scalar as everything else. What matters is that the
        // lens has no clock of its own — a FOV excursion the player can't attribute to
        // anything is a dolly zoom, and it's the one camera trick people consciously notice.
        _cam.fieldOfView = Mathf.Lerp(_fovFrom, fovTo, _s);
    }

    // Where the lens wants to be RIGHT NOW. Called every frame, not cached at click
    // time: FocusPos() reads a rig child, so a baked pose would leave the subject
    // sliding under a dead camera as the pointer parallax breathes.
    void ResolveTarget(out Vector3 eye, out Vector3 aim, out float fov)
    {
        if (_view == View.Overview)
        {
            eye = overviewCamPos;
            aim = overviewLookAt;
            fov = _baseFov;
            return;
        }

        Vector3 focus  = FocusPos(_cat);
        Transform mark = DetailCamFor(_cat);

        if (mark != null)
        {
            // The hand-placed mark decides where the lens STANDS — that's the whole
            // point of the per-section Transforms and it stays authoritative.
            eye = mark.position;

            // Its ROTATION is only trusted if it was actually aimed. All three marks
            // in Gallery.unity carry an identity rotation, so every detail shot was
            // pointing flat down world +Z rather than at its exhibit — which is why
            // the Music view framed empty air with the record below the bottom edge.
            float depth = Vector3.Dot(focus - eye, mark.forward);
            if (!aimFromSubject && depth > 0.35f)
            {
                // Project the subject onto the view axis rather than aiming straight
                // at it, so a deliberately off-centre composition survives intact.
                eye.y = Mathf.Max(eye.y, deskClearance);
                aim   = eye + mark.forward * depth;
                fov   = _baseFov;   // composed at the scene's own lens — don't re-crop it
                return;
            }

            fov = LensFor(ref eye, focus);
            eye.y = Mathf.Max(eye.y, deskClearance);
            aim = AimPoint(eye, focus, fov, DetailFraming());
            return;
        }

        // No mark for this section — the original offset-from-object fallback, with
        // the framing solved the same way so it benefits from the fix too.
        eye = focus + detailCamOffset;
        fov = LensFor(ref eye, focus);
        eye.y = Mathf.Max(eye.y, deskClearance);
        aim = aimFromSubject ? AimPoint(eye, focus, fov, DetailFraming())
                             : focus + detailLookOffset;
    }

    // Narrowing the lens would crop the hand-tuned framing tighter, so back the camera
    // off along its own view axis by exactly the amount that holds the subject the same
    // apparent size. Only the compression changes — which is the entire point of
    // reaching for a longer lens, and it's what makes detailFovDrop free.
    float LensFor(ref Vector3 eye, Vector3 focus)
    {
        float fov = Mathf.Clamp(_baseFov - (reducedCameraMotion ? 0f : detailFovDrop), 12f, 170f);
        Vector3 back = eye - focus;
        float d = back.magnitude;
        if (d < 1e-3f) return fov;

        float t0 = Mathf.Tan(Mathf.Clamp(_baseFov, 5f, 170f) * 0.5f * Mathf.Deg2Rad);
        float t1 = Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad);
        if (t1 > 1e-4f) eye = focus + back / d * (d * t0 / t1);
        return fov;
    }

    // The rotation that puts `focus` at normalised screen point `framing`, expressed
    // as the world point the frame CENTRE must sit on. Interpolating an aim point
    // rather than a quaternion is what keeps the subject nailed for every frame of the
    // move; asking for a screen position rather than a world offset is what keeps it
    // framed on a display that isn't 16:9.
    Vector3 AimPoint(Vector3 eye, Vector3 focus, float fov, Vector2 framing)
    {
        Vector3 to = focus - eye;
        float dist = to.magnitude;
        if (dist < 1e-3f) return focus + Vector3.forward;

        // Camera.fieldOfView is the VERTICAL angle, hence the aspect only on yaw.
        float tanH   = Mathf.Tan(Mathf.Clamp(fov, 5f, 170f) * 0.5f * Mathf.Deg2Rad);
        float aspect = _cam != null && _cam.aspect > 0.01f ? _cam.aspect : 16f / 9f;
        float yaw    = -Mathf.Atan((framing.x * 2f - 1f) * tanH * aspect) * Mathf.Rad2Deg;  // yaw left → subject moves right
        float pitch  =  Mathf.Atan((framing.y * 2f - 1f) * tanH)          * Mathf.Rad2Deg;  // nose down → subject moves up
        return eye + LookSafe(to) * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward * dist;
    }

    // The exhibit belongs in the middle of the area the detail UI does NOT occupy. A
    // hard-coded point is right at 1920x1080 and drifts everywhere else; deriving it
    // from the live rects also means it re-derives itself if anyone resizes the menu
    // strip or the caption plaque.
    Vector2 DetailFraming()
    {
        if (detailFraming.x > 0f && detailFraming.y > 0f) return detailFraming;   // inspector override

        float scale = _canvas != null ? _canvas.scaleFactor : 1f;
        // scaleFactor isn't final until the canvas has run once, so key the cache on it
        // as well as on the resolution rather than solving a single time at Awake.
        if (Screen.width != _framingW || Screen.height != _framingH
            || !Mathf.Approximately(scale, _framingScale))
        {
            _framingW = Screen.width; _framingH = Screen.height; _framingScale = scale;
            _framing = SolveFramingFromUI(scale);
        }
        return _framing;
    }

    Vector2 SolveFramingFromUI(float scale)
    {
        float w = Mathf.Max(1f, Screen.width);
        float h = Mathf.Max(1f, Screen.height);

        float left = _menuStrip != null
            ? Mathf.Clamp(_menuStrip.rect.width * scale / w, 0f, 0.5f) : 0.2f;
        float bottom = _plaqueRect != null
            ? Mathf.Clamp((_plaqueRect.anchoredPosition.y + _plaqueRect.sizeDelta.y) * scale / h, 0f, 0.45f) : 0.2f;
        float top = _titleText != null
            ? Mathf.Clamp((-_titleText.rectTransform.anchoredPosition.y + _titleText.rectTransform.sizeDelta.y) * scale / h, 0f, 0.45f) : 0.14f;

        return new Vector2(left   + (1f - left) * 0.5f,
                           bottom + Mathf.Max(0.1f, 1f - bottom - top) * 0.5f);
    }

    // Orbit-and-dolly rather than a straight chord. The point isn't the swoop — at
    // these distances the arc departs from the line by about 20cm — it's the RADIUS
    // LAW: apparent size goes as 1/d, so a linear position lerp on the Music approach
    // (7.5m → 2.9m) puts the halfway point at 5.2m, only 1.44× the start, and more
    // than half the perceived approach happens in the last quarter of the move. A
    // geometric radius puts it at sqrt(7.5*2.9) = 4.67m, the true perceptual middle.
    static Vector3 ArcLerp(Vector3 a, Vector3 b, Vector3 pivot, float s, float amount)
    {
        Vector3 straight = Vector3.Lerp(a, b, s);
        if (amount <= 0.001f) return straight;

        Vector3 oa = a - pivot, ob = b - pivot;
        float ra = oa.magnitude, rb = ob.magnitude;
        // Both guards are load-bearing, not tidiness: Vector3.Slerp with near-zero or
        // near-antiparallel inputs returns NaN, and a NaN written into a camera
        // transform is permanent — the view never comes back.
        if (ra < 0.05f || rb < 0.05f) return straight;
        Vector3 na = oa / ra, nb = ob / rb;
        if (Vector3.Dot(na, nb) < -0.98f) return straight;

        // Vector3.Slerp interpolates magnitude linearly, so feed it unit vectors and
        // carry the radius separately.
        Vector3 arc = pivot + Vector3.Slerp(na, nb, s) * (ra * Mathf.Pow(rb / ra, s));
        return Vector3.Lerp(straight, arc, Mathf.Clamp01(amount));
    }

    // Vector3.up is degenerate for a shot looking straight down — no pose here comes
    // close, but a hand-placed mark overhead would spam LookRotation errors.
    static Quaternion LookSafe(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-8f) return Quaternion.identity;
        dir.Normalize();
        return Quaternion.LookRotation(dir, Mathf.Abs(dir.y) > 0.999f ? Vector3.forward : Vector3.up);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style)
    {
        var rt = NewRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.fontSize = size;
        t.color = color;
        t.fontStyle = style;
        t.alignment = TextAlignmentOptions.TopLeft;
        t.raycastTarget = false;
        return t;
    }
}

// Marker for a clickable desk object; `cat` is the GalleryScreen Cat enum value.
public class GalleryPick : MonoBehaviour
{
    public int cat;
}

// Small, reusable interaction treatment for Gallery controls. It deliberately
// changes only transform scale so selected menu rows can keep their data-driven
// palette from GalleryScreen.HighlightMenu while still feeling tactile.
[DisallowMultipleComponent]
public class GalleryHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
    IPointerDownHandler, IPointerUpHandler
{
    [HideInInspector] public Color accent = Color.white;
    [Range(1f, 1.2f)] public float hoverScale = 1.045f;
    [Range(0.8f, 1f)] public float pressScale = 0.94f;
    [Min(1f)] public float speed = 18f;

    RectTransform _rect;
    Image _frame;
    Vector3 _baseScale;
    float _hover;
    float _press;

    void Awake()
    {
        _rect = transform as RectTransform;
        if (_rect == null) return;
        _baseScale = _rect.localScale;

        var frameGo = new GameObject("HoverFrame", typeof(RectTransform), typeof(Image));
        frameGo.transform.SetParent(_rect, false);
        frameGo.transform.SetAsFirstSibling();
        var frameRt = (RectTransform)frameGo.transform;
        frameRt.anchorMin = Vector2.zero; frameRt.anchorMax = Vector2.one;
        frameRt.offsetMin = new Vector2(-4f, -4f); frameRt.offsetMax = new Vector2(4f, 4f);
        _frame = frameGo.GetComponent<Image>();
        _frame.sprite = UIRoundedRect.GetFrame(12, 2);
        _frame.type = Image.Type.Sliced;
        _frame.color = GeoPalette.WithAlpha(accent, 0f);
        _frame.raycastTarget = false;
    }

    void OnEnable()
    {
        if (_rect != null) _baseScale = _rect.localScale;
    }

    void Update()
    {
        if (_rect == null) return;
        float target = Mathf.Lerp(1f, hoverScale, _hover) * Mathf.Lerp(1f, pressScale, _press);
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        _rect.localScale = Vector3.Lerp(_rect.localScale, _baseScale * target, k);
        if (_frame != null)
        {
            Color targetColor = GeoPalette.WithAlpha(accent, _hover * 0.9f + _press * 0.1f);
            _frame.color = Color.Lerp(_frame.color, targetColor, k);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hover = 1f;
        AudioManager.Instance?.PlayUISound();
    }

    public void OnPointerExit(PointerEventData eventData) => _hover = 0f;
    public void OnPointerDown(PointerEventData eventData) => _press = 1f;
    public void OnPointerUp(PointerEventData eventData) => _press = 0f;
}
