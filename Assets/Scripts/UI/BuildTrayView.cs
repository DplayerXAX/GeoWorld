using UnityEngine;
using UnityEngine.UI;
using TMPro;

// LevelSelect's build tray — the reward blocks you can lay on the map — authored in
// the scene (the BuildTrayUI prefab, placed with GeoWorld ▸ UI ▸ Place LevelSelect
// Build Tray). Same shape as the gameplay shop strip: move and size anything here;
// LevelMapController only fills in the entries and opens / closes it.
//
//   panel          — the strip. Its position in the editor is where it sits OPEN;
//                    closed, it slides down by its own height (+ closedDrop).
//   list           — parent the entries are cloned into (give it a layout group).
//   entryTemplate  — one entry (kept inactive): icon, label, Button.
//   hintLabel      — the line saying what to do.
public class BuildTrayView : MonoBehaviour
{
    public Canvas        canvas;
    public RectTransform panel;
    public CanvasGroup   panelGroup;
    public RectTransform list;
    public ShopItemView  entryTemplate;
    public TMP_Text      hintLabel;

    [Header("Open / close")]
    [Tooltip("Extra distance (canvas units) the panel drops below its own height when closed.")]
    public float closedDrop = 20f;
    [Tooltip("Gap kept to the screen edges when there are too many entries and the strip shrinks to fit.")]
    public float screenMargin = 16f;

    [Header("Entries")]
    [Tooltip("How much a hovered entry's icon grows. Its laid-out size is its LARGEST.")]
    [Range(1f, 1.5f)] public float hoverScale = 1.15f;

    [Header("Icons (same photograph as the shop's)")]
    public float iconYaw = 45f;
    [Range(0f, 90f)] public float iconPitch = 35f;
    public float iconPadding = 1.12f;
    [Tooltip("Shapes photographed from the other side (turned 180° round Y).")]
    public BlockShape[] flipIconShapes = { BlockShape.Corner3D };
}
