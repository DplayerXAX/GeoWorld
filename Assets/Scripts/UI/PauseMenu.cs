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

    [Header("Top-right controls (pause / speed)")]
    [Tooltip("Show the pause + speed buttons docked to the top-right corner.")]
    public bool  showControls  = true;
    [Tooltip("Margins from the right / top screen edges (px). Nudge to sit beside your WAVE counter.")]
    public float controlsRight = 12f;
    public float controlsTop   = 12f;
    public float controlSize   = 40f;
    public float controlGap    = 8f;
    public Color controlBg     = new Color(0f, 0f, 0f, 0.55f);
    public Color controlFg     = new Color(1f, 0.82f, 0.32f);   // gold

    bool  _paused;
    float _prevTimeScale = 1f;

    GUIStyle  _title, _btn, _iconLabel;
    Texture2D _overlay;
    bool _stylesBuilt;

    public bool IsPaused => _paused;

    void Update()
    {
        // While the settings overlay is open, Esc closes it (handled there) —
        // don't also toggle pause.
        if (Input.GetKeyDown(toggleKey) && !SettingsScreen.Open)
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
        EnsureStyles();
        DrawTopRightControls();

        if (!_paused || SettingsScreen.Open) return;   // settings overlay draws on top

        // Dim the whole screen behind the panel.
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height),
                        _overlay, ScaleMode.StretchToFill);

        // Centered panel.
        float w = panelWidth;
        float h = 300f;
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
        GUILayout.BeginArea(rect, GUI.skin.box);

        GUILayout.Space(12);
        GUILayout.Label("PAUSED", _title, GUILayout.ExpandWidth(true));
        GUILayout.Space(18);

        if (GUILayout.Button("Resume  (Esc)", _btn, GUILayout.Height(buttonHeight)))
            SetPaused(false);

        GUILayout.Space(8);
        if (GUILayout.Button("Settings", _btn, GUILayout.Height(buttonHeight)))
            SettingsScreen.Open = true;

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

    // ── Top-right pause + speed controls (always visible) ──────────────────────
    void DrawTopRightControls()
    {
        if (!showControls) return;

        float s = controlSize, g = controlGap;
        float y = controlsTop;
        float xPause = Screen.width - controlsRight - s;   // rightmost
        float xSpeed = xPause - g - s;                      // left of pause

        // Shared dark backing.
        Color prev = GUI.color;
        GUI.color  = controlBg;
        GUI.DrawTexture(new Rect(xSpeed - 4f, y - 4f, s * 2f + g + 8f, s + 8f), Texture2D.whiteTexture);
        GUI.color  = prev;

        // Pause / resume.
        var pauseRect = new Rect(xPause, y, s, s);
        if (GUI.Button(pauseRect, GUIContent.none, GUIStyle.none)) SetPaused(!_paused);
        DrawPauseGlyph(pauseRect, _paused);

        // Speed cycle (1× → 2× → 3×).
        var speedRect = new Rect(xSpeed, y, s, s);
        if (GUI.Button(speedRect, GUIContent.none, GUIStyle.none)) CycleSpeed();
        float shown = _paused ? _prevTimeScale : Time.timeScale;
        if (shown < 0.01f) shown = 1f;
        _iconLabel.normal.textColor = controlFg;
        GUI.Label(speedRect, $"{shown:0.##}×", _iconLabel);
    }

    void DrawPauseGlyph(Rect r, bool paused)
    {
        if (paused)
        {
            // Play triangle.
            _iconLabel.normal.textColor = controlFg;
            GUI.Label(r, "▶", _iconLabel);
            return;
        }

        // Pause: two vertical bars.
        Color prev = GUI.color;
        GUI.color  = controlFg;
        float bw = r.width * 0.13f;
        float bh = r.height * 0.42f;
        float cx = r.center.x, cy = r.center.y;
        GUI.DrawTexture(new Rect(cx - bw - 3f, cy - bh * 0.5f, bw, bh), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cx + 3f,      cy - bh * 0.5f, bw, bh), Texture2D.whiteTexture);
        GUI.color  = prev;
    }

    // Cycle the non-paused play speed: 1× → 2× → 3× → 1×.
    void CycleSpeed()
    {
        float cur  = _paused ? _prevTimeScale : Time.timeScale;
        float next = cur >= 2.95f ? 1f
                   : cur >= 1.95f ? 3f
                   : cur >= 0.95f ? 2f
                   :                1f;
        if (_paused) _prevTimeScale = next;   // applied when resumed
        else         Time.timeScale = next;
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

        _iconLabel = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 20,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
        };

        _overlay = new Texture2D(1, 1);
        _overlay.SetPixel(0, 0, overlayColor);
        _overlay.Apply();
    }
}
