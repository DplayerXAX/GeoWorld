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

    [Header("Atelier materials")]
    [Tooltip("A cool graphite blue makes the desk recess while the exhibit accents stay vivid.")]
    public Color deskColor = new Color(0.055f, 0.085f, 0.12f);

    [Header("Camera poses")]
    public Vector3 overviewCamPos  = new Vector3(0.78f, 4.25f, -5.7f);
    public Vector3 overviewLookAt  = new Vector3(0f, 1.5f, 0.28f);
    [Tooltip("Detail camera position = focused object + this.")]
    public Vector3 detailCamOffset = new Vector3(2.7f, 1.05f, -3.05f);
    [Tooltip("Detail camera looks at focused object + this. Non-zero X pushes the object off-centre (right) so the left menu has room.")]
    public Vector3 detailLookOffset = new Vector3(-3.35f, -0.95f, 0.18f);
    public float   camGlide = 8f;

    [Header("Atelier presentation")]
    [Tooltip("Subtle movement applied to the complete exhibit composition as the pointer explores the screen.")]
    public float exhibitParallax = 0.11f;
    [Tooltip("How quickly the composition catches up to the pointer.")]
    public float parallaxGlide = 5f;
    [Tooltip("Seconds used by the overview/detail information crossfade.")]
    [Min(0.05f)] public float panelFadeTime = 0.32f;

    enum View { Overview, Detail }
    enum Cat  { Music, Monster, Shaders }

    View _view = View.Overview;
    Cat  _cat;
    int  _index;

    Camera _cam;
    Vector3    _camPosTarget;
    Quaternion _camRotTarget;
    Transform  _galleryRig;
    Vector2    _parallax;
    readonly List<Transform> _orbitAccents = new();

    // Desk objects
    GameObject        _recordObj;
    GameObject        _cubeObj;
    TitleCubeShowcase _showcase;
    GameObject        _monsterObj;
    SpriteRenderer    _monsterSprite;
    readonly List<TextMeshPro> _worldLabels = new();

    // Hover state (overview): the hovered pick swells slightly and its label gilds.
    GalleryPick _hovered;
    readonly Dictionary<GalleryPick, Vector3> _baseScales = new();
    readonly Dictionary<GalleryPick, TextMeshPro> _pickLabels = new();

    // Data
    static readonly (string file, string label, string desc)[] Shaders =
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

    static readonly (string name, string desc)[] Music =
    {
        ("Calm",   "The build phase. Quiet, patient, room to think between the tides of chaos."),
        ("Battle", "The wave. The score sharpens as the horde marches on your Core."),
    };

    struct MonsterEntry { public string name; public EnemySurfaceUnit prefab; public string desc; }
    readonly List<MonsterEntry> _monsters = new();

    // Detail UI
    Canvas   _canvas;
    RectTransform _detailRoot;
    RectTransform _overviewRoot;
    CanvasGroup _detailGroup;
    CanvasGroup _overviewGroup;
    bool _detailVisible;
    bool _overviewVisible;
    RectTransform _menuList;
    TMP_Text _titleText, _descText, _menuHeading, _detailIndexText, _overviewStatus;
    readonly List<Button> _menuButtons = new();

    void Awake()
    {
        _cam = Camera.main;
        if (_cam != null && (_cam.clearFlags != CameraClearFlags.Skybox || RenderSettings.skybox == null))
        {
            // Default to a paper-white room, while respecting a Gallery scene that
            // intentionally supplies its own atelier skybox material.
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = GeoPalette.Paper;
        }
        LoadMonsters();
        BuildDesk();
        BuildObjects();
        BuildUI();
        GoOverview(instant: true);
    }

    void Update()
    {
        // Camera glide
        if (_cam != null)
        {
            float k = 1f - Mathf.Exp(-camGlide * Time.deltaTime);
            _cam.transform.position = Vector3.Lerp(_cam.transform.position, _camPosTarget, k);
            _cam.transform.rotation = Quaternion.Slerp(_cam.transform.rotation, _camRotTarget, k);
        }

        UpdateAtelierMotion();
        UpdatePanelTransitions();

        // Idle spin for record + billboard sprite / labels
        if (_recordObj != null) _recordObj.transform.Rotate(0f, 35f * Time.deltaTime, 0f, Space.Self);
        BillboardToCamera(_monsterSprite != null ? _monsterSprite.transform : null);
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
            _parallax = Vector2.Lerp(_parallax, pointer, 1f - Mathf.Exp(-parallaxGlide * Time.deltaTime));
            _galleryRig.localPosition = new Vector3(_parallax.x, _parallax.y * 0.45f, 0f) * exhibitParallax;
            _galleryRig.localRotation = Quaternion.Euler(-_parallax.y * 1.3f, _parallax.x * 1.8f, 0f);
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
        bool interactive = visible && group.alpha > 0.96f;
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
            Vector3 target = kv.Value * (pick == _hovered ? 1.12f : 1f);
            pick.transform.localScale = Vector3.Lerp(pick.transform.localScale, target, k);

            if (_pickLabels.TryGetValue(pick, out var label) && label != null)
                label.color = Color.Lerp(label.color,
                    pick == _hovered ? GeoPalette.Gold : GeoPalette.Ink, k);
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
        _pickLabels[recordPick] = AddWorldLabel(_galleryRig, recordPos + Vector3.down * 0.9f, "MUSIC");
        BuildExhibitBase(recordPos, GeoPalette.Signal, "01");

        // Cube (Shaders) — the TitleCubeShowcase.
        _cubeObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cubeObj.name = "ShaderCube";
        _cubeObj.transform.SetParent(_galleryRig, false);
        _cubeObj.transform.position   = cubePos;
        _cubeObj.transform.localScale = Vector3.one * 1.3f;
        var mats = new List<Material>();
        foreach (var s in Shaders)
        {
            var m = Resources.Load<Material>($"Gallery/Shaders/{s.file}");
            if (m != null) mats.Add(m);
        }
        _showcase = _cubeObj.AddComponent<TitleCubeShowcase>();
        _showcase.materials = mats;
        _showcase.autoAdvance = false;   // never auto-cycles — only the detail ‹ / › arrows change it
        _showcase.ShowIndex(0);
        var cubePick = MakePickable(_cubeObj, Cat.Shaders);
        _pickLabels[cubePick] = AddWorldLabel(_galleryRig, cubePos + Vector3.down * 1.2f, "SHADERS");
        BuildExhibitBase(cubePos + Vector3.down * cubePlinthDrop, GeoPalette.Blue, "02");

        // Monster — a billboarded portrait card standing on the desk.
        _monsterObj = new GameObject("Monster");
        _monsterObj.transform.SetParent(_galleryRig, false);
        _monsterObj.transform.position = monsterPos;
        _monsterSprite = _monsterObj.AddComponent<SpriteRenderer>();
        var box = _monsterObj.AddComponent<BoxCollider>();
        box.size = new Vector3(1.4f, 1.4f, 0.2f);
        if (_monsters.Count > 0)
            EnemyThumbnail.Request(_monsters[0].prefab, s => ApplyMonsterSprite(s));
        var monsterPick = MakePickable(_monsterObj, Cat.Monster);
        _pickLabels[monsterPick] = AddWorldLabel(_galleryRig, monsterPos + Vector3.down * 1.0f, "MONSTER");
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

    void ApplyMonsterSprite(Sprite s)
    {
        if (_monsterSprite == null || s == null) return;
        _monsterSprite.sprite = s;
        // Scale so the 128px sprite reads ~1.4 world units tall.
        float h = s.rect.height / s.pixelsPerUnit;
        Vector3 scale = Vector3.one * (1.4f / Mathf.Max(0.01f, h));
        _monsterObj.transform.localScale = scale;
        // Keep the hover system's rest-scale in sync, else it eases back to the old one.
        var pick = _monsterObj.GetComponent<GalleryPick>();
        if (pick != null) _baseScales[pick] = scale;
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
        tmp.color = GeoPalette.Ink;   // ink on the pale atelier sky
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        _worldLabels.Add(tmp);
        return tmp;
    }

    void LoadMonsters()
    {
        var balance = Resources.Load<BalanceTable>("Gallery/Gallery_BalanceTable");
        if (balance?.enemies == null) return;
        foreach (var rec in balance.enemies)
        {
            if (rec == null || rec.prefab == null) continue;
            string desc = $"Health {rec.maxHealth}   ·   Speed {rec.speedMultiplier:0.##}×   ·   Bounty {rec.rewardOnKill}\nFirst appears round {rec.minRound + 1}.";
            _monsters.Add(new MonsterEntry { name = rec.name, prefab = rec.prefab, desc = desc });
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    void GoOverview(bool instant)
    {
        _view = View.Overview;
        if (_battleMood) SetMood(false);
        SetPanelVisible(_detailGroup, false, instant);
        SetPanelVisible(_overviewGroup, true, instant);
        if (_overviewStatus != null) _overviewStatus.text = "THREE STUDIES  /  SELECT AN EXHIBIT";

        SetCameraPose(overviewCamPos, Quaternion.LookRotation(overviewLookAt - overviewCamPos, Vector3.up), instant);
    }

    void EnterDetail(Cat cat)
    {
        AudioManager.Instance?.PlayUISound();
        _view = View.Detail;
        _cat  = cat;
        _index = 0;
        if (cat != Cat.Music && _battleMood) SetMood(false);
        SetPanelVisible(_overviewGroup, false, false);
        SetPanelVisible(_detailGroup, true, false);

        Vector3 focus = FocusPos(cat);
        Vector3 pos = focus + detailCamOffset;
        Vector3 look = focus + detailLookOffset;
        SetCameraPose(pos, Quaternion.LookRotation(look - pos, Vector3.up), instant: false);

        RebuildMenu();
        ApplyItem();
    }

    Vector3 FocusPos(Cat cat) => cat switch
    {
        Cat.Music   => _recordObj  != null ? _recordObj.transform.position  : recordPos,
        Cat.Monster => _monsterObj != null ? _monsterObj.transform.position : monsterPos,
        _           => _cubeObj    != null ? _cubeObj.transform.position    : cubePos,
    };

    int ItemCount() => _cat switch
    {
        Cat.Music   => Music.Length,
        Cat.Monster => _monsters.Count,
        _           => Shaders.Length,
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
                _titleText.text = Music[_index].name;
                _descText.text  = Music[_index].desc;
                SetMood(_index == 1);
                break;
            case Cat.Monster:
                var m = _monsters[_index];
                _titleText.text = m.name;
                _descText.text  = m.desc;
                EnemyThumbnail.Request(m.prefab, s => ApplyMonsterSprite(s));
                break;
            default:
                _titleText.text = Shaders[_index].label;
                _descText.text  = Shaders[_index].desc;
                if (_showcase != null) _showcase.ShowIndex(_index);
                break;
        }
    }

    bool _battleMood;
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
                Cat.Music   => Music[i].name,
                Cat.Monster => _monsters[i].name,
                _           => Shaders[i].label,
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

    void SetCameraPose(Vector3 pos, Quaternion rot, bool instant)
    {
        _camPosTarget = pos;
        _camRotTarget = rot;
        if (instant && _cam != null)
        {
            _cam.transform.position = pos;
            _cam.transform.rotation = rot;
        }
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
