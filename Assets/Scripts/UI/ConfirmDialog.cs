using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// A yes / no prompt for anything that can't be undone (deleting a save). Same look
// and same lifetime rule as DevGate: built per use, modal behind a full-screen scrim,
// destroyed when answered — a persistent canvas that swallows input when a bug left
// it enabled is a worse failure than rebuilding a few GameObjects.
//
// Esc / the No button back out; only the Yes button (never Enter — a reflex tap must
// not confirm something permanent) goes ahead.
[DisallowMultipleComponent]
public class ConfirmDialog : MonoBehaviour
{
    static ConfirmDialog _open;

    /// <summary>True while a prompt is up — hosts should ignore their own Esc / back handling.</summary>
    public static bool IsOpen => _open != null;

    // Also true on the frame it closed: the Esc that dismissed it is still "down"
    // for any host whose Update runs after this one's, and must not ALSO back out
    // of the host's own screen.
    static int _closedFrame = -1;
    public static bool BlockingInput => _open != null || Time.frameCount == _closedFrame;

    public static void Ask(string headline, string body, string yesLabel, Action onYes)
    {
        if (_open != null) return;
        var go = new GameObject("ConfirmDialog");
        _open = go.AddComponent<ConfirmDialog>();
        _open._headline = headline;
        _open._body     = body;
        _open._yesLabel = yesLabel;
        _open._onYes    = onYes;
    }

    string _headline, _body, _yesLabel;
    Action _onYes;
    int    _openedFrame;

    void Start()
    {
        _openedFrame = Time.frameCount;
        Build();
    }

    void Update()
    {
        // Not on the frame it opened: the key that opened it must not also close it.
        if (Time.frameCount != _openedFrame && (Input.GetKeyDown(KeyCode.Escape) || GamepadInput.CancelDown))
            Close();
    }

    void Yes()
    {
        var go = _onYes;
        Close();
        go?.Invoke();
    }

    void Close()
    {
        _open = null;
        _closedFrame = Time.frameCount;
        Destroy(gameObject);
    }

    void Build()
    {
        var go = new GameObject("ConfirmCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;   // over everything, the pause menu's fold-away paper (900) included

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;
        BlockInfoPanel.EnsureEventSystem();

        var scrim = NewRect("Scrim", go.transform);
        scrim.anchorMin = Vector2.zero; scrim.anchorMax = Vector2.one;
        scrim.offsetMin = scrim.offsetMax = Vector2.zero;
        scrim.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.72f);

        var panel = NewRect("Panel", go.transform);
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(720f, 320f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.07f, 0.07f, 0.08f, 1f);

        var head = NewText("Head", panel, 30f, GeoPalette.Gold, FontStyles.Bold);
        Place(head.rectTransform, new Vector2(0f, 100f), new Vector2(640f, 44f));
        head.text = _headline;

        var body = NewText("Body", panel, 22f, GeoPalette.WithAlpha(GeoPalette.Paper, 0.75f), FontStyles.Normal);
        Place(body.rectTransform, new Vector2(0f, 30f), new Vector2(620f, 80f));
        body.textWrappingMode = TextWrappingModes.Normal;
        body.text = _body;

        NewButton(panel, "No",  "Cancel",  new Vector2(-150f, -90f), new Color(1f, 1f, 1f, 0.10f), GeoPalette.Paper, Close);
        NewButton(panel, "Yes", _yesLabel, new Vector2( 150f, -90f), GeoPalette.Signal,            Color.white,      Yes);
    }

    void NewButton(Transform parent, string name, string label, Vector2 pos, Color bg, Color fg, Action onClick)
    {
        var rt = NewRect(name, parent);
        Place(rt, pos, new Vector2(240f, 60f));
        var img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(() => onClick());

        var t = NewText("Label", rt, 24f, fg, FontStyles.Bold);
        t.rectTransform.anchorMin = Vector2.zero; t.rectTransform.anchorMax = Vector2.one;
        t.rectTransform.offsetMin = t.rectTransform.offsetMax = Vector2.zero;
        t.text = label;
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
