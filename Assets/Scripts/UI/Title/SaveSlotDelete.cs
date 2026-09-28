using TMPro;
using UnityEngine;
using UnityEngine.UI;

// The save-select screen's one Delete button. Put on the save panel by
// TitleFlow.WireSaveSlots.
//
// ONE button, not one per slot: the slots live in a carousel that slides, scales
// and fades them, and a Delete riding along on every slot was three moving red
// buttons at three sizes. This one sits just right of whichever slot is in the
// centre — the slot Enter would load — and only while that slot has a save.
//
// A child of the panel (not of a slot), so the carousel neither scales it nor
// collects it as an item; it still fades in and out with the panel's CanvasGroup.
public class SaveSlotDelete : MonoBehaviour
{
    [Tooltip("Gap between the centred slot's right edge and the button, in panel units. Negative tucks it over the slot's edge.")]
    public float gap = -50f;

    CarouselMenu  _carousel;
    RectTransform _rt;
    int           _shownFor = int.MinValue;   // slot the visibility was last worked out for
    bool          _hasData;

    public static SaveSlotDelete Attach(GameObject panel)
    {
        var d = panel.GetComponent<SaveSlotDelete>();
        if (d == null) d = panel.AddComponent<SaveSlotDelete>();
        d._carousel = panel.GetComponent<CarouselMenu>();
        d.Build();
        return d;
    }

    void Build()
    {
        if (_rt != null) return;

        _rt = new GameObject("DeleteSlot", typeof(RectTransform)).GetComponent<RectTransform>();
        _rt.SetParent(transform, false);
        _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot     = new Vector2(0f, 0.5f);   // grows rightward from the slot's edge
        _rt.sizeDelta = new Vector2(124f, 38f);   // about the centred slot's own height

        var img = _rt.gameObject.AddComponent<Image>();
        img.color = GeoPalette.WithAlpha(GeoPalette.Signal, 0.9f);
        var btn = _rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;
        btn.onClick.AddListener(AskDelete);

        var labelRt = new GameObject("Label", typeof(RectTransform)).GetComponent<RectTransform>();
        labelRt.SetParent(_rt, false);
        labelRt.anchorMin = Vector2.zero; labelRt.anchorMax = Vector2.one;
        labelRt.offsetMin = labelRt.offsetMax = Vector2.zero;
        var t = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        var slotLabel = _carousel != null && _carousel.SelectedItem != null
                      ? _carousel.SelectedItem.GetComponentInChildren<TMP_Text>(true) : null;
        if (slotLabel != null) t.font = slotLabel.font;   // same face as the slots
        t.text = "DELETE";
        t.fontSize = 22f; t.fontStyle = FontStyles.Bold;
        t.color = Color.white;
        t.alignment = TextAlignmentOptions.Center;
        t.raycastTarget = false;

        _rt.gameObject.SetActive(false);
    }

    int SelectedSlot()
    {
        var item = _carousel != null ? _carousel.SelectedItem : null;
        var sb   = item != null ? item.GetComponent<SaveSlotButton>() : null;
        return sb != null ? sb.Slot : -1;
    }

    // After the carousel's Update has laid the slots out for this frame.
    void LateUpdate()
    {
        if (_rt == null) return;

        int slot = SelectedSlot();
        if (slot != _shownFor) { _shownFor = slot; _hasData = slot >= 0 && SaveSystem.SlotHasData(slot); }

        bool show = _hasData && !ConfirmDialog.IsOpen;
        if (_rt.gameObject.activeSelf != show) _rt.gameObject.SetActive(show);
        if (!show) return;

        // Just past the right edge of the centred slot, at its current (carousel) scale.
        var item = _carousel.SelectedItem;
        float right = item.rect.width * item.localScale.x * (1f - item.pivot.x);
        _rt.anchoredPosition = item.anchoredPosition + new Vector2(right + gap, 0f);
    }

    void AskDelete()
    {
        int slot = SelectedSlot();
        if (slot < 0) return;
        SaveSlotInfoDisplay.Hide();
        ConfirmDialog.Ask(
            $"DELETE SLOT {slot + 1}?",
            "Every level cleared, every block built on the map and all tech in this slot will be gone. This can't be undone.",
            "Delete",
            () =>
            {
                SaveSystem.DeleteSlot(slot);
                _shownFor = int.MinValue;   // re-check: it's empty now
            });
    }
}
