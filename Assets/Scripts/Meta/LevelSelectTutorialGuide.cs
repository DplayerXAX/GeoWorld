using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Visual accompaniment for LevelMapController's existing hands-on tutorial gate
// system (Walk / EnterLevel / OpenBuild / Place, driven by firstVisitConversation
// + T_rewardBlock). The dialogue text alone never says WHERE to click — this
// reacts to LevelMapController.OnTutorialGateChanged and adds:
//   - Walk / OpenBuild / Place (all anchored to the pawn's current position): a
//     bobbing world-space arrow over the pawn, plus a single eased camera pan
//     there (non-blocking — the player can immediately pan away again). Walk
//     ("You start out at your home block...") also zooms the camera in.
//   - EnterLevel (a fixed-position UI panel, not a world object): a bouncing
//     UI arrow beside the panel's Enter button, PLUS a world-space arrow over
//     enterLevelTargetId's node ("only this one is connected...").
//
// Purely additive: never touches gating/dialogue content, only reacts to the
// event. Auto-spawns — same RuntimeInitializeOnLoadMethod pattern as
// ChaosBlockController/ShrineController — no scene wiring needed.
[DisallowMultipleComponent]
public class LevelSelectTutorialGuide : MonoBehaviour
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
        if (LevelMapController.Instance == null) return;   // LevelSelect scene only
        if (FindFirstObjectByType<LevelSelectTutorialGuide>() != null) return;
        new GameObject("LevelSelectTutorialGuide").AddComponent<LevelSelectTutorialGuide>();
    }

    [Header("World arrow (Walk / OpenBuild / Place)")]
    [Tooltip("Height above the target (pawn, or a fixed level node) the floating arrow hovers at.")]
    public float arrowHeight = 1f;
    public float bobAmplitude = 0.05f;
    public float bobSpeed = 1.1f;
    public Color worldArrowColor = new Color(0.95f, 0.75f, 0.25f);
    [Tooltip("World-space size of the arrow (its longest dimension).")]
    public float arrowSize = 0.5f;

    [Header("Walk gate camera zoom-in")]
    [Tooltip("When the 'start' line (ls.walk gate) fires, the camera zooms to this OrbitCamera distance/orthoSize instead of just panning — reads as a deliberate zoom-in on the home block. LevelSelect's default orbit distance is 10.")]
    public float walkFocusZoom = 5f;

    [Header("Tutorial-level arrow (EnterLevel)")]
    [Tooltip("levelId of the level the EnterLevel gate ('only this one is connected') should point the world arrow at.")]
    public string enterLevelTargetId = "Tutorial";
    [Tooltip("Height for the fixed arrow over enterLevelTargetId's node — taller than arrowHeight since that node sits under floating decor cubes that would otherwise occlude it.")]
    public float tutorialNodeArrowHeight = 2f;

    [Header("UI pointer (EnterLevel)")]
    [Tooltip("Distance from the Enter button's left edge, in canvas units, before the bob is added.")]
    public float uiPointerOffset = 30f;
    public float uiPointerBobPixels = 8f;
    public Color uiPointerColor = new Color(0.95f, 0.75f, 0.25f);

    Mesh _arrowMesh;

    Transform _worldArrow;
    bool _worldArrowFollowsPawn;      // true: tracks lmc.pawn every frame; false: hovers _worldArrowFixedPos
    Vector3 _worldArrowFixedPos;
    float _worldArrowHeight;
    RectTransform _uiPointer;
    string _activeGate;
    Camera _cam;
    OrbitCamera _orbit;

    void OnEnable()  => LevelMapController.OnTutorialGateChanged += HandleGateChanged;
    void OnDisable() => LevelMapController.OnTutorialGateChanged -= HandleGateChanged;

    void HandleGateChanged(string gateId)
    {
        _activeGate = gateId;
        ClearWorldArrow();
        ClearUiPointer();
        if (string.IsNullOrEmpty(gateId)) return;

        var lmc = LevelMapController.Instance;
        if (lmc == null) return;

        if (gateId == LevelMapController.TutorialGateIds.EnterLevel)
        {
            var enterRect = lmc.infoPanel != null ? lmc.infoPanel.EnterButtonRect : null;
            if (enterRect != null) ShowUiPointer(enterRect);

            // "Right now only this one is connected" — also point a world arrow
            // at the actual level node being talked about, not just the button.
            var targetPos = lmc.FindLevelNodePosition(enterLevelTargetId);
            if (targetPos.HasValue)
            {
                ShowWorldArrow(targetPos.Value, height: tutorialNodeArrowHeight);
            }
            return;
        }

        // Walk / OpenBuild / Place all happen right where the pawn is standing.
        if (lmc.pawn != null)
        {
            ShowWorldArrow(lmc.pawn.position, followPawn: true, height: arrowHeight);
            FocusCameraOnce(lmc.pawn.position, zoomIn: gateId == LevelMapController.TutorialGateIds.Walk);
        }
    }

    void FocusCameraOnce(Vector3 worldPos, bool zoomIn = false)
    {
        if (_cam == null) _cam = Camera.main;
        if (_orbit == null && _cam != null) _orbit = _cam.GetComponent<OrbitCamera>();
        _orbit?.FocusOnPoint(worldPos, snap: false);   // eased pan, not a jump-cut
        if (zoomIn && walkFocusZoom > 0f) _orbit?.SetZoom(walkFocusZoom);
    }

    void ShowWorldArrow(Vector3 worldPos, bool followPawn = false, float height = -1f)
    {
        _worldArrowFollowsPawn = followPawn;
        _worldArrowFixedPos    = worldPos;
        _worldArrowHeight      = height >= 0f ? height : arrowHeight;

        var go = new GameObject("TutorialArrow");
        _worldArrow = go.transform;

        var mf = go.AddComponent<MeshFilter>();
        mf.sharedMesh = ArrowMesh();
        var mr = go.AddComponent<MeshRenderer>();
        var sh = Shader.Find("GeoWorld/StatusArrow");
        var mat = new Material(sh != null ? sh : Shader.Find("Universal Render Pipeline/Unlit"))
        {
            name = "LevelSelectTutorialArrow",
        };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", worldArrowColor);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    void ClearWorldArrow()
    {
        if (_worldArrow != null) Destroy(_worldArrow.gameObject);
        _worldArrow = null;
    }

    void ShowUiPointer(RectTransform target)
    {
        // Parented to the BUTTON itself (not a sibling in whatever layout group
        // hosts it) so it can't get swept into that group's own layout pass.
        var go = new GameObject("TutorialPointer", typeof(RectTransform));
        go.transform.SetParent(target, false);
        _uiPointer = (RectTransform)go.transform;
        _uiPointer.anchorMin = _uiPointer.anchorMax = new Vector2(0f, 0.5f);   // left edge of the button
        _uiPointer.pivot = new Vector2(1f, 0.5f);
        _uiPointer.sizeDelta = new Vector2(36f, 36f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = "›";   // same glyph GalleryScreen's ‹ / › arrows use — ▶ isn't in the project's TMP font atlas and rendered as a missing-glyph box
        tmp.fontSize = 30f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = uiPointerColor;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
    }

    void ClearUiPointer()
    {
        if (_uiPointer != null) Destroy(_uiPointer.gameObject);
        _uiPointer = null;
    }

    void Update()
    {
        if (_worldArrow != null)
        {
            Vector3 anchor;
            if (_worldArrowFollowsPawn)
            {
                var lmc = LevelMapController.Instance;
                if (lmc == null || lmc.pawn == null) { ClearWorldArrow(); return; }
                anchor = lmc.pawn.position;
            }
            else
            {
                anchor = _worldArrowFixedPos;
            }

            if (_cam == null) _cam = Camera.main;
            float bob = Mathf.Sin(Time.time * bobSpeed) * bobAmplitude;
            _worldArrow.position = anchor + Vector3.up * (_worldArrowHeight + bob);
            if (_cam != null)
                _worldArrow.rotation = Quaternion.LookRotation(_cam.transform.forward, Vector3.up);
        }

        if (_uiPointer != null)
        {
            float bob = Mathf.Abs(Mathf.Sin(Time.time * bobSpeed)) * uiPointerBobPixels;
            _uiPointer.anchoredPosition = new Vector2(-(uiPointerOffset + bob), 0f);
        }
    }

    // A single downward-pointing arrow — same shape language as StatusArrowFx's,
    // just one instance instead of a pooled stream. Cached per-component (not
    // static) since it's sized from the instance's own arrowSize field.
    Mesh ArrowMesh()
    {
        if (_arrowMesh != null) return _arrowMesh;

        float halfShaft = 0.11f * arrowSize;
        float halfHead  = 0.30f * arrowSize;
        float neckY     = 0.06f * arrowSize;
        float half      = 0.5f * arrowSize;

        var verts = new[]
        {
            new Vector3(-halfShaft,  half,  0f),
            new Vector3( halfShaft,  half,  0f),
            new Vector3( halfShaft,  neckY, 0f),
            new Vector3(-halfShaft,  neckY, 0f),
            new Vector3(-halfHead,   neckY, 0f),
            new Vector3( halfHead,   neckY, 0f),
            new Vector3( 0f,        -half,  0f),   // tip points DOWN — at the target
        };
        var uvs = new Vector2[verts.Length];
        for (int i = 0; i < verts.Length; i++)
            uvs[i] = new Vector2(verts[i].x / arrowSize + 0.5f, 1f - (verts[i].y / arrowSize + 0.5f));

        var m = new Mesh { name = "TutorialArrow" };
        m.vertices  = verts;
        m.uv        = uvs;
        m.triangles = new[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 };
        m.RecalculateBounds();
        _arrowMesh = m;
        return m;
    }
}
