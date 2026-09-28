using UnityEngine;
using TMPro;

// The gameplay HUD's on-screen layout, authored in the scene (the GameplayHUD
// prefab — GeoWorld ▸ UI ▸ Place Gameplay HUD). Move, resize and restyle anything
// here; TopLeftHUD only writes the numbers.
public class HudView : MonoBehaviour
{
    public Canvas canvas;

    [Header("Resources (bottom-left)")]
    public RectTransform resourcePanel;
    public RectTransform blockRow;
    public TMP_Text      blockValue;
    public TMP_Text      blockIncome;
    public RectTransform turretRow;
    public TMP_Text      turretValue;
    public TMP_Text      turretIncome;

    [Header("Lives + wave (top)")]
    public RectTransform topPanel;
    public TMP_Text      livesValue;
    public TMP_Text      waveValue;

    [Header("Cell readout (shown while placing)")]
    public RectTransform cellReadout;
    public TMP_Text      cellValue;
    [Tooltip("ON: the readout follows the top of the shop panel. OFF: it stays where it is laid out.")]
    public bool dockCellReadoutToShop = true;
}
