using UnityEngine;
using UnityEngine.UI;
using TMPro;

// The shop's on-screen layout, authored in the scene (the ShopUI prefab — create it
// with GeoWorld ▸ UI ▸ Place Gameplay HUD). Move and size anything here freely;
// ShopController only fills in the items and drives open/closed.
//
//   panel            — the strip itself. Its position in the editor is where it sits
//                      OPEN; closed, it slides down by its own height (+ closedDrop).
//   blockRow         — parent the block slots are cloned into (give it a layout group).
//   turretRow        — parent the turret slots are cloned into.
//   blockTemplate    — slot to clone for blocks (kept inactive).
//   turretTemplate   — slot to clone for turrets (kept inactive; make it bigger).
//   refreshButton    — re-roll the shop; refreshCost shows the price.
//   openButton       — shown while the shop is closed; opens it.
//   tooltip…         — the hover card.
public class ShopPanelView : MonoBehaviour
{
    public Canvas        canvas;
    public RectTransform panel;
    public CanvasGroup   panelGroup;

    [Header("Rows")]
    public RectTransform blockRow;
    public RectTransform turretRow;
    public ShopItemView  blockTemplate;
    public ShopItemView  turretTemplate;

    [Header("Buttons")]
    public Button   refreshButton;
    public TMP_Text refreshCost;
    public Button   openButton;
    public TMP_Text hintLabel;

    [Header("Tooltip")]
    public RectTransform tooltip;
    public TMP_Text      tooltipTitle;
    public TMP_Text      tooltipPrice;
    public TMP_Text      tooltipDesc;
    public Image         tooltipAccent;

    [Header("Open / close")]
    [Tooltip("Extra distance (canvas units) the panel drops below its own height when closed.")]
    public float closedDrop = 20f;
    [Tooltip("Flash colour of the panel edge after a failed purchase.")]
    public Image  flashTarget;
    public Color  flashColor = new Color(1f, 0.3f, 0.3f, 0.9f);
}
