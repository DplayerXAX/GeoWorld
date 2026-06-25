using UnityEngine;

public class PauseMenu : MonoBehaviour
{
    [Header("Hotkey")]
    public KeyCode toggleKey = KeyCode.Escape;

    [Tooltip("Scene loaded by the 'Back to Title' button (must be in Build Settings).")]
    public string titleScene = "Title";

    [Header("Look")]
    public Color overlayColor = new Color(0f, 0f, 0f, 0.6f);
    public Color titleColor = new Color(0.6f, 0.95f, 1f);
    public float panelWidth = 260f;
    public float buttonHeight = 42f;

    [Header("Top-right controls")]
    public bool showControls = true;
    public float controlsRight = 12f;
    public float controlsTop = 12f;
    public float controlSize = 50f;
    public Color controlFg = new Color(1f, 1f, 1f);

    bool _paused;
    float _prevTimeScale = 1f;

    GUIStyle _title, _btn, _iconLabel, _diamondStyle, _fastForwardStyle;
    Texture2D _overlay;
    Material _glMat;
    bool _stylesBuilt;

    public bool IsPaused => _paused;
    public static bool Paused;

    void Update()
    {
        if (Input.GetKeyDown(toggleKey) && !SettingsScreen.Open)
            SetPaused(!_paused);
    }

    void OnDisable()
    {
        if (_paused)
        {
            Time.timeScale = _prevTimeScale;
            _paused = false;
        }
        Paused = false;
        if (_overlay != null) { Destroy(_overlay); _overlay = null; _stylesBuilt = false; }
        if (_glMat != null)   { Destroy(_glMat);   _glMat = null; }
    }

    public void SetPaused(bool paused)
    {
        if (paused == _paused) return;
        _paused = paused;
        Paused = paused;

        if (_paused)
        {
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
        if (IntroDirector.Playing) return;   // hidden behind the intro overlay
        EnsureStyles();
        DrawTopRightControls();

        if (!_paused || SettingsScreen.Open) return;

        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), _overlay, ScaleMode.StretchToFill);

        float w = panelWidth;
        float h = 44f + 5 * (buttonHeight + 8f) + 28f;
        var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);

        // Paper panel — ink top rule + signal spine (constructivist).
        Fill(rect, GeoPalette.Paper);
        Fill(new Rect(rect.x, rect.y, rect.width, 5f), GeoPalette.Ink);
        Fill(new Rect(rect.x, rect.y, 6f, rect.height), GeoPalette.Signal);

        GUILayout.BeginArea(new Rect(rect.x + 20f, rect.y + 14f, rect.width - 40f, rect.height - 26f));
        GUILayout.Label("PAUSED", _title, GUILayout.ExpandWidth(true));
        GUILayout.Space(16);

        var prevBg = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.86f, 0.85f, 0.81f);   // paper-tinted buttons

        if (GUILayout.Button("Resume",   _btn, GUILayout.Height(buttonHeight))) SetPaused(false);
        GUILayout.Space(8);
        if (GUILayout.Button("Settings", _btn, GUILayout.Height(buttonHeight))) SettingsScreen.Open = true;
        GUILayout.Space(8);
        if (GUILayout.Button("Restart",  _btn, GUILayout.Height(buttonHeight)))
        {
            Time.timeScale = 1f;
            _paused = false;
            GameFlowManager.Instance?.RestartGame();
        }
        GUILayout.Space(8);
        if (GUILayout.Button("Back to Title", _btn, GUILayout.Height(buttonHeight))) GoToTitle();
        GUILayout.Space(8);

        GUI.backgroundColor = new Color(0.86f, 0.36f, 0.32f);   // red-tinted quit
        if (GUILayout.Button("Quit", _btn, GUILayout.Height(buttonHeight))) QuitGame();
        GUI.backgroundColor = prevBg;

        GUILayout.EndArea();
    }

    static void Fill(Rect r, Color c)
    {
        var p = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = p;
    }

    void GoToTitle()
    {
        Time.timeScale = 1f;
        _paused = false; Paused = false;
        LoadingScreen.Go(titleScene);
    }

    void DrawTopRightControls()
    {
        if (!showControls) return;

        // White icons while the shop (black letterbox bar) is open, black otherwise.
        bool shopOpen = ShopController.Instance != null && ShopController.Instance.IsExpanded;
        controlFg = shopOpen ? Color.white : Color.black;

        float ui = Screen.height / 1080f;
        float scale = ui * 2f;
        float s = controlSize * scale;
        float gap = 1f * scale;

        float xPause = Screen.width - (controlsRight * ui) - s;
        float xSpeed = xPause - gap - s;
        float y = controlsTop * ui;

        var pauseRect = new Rect(xPause, y, s, s);
        if (GUI.Button(pauseRect, GUIContent.none, GUIStyle.none)) SetPaused(!_paused);
        DrawPauseGlyph(pauseRect, _paused);

        var speedRect = new Rect(xSpeed, y+4f, s, s);
        if (GUI.Button(speedRect, GUIContent.none, GUIStyle.none)) CycleSpeed();

        _fastForwardStyle.normal.textColor = controlFg;
        DrawFastForwardGlyph(speedRect);

        float speed = _paused ? _prevTimeScale : Time.timeScale;
        int level = speed >= 2.95f ? 3 : (speed >= 1.95f ? 2 : 1);

        // Diamonds scale with the icon (font was fixed → looked tiny + far apart).
        float dFont = s * 0.45f;
        _diamondStyle.fontSize = Mathf.Max(8, Mathf.RoundToInt(dFont));
        _diamondStyle.normal.textColor = controlFg;   // white when shop open, black otherwise
        float cell    = dFont * 0.7f;     // tight columns
        float spacing = dFont * 0f;
        float dRowH   = dFont * 1.25f;
        float diamondY = y + s - dRowH+8f;    // just inside the bottom edge

        float totalW = (cell * 3f) + (spacing * 2f);
        float startX = xSpeed + (s - totalW) * 0.5f;

        for (int i = 0; i < 3; i++)
        {
            Rect dRect = new Rect(startX + (i * (cell + spacing)), diamondY, cell, dRowH);
            GUI.Label(dRect, i < level ? "◆" : "◇", _diamondStyle);
        }
    }

    void DrawTriangle(Vector2 a, Vector2 b, Vector2 c)
    {
        GL.Begin(GL.TRIANGLES);
        GL.Color(controlFg);

        GL.Vertex(a);
        GL.Vertex(b);
        GL.Vertex(c);

        GL.End();
    }
    void DrawFastForwardGlyph(Rect r)
    {
        if (Event.current.type != EventType.Repaint) return;   // GL only valid on Repaint

        EnsureGlMaterial();
        Color prev = GUI.color;
        GUI.color = controlFg;

        GL.PushMatrix();
        GL.LoadPixelMatrix();
        _glMat.SetPass(0);   // required before GL.Begin/End

        float w = r.width;
        float h = r.height;

        float triW = w * 0.3f;     // wider → chunkier, less pointy
        float triH = h * 0.42f;

        float x = r.x + w * 0.24f;  // recentred for the wider pair
        float y = r.y + h * 0.45f;  // lift a touch so the diamonds sit below

        // left triangle
        DrawTriangle(
            new Vector2(x, y - triH / 2),
            new Vector2(x, y + triH / 2),
            new Vector2(x + triW, y)
        );

        // right triangle
        DrawTriangle(
            new Vector2(x + triW * 0.7f, y - triH / 2),
            new Vector2(x + triW * 0.7f, y + triH / 2),
            new Vector2(x + triW * 1.7f, y)
        );

        GL.PopMatrix();
        GUI.color = prev;
    }
    void DrawPauseGlyph(Rect r, bool paused)
    {
        Color prev = GUI.color;
        GUI.color = controlFg;
        if (paused)
        {
            GUI.Label(r, "▶", _iconLabel);
        }
        else
        {
            float bw = r.width * 0.15f;
            float bh = r.height * 0.5f;
            float cx = r.center.x, cy = r.center.y;
            GUI.DrawTexture(new Rect(cx - bw - 4f, cy - bh * 0.5f, bw, bh), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 4f, cy - bh * 0.5f, bw, bh), Texture2D.whiteTexture);
        }
        GUI.color = prev;
    }

    void CycleSpeed()
    {
        float cur = _paused ? _prevTimeScale : Time.timeScale;
        float next = cur >= 2.95f ? 1f : cur >= 1.95f ? 3f : 2f;
        if (_paused) _prevTimeScale = next;
        else Time.timeScale = next;
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

        _title = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _title.normal.textColor = GeoPalette.Ink;
        _btn = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold };
        _btn.normal.textColor = _btn.hover.textColor = _btn.active.textColor = GeoPalette.Ink;
        _iconLabel = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _fastForwardStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        _diamondStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };

        _overlay = new Texture2D(1, 1);
        _overlay.SetPixel(0, 0, overlayColor);
        _overlay.Apply();
    }

    void EnsureGlMaterial()
    {
        if (_glMat != null) return;
        var sh = Shader.Find("Hidden/Internal-Colored");
        _glMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
        _glMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _glMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _glMat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);
        _glMat.SetInt("_ZWrite",   0);
    }
}
