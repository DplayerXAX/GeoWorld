using UnityEngine;
using UnityEngine.UI;
using TMPro;

// One slot in the shop panel: the piece's icon and its price. A TEMPLATE lives in
// the ShopPanel prefab (one for blocks, a bigger one for turrets) — lay it out in
// the editor; ShopController clones it per item and only fills in the data.
public class ShopItemView : MonoBehaviour
{
    [Tooltip("The whole slot — hover and click are tested against this rect.")]
    public RectTransform rect;
    [Tooltip("Scaled for the hover pop / tutorial emphasis. Usually the icon's holder.")]
    public RectTransform scaleRoot;
    public Image    icon;
    [Tooltip("Optional slot background — tinted when hovered.")]
    public Image    background;
    public TMP_Text price;
    [Tooltip("Optional — used to fade the slot while it is held in hand.")]
    public CanvasGroup group;

    [Header("Colours")]
    public Color backgroundColor      = new Color(0f, 0f, 0f, 0.25f);
    public Color backgroundHoverColor = new Color(1f, 0.85f, 0.35f, 0.35f);
    public Color affordableColor      = new Color(0.55f, 1f, 0.60f, 1f);
    public Color unaffordableColor    = new Color(1f, 0.45f, 0.45f, 1f);

    void Reset()
    {
        rect = transform as RectTransform;
        scaleRoot = rect;
        icon = GetComponentInChildren<Image>();
        price = GetComponentInChildren<TMP_Text>();
        group = GetComponent<CanvasGroup>();
    }
}
