using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Tutorial-level-only: three separate, borderless world-space labels spread
// left / center / right teaching the three camera controls (move / rotate /
// zoom). Each watches for its OWN action; once performed, it holds for
// actionHoldDelay then fades and destroys itself. The whole thing destroys
// once every label is gone.
//
// Auto-spawns — same RuntimeInitializeOnLoadMethod pattern as
// ShrineController/ChaosBlockController — no scene wiring REQUIRED. But if you
// want to configure it (assign icons, retune spacing/colors/timing), drop a
// GameObject with THIS component on it into the (single, shared) gameplay
// scene and set the fields in the Inspector — TrySpawn()'s
// FindFirstObjectByType check sees your instance already exists and skips
// creating the blank auto-spawned one, so your scene instance runs instead
// with whatever you configured.
//
// IMPORTANT: every level plays through the SAME gameplay scene (there is no
// separate Tutorial scene), so a hand-placed instance sits in that shared
// scene file and would otherwise load for every level, not just the tutorial.
// The Tutorial-only check therefore lives in THIS component's own Awake() —
// not only in TrySpawn() — so it applies equally whether the object was
// auto-spawned or hand-placed: on any non-Tutorial level it deletes itself
// before Update() ever runs.
[DisallowMultipleComponent]
public class CameraControlHintOverlay : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static bool IsTutorialLevel()
        => RunConfig.Mode == GameMode.Level && RunConfig.Level != null && RunConfig.Level.levelId == "Tutorial";

    static void TrySpawn()
    {
        if (!IsTutorialLevel()) return;
        // GameFlowManager.Instance is only assigned in its Start(), which hasn't run
        // yet at this hook point — gate on PlacementController.Instance instead (set
        // in Awake(), same pattern as ShrineController/ChaosBlockController/LevelObjectivesTracker).
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<CameraControlHintOverlay>() != null) return;
        new GameObject("CameraControlHintOverlay").AddComponent<CameraControlHintOverlay>();
    }

    // Runs even for a hand-placed instance sitting in the shared gameplay scene —
    // see the class comment above. Self-removes on any non-Tutorial level before
    // Update() gets a chance to build the labels.
    void Awake()
    {
        if (!IsTutorialLevel()) Destroy(gameObject);
    }

    [Header("Icons (optional — leave empty to show text only; each hint can show several icons in a row, e.g. one per key)")]
    public Sprite[] moveIcons;
    public Sprite[] rotateIcons;
    public Sprite[] zoomIcons;
    [Tooltip("Horizontal gap between icons within the same hint's row, in world units.")]
    public float iconSpacing = 0.55f;

    [Header("Font (leave null for TMP default)")]
    public TMP_FontAsset font;
    [Tooltip("World-space text height, in world units.")]
    public float fontWorldSize = 0.18f;
    [Tooltip("World-space icon height, in world units.")]
    public float iconWorldSize = 0.5f;
    public Color textColor = new Color(0.949f, 0.937f, 0.902f, 0.95f);

    [Tooltip("Offset from the first start point.")]
    public Vector3 worldOffset = new Vector3(0f, 4.2f, 0f);
    [Tooltip("Horizontal spread between the left/center/right labels, in world units.")]
    public float spacing = 5f;
    [Tooltip("How far below the center label the left/right labels sit, in world units.")]
    public float sideYDrop = 2.2f;

    [Header("Timing")]
    [Tooltip("Seconds a label stays visible after its action fires, before fading out.")]
    public float actionHoldDelay = 0.8f;
    public float fadeDuration = 0.6f;

    Camera _cam;
    Vector3 _anchor;
    CameraHintLabel[] _labels;
    bool _built;

    void Update()
    {
        // Wait out the intro so the hint doesn't pop up mid pan-in.
        if (!_built)
        {
            if (IntroDirector.Playing) return;
            Build();
            _built = true;
        }

        if (_cam == null) _cam = Camera.main;
        if (_cam == null) return;

        Vector3 camRight = _cam.transform.right;
        float[] xOffsets = { -spacing, 0f, spacing };
        float[] yOffsets = { -sideYDrop, 0f, -sideYDrop };
        bool anyAlive = false;
        for (int i = 0; i < _labels.Length; i++)
        {
            var l = _labels[i];
            if (l == null) continue;
            anyAlive = true;
            Vector3 pos = _anchor + camRight * xOffsets[i] + Vector3.up * yOffsets[i];
            l.transform.position = pos;
            l.transform.rotation = Quaternion.LookRotation(pos - _cam.transform.position, Vector3.up);
        }

        if (!anyAlive) Destroy(gameObject);
    }

    void Build()
    {
        var gfm = GameFlowManager.Instance;
        _anchor = worldOffset;
        if (gfm != null && gfm.gridSystem != null && gfm.AllStarts.Count > 0)
            _anchor = gfm.gridSystem.GridToWorld(gfm.AllStarts[0]) + worldOffset;

        _labels = new CameraHintLabel[3];
        _labels[0] = AddLabel("Move", "WASDQE\nto move", moveIcons, () =>
            Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) ||
            Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E));
        _labels[1] = AddLabel("Zoom", "Scroll\nto zoom", zoomIcons, () =>
            Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f);
        _labels[2] = AddLabel("Rotate", "Right drag\nto rotate view", rotateIcons, () =>
            Input.GetMouseButton(1) &&
            (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.01f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.01f));
    }

    CameraHintLabel AddLabel(string name, string text, Sprite[] icons, System.Func<bool> detect)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        SpriteRenderer[] iconRends = BuildIconRow(go.transform, icons);

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        var tmp = textGo.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontWorldSize * 40f;   // TextMeshPro world units ≈ fontSize/40 world-space height
        tmp.color = textColor;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return go.AddComponent<CameraHintLabel>().Init(tmp, iconRends, actionHoldDelay, fadeDuration, detect, textColor);
    }

    // Builds a centred horizontal row of icons above the label — one hint can now
    // show several (e.g. separate W/A/S/D/Q/E key glyphs) instead of a single
    // generic icon. Empty/null entries in `icons` are skipped but still reserve
    // their slot in the row, so a sparse array (e.g. only Q/E filled in) still
    // spaces correctly around its neighbours.
    SpriteRenderer[] BuildIconRow(Transform parent, Sprite[] icons)
    {
        if (icons == null || icons.Length == 0) return System.Array.Empty<SpriteRenderer>();

        var rends = new SpriteRenderer[icons.Length];
        float totalWidth = (icons.Length - 1) * iconSpacing;
        for (int i = 0; i < icons.Length; i++)
        {
            if (icons[i] == null) continue;

            var iconGo = new GameObject($"Icon_{i}");
            iconGo.transform.SetParent(parent, false);
            float x = -totalWidth * 0.5f + i * iconSpacing;
            iconGo.transform.localPosition = new Vector3(x, iconWorldSize * 0.9f, 0f);
            var sr = iconGo.AddComponent<SpriteRenderer>();
            sr.sprite = icons[i];
            sr.color = textColor;
            float sz = iconWorldSize / Mathf.Max(icons[i].bounds.size.x, icons[i].bounds.size.y, 0.01f);
            iconGo.transform.localScale = Vector3.one * sz;
            rends[i] = sr;
        }
        return rends;
    }

    // One label's lifecycle: waits for `detect()`, holds, fades, self-destroys.
    class CameraHintLabel : MonoBehaviour
    {
        TMP_Text _tmp;
        SpriteRenderer[] _icons;
        System.Func<bool> _detect;
        float _holdDelay, _fadeDuration;
        Color _baseColor;
        bool _triggered;
        float _triggerTime;

        public CameraHintLabel Init(TMP_Text tmp, SpriteRenderer[] icons, float holdDelay, float fadeDuration, System.Func<bool> detect, Color baseColor)
        {
            _tmp = tmp; _icons = icons; _holdDelay = holdDelay; _fadeDuration = fadeDuration; _detect = detect; _baseColor = baseColor;
            return this;
        }

        void Update()
        {
            if (!_triggered)
            {
                if (_detect != null && _detect()) { _triggered = true; _triggerTime = Time.time; }
                return;
            }

            float t = Time.time - _triggerTime;
            if (t < _holdDelay) return;

            float fadeT = Mathf.Clamp01((t - _holdDelay) / Mathf.Max(0.01f, _fadeDuration));
            float alpha = 1f - fadeT;
            if (_tmp != null) { var c = _baseColor; c.a = alpha; _tmp.color = c; }
            if (_icons != null)
                for (int i = 0; i < _icons.Length; i++)
                {
                    if (_icons[i] == null) continue;
                    var c = _icons[i].color; c.a = alpha; _icons[i].color = c;
                }
            if (fadeT >= 1f) Destroy(gameObject);
        }
    }
}
