using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// Attached at runtime (TitleFlow.WireSaveSlots) to each save-slot button. HOVER
// ONLY: shows that slot's stats over the right-side TitleCube via
// SaveSlotInfoDisplay. Slot SELECTION lives in each button's onClick() calling
// TitleFlow.SelectSlotAndPlay(slot) directly (Inspector-baked), not here.
//
// Which slot a button IS comes from that same onClick argument (SlotOf) — never
// from sibling order: the buttons sit in the panel as slot 1, 3, 2, and the panel
// itself carries a Button too.
public class SaveSlotButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    int _slot;

    public int Slot => _slot;

    public void Init(int slot) => _slot = slot;

    public void OnPointerEnter(PointerEventData e)
    {
        var cube = TitleFlow.Instance != null && TitleFlow.Instance.cube != null
                 ? TitleFlow.Instance.cube.transform : null;
        // Match this button's own label font so the card reads consistently.
        var label = GetComponentInChildren<TMP_Text>(true);
        SaveSlotInfoDisplay.Show(_slot, cube, label != null ? label.font : null);
    }

    public void OnPointerExit(PointerEventData e) => SaveSlotInfoDisplay.Hide();

    // The int argument of the button's persistent SelectSlotAndPlay call — the slot
    // number the Inspector wiring already states. UnityEvent keeps persistent calls
    // private, so this reads them by reflection; -1 if there isn't one.
    public static int SlotOf(Button b)
    {
        if (b == null) return -1;
        try
        {
            const System.Reflection.BindingFlags F =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var group = typeof(UnityEngine.Events.UnityEventBase).GetField("m_PersistentCalls", F)?.GetValue(b.onClick);
            var calls = group?.GetType().GetField("m_Calls", F)?.GetValue(group) as System.Collections.IList;
            if (calls == null) return -1;
            foreach (var call in calls)
            {
                var t = call.GetType();
                if (t.GetField("m_MethodName", F)?.GetValue(call) as string != nameof(TitleFlow.SelectSlotAndPlay)) continue;
                var args = t.GetField("m_Arguments", F)?.GetValue(call);
                if (args?.GetType().GetField("m_IntArgument", F)?.GetValue(args) is int slot) return slot;
            }
        }
        catch (System.Exception e) { Debug.LogWarning($"[SaveSlot] couldn't read slot from {b.name}: {e.Message}"); }
        return -1;
    }
}

// One shared floating info card that hovers over the right-side TitleCube. Built
// at runtime (house style: overlay canvas + TMP) with a soft white halo behind
// the text (opaque core → transparent rim) so the text stays readable over the
// block. Tracks the cube's screen position every frame while visible.
public class SaveSlotInfoDisplay : MonoBehaviour
{
    static SaveSlotInfoDisplay _inst;

    Canvas        _canvas;
    RectTransform _root;
    TMP_Text      _text;
    Transform     _cube;
    bool          _visible;

    public static void Show(int slot, Transform cube, TMP_FontAsset font)
    {
        Ensure();
        _inst._cube    = cube;
        if (font != null) _inst._text.font = font;
        _inst._text.text = BuildText(slot);
        _inst._visible = true;
        _inst.Reposition();
    }

    public static void Hide()
    {
        if (_inst == null) return;
        _inst._visible = false;
        if (_inst._canvas != null) _inst._canvas.enabled = false;
    }

    static void Ensure()
    {
        if (_inst != null) return;
        var go = new GameObject("SaveSlotInfoDisplay");
        DontDestroyOnLoad(go);
        _inst = go.AddComponent<SaveSlotInfoDisplay>();
        _inst.Build();
    }

    void Build()
    {
        var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 200;
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, 1080f);
        sc.matchWidthOrHeight  = 0.5f;

        var card = new GameObject("Card", typeof(RectTransform));
        card.transform.SetParent(go.transform, false);
        _root = (RectTransform)card.transform;
        _root.sizeDelta = new Vector2(688f, 406f);   // base 440×260, +25% twice
        _root.pivot     = new Vector2(0.5f, 0.5f);

        var halo = new GameObject("Halo", typeof(RectTransform), typeof(Image));
        var hrt = (RectTransform)halo.transform;
        hrt.SetParent(_root, false);
        hrt.anchorMin = Vector2.zero; hrt.anchorMax = Vector2.one;
        hrt.offsetMin = Vector2.zero; hrt.offsetMax = Vector2.zero;
        var himg = halo.GetComponent<Image>();
        himg.sprite        = SoftRadialSprite();
        himg.color         = new Color(1f, 1f, 1f, 0.92f);
        himg.raycastTarget = false;

        var txtGO = new GameObject("Text", typeof(RectTransform));
        var trt = (RectTransform)txtGO.transform;
        trt.SetParent(_root, false);
        trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(34f, 30f); trt.offsetMax = new Vector2(-34f, -30f);
        _text = txtGO.AddComponent<TextMeshProUGUI>();
        _text.enableAutoSizing = true;
        _text.fontSizeMin      = 12f;
        _text.fontSizeMax      = 30f;
        _text.alignment        = TextAlignmentOptions.Center;
        _text.color            = GeoPalette.Ink;
        _text.raycastTarget    = false;

        _canvas.enabled = false;
    }

    void LateUpdate()
    {
        if (_visible) Reposition();
    }

    void Reposition()
    {
        var cam = Camera.main;
        if (_cube == null || cam == null) { if (_canvas != null) _canvas.enabled = false; return; }

        Vector3 sp = cam.WorldToScreenPoint(_cube.position);
        if (sp.z <= 0f) { _canvas.enabled = false; return; }   // cube behind camera

        _canvas.enabled = true;
        _root.position  = new Vector3(sp.x, sp.y, 0f);
    }

    // ── Content ─────────────────────────────────────────────────────────────

    static string BuildText(int slot)
    {
        var p  = SaveSystem.PeekSlot(slot);
        var sb = new StringBuilder();
        sb.Append($"<b>SLOT {slot + 1}</b>\n");

        if (p == null)
        {
            sb.Append("<color=#5A5A5A>Empty — New Game</color>");
            return sb.ToString();
        }

        var db = TitleFlow.Instance != null ? TitleFlow.Instance.database : null;
        int total = 0, cleared = 0;
        string furthest = null;

        if (db != null && db.levels != null)
        {
            // db.levels is authored in progression order → last cleared = furthest.
            foreach (var lv in db.levels)
            {
                if (lv == null || lv.isTutorial) continue;
                total++;
                var rec = p.GetRecord(lv.levelId);
                if (rec != null && rec.cleared)
                {
                    cleared++;
                    furthest = string.IsNullOrEmpty(lv.displayName) ? lv.levelId : lv.displayName;
                }
            }
        }
        else
        {
            for (int i = 0; i < p.levelRecords.Count; i++)
                if (p.levelRecords[i] != null && p.levelRecords[i].cleared)
                {
                    cleared++;
                    furthest = p.levelRecords[i].levelId;
                }
        }

        if (total > 0)
            sb.Append($"Completion <b>{Mathf.RoundToInt(100f * cleared / total)}%</b>  ({cleared}/{total})\n");
        else
            sb.Append($"Cleared <b>{cleared}</b>\n");

        sb.Append($"Furthest <b>{(furthest ?? "—")}</b>\n");
        sb.Append($"Tech <b>{p.techPoints}</b>\n");

        if (p.endlessBestWave > 0)
            sb.Append($"Endless <b>W{p.endlessBestWave}</b>  ({p.endlessBestScore})");
        else
            sb.Append("Endless <color=#5A5A5A>—</color>");

        return sb.ToString();
    }

    // ── Soft radial white sprite (opaque core → transparent rim) ─────────────

    static Sprite _softRadial;
    static Sprite SoftRadialSprite()
    {
        if (_softRadial != null) return _softRadial;

        const int s = 64;
        var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags  = HideFlags.DontSave,
        };

        float c = (s - 1) * 0.5f;
        var px = new Color32[s * s];
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            float dx = (x - c) / c;
            float dy = (y - c) / c;
            float r  = Mathf.Sqrt(dx * dx + dy * dy);   // 0 at centre → ~1 at edge
            float a  = 1f - Mathf.SmoothStep(0.12f, 1f, r);
            px[y * s + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(a) * 255f));
        }
        tex.SetPixels32(px);
        tex.Apply();

        _softRadial = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        _softRadial.hideFlags = HideFlags.DontSave;
        return _softRadial;
    }
}
