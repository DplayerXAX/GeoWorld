using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// UGUI selection panel for placed blocks / turrets / the spawn point. Bind your
// panel asset: put this on the panel root (needs a CanvasGroup), drop in the TMP
// texts + the two buttons. PlacementController drives it (see UpdateInfoPanel there).
// Translucency = the panel background Image's color alpha; the text stays crisp
// (the CanvasGroup only fades in/out, it doesn't dim when shown).
[RequireComponent(typeof(CanvasGroup))]
public class BlockInfoPanel : MonoBehaviour
{
    [Header("Texts (TextMeshPro)")]
    public TMP_Text titleText;
    public TMP_Text bodyText;     // multiline stats; rich-text enabled
    public TMP_Text lockedNote;   // optional: "Locked during combat"

    [Header("Buttons")]
    public Button   pickUpButton;
    public Button   sellButton;
    public TMP_Text sellLabel;    // the Sell button's label (shows "Sell +N")

    [Header("Font (optional — applied to every text above at Awake)")]
    public TMP_FontAsset font;

    [Header("Feel")]
    public float fadeSpeed = 14f;

    CanvasGroup _cg;
    Action      _onPickUp, _onSell;
    float       _target;

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();

        if (font != null)
            foreach (var t in new[] { titleText, bodyText, lockedNote, sellLabel })
                if (t != null) t.font = font;

        if (pickUpButton != null) pickUpButton.onClick.AddListener(() => _onPickUp?.Invoke());
        if (sellButton   != null) sellButton.onClick.AddListener(() => _onSell?.Invoke());

        _cg.alpha = 0f;
        _cg.interactable = _cg.blocksRaycasts = false;
    }

    void Update()
    {
        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;
    }

    // Editable block / turret: title + stats + Pick up / Sell.
    public void Show(string title, string body, bool canEdit, string sellText, Action onPickUp, Action onSell)
    {
        if (titleText) titleText.text = title;
        if (bodyText)  bodyText.text  = body;
        if (sellLabel) sellLabel.text = sellText;

        if (pickUpButton) { pickUpButton.gameObject.SetActive(true); pickUpButton.interactable = canEdit; }
        if (sellButton)   { sellButton.gameObject.SetActive(true);   sellButton.interactable   = canEdit; }
        if (lockedNote)   lockedNote.gameObject.SetActive(!canEdit);

        _onPickUp = onPickUp;
        _onSell   = onSell;
        _target   = 1f;
    }

    // Read-only (spawn-point forecast): title + body, no buttons.
    public void ShowReadonly(string title, string body)
    {
        if (titleText) titleText.text = title;
        if (bodyText)  bodyText.text  = body;
        if (pickUpButton) pickUpButton.gameObject.SetActive(false);
        if (sellButton)   sellButton.gameObject.SetActive(false);
        if (lockedNote)   lockedNote.gameObject.SetActive(false);

        _onPickUp = _onSell = null;
        _target   = 1f;
    }

    public void Hide() { _target = 0f; _onPickUp = _onSell = null; }
}
