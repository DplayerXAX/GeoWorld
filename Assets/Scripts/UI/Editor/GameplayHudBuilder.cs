using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// Authoring for the gameplay HUD and shop layouts. The game no longer builds these
// at runtime — it binds to a HudView / ShopPanelView in the scene. This puts them
// there:
//
//   GeoWorld ▸ UI ▸ Place Gameplay HUD
//       Drops the GameplayHUD, ShopUI and WaveBar prefabs into the open scene and
//       wires them to TopLeftHUD / ShopController. A prefab that doesn't exist yet
//       is built first, from TopLeftHUD's / ShopController's icon, colour and font
//       fields. Existing prefabs keep your edits — only parts added since they were
//       made are put in (the shop's refresh tab). Also clears the old 3D shop
//       (ShopCamera, the backdrop's missing script) out of the scene.
//
//   GeoWorld ▸ UI ▸ Rebuild Gameplay HUD Prefabs (overwrite)
//       Starts the prefabs over from the defaults. Asks first.
//
// Run it in gamePlay, gamePlay_MP and mapMaker, then save each scene.
public static class GameplayHudBuilder
{
    const string Folder     = "Assets/Prefab/UI";
    const string HudPath    = Folder + "/GameplayHUD.prefab";
    const string ShopPath   = Folder + "/ShopUI.prefab";
    const string WavePath   = Folder + "/WaveBar.prefab";
    const string TrayPath   = Folder + "/BuildTrayUI.prefab";

    // LevelSelect's build tray — same strip as the shop. Places the prefab (built
    // the first time) and wires LevelMapController.buildTray. Run it in LevelSelect
    // and save the scene; after that edit the prefab by hand.
    [MenuItem("GeoWorld/UI/Place LevelSelect Build Tray")]
    static void PlaceBuildTray()
    {
        EnsureFolder();
        var map = Object.FindFirstObjectByType<LevelMapController>(FindObjectsInactive.Include);
        if (map == null)
        {
            EditorUtility.DisplayDialog("Place LevelSelect Build Tray", "No LevelMapController in the open scene.", "OK");
            return;
        }
        var go = PlaceOrBuild(TrayPath, false, BuildTray);
        Undo.RecordObject(map, "Wire Build Tray");
        map.buildTray = go != null ? go.GetComponent<BuildTrayView>() : null;
        EditorUtility.SetDirty(map);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static GameObject BuildTray()
    {
        var root = NewCanvas("BuildTrayUI", 60);
        var v = root.AddComponent<BuildTrayView>();
        v.canvas = root.GetComponent<Canvas>();

        // The strip — bottom-right, grows leftward with the number of entries.
        var panel = Rect("TrayPanel", root.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-16f, 16f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = Skin("UISprite"); bg.type = Image.Type.Sliced; bg.color = PanelBg;
        var hl = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(16, 16, 14, 14); hl.spacing = 12f;
        hl.childAlignment = TextAnchor.LowerRight;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        v.panel = panel;
        v.panelGroup = panel.gameObject.AddComponent<CanvasGroup>();

        v.list = Row("List", panel, 10f);

        // One entry: the shop's slot, made clickable.
        var slot = Slot("EntryTemplate", v.list, new Vector2(110f, 134f), 100f, 16f, null);
        slot.group.blocksRaycasts = true; slot.group.interactable = true;
        slot.background.raycastTarget = true;
        var btn = slot.gameObject.AddComponent<Button>();
        btn.targetGraphic = slot.background;
        btn.transition = Selectable.Transition.None;   // the hover look is driven by LevelMapController
        v.entryTemplate = slot;

        // What to do — on the strip's top edge, outside the layout.
        v.hintLabel = Text("Hint", panel, "Pick a reward block.", 18f, new Color(0.92f, 0.92f, 0.94f), FontStyles.Bold,
                           TextAlignmentOptions.BottomLeft, null);
        v.hintLabel.textWrappingMode = TextWrappingModes.Normal;
        v.hintLabel.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var hrt = (RectTransform)v.hintLabel.transform;
        hrt.anchorMin = hrt.anchorMax = new Vector2(0f, 1f);
        hrt.pivot = new Vector2(0f, 0f);
        hrt.anchoredPosition = new Vector2(10f, 6f);
        hrt.sizeDelta = new Vector2(760f, 28f);

        return root;
    }
    const string SpriteDir  = Folder + "/Sprites";

    [MenuItem("GeoWorld/UI/Place Gameplay HUD")]
    static void Place() => Run(overwrite: false);

    [MenuItem("GeoWorld/UI/Rebuild Gameplay HUD Prefabs (overwrite)")]
    static void Rebuild()
    {
        if (!EditorUtility.DisplayDialog("Rebuild HUD prefabs",
                "Overwrite GameplayHUD, ShopUI and WaveBar with the default layout? Any layout edits in those prefabs are lost.",
                "Overwrite", "Cancel")) return;
        Run(overwrite: true);
    }

    static void Run(bool overwrite)
    {
        EnsureFolder();
        var hud  = Object.FindFirstObjectByType<TopLeftHUD>(FindObjectsInactive.Include);
        var shop = Object.FindFirstObjectByType<ShopController>(FindObjectsInactive.Include);
        if (hud == null && shop == null)
        {
            EditorUtility.DisplayDialog("Place Gameplay HUD", "No TopLeftHUD or ShopController in the open scene.", "OK");
            return;
        }

        if (hud != null)
        {
            var view = PlaceOrBuild(HudPath, overwrite, () => BuildHud(hud).gameObject);
            Undo.RecordObject(hud, "Wire HUD");
            hud.view = view != null ? view.GetComponent<HudView>() : null;
            EditorUtility.SetDirty(hud);
        }
        if (shop != null)
        {
            RemoveLegacyShop(shop);
            if (!overwrite) UpgradeShopPrefab(shop, hud != null ? hud.font : null);
            var view = PlaceOrBuild(ShopPath, overwrite, () => BuildShop(shop, hud).gameObject);
            Undo.RecordObject(shop, "Wire Shop");
            shop.view = view != null ? view.GetComponent<ShopPanelView>() : null;
            EditorUtility.SetDirty(shop);
        }
        PlaceOrBuild(WavePath, overwrite, () => BuildWaveBar(hud));
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    // The prefab's instance in the open scene: the one already there, else a new one
    // (building the prefab first if it doesn't exist or is being overwritten).
    static GameObject PlaceOrBuild(string path, bool overwrite, System.Func<GameObject> build)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);

        // Instances of this prefab already in the scene.
        GameObject existing = null;
        foreach (var go in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (asset == null || !PrefabUtility.IsAnyPrefabInstanceRoot(go.gameObject)) continue;
            if (PrefabUtility.GetCorrespondingObjectFromSource(go.gameObject) == asset) { existing = go.gameObject; break; }
        }

        if (asset != null && !overwrite)
        {
            if (existing != null) return existing;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            Undo.RegisterCreatedObjectUndo(inst, "Place " + asset.name);
            return inst;
        }

        if (existing != null) Undo.DestroyObjectImmediate(existing);
        var built = build();
        var saved = PrefabUtility.SaveAsPrefabAssetAndConnect(built, path, InteractionMode.UserAction);
        Undo.RegisterCreatedObjectUndo(built, "Build " + saved.name);
        return built;
    }

    static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefab")) AssetDatabase.CreateFolder("Assets", "Prefab");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Prefab", "UI");
    }

    // ── HUD ──────────────────────────────────────────────────────────────────

    static HudView BuildHud(TopLeftHUD src)
    {
        var root = NewCanvas("GameplayHUD", 90);
        var v = root.AddComponent<HudView>();
        v.canvas = root.GetComponent<Canvas>();
        TMP_FontAsset font = src.font;

        // Resources — bottom-left.
        var panel = Rect("ResourcePanel", root.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = Vector2.zero;
        panel.anchoredPosition = src.panelMargin;
        var bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = Skin("UISprite"); bg.type = Image.Type.Sliced; bg.color = src.panelColor; bg.raycastTarget = false;
        var vlg = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 12, 8, 8); vlg.spacing = 4f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = vlg.childControlHeight = true;
        vlg.childForceExpandWidth = vlg.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        v.resourcePanel = panel;

        v.blockRow  = ResourceRow("BlockRow",  panel, src.blockIcon,  src.blockColor,  src, font, out v.blockValue,  out v.blockIncome);
        v.turretRow = ResourceRow("TurretRow", panel, src.turretIcon, src.turretColor, src, font, out v.turretValue, out v.turretIncome);

        // Lives + wave — top centre.
        var top = Rect("TopPanel", root.transform);
        top.anchorMin = top.anchorMax = new Vector2(0.5f, 1f);
        top.pivot = new Vector2(0.5f, 1f);
        top.sizeDelta = src.topPanelSize;
        top.anchoredPosition = new Vector2(0f, -src.topMargin);
        var tbg = top.gameObject.AddComponent<Image>();
        tbg.raycastTarget = false;
        if (src.topPanelSprite != null)
        {
            tbg.sprite = src.topPanelSprite;
            tbg.type = src.topPanelSprite.border != Vector4.zero ? Image.Type.Sliced : Image.Type.Simple;
            tbg.color = Color.white;
        }
        else { tbg.sprite = Skin("UISprite"); tbg.type = Image.Type.Sliced; tbg.color = src.panelColor; }
        v.topPanel = top;
        v.livesValue = TopGroup("Lives", top, src.heartIcon, src.heartColor, true,  src.valueSize, src, font);
        v.waveValue  = TopGroup("Wave",  top, null,          src.waveColor,  false, src.waveSize,  src, font);

        // Cell readout — docks above the shop while placing.
        var cell = Rect("CellReadout", root.transform);
        cell.anchorMin = cell.anchorMax = Vector2.zero;
        cell.pivot = new Vector2(0.5f, 0f);
        cell.sizeDelta = new Vector2(160f, 30f);
        cell.anchoredPosition = new Vector2(960f, 220f);
        var cbg = cell.gameObject.AddComponent<Image>();
        cbg.sprite = Skin("UISprite"); cbg.type = Image.Type.Sliced; cbg.color = src.panelColor; cbg.raycastTarget = false;
        var h = cell.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(8, 8, 4, 4); h.spacing = 6f; h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        if (src.gridIcon != null) Icon("Icon", cell, src.gridIcon, Color.white, 18f);
        v.cellReadout = cell;
        v.cellValue = Text("Value", cell, "---", src.valueSize, src.valueColor, FontStyles.Bold, TextAlignmentOptions.Midline, font);

        return v;
    }

    static RectTransform ResourceRow(string name, RectTransform parent, Sprite icon, Color col, TopLeftHUD src,
                                     TMP_FontAsset font, out TMP_Text value, out TMP_Text income)
    {
        var row = Rect(name, parent);
        var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.childAlignment = TextAnchor.MiddleLeft; h.spacing = 8f;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        row.gameObject.AddComponent<LayoutElement>().minHeight = src.iconSize;

        var img = Icon("Icon", row, icon, col, src.iconSize);
        img.enabled = icon != null;

        value = Text("Value", row, "0", src.valueSize, col, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, font);
        var le = value.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = 56f;
        income = Text("Income", row, "+0", src.incomeSize, src.incomeColor, FontStyles.Normal, TextAlignmentOptions.MidlineLeft, font);
        return row;
    }

    static TMP_Text TopGroup(string name, RectTransform parent, Sprite icon, Color col, bool left, float size,
                             TopLeftHUD src, TMP_FontAsset font)
    {
        float ax = left ? 0f : 1f;
        var g = Rect(name, parent);
        g.anchorMin = g.anchorMax = new Vector2(ax, 0.5f);
        g.pivot = new Vector2(ax, 0.5f);
        g.anchoredPosition = new Vector2(left ? src.topInset : -src.topInset, 0f);
        var h = g.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 8f; h.childAlignment = left ? TextAnchor.MiddleLeft : TextAnchor.MiddleRight;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        var fit = g.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        if (icon != null) Icon("Icon", g, icon, col, src.iconSize);
        return Text("Value", g, "0", size, col, FontStyles.Bold,
                    left ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineRight, font);
    }

    // ── Shop ─────────────────────────────────────────────────────────────────

    static readonly Color PanelBg  = new Color(0.05f, 0.06f, 0.10f, 0.86f);
    static readonly Color PaperBg  = new Color(0.949f, 0.937f, 0.902f, 0.96f);
    static readonly Color InkCol   = new Color(0.12f, 0.12f, 0.14f, 1f);

    static ShopPanelView BuildShop(ShopController src, TopLeftHUD hud)
    {
        TMP_FontAsset font = hud != null ? hud.font : null;
        var root = NewCanvas("ShopUI", 60);
        var v = root.AddComponent<ShopPanelView>();
        v.canvas = root.GetComponent<Canvas>();

        // The strip — bottom-right, grows leftward with the number of items.
        var panel = Rect("ShopPanel", root.transform);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 0f);
        panel.anchoredPosition = new Vector2(-16f, 16f);
        var bg = panel.gameObject.AddComponent<Image>();
        bg.sprite = Skin("UISprite"); bg.type = Image.Type.Sliced; bg.color = PanelBg;
        var hl = panel.gameObject.AddComponent<HorizontalLayoutGroup>();
        hl.padding = new RectOffset(16, 16, 14, 14); hl.spacing = 18f;
        hl.childAlignment = TextAnchor.LowerRight;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = hl.childForceExpandHeight = false;
        var fit = panel.gameObject.AddComponent<ContentSizeFitter>();
        fit.horizontalFit = fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        v.panel = panel;
        v.panelGroup = panel.gameObject.AddComponent<CanvasGroup>();
        v.flashTarget = bg;

        v.blockRow = Row("BlockRow", panel, 8f);
        Divider(panel);
        v.turretRow = Row("TurretRow", panel, 12f);

        v.blockTemplate  = Slot("BlockSlotTemplate",  v.blockRow,  new Vector2(96f, 118f),  88f, 17f, font);
        v.turretTemplate = Slot("TurretSlotTemplate", v.turretRow, new Vector2(136f, 158f), 128f, 19f, font);

        BuildRefreshTab(v, src, font);

        // "SHOP [F]" — sits on the strip's top edge, outside the layout.
        v.hintLabel = Text("Hint", panel, "SHOP  [F]", 16f, new Color(1f, 0.88f, 0.38f, 0.9f), FontStyles.Bold,
                           TextAlignmentOptions.BottomLeft, font);
        var hle = v.hintLabel.gameObject.AddComponent<LayoutElement>(); hle.ignoreLayout = true;
        var hrt = (RectTransform)v.hintLabel.transform;
        hrt.anchorMin = hrt.anchorMax = hrt.pivot = new Vector2(0f, 1f);
        hrt.pivot = new Vector2(0f, 0f);
        hrt.anchoredPosition = new Vector2(10f, 4f);
        hrt.sizeDelta = new Vector2(200f, 24f);

        // Open button — shown while the strip is closed.
        v.openButton = RoundButton("OpenButton", root.transform, src.shopButtonIcon, "S", 64f, font);
        var ort = (RectTransform)v.openButton.transform;
        ort.anchorMin = ort.anchorMax = ort.pivot = new Vector2(1f, 0f);
        ort.anchoredPosition = new Vector2(-24f, 24f);
        Object.DestroyImmediate(v.openButton.GetComponent<LayoutElement>());
        ort.sizeDelta = new Vector2(64f, 64f);

        // Tooltip card.
        var tip = Rect("Tooltip", root.transform);
        tip.anchorMin = tip.anchorMax = Vector2.zero;
        tip.pivot = new Vector2(0.5f, 0f);
        tip.sizeDelta = new Vector2(320f, 0f);
        var tbg = tip.gameObject.AddComponent<Image>();
        tbg.sprite = Skin("UISprite"); tbg.type = Image.Type.Sliced; tbg.color = PaperBg; tbg.raycastTarget = false;
        var tvl = tip.gameObject.AddComponent<VerticalLayoutGroup>();
        tvl.padding = new RectOffset(18, 14, 12, 12); tvl.spacing = 4f;
        tvl.childControlWidth = tvl.childControlHeight = true;
        tvl.childForceExpandWidth = true; tvl.childForceExpandHeight = false;
        var tfit = tip.gameObject.AddComponent<ContentSizeFitter>();
        tfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var accent = new GameObject("Accent", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        accent.transform.SetParent(tip, false);
        accent.GetComponent<LayoutElement>().ignoreLayout = true;
        var art = (RectTransform)accent.transform;
        art.anchorMin = Vector2.zero; art.anchorMax = new Vector2(0f, 1f); art.pivot = new Vector2(0f, 0.5f);
        art.sizeDelta = new Vector2(5f, 0f); art.anchoredPosition = Vector2.zero;
        v.tooltipAccent = accent.GetComponent<Image>(); v.tooltipAccent.raycastTarget = false;
        v.tooltip      = tip;
        v.tooltipTitle = Text("Title", tip, "Block", 20f, InkCol, FontStyles.Bold, TextAlignmentOptions.TopLeft, font);
        v.tooltipPrice = Text("Price", tip, "0 B",   18f, InkCol, FontStyles.Bold, TextAlignmentOptions.TopLeft, font);
        v.tooltipDesc  = Text("Desc",  tip, "",      16f, new Color(0.3f, 0.3f, 0.3f), FontStyles.Normal, TextAlignmentOptions.TopLeft, font);
        v.tooltipTitle.richText = true;
        v.tooltipDesc.textWrappingMode = TextWrappingModes.Normal;
        tip.gameObject.SetActive(false);

        return v;
    }

    static RectTransform Row(string name, RectTransform parent, float spacing)
    {
        var r = Rect(name, parent);
        var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing; h.childAlignment = TextAnchor.LowerCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        return r;
    }

    static void Divider(RectTransform parent)
    {
        var d = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        d.transform.SetParent(parent, false);
        var img = d.GetComponent<Image>(); img.color = new Color(1f, 1f, 1f, 0.18f); img.raycastTarget = false;
        var le = d.GetComponent<LayoutElement>();
        le.preferredWidth = 2f; le.preferredHeight = 120f;
    }

    // One slot: background, icon (the part that pops on hover), price underneath.
    static ShopItemView Slot(string name, RectTransform parent, Vector2 size, float iconSize, float priceSize, TMP_FontAsset font)
    {
        var s = Rect(name, parent);
        var le = s.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = size.x; le.preferredHeight = size.y;
        var bg = s.gameObject.AddComponent<Image>();
        bg.sprite = Skin("UISprite"); bg.type = Image.Type.Sliced; bg.raycastTarget = false;
        var group = s.gameObject.AddComponent<CanvasGroup>();
        group.blocksRaycasts = false; group.interactable = false;

        var icon = Rect("Icon", s);
        icon.anchorMin = icon.anchorMax = new Vector2(0.5f, 1f);
        icon.pivot = new Vector2(0.5f, 1f);
        icon.sizeDelta = new Vector2(iconSize, iconSize);
        icon.anchoredPosition = new Vector2(0f, -4f);
        var img = icon.gameObject.AddComponent<Image>();
        img.preserveAspect = true; img.raycastTarget = false;

        var price = Text("Price", s, "0¤B", priceSize, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, font);
        var prt = (RectTransform)price.transform;
        prt.anchorMin = new Vector2(0f, 0f); prt.anchorMax = new Vector2(1f, 0f);
        prt.pivot = new Vector2(0.5f, 0f);
        prt.sizeDelta = new Vector2(0f, priceSize + 6f);
        prt.anchoredPosition = new Vector2(0f, 2f);

        var v = s.gameObject.AddComponent<ShopItemView>();
        v.rect = s; v.scaleRoot = icon; v.icon = img; v.background = bg; v.price = price; v.group = group;
        bg.color = v.backgroundColor;
        s.gameObject.SetActive(false);
        return v;
    }

    static Button RoundButton(string name, Transform parent, Sprite icon, string glyph, float size, TMP_FontAsset font)
    {
        var r = Rect(name, parent);
        var le = r.gameObject.AddComponent<LayoutElement>();
        le.preferredWidth = le.preferredHeight = size;
        var bg = r.gameObject.AddComponent<Image>();
        bg.sprite = Skin("Knob"); bg.color = PaperBg;
        var b = r.gameObject.AddComponent<Button>();
        var cb = b.colors; cb.highlightedColor = new Color(1f, 0.9f, 0.6f); b.colors = cb;
        if (icon != null)
        {
            var i = Rect("Icon", r);
            i.anchorMin = Vector2.zero; i.anchorMax = Vector2.one;
            i.offsetMin = new Vector2(6f, 6f); i.offsetMax = new Vector2(-6f, -6f);
            var img = i.gameObject.AddComponent<Image>();
            img.sprite = icon; img.preserveAspect = true; img.color = InkCol; img.raycastTarget = false;
        }
        else
        {
            var t = Text("Glyph", r, glyph, size * 0.45f, InkCol, FontStyles.Bold, TextAlignmentOptions.Center, font);
            var trt = (RectTransform)t.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        }
        return b;
    }

    // Refresh as a folder tab sticking up from the strip's top-right corner, in the
    // strip's own colour, so it reads as part of the panel.
    static void BuildRefreshTab(ShopPanelView v, ShopController src, TMP_FontAsset font)
    {
        var tab = Rect("RefreshTab", v.panel);
        tab.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        tab.anchorMin = tab.anchorMax = new Vector2(1f, 1f);
        tab.pivot = new Vector2(1f, 0f);
        tab.anchoredPosition = new Vector2(-18f, -2f);   // tucked 2px into the strip — no seam
        tab.sizeDelta = new Vector2(132f, 44f);
        var bg = tab.gameObject.AddComponent<Image>();
        bg.sprite = TabSprite(); bg.type = Image.Type.Sliced;
        var panelImg = v.panel.GetComponent<Image>();
        bg.color = panelImg != null ? panelImg.color : PanelBg;
        var b = tab.gameObject.AddComponent<Button>();
        b.targetGraphic = bg;
        var cb = b.colors; cb.highlightedColor = new Color(1f, 0.95f, 0.8f, 1f); cb.colorMultiplier = 1.6f; b.colors = cb;

        var h = tab.gameObject.AddComponent<HorizontalLayoutGroup>();
        h.padding = new RectOffset(14, 14, 6, 4); h.spacing = 8f; h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = h.childControlHeight = true;
        h.childForceExpandWidth = h.childForceExpandHeight = false;
        if (src != null && src.refreshIcon != null) Icon("Icon", tab, src.refreshIcon, Color.white, 26f);
        else Text("Glyph", tab, "⟳", 24f, Color.white, FontStyles.Bold, TextAlignmentOptions.Center, font);
        v.refreshButton = b;
        v.refreshCost   = Text("Cost", tab, "0", 20f, Color.white, FontStyles.Bold, TextAlignmentOptions.MidlineLeft, font);
    }

    // An existing ShopUI prefab from before the refresh tab: swap its round refresh
    // button for the tab, leaving everything else in the prefab as it was edited.
    static void UpgradeShopPrefab(ShopController src, TMP_FontAsset font)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(ShopPath) == null) return;
        var root = PrefabUtility.LoadPrefabContents(ShopPath);
        try
        {
            var v = root.GetComponent<ShopPanelView>();
            if (v == null || v.panel == null) return;
            if (v.refreshButton != null && v.refreshButton.name == "RefreshTab") return;

            if (font == null && v.hintLabel != null) font = v.hintLabel.font;
            if (v.refreshButton != null)
            {
                var parent = v.refreshButton.transform.parent;
                Object.DestroyImmediate(v.refreshButton.gameObject);
                if (parent != null && parent.name == "Actions") Object.DestroyImmediate(parent.gameObject);
            }
            if (v.refreshCost != null) Object.DestroyImmediate(v.refreshCost.gameObject);
            BuildRefreshTab(v, src, font);
            PrefabUtility.SaveAsPrefabAsset(root, ShopPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }

    // The old 3D shop: its camera (which would now draw straight onto the screen)
    // and the deleted backdrop script left on the ShopController.
    static void RemoveLegacyShop(ShopController shop)
    {
        foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cam != null && cam.gameObject.name == "ShopCamera") Undo.DestroyObjectImmediate(cam.gameObject);
        if (shop != null && GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(shop.gameObject) > 0)
        {
            Undo.RegisterCompleteObjectUndo(shop.gameObject, "Remove missing scripts");
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(shop.gameObject);
        }
    }

    // ── Wave bar ─────────────────────────────────────────────────────────────

    static GameObject BuildWaveBar(TopLeftHUD hud)
    {
        TMP_FontAsset font = hud != null ? hud.font : null;
        var root = NewCanvas("WaveBar", 88);
        var bar = root.AddComponent<WaveProgressBar>();
        bar.group = root.AddComponent<CanvasGroup>();
        bar.group.blocksRaycasts = false; bar.group.interactable = false;

        // Just under the lives / wave panel.
        float below = hud != null ? hud.topMargin + hud.topPanelSize.y : 50f;
        float width = hud != null ? hud.topPanelSize.x : 380f;
        var track = Rect("Track", root.transform);
        track.anchorMin = track.anchorMax = new Vector2(0.5f, 1f);
        track.pivot = new Vector2(0.5f, 1f);
        track.anchoredPosition = new Vector2(0f, -(below + 12f));
        track.sizeDelta = new Vector2(width, 26f);
        bar.track = track;

        // Segment: a thin line, filled from the left as its wave is fought.
        var seg = Rect("SegmentTemplate", track);
        seg.sizeDelta = new Vector2(0f, 4f);
        var line = seg.gameObject.AddComponent<Image>();
        line.sprite = PixelSprite(); line.color = new Color(1f, 1f, 1f, 0.3f); line.raycastTarget = false;
        var fillRt = Rect("Fill", seg);
        fillRt.anchorMin = Vector2.zero; fillRt.anchorMax = Vector2.one; fillRt.offsetMin = fillRt.offsetMax = Vector2.zero;
        var fill = fillRt.gameObject.AddComponent<Image>();
        fill.sprite = PixelSprite(); fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal; fill.fillOrigin = (int)Image.OriginHorizontal.Left;
        fill.fillAmount = 0f; fill.color = bar.clearedColor; fill.raycastTarget = false;
        var sv = seg.gameObject.AddComponent<WaveSegmentView>();
        sv.rect = seg; sv.line = line; sv.fill = fill;
        seg.gameObject.SetActive(false);
        bar.segmentTemplate = sv;

        // Node: hollow ring, a dot inside (portal waves), the wave number under it.
        var node = Rect("NodeTemplate", track);
        node.sizeDelta = new Vector2(22f, 22f);
        var ring = node.gameObject.AddComponent<Image>();
        ring.sprite = RingSprite(); ring.raycastTarget = false;
        var dotRt = Rect("Dot", node);
        dotRt.anchorMin = Vector2.zero; dotRt.anchorMax = Vector2.one;
        dotRt.offsetMin = new Vector2(5f, 5f); dotRt.offsetMax = new Vector2(-5f, -5f);
        var dot = dotRt.gameObject.AddComponent<Image>();
        dot.sprite = DotSprite(); dot.raycastTarget = false;
        var label = Text("Label", node, "1", 13f, bar.labelColor, FontStyles.Bold, TextAlignmentOptions.Top, font);
        var lrt = (RectTransform)label.transform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0f);
        lrt.pivot = new Vector2(0.5f, 1f);
        lrt.anchoredPosition = new Vector2(0f, -2f);
        lrt.sizeDelta = new Vector2(40f, 18f);
        var nv = node.gameObject.AddComponent<WaveNodeView>();
        nv.rect = node; nv.ring = ring; nv.dot = dot; nv.label = label;
        node.gameObject.SetActive(false);
        bar.nodeTemplate = nv;

        // Tooltip — hangs under the hovered node.
        var tip = Rect("Tooltip", root.transform);
        tip.anchorMin = tip.anchorMax = Vector2.zero;
        tip.pivot = new Vector2(0.5f, 1f);
        tip.sizeDelta = new Vector2(420f, 0f);
        var tbg = tip.gameObject.AddComponent<Image>();
        tbg.sprite = Skin("UISprite"); tbg.type = Image.Type.Sliced;
        tbg.color = new Color(0.06f, 0.06f, 0.06f, 0.92f); tbg.raycastTarget = false;
        var tvl = tip.gameObject.AddComponent<VerticalLayoutGroup>();
        tvl.padding = new RectOffset(14, 14, 10, 10);
        tvl.childControlWidth = tvl.childControlHeight = true;
        tvl.childForceExpandWidth = true; tvl.childForceExpandHeight = false;
        tip.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var tt = Text("Text", tip, "", 17f, new Color(0.95f, 0.94f, 0.9f), FontStyles.Normal, TextAlignmentOptions.TopLeft, font);
        tt.textWrappingMode = TextWrappingModes.Normal;
        tt.richText = true;
        tip.gameObject.SetActive(false);
        bar.tip = tip; bar.tipText = tt;

        return root;
    }

    // ── Generated sprites (written once to Prefab/UI/Sprites, then reused) ────

    static Sprite RingSprite() => GenSprite("Ring", 128, Vector4.zero, (x, y) =>
    {
        float r = Vector2.Distance(new Vector2(x, y), new Vector2(64f, 64f));
        return Mathf.Clamp01(62f - r + 0.5f) * Mathf.Clamp01(r - 50f + 0.5f);
    });

    static Sprite DotSprite() => GenSprite("Dot", 128, Vector4.zero, (x, y) =>
        Mathf.Clamp01(62f - Vector2.Distance(new Vector2(x, y), new Vector2(64f, 64f)) + 0.5f));

    static Sprite PixelSprite() => GenSprite("Pixel", 8, Vector4.zero, (x, y) => 1f);

    // Rounded top corners, square bottom — a folder tab. 9-sliced.
    static Sprite TabSprite() => GenSprite("Tab", 64, new Vector4(18f, 2f, 18f, 18f), (x, y) =>
    {
        const float R = 16f;
        float cy = 64f - R;
        if (y < cy) return 1f;
        float cx = x < R ? R : x > 64f - R ? 64f - R : x;
        return Mathf.Clamp01(R - Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) + 0.5f);
    });

    static Sprite GenSprite(string name, int size, Vector4 border, System.Func<float, float, float> alpha)
    {
        string path = $"{SpriteDir}/{name}.png";
        var have = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (have != null) return have;
        if (!AssetDatabase.IsValidFolder(SpriteDir)) AssetDatabase.CreateFolder(Folder, "Sprites");

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(x + 0.5f, y + 0.5f)) * 255f));
        tex.SetPixels32(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);

        AssetDatabase.ImportAsset(path);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType         = TextureImporterType.Sprite;
        ti.spriteImportMode    = SpriteImportMode.Single;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled       = false;
        ti.filterMode          = FilterMode.Bilinear;
        ti.spriteBorder        = border;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    // ── Primitives ───────────────────────────────────────────────────────────

    static GameObject NewCanvas(string name, int order)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var c = go.GetComponent<Canvas>();
        c.renderMode = RenderMode.ScreenSpaceOverlay;
        c.sortingOrder = order;
        var s = go.GetComponent<CanvasScaler>();
        s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        s.referenceResolution = new Vector2(1920f, 1080f);
        s.matchWidthOrHeight = 1f;
        return go;
    }

    static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    static Image Icon(string name, Transform parent, Sprite sprite, Color col, float size)
    {
        var r = Rect(name, parent);
        var img = r.gameObject.AddComponent<Image>();
        img.sprite = sprite; img.color = col; img.raycastTarget = false; img.preserveAspect = true;
        var le = r.gameObject.AddComponent<LayoutElement>();
        le.minWidth = le.preferredWidth = le.minHeight = le.preferredHeight = size;
        return img;
    }

    static TMP_Text Text(string name, Transform parent, string text, float size, Color col,
                         FontStyles style, TextAlignmentOptions align, TMP_FontAsset font)
    {
        var r = Rect(name, parent);
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) t.font = font;
        t.text = text; t.fontSize = size; t.color = col; t.fontStyle = style; t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        return t;
    }

    static Sprite Skin(string name) =>
        AssetDatabase.GetBuiltinExtraResource<Sprite>($"UI/Skin/{name}.psd");
}
