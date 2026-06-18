using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// UGUI level-info panel for the level-select screen. Bind your existing panel asset:
// put this on the panel root (needs a CanvasGroup), drop in the TMP texts + Enter button.
// LevelMapController calls Show(...) / Hide() — see OpenPanel there.
// Translucency = set the panel background Image's color alpha in the asset (the text
// stays crisp because the CanvasGroup only fades in/out, it doesn't dim when shown).
[RequireComponent(typeof(CanvasGroup))]
public class LevelInfoPanel : MonoBehaviour
{
    [Header("Texts (TextMeshPro)")]
    public TMP_Text titleText;
    public TMP_Text descText;
    public TMP_Text statusText;
    public TMP_Text bestText;

    [Header("Enter button")]
    public Button   enterButton;
    public TMP_Text enterLabel;

    [Header("Font (optional — applied to every text above at Awake)")]
    public TMP_FontAsset font;

    [Header("Feel")]
    public float fadeSpeed = 12f;

    CanvasGroup _cg;
    float       _target;
    Action      _onEnter;

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();

        if (font != null)
            foreach (var t in new[] { titleText, descText, statusText, bestText, enterLabel })
                if (t != null) t.font = font;

        if (enterButton != null) enterButton.onClick.AddListener(() => _onEnter?.Invoke());

        _cg.alpha = 0f;
        _cg.interactable = _cg.blocksRaycasts = false;
    }

    void Update()
    {
        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;
    }

    public void Show(string title, string desc, string status, string best, bool canEnter, Action onEnter)
    {
        if (titleText)  titleText.text  = title;
        if (descText)   { bool has = !string.IsNullOrEmpty(desc); descText.gameObject.SetActive(has); descText.text = desc; }
        if (statusText) statusText.text = status;
        if (bestText)   { bool has = !string.IsNullOrEmpty(best); bestText.gameObject.SetActive(has); bestText.text = best; }
        if (enterLabel) enterLabel.text = canEnter ? "Enter" : "Locked";
        if (enterButton) enterButton.interactable = canEnter;

        _onEnter = onEnter;
        _target  = 1f;
    }

    public void Hide() { _target = 0f; _onEnter = null; }
}
