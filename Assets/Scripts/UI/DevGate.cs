using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A password prompt in front of an unfinished feature.
//
// This keeps a work-in-progress mode out of a player's way; it is NOT security. The
// password is a literal in the build and anyone willing to open the assembly can read
// it in a minute. That is fine for what it is for — a door marked "not finished yet"
// that you can still walk through — but it should never be the thing standing between
// a player and something that actually matters.
//
// Built and destroyed per use rather than kept around: it is modal, it is rare, and a
// persistent canvas that swallows input when a bug leaves it enabled is a worse
// failure than rebuilding a dozen GameObjects.
[DisallowMultipleComponent]
public class DevGate : MonoBehaviour
{
    const string Password = "Leinad";

    static DevGate _open;

    /// <summary>Ask for the developer password; runs onPass only if it matches.</summary>
    public static void Ask(string headline, Action onPass)
    {
        if (_open != null) return;                 // already asking
        var go = new GameObject("DevGate");
        _open = go.AddComponent<DevGate>();
        _open._headline = headline;
        _open._onPass   = onPass;
    }

    string _headline;
    Action _onPass;

    Canvas         _canvas;
    TMP_InputField _field;
    TMP_Text       _error;
    RectTransform  _panel;
    float          _shake;

    void Start()
    {
        Build();
        // Focused immediately: this prompt exists to be typed into, and making the
        // player click the field first is pure friction.
        _field.Select();
        _field.ActivateInputField();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit();

        if (_shake > 0f)
        {
            _shake -= Time.unscaledDeltaTime * 4f;
            float k = Mathf.Max(0f, _shake);
            _panel.anchoredPosition = new Vector2(Mathf.Sin(Time.unscaledTime * 60f) * 16f * k, 0f);
        }
    }

    void Submit()
    {
        // Ordinal, case-sensitive: a password that quietly accepts a different case
        // is a password nobody can be sure they typed right.
        if (string.Equals(_field.text, Password, StringComparison.Ordinal))
        {
            var go = _onPass;
            Close();
            go?.Invoke();
            return;
        }

        _error.text = "wrong password";
        _shake = 1f;
        _field.text = "";
        _field.Select();
        _field.ActivateInputField();
    }

    void Close()
    {
        _open = null;
        Destroy(gameObject);
    }

    void Build()
    {
        var go = new GameObject("DevGateCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 500;                 // over anything the host screen draws

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        // Full-screen scrim, and it takes raycasts — the menu behind stays visible but
        // unclickable, so there is no way to start something else mid-prompt.
        var scrim = NewRect("Scrim", go.transform);
        scrim.anchorMin = Vector2.zero; scrim.anchorMax = Vector2.one;
        scrim.offsetMin = scrim.offsetMax = Vector2.zero;
        var scrimImg = scrim.gameObject.AddComponent<Image>();
        scrimImg.color = new Color(0f, 0f, 0f, 0.72f);

        _panel = NewRect("Panel", go.transform);
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
        _panel.sizeDelta = new Vector2(720f, 330f);
        _panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.08f, 1f);

        var head = NewText("Head", _panel, 30f, GeoPalette.Gold, FontStyles.Bold);
        Place(head.rectTransform, new Vector2(0f, 108f), new Vector2(640f, 40f));
        head.text = _headline;

        var sub = NewText("Sub", _panel, 20f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.55f), FontStyles.Normal);
        Place(sub.rectTransform, new Vector2(0f, 66f), new Vector2(640f, 30f));
        sub.text = "developer password";

        _field = NewPasswordField(_panel);

        _error = NewText("Error", _panel, 20f, GeoPalette.Signal, FontStyles.Normal);
        Place(_error.rectTransform, new Vector2(0f, -50f), new Vector2(640f, 28f));

        var hint = NewText("Hint", _panel, 18f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.4f), FontStyles.Normal);
        Place(hint.rectTransform, new Vector2(0f, -112f), new Vector2(640f, 26f));
        hint.text = "Enter  ·  confirm          Esc  ·  back";
    }

    TMP_InputField NewPasswordField(Transform parent)
    {
        var rt = NewRect("Password", parent);
        Place(rt, new Vector2(0f, 8f), new Vector2(560f, 62f));

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.10f);

        var viewport = NewRect("Viewport", rt);
        viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
        viewport.offsetMin = new Vector2(18f, 8f); viewport.offsetMax = new Vector2(-18f, -8f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var text = NewText("Text", viewport, 28f, GeoPalette.Paper, FontStyles.Normal);
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.anchorMin = Vector2.zero; text.rectTransform.anchorMax = Vector2.one;
        text.rectTransform.offsetMin = text.rectTransform.offsetMax = Vector2.zero;

        // The field goes on the same object as the background, so targetGraphic, the
        // caret and the click area all agree; on a separate object the caret drifts
        // from the box.
        var field = rt.gameObject.AddComponent<TMP_InputField>();
        field.targetGraphic     = img;
        field.textViewport      = viewport;
        field.textComponent     = (TextMeshProUGUI)text;
        field.lineType          = TMP_InputField.LineType.SingleLine;
        field.contentType       = TMP_InputField.ContentType.Password;
        field.caretColor        = GeoPalette.Gold;
        field.customCaretColor  = true;
        field.onSubmit.AddListener(_ => Submit());
        return field;
    }

    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static TMP_Text NewText(string name, Transform parent, float size, Color color, FontStyles style)
    {
        var rt = NewRect(name, parent);
        var t  = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize = size; t.color = color; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;
        return t;
    }
}
