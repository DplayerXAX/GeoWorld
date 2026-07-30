using UnityEngine;
using UnityEngine.SceneManagement;
using TMPro;

// Tutorial-level-only: three separate, borderless world-space labels spread
// left / center / right teaching the three camera controls (move / rotate /
// zoom). Each watches for its OWN action; once performed, it holds for
// actionHoldDelay then fades and destroys itself. The whole thing destroys
// once every label is gone.
// Auto-spawns — same RuntimeInitializeOnLoadMethod pattern as
// ShrineController/ChaosBlockController — no scene wiring needed.
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

    static void TrySpawn()
    {
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null) return;
        if (RunConfig.Level.levelId != "Tutorial") return;
        // GameFlowManager.Instance is only assigned in its Start(), which hasn't run
        // yet at this hook point — gate on PlacementController.Instance instead (set
        // in Awake(), same pattern as ShrineController/ChaosBlockController/LevelObjectivesTracker).
        if (PlacementController.Instance == null) return;   // gameplay scene only
        if (FindFirstObjectByType<CameraControlHintOverlay>() != null) return;
        new GameObject("CameraControlHintOverlay").AddComponent<CameraControlHintOverlay>();
    }

    [Header("Icons (optional — leave empty to show text only)")]
    public Sprite moveIcon;
    public Sprite rotateIcon;
    public Sprite zoomIcon;

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
        _labels[0] = AddLabel("Move", "WASDQE\nto move", moveIcon, () =>
            Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.S) ||
            Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.Q) || Input.GetKeyDown(KeyCode.E));
        _labels[1] = AddLabel("Zoom", "Scroll\nto zoom", zoomIcon, () =>
            Mathf.Abs(Input.GetAxis("Mouse ScrollWheel")) > 0.0001f);
        _labels[2] = AddLabel("Rotate", "Right drag\nto rotate view", rotateIcon, () =>
            Input.GetMouseButton(1) &&
            (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.01f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.01f));
    }

    CameraHintLabel AddLabel(string name, string text, Sprite icon, System.Func<bool> detect)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        TMP_Text tmp = null;
        if (icon != null)
        {
            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(go.transform, false);
            iconGo.transform.localPosition = Vector3.up * (iconWorldSize * 0.9f);
            var sr = iconGo.AddComponent<SpriteRenderer>();
            sr.sprite = icon;
            sr.color = textColor;
            float sz = iconWorldSize / Mathf.Max(icon.bounds.size.x, icon.bounds.size.y, 0.01f);
            iconGo.transform.localScale = Vector3.one * sz;
        }

        var textGo = new GameObject("Label");
        textGo.transform.SetParent(go.transform, false);
        tmp = textGo.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = fontWorldSize * 40f;   // TextMeshPro world units ≈ fontSize/40 world-space height
        tmp.color = textColor;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;

        return go.AddComponent<CameraHintLabel>().Init(tmp, icon != null ? go.transform.GetChild(0).GetComponent<SpriteRenderer>() : null, actionHoldDelay, fadeDuration, detect, textColor);
    }

    // One label's lifecycle: waits for `detect()`, holds, fades, self-destroys.
    class CameraHintLabel : MonoBehaviour
    {
        TMP_Text _tmp;
        SpriteRenderer _icon;
        System.Func<bool> _detect;
        float _holdDelay, _fadeDuration;
        Color _baseColor;
        bool _triggered;
        float _triggerTime;

        public CameraHintLabel Init(TMP_Text tmp, SpriteRenderer icon, float holdDelay, float fadeDuration, System.Func<bool> detect, Color baseColor)
        {
            _tmp = tmp; _icon = icon; _holdDelay = holdDelay; _fadeDuration = fadeDuration; _detect = detect; _baseColor = baseColor;
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
            if (_icon != null) { var c = _icon.color; c.a = alpha; _icon.color = c; }
            if (fadeT >= 1f) Destroy(gameObject);
        }
    }
}
