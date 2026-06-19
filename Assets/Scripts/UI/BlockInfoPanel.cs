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
    public Button   upgradeAButton;
    public Button   upgradeBButton;
    public TMP_Text sellLabel;    // the Sell button's label (shows "Sell +N")
    public TMP_Text upgradeALabel;
    public TMP_Text upgradeBLabel;

    [Header("Font (optional — applied to every text above at Awake)")]
    public TMP_FontAsset font;

    [Header("Feel")]
    public float fadeSpeed = 14f;

    CanvasGroup _cg;
    Action      _onPickUp, _onSell, _onUpgradeA, _onUpgradeB;
    float       _target;

    void Awake()
    {
        _cg = GetComponent<CanvasGroup>();

        EnsureUpgradeButtons();

        if (font != null)
            foreach (var t in new[] { titleText, bodyText, lockedNote, sellLabel, upgradeALabel, upgradeBLabel })
                if (t != null) t.font = font;

        if (pickUpButton != null) pickUpButton.onClick.AddListener(() => _onPickUp?.Invoke());
        if (sellButton   != null) sellButton.onClick.AddListener(() => _onSell?.Invoke());
        if (upgradeAButton != null) upgradeAButton.onClick.AddListener(() => _onUpgradeA?.Invoke());
        if (upgradeBButton != null) upgradeBButton.onClick.AddListener(() => _onUpgradeB?.Invoke());

        _cg.alpha = 0f;
        _cg.interactable = _cg.blocksRaycasts = false;
    }

    void EnsureUpgradeButtons()
    {
        if (pickUpButton == null) return;

        if (upgradeAButton == null)
            upgradeAButton = CreateRuntimeUpgradeButton("Upgrade A Button", pickUpButton.transform.GetSiblingIndex() + 1);
        if (upgradeBButton == null)
            upgradeBButton = CreateRuntimeUpgradeButton("Upgrade B Button", pickUpButton.transform.GetSiblingIndex() + 2);

        if (upgradeALabel == null && upgradeAButton != null)
            upgradeALabel = upgradeAButton.GetComponentInChildren<TMP_Text>(true);
        if (upgradeBLabel == null && upgradeBButton != null)
            upgradeBLabel = upgradeBButton.GetComponentInChildren<TMP_Text>(true);
    }

    Button CreateRuntimeUpgradeButton(string objectName, int siblingIndex)
    {
        var button = Instantiate(pickUpButton, pickUpButton.transform.parent);
        button.name = objectName;
        button.transform.SetSiblingIndex(siblingIndex);
        button.onClick.RemoveAllListeners();
        button.gameObject.SetActive(false);
        return button;
    }

    void Update()
    {
        _cg.alpha = Mathf.Lerp(_cg.alpha, _target, 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime));
        bool on = _target > 0.5f && _cg.alpha > 0.5f;
        _cg.interactable = _cg.blocksRaycasts = on;
    }

    // Editable block / turret: title + stats + Pick up / Sell.
    public void Show(
        string title,
        string body,
        bool canEdit,
        string sellText,
        Action onPickUp,
        Action onSell,
        string upgradeAText = null,
        bool canUpgradeA = false,
        Action onUpgradeA = null,
        string upgradeBText = null,
        bool canUpgradeB = false,
        Action onUpgradeB = null)
    {
        if (titleText) titleText.text = title;
        if (bodyText)  bodyText.text  = body;
        if (sellLabel) sellLabel.text = sellText;

        if (pickUpButton) { pickUpButton.gameObject.SetActive(true); pickUpButton.interactable = canEdit; }
        if (sellButton)   { sellButton.gameObject.SetActive(true);   sellButton.interactable   = canEdit; }
        SetupUpgradeButton(upgradeAButton, upgradeALabel, upgradeAText, canUpgradeA);
        SetupUpgradeButton(upgradeBButton, upgradeBLabel, upgradeBText, canUpgradeB);
        if (lockedNote)   lockedNote.gameObject.SetActive(!canEdit);

        _onPickUp = onPickUp;
        _onSell   = onSell;
        _onUpgradeA = onUpgradeA;
        _onUpgradeB = onUpgradeB;
        _target   = 1f;
    }

    // Read-only (spawn-point forecast): title + body, no buttons.
    public void ShowReadonly(string title, string body)
    {
        if (titleText) titleText.text = title;
        if (bodyText)  bodyText.text  = body;
        if (pickUpButton) pickUpButton.gameObject.SetActive(false);
        if (sellButton)   sellButton.gameObject.SetActive(false);
        if (upgradeAButton) upgradeAButton.gameObject.SetActive(false);
        if (upgradeBButton) upgradeBButton.gameObject.SetActive(false);
        if (lockedNote)   lockedNote.gameObject.SetActive(false);

        _onPickUp = _onSell = _onUpgradeA = _onUpgradeB = null;
        _target   = 1f;
    }

    void SetupUpgradeButton(Button button, TMP_Text label, string text, bool canUpgrade)
    {
        if (button == null) return;

        bool show = !string.IsNullOrEmpty(text);
        button.gameObject.SetActive(show);
        button.interactable = show && canUpgrade;
        if (label != null) label.text = text;
    }

    public void Hide() { _target = 0f; _onPickUp = _onSell = _onUpgradeA = _onUpgradeB = null; }
}
