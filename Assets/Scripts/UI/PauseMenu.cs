using UnityEngine;

// Simple pause menu — press Esc to toggle. Drop on any persistent scene object
// (e.g. the GameManager / GameFlowManager object). No Canvas / EventSystem /
// inspector wiring required: uses IMGUI to match DevPanel and TopLeftHUD.
//
// Pausing sets Time.timeScale = 0, which freezes everything driven by scaled
// time (enemies, WaitForSeconds coroutines, animations). OnGUI and Input keep
// running at timeScale 0, so the menu stays interactive while the game is frozen.
public class PauseMenu : MonoBehaviour
{
    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.Escape;

    [Header("Look")]
    [Tooltip("Full-screen dim drawn behind the menu while paused.")]
    public Color overlayColor = new Color(0f, 0f, 0f, 0.6f);
    public Color titleColor   = new Color(0.6f, 0.95f, 1f);
    public float panelWidth   = 260f;
    public float buttonHeight = 42f;

    bool  _paused;
    float _prevTimeScale = 1f;

    GUIStyle  _title, _btn;
    Texture2D _overlay;
    bool _stylesBuilt;

    public bool IsPaused => _paused;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            SetPaused(!_paused);
    }

    void OnDisable()
    {
        // Never leave the game frozen if this object is disabled/destroyed mid-pause.
        if (_paused)
        {
            Time.timeScale = _prevTimeScale;
            _paused = false;
        }
        if (_overlay != null) { Destroy(_overlay); _overlay = null; _stylesBuilt = false; }
    }

    // Public so other systems (e.g. a phone-style pause button) can drive it too.
    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;

        if (_paused)
        {
            // Remember the current speed (DevPanel may have set 0.25x / 2x) and freeze.
            _prevTimeScale = Time.timeScale > 0.0001f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }
        else
        {
            Time.timeScale = _prevTimeScale;
        }
    }

    void OnGUI()
    {
        if (!_paused) return;
        EnsureStyles();

        // Dim the whole screen behind the panel.
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height),
                        _overlay, ScaleMode.StretchToFill);

        // Centered panel.
        float w = panelWidth;
        float h = 250f;
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Space(12);
        GUILayout.Label("PAUSED", _title, GUILayout.ExpandWidth(true));
        GUILayout.Space(18);

        if (GUILayout.Button("Resume  (Esc)", _btn, GUILayout.Height(buttonHeight)))
            SetPaused(false);

        GUILayout.Space(8);
        if (GUILayout.Button("Restart", _btn, GUILayout.Height(buttonHeight)))
        {
            Time.timeScale = 1f;          // timeScale persists across scene loads — unfreeze first
            _paused = false;
            GameFlowManager.Instance?.RestartGame();
        }

        GUILayout.Space(8);
        if (GUILayout.Button("Quit", _btn, GUILayout.Height(buttonHeight)))
            QuitGame();

        GUILayout.EndArea();
    }

    static void QuitGame()
    {
        Time.timeScale = 1f;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void EnsureStyles()
    {
        if (_stylesBuilt) return;
        _stylesBuilt = true;

        _title = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 22,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        _title.normal.textColor = titleColor;

        _btn = new GUIStyle(GUI.skin.button) { fontSize = 15 };

        _overlay = new Texture2D(1, 1);
        _overlay.SetPixel(0, 0, overlayColor);
        _overlay.Apply();
    }
}
