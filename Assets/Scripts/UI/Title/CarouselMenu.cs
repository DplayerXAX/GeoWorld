using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

// Vertical carousel for a UGUI menu panel: the selected item sits centred and big,
// items scroll up/down with scale + fade falloff (far ones shrink/fade), and each
// scroll step spins the cube a little extra (TitleCube.AddScroll). Put this on the
// panel; child items that have a Button are auto-collected (or assign `items`).
// Only reacts while its panel is shown (CanvasGroup.interactable).
public class CarouselMenu : MonoBehaviour
{
    public TitleCube cube;
    [Tooltip("Leave null for Screen Space - Overlay canvases; set for Screen Space - Camera.")]
    public Camera uiCamera;
    public List<RectTransform> items = new();

    [Header("Layout")]
    public float spacing        = 96f;     // px between items
    public float selectedScale  = 1.3f;
    public float minScale       = 0.65f;
    public float falloffPerItem = 0.35f;   // scale lost per item away from centre
    public float fadePerItem    = 0.40f;   // alpha lost per item away

    [Header("Feel")]
    public float scrollLerp = 12f;
    public bool  wrap       = false;

    int         _selected;
    float       _scroll;
    CanvasGroup _panelCg;

    void Awake()
    {
        _panelCg = GetComponentInParent<CanvasGroup>();

        if (items == null || items.Count == 0)
        {
            items = new List<RectTransform>();
            foreach (Transform c in transform)
                if (c.GetComponent<Button>() != null) items.Add((RectTransform)c);
        }
        foreach (var rt in items)
            if (rt != null && rt.GetComponent<CanvasGroup>() == null)
                rt.gameObject.AddComponent<CanvasGroup>();

        _scroll = _selected;
    }

    void OnEnable()
    {
        _selected = Mathf.Clamp(_selected, 0, Mathf.Max(0, items.Count - 1));
        _scroll = _selected;
    }

    void Update()
    {
        if (items.Count == 0) return;

        bool active = _panelCg == null || _panelCg.interactable;   // only when the panel is shown
        if (active)
        {
            HandleHover();
            HandleInput();
        }

        _scroll = Mathf.Lerp(_scroll, _selected, 1f - Mathf.Exp(-scrollLerp * Time.unscaledDeltaTime));
        Layout();
    }

    void HandleHover()
    {
        Vector2 mp = Input.mousePosition;
        for (int i = 0; i < items.Count; i++)
            if (items[i] != null && RectTransformUtility.RectangleContainsScreenPoint(items[i], mp, uiCamera))
            { Select(i); break; }
    }

    void HandleInput()
    {
        float wheel = Input.mouseScrollDelta.y;
        if (wheel >  0.01f) Move(-1);
        if (wheel < -0.01f) Move( 1);
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) Move( 1);
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))   Move(-1);
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space))
            Confirm();
    }

    void Move(int dir)
    {
        int n = items.Count, prev = _selected;
        _selected = wrap ? (_selected + dir + n) % n : Mathf.Clamp(_selected + dir, 0, n - 1);
        if (_selected != prev) cube?.AddScroll(dir);
    }

    void Select(int i)
    {
        if (i == _selected) return;
        cube?.AddScroll(i > _selected ? 1 : -1);
        _selected = i;
    }

    void Confirm()
    {
        var b = items[_selected].GetComponent<Button>();
        if (b != null && b.interactable) b.onClick.Invoke();
    }

    void Layout()
    {
        for (int i = 0; i < items.Count; i++)
        {
            var rt = items[i];
            if (rt == null) continue;

            float d  = i - _scroll;                 // 0 = selected/centre
            float ad = Mathf.Abs(d);
            rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, -d * spacing);
            rt.localScale = Vector3.one * Mathf.Max(minScale, selectedScale - ad * falloffPerItem);

            var cg = rt.GetComponent<CanvasGroup>();
            if (cg != null) cg.alpha = Mathf.Clamp01(1f - ad * fadePerItem);
        }
    }
}
