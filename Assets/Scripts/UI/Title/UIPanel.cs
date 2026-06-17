using UnityEngine;

// A UGUI panel that fades + slides in/out smoothly — the "Persona-fluid" feel,
// but your own art (you build/style the buttons inside). TitleFlow calls Show()/
// Hide(); put this on a panel that has a CanvasGroup.
[RequireComponent(typeof(CanvasGroup))]
public class UIPanel : MonoBehaviour
{
    public enum Slide { None, FromLeft, FromRight, FromTop, FromBottom }

    [Header("Transition")]
    public Slide slideFrom     = Slide.FromRight;
    public float slideDistance = 120f;   // canvas pixels
    public float speed         = 9f;     // higher = snappier
    public bool  shownOnStart  = false;

    CanvasGroup   _cg;
    RectTransform _rt;
    Vector2       _home;
    public bool _show { get; private set; }

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();
        _rt = (RectTransform)transform;
        _home = _rt.anchoredPosition;

        _show = shownOnStart;
        _cg.alpha = _show ? 1f : 0f;
        _cg.interactable = _cg.blocksRaycasts = _show;
        _rt.anchoredPosition = _show ? _home : _home + Offset();
    }

    public void Show() { _show = true;  _cg.interactable = _cg.blocksRaycasts = true; }
    public void Hide() { _show = false; _cg.interactable = _cg.blocksRaycasts = false; }
    public void Set(bool show) { if (show) Show(); else Hide(); }

    void Update()
    {
        float k = 1f - Mathf.Exp(-speed * Time.unscaledDeltaTime);
        _cg.alpha = Mathf.Lerp(_cg.alpha, _show ? 1f : 0f, k);
        Vector2 target = _show ? _home : _home + Offset();
        _rt.anchoredPosition = Vector2.Lerp(_rt.anchoredPosition, target, k);
    }

    Vector2 Offset()
    {
        switch (slideFrom)
        {
            case Slide.FromLeft:   return new Vector2(-slideDistance, 0f);
            case Slide.FromRight:  return new Vector2( slideDistance, 0f);
            case Slide.FromTop:    return new Vector2(0f,  slideDistance);
            case Slide.FromBottom: return new Vector2(0f, -slideDistance);
            default:               return Vector2.zero;
        }
    }
}
