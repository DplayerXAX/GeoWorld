using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// Drives a tutorial level through ordered TutorialSteps:
//   • Place  — shows a ghost (the required shape/position) the player must match;
//              placement is blocked anywhere else.
//   • Rotate — waits for the player to rotate the held block.
//   • Run    — waits for the player to start the wave (combat begins).
// Auto-spawns ONLY for levels flagged isTutorial. Endpoints are pinned by
// GameFlowManager via the level's fixedEndpoints/startCell/endCell.
[DisallowMultipleComponent]
public class TutorialDirector : MonoBehaviour
{
    public Color ghostColor = new Color(1f, 1f, 1f, 0.8f);   // gold guide

    [Header("Hint box (UGUI — sizes are in 1920×1080 reference units, scaled to the window)")]
    [Tooltip("Screen position (normalized): x = horizontal centre (0..1), y = distance from the TOP (0..1).")]
    public Vector2 hintScreenPos = new Vector2(0.5f, 0.92f);
    public float   hintWidth  = 760f;
    public float   hintHeight = 120f;
    public float   hintFontSize = 30f;
    [Tooltip("Typewriter reveal speed (characters per second). 0 = show instantly.")]
    public float   hintCharsPerSecond = 38f;
    [Tooltip("Optional TMP font for the hint text (leave null for TMP default).")]
    public TMP_FontAsset font;

    // The running tutorial (null when none) — used by the operation gates below.
    static TutorialDirector _active;

    // Operation gates: while a tutorial is active, an operation is allowed ONLY when
    // the current step teaches it (and, for buying, only the correct block). This
    // disables operations before their step and blocks wrong ones (e.g. wrong block).
    // A step flagged freeOperations lifts the gating. No tutorial → everything allowed.
    public static bool CanPurchase(BlockData d) => _active == null || _active.AllowOp(TutorialStepKind.Purchase, d);
    public static bool CanSell()    => _active == null || _active.AllowOp(TutorialStepKind.Sell,    null);
    public static bool CanRefresh() => _active == null || _active.AllowOp(TutorialStepKind.Refresh, null);
    public static bool CanUpgrade() => _active == null || _active.AllowOp(TutorialStepKind.Upgrade, null);
    public static bool CanRun()     => _active == null || _active.AllowOp(TutorialStepKind.Run,     null);

    bool AllowOp(TutorialStepKind op, BlockData d)
    {
        var step = Cur;
        if (step == null) return true;            // tutorial finished → no gating
        if (step.freeOperations) return true;     // sandbox step
        // Hidden-in-combat steps lift their gating during the wave so the tutorial
        // never blocks combat actions.
        if (step.hideInCombat && GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running) return true;
        // Buying: CanPurchase passes the generic Purchase intent. Allow it when the
        // current step is ANY purchase kind and the bought item fits that kind.
        if (op == TutorialStepKind.Purchase)
            return IsPurchaseKind(step.kind) && PurchaseMatches(step, d);
        if (step.kind != op) return false;        // this op isn't the current step → locked
        return true;
    }

    static bool IsPurchaseKind(TutorialStepKind k) =>
        k == TutorialStepKind.Purchase
        || k == TutorialStepKind.PurchaseBlock
        || k == TutorialStepKind.PurchaseTurret;

    // Does `got` satisfy the current purchase step's restriction?
    //   • PurchaseTurret → must be a turret; block==null = any, else match blockType
    //   • PurchaseBlock  → must be a non-turret; block==null = any, else match blockShape
    //   • Purchase       → legacy SameBlock (any category)
    static bool PurchaseMatches(TutorialStep step, BlockData got)
    {
        if (got == null) return false;
        bool gotTurret = TurretTypes.Is(got.blockType);
        switch (step.kind)
        {
            case TutorialStepKind.PurchaseTurret:
                if (!gotTurret) return false;
                return step.block == null
                    || (TurretTypes.Is(step.block.blockType) && step.block.blockType == got.blockType);
            case TutorialStepKind.PurchaseBlock:
                if (gotTurret) return false;
                return step.block == null || step.block.blockShape == got.blockShape;
            default: // legacy Purchase
                return SameBlock(step.block, got);
        }
    }

    LevelDefinition _lv;
    int        _step;
    float      _stepTimer;     // for Wait steps
    int        _freePlaced;    // for FreePlace steps
    GameObject _ghost;
    Material   _ghostMat;
    readonly List<Renderer> _ghostRends = new();

    // ── Hint UI (UGUI) ────────────────────────────────────────────────────────
    Canvas        _hintCanvas;
    RectTransform _hintPanel;
    TMP_Text      _hintText;
    Image         _continueBar;

    // Typewriter state — typing starts the first frame the hint is actually shown
    // (so it doesn't "pre-type" while hidden during the intro / pause).
    int    _typedStep = -1;
    float  _typeStart;
    string _hintMsg = "";
    int    _hintCharCount;

    // ── Dialogue delivery ───────────────────────────────────────────────────────
    int                  _dialoguePlayedStep = -1;   // which step has had its convo kicked off
    bool                 _dialogueActive;            // a tutorial conversation is on screen
    DialogueConversation _runtimeConvo;              // built for one-line speaker hints
    DialogueRunner       _runner;                    // cached so OnDestroy doesn't spawn one

    // RuntimeInitializeOnLoadMethod runs ONCE at startup — not per scene load — so
    // hook sceneLoaded and (re)spawn whenever a gameplay scene loads with a tutorial
    // RunConfig (e.g. after entering the level from LevelSelect).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        TrySpawn();   // also covers starting directly in a tutorial gameplay scene
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => TrySpawn();

    static void TrySpawn()
    {
        if (RunConfig.Mode != GameMode.Level || RunConfig.Level == null || !RunConfig.Level.isTutorial) return;
        if (PlacementController.Instance == null) return;          // gameplay scene only
        if (FindFirstObjectByType<TutorialDirector>() != null) return;
        new GameObject("TutorialDirector").AddComponent<TutorialDirector>();
    }

    void Start()
    {
        _lv = RunConfig.Level;
        if (_lv == null || !_lv.isTutorial) { Destroy(gameObject); return; }
        _active = this;

        var pc = PlacementController.Instance;
        if (pc != null) pc.placementConstraint = MatchesStep;
        PlacementController.BlockPlaced    += OnBlockPlaced;
        PlacementController.BlockRotated   += OnBlockRotated;
        PlacementController.BlockPurchased += OnPurchased;
        PlacementController.BlockSelected  += OnSelected;
        PlacementController.BlockSold      += OnSold;
        PlacementController.ShopRefreshed  += OnRefreshed;
        PlacementController.TurretUpgraded += OnUpgraded;
        Debug.Log($"[Tutorial] active — {(_lv.tutorialSteps != null ? _lv.tutorialSteps.Count : 0)} step(s).");
        BuildHintUI();
        ShowStep();
    }

    void OnDestroy()
    {
        if (_active == this) _active = null;
        PlacementController.BlockPlaced    -= OnBlockPlaced;
        PlacementController.BlockRotated   -= OnBlockRotated;
        PlacementController.BlockPurchased -= OnPurchased;
        PlacementController.BlockSelected  -= OnSelected;
        PlacementController.BlockSold      -= OnSold;
        PlacementController.ShopRefreshed  -= OnRefreshed;
        PlacementController.TurretUpgraded -= OnUpgraded;
        var pc = PlacementController.Instance;
        if (pc != null && pc.placementConstraint == (System.Func<BlockData, Vector3Int[], bool>)MatchesStep)
            pc.placementConstraint = null;
        if (_ghost != null) Destroy(_ghost);
        if (_runner != null) _runner.OnFinished -= OnDialogueFinished;
        if (_runtimeConvo != null) Destroy(_runtimeConvo);
    }

    TutorialStep Cur =>
        (_lv != null && _lv.tutorialSteps != null && _step < _lv.tutorialSteps.Count)
            ? _lv.tutorialSteps[_step] : null;

    void Update()
    {
        if (IntroDirector.Playing) return;   // wait for the entrance animation before running steps

        var step = Cur;

        // Steps flagged hideInCombat go dark during the Running phase: hide their
        // dialogue/hint and don't advance until combat ends (then they re-show).
        if (step != null && step.hideInCombat
            && GameFlowManager.Instance != null && GameFlowManager.Instance.phase == GamePhase.Running)
        {
            if (_dialogueActive) CloseDialogue();
            if (_hintCanvas != null) _hintCanvas.enabled = false;
            _dialoguePlayedStep = -1;   // replay the dialogue when combat ends
            return;
        }

        // Kick off this step's conversation once (now that the intro is done). The
        // dialogue REPLACES the hint box for the step.
        if (step != null && step.UsesDialogue && _dialoguePlayedStep != _step)
        {
            _dialoguePlayedStep = _step;
            PlayStepDialogue(step);
        }

        // Note: a tutorial conversation is DISPLAY-ONLY (passive) — it ignores clicks
        // and doesn't block the game. The step's own completion (action / Input / Wait
        // timer) is what advances to the next step (and the next dialogue). So we do
        // NOT early-out here while a dialogue is showing.

        if (step != null && step.kind == TutorialStepKind.Run
            && GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running)
            Advance();

        // Wait step: auto-advance after waitSeconds, or on a click if waitSeconds <= 0.
        if (step != null && step.kind == TutorialStepKind.Wait)
        {
            _stepTimer += Time.deltaTime;
            bool timed   = step.waitSeconds > 0f && _stepTimer >= step.waitSeconds;
            bool clicked = step.waitSeconds <= 0f && Input.GetMouseButtonDown(0);
            if (timed || clicked) { Advance(); return; }
        }

        // Input step: advance when the required key is pressed.
        if (step != null && step.kind == TutorialStepKind.Input && Input.GetKeyDown(step.inputKey))
        {
            Advance();
            return;
        }

        // PathLength step: advance once the current enemy path is long enough.
        if (step != null && step.kind == TutorialStepKind.PathLength
            && GameFlowManager.Instance != null
            && GameFlowManager.Instance.CurrentPathLength >= Mathf.Max(1, step.pathLength))
        {
            Advance();
            return;
        }

        // (Re)build the ghost if it's missing for a Place step, then pulse it so
        // the guide is easy to spot.
        if (step != null && step.kind == TutorialStepKind.Place && _ghost == null && GridSystem.instance != null)
            BuildGhost(step);

        if (_ghostRends.Count > 0)
        {
            float a = ghostColor.a * (0.45f + 0.55f * Mathf.PingPong(Time.time * 1.6f, 1f));
            var c = new Color(ghostColor.r, ghostColor.g, ghostColor.b, a);
            for (int i = 0; i < _ghostRends.Count; i++)
                if (_ghostRends[i] != null) MpbColor.Set(_ghostRends[i], c);
        }

        UpdateHint();
    }

    // Placement is only allowed on the current Place step's ghost (matching shape +
    // position, and the specific block if requireBlock is set). Other step kinds
    // don't restrict placement.
    // Match by CELLS (shape + position) only — any block placed exactly on the
    // ghost passes. `block` just defines the ghost shape, not a required asset.
    bool MatchesStep(BlockData block, Vector3Int[] worldCells)
    {
        var step = Cur;
        if (step == null || step.kind != TutorialStepKind.Place) return true;
        bool ok = SameSet(step.TargetCells(), worldCells);
        if (!ok) Debug.Log($"[Tutorial] not on guide. placed=[{Fmt(worldCells)}]  target=[{Fmt(step.TargetCells())}]");
        return ok;
    }

    void OnBlockPlaced(BlockData block, Vector3Int[] worldCells)
    {
        var step = Cur;
        if (step == null) return;

        if (step.kind == TutorialStepKind.Place && SameSet(step.TargetCells(), worldCells))
            Advance();
        else if (step.kind == TutorialStepKind.FreePlace)
        {
            _freePlaced++;
            if (_freePlaced >= Mathf.Max(1, step.count)) Advance();
        }
    }

    static string Fmt(Vector3Int[] cells)
    {
        if (cells == null || cells.Length == 0) return "";
        var sb = new System.Text.StringBuilder();
        foreach (var c in cells) sb.Append(c).Append(' ');
        return sb.ToString().TrimEnd();
    }

    void OnBlockRotated()
    {
        var step = Cur;
        if (step != null && step.kind == TutorialStepKind.Rotate) Advance();
    }

    void OnPurchased(BlockData d)
    {
        var step = Cur;
        if (step != null && IsPurchaseKind(step.kind) && PurchaseMatches(step, d)) Advance();
    }

    void OnSelected(BlockData d)
    {
        var step = Cur;
        if (step != null && step.kind == TutorialStepKind.Select && SameBlock(step.block, d)) Advance();
    }

    void OnSold(BlockData d)
    {
        var step = Cur;
        if (step != null && step.kind == TutorialStepKind.Sell && SameBlock(step.block, d)) Advance();
    }

    // Tutorial block match: null = any; same asset always matches. Otherwise:
    //   • turret  → match by blockType (e.g. any AOE turret),
    //   • block   → match by blockShape (e.g. any L block).
    static bool SameBlock(BlockData want, BlockData got)
    {
        if (want == null) return true;
        if (got == null)  return false;
        if (want == got)  return true;

        if (TurretTypes.Is(want.blockType))
            return TurretTypes.Is(got.blockType) && want.blockType == got.blockType;

        return !TurretTypes.Is(got.blockType) && want.blockShape == got.blockShape;
    }

    void OnRefreshed()
    {
        var step = Cur;
        if (step != null && step.kind == TutorialStepKind.Refresh) Advance();
    }

    void OnUpgraded()
    {
        var step = Cur;
        if (step != null && step.kind == TutorialStepKind.Upgrade) Advance();
    }

    void Advance() { _step++; ShowStep(); }

    void ShowStep()
    {
        if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostRends.Clear(); }
        _stepTimer = 0f;
        _freePlaced = 0;

        var step = Cur;
        var pc   = PlacementController.Instance;

        if (step == null)                              // tutorial finished → lift constraint
        {
            if (pc != null) pc.placementConstraint = null;
            CloseDialogue();
            return;
        }

        if (pc != null) pc.placementConstraint = MatchesStep;   // re-arm (Place restricts; others pass)
        if (step.kind == TutorialStepKind.Place) BuildGhost(step);

        FocusCameraForStep(step);

        // Deliver this step's dialogue the moment the step begins — so completing the
        // previous step flows straight into it. During the intro it's deferred; Update
        // plays it after. If the new step has NO dialogue, close any open one.
        if (step.UsesDialogue && !IntroDirector.Playing)
        {
            _dialoguePlayedStep = _step;
            PlayStepDialogue(step);
        }
        else if (!step.UsesDialogue)
        {
            CloseDialogue();
        }
    }

    void CloseDialogue()
    {
        if (_dialogueActive && _runner != null) _runner.Stop();
        _dialogueActive = false;
    }

    // ── Dialogue delivery ───────────────────────────────────────────────────────

    void PlayStepDialogue(TutorialStep step)
    {
        var convo = step.conversation != null ? step.conversation : BuildOneLineConvo(step);
        if (convo == null) return;

        _runner = DialogueRunner.Instance;
        if (_runner == null) return;

        _dialogueActive = true;
        _runner.OnFinished -= OnDialogueFinished;
        _runner.OnFinished += OnDialogueFinished;
        _runner.Play(convo, passive: true);   // display-only: only completing the step advances it
    }

    // A throwaway one-line conversation: `speaker` says the step's `hint`.
    DialogueConversation BuildOneLineConvo(TutorialStep step)
    {
        if (step.speaker == null || string.IsNullOrEmpty(step.hint)) return null;
        if (_runtimeConvo != null) Destroy(_runtimeConvo);
        _runtimeConvo = ScriptableObject.CreateInstance<DialogueConversation>();
        _runtimeConvo.topic = "";
        _runtimeConvo.lines = new List<DialogueLine>
        {
            new DialogueLine
            {
                character = step.speaker,
                text      = step.hint,
                slot      = step.speakerSlot,
                portrait  = step.speakerPortrait,
            }
        };
        return _runtimeConvo;
    }

    // Tutorial dialogue is passive — it never finishes by a click. This only fires
    // when WE stop/replace it (advancing the step), so just clear the active flag.
    void OnDialogueFinished(DialogueConversation _) => _dialogueActive = false;

    // Glide the orbit camera to the start / end point when the step requests it.
    void FocusCameraForStep(TutorialStep step)
    {
        if (step == null || step.cameraFocus == TutorialFocus.None) return;

        var gfm  = GameFlowManager.Instance;
        var grid = GridSystem.instance;
        if (gfm == null || grid == null) return;

        var cells = step.cameraFocus == TutorialFocus.StartPoint ? gfm.AllStarts : gfm.AllEnds;
        if (cells == null || cells.Count == 0) return;

        Vector3 p = grid.GridToWorld(cells[0]) + Vector3.up * (grid.cellSize * 0.5f);
        var orbit = FindFirstObjectByType<OrbitCamera>();
        if (orbit != null)
        {
            orbit.FocusOnPoint(p, snap: false);          // smooth glide
            if (step.focusZoom > 0f) orbit.SetZoom(step.focusZoom);   // optional zoom-in
        }
    }

    void BuildGhost(TutorialStep step)
    {
        var grid = GridSystem.instance;
        if (grid == null) return;
        var target = step.TargetCells();
        if (target.Length == 0)
        {
            Debug.LogWarning("[Tutorial] Place step has no shape — assign a `block` (and set `origin`), or fill `cellsOverride`.");
            return;
        }

        _ghost = new GameObject("TutorialGhost");
        _ghostRends.Clear();
        foreach (var c in target)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(_ghost.transform, false);
            cube.transform.position   = grid.GridToWorld(c);
            cube.transform.localScale = Vector3.one * grid.cellSize * 0.96f;
            var col = cube.GetComponent<Collider>(); if (col != null) Destroy(col);
            var r = cube.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial    = GhostMat();
            MpbColor.Set(r, ghostColor);
            _ghostRends.Add(r);
        }
    }

    Material GhostMat()
    {
        if (_ghostMat != null) return _ghostMat;
        var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        _ghostMat = new Material(sh);
        if (_ghostMat.HasProperty("_Surface"))
        {
            _ghostMat.SetFloat("_Surface", 1f);
            _ghostMat.SetFloat("_ZWrite",  0f);
            _ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _ghostMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return _ghostMat;
    }

    static bool SameSet(Vector3Int[] a, Vector3Int[] b)
    {
        if (a == null || b == null || a.Length != b.Length) return false;
        var set = new HashSet<Vector3Int>(a);
        foreach (var c in b) if (!set.Contains(c)) return false;
        return true;
    }

    // ── Hint UI (UGUI) ────────────────────────────────────────────────────────

    // Reference canvas height — CanvasScaler matches height, so screen fractions
    // map to canvas units by × this. Keeps the shop-bar squeeze math window-relative.
    const float RefH = 1080f;

    void BuildHintUI()
    {
        var go = new GameObject("TutorialHintCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _hintCanvas = go.GetComponent<Canvas>();
        _hintCanvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        _hintCanvas.sortingOrder = 120;   // above HUD (100) & shop bars (55), below pause/intro
        var sc = go.GetComponent<CanvasScaler>();
        sc.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920f, RefH);
        sc.matchWidthOrHeight  = 1f;   // match height → scales with window proportion

        // Start hidden: Update() early-returns during the intro, so UpdateHint()
        // (which sets enabled) doesn't run until the intro finishes — keep the box
        // from flashing its empty background before the first step is shown.
        _hintCanvas.enabled = false;

        // Panel — centred on the normalized hint position (y = distance from top).
        _hintPanel = NewRect("HintPanel", go.transform);
        _hintPanel.anchorMin = _hintPanel.anchorMax = new Vector2(hintScreenPos.x, 1f - hintScreenPos.y);
        _hintPanel.pivot     = new Vector2(0.5f, 0.5f);
        _hintPanel.sizeDelta = new Vector2(hintWidth, hintHeight);
        var bg = _hintPanel.gameObject.AddComponent<Image>();
        bg.color  = new Color(0.05f, 0.05f, 0.06f, 0.9f);
        bg.sprite = UIRoundedRect.Get(18);
        bg.type   = Image.Type.Sliced;
        bg.raycastTarget = false;

        // Left accent bar.
        var accent = NewRect("Accent", _hintPanel);
        accent.anchorMin = new Vector2(0f, 0f);
        accent.anchorMax = new Vector2(0f, 1f);
        accent.pivot     = new Vector2(0f, 0.5f);
        accent.sizeDelta = new Vector2(8f, 0f);
        accent.anchoredPosition = Vector2.zero;
        var accentImg = accent.gameObject.AddComponent<Image>();
        accentImg.color = GeoPalette.Signal;
        accentImg.raycastTarget = false;

        // Text — padded inside the panel.
        var textRT = NewRect("HintText", _hintPanel);
        textRT.anchorMin = Vector2.zero; textRT.anchorMax = Vector2.one;
        textRT.offsetMin = new Vector2(24f, 10f);
        textRT.offsetMax = new Vector2(-18f, -10f);
        _hintText = textRT.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) _hintText.font = font;
        _hintText.fontSize  = hintFontSize;
        _hintText.color     = GeoPalette.Paper;
        _hintText.fontStyle = FontStyles.Bold;
        _hintText.alignment = TextAlignmentOptions.Center;
        _hintText.textWrappingMode = TextWrappingModes.Normal;
        _hintText.raycastTarget = false;
        _hintText.richText = true;

        // Continue light bar — bottom-centre of the panel; pulsed when typing finishes.
        var barRT = NewRect("ContinueBar", _hintPanel);
        barRT.anchorMin = new Vector2(0.5f, 0f);
        barRT.anchorMax = new Vector2(0.5f, 0f);
        barRT.pivot     = new Vector2(0.5f, 0f);
        barRT.sizeDelta = new Vector2(hintWidth * 0.45f, 4f);
        barRT.anchoredPosition = new Vector2(0f, 5f);
        _continueBar = barRT.gameObject.AddComponent<Image>();
        _continueBar.color = GeoPalette.Signal;
        _continueBar.raycastTarget = false;
        _continueBar.enabled = false;
    }

    void UpdateHint()
    {
        if (_hintCanvas == null) return;

        var    step = Cur;
        string msg  = step != null ? step.hint : null;
        bool   show = !string.IsNullOrEmpty(msg) && step != null && !step.UsesDialogue
                      && !SettingsScreen.Open && !PauseMenu.Paused && !IntroDirector.Playing;

        _hintCanvas.enabled = show;
        if (!show) return;

        // (Re)set text & typewriter clock when the step's message changes.
        if (_typedStep != _step || _hintMsg != msg)
        {
            _typedStep = _step;
            _hintMsg   = msg;
            _typeStart = Time.unscaledTime;
            _hintText.text = msg;
            _hintText.ForceMeshUpdate();
            _hintCharCount = _hintText.textInfo.characterCount;
        }

        // Keep the box ABOVE the shop's bottom letterbox bar so expanding the shop
        // squeezes it up (window-relative: bar height as a screen fraction × RefH).
        float barFrac    = ShopController.Instance != null
            ? ShopController.Instance.TopBarHeight / Mathf.Max(1, Screen.height) : 0f;
        float halfHFrac  = (hintHeight * 0.5f) / RefH;
        float centerFrac = 1f - hintScreenPos.y;                       // from bottom
        float minCenter  = barFrac + (8f / RefH) + halfHFrac;
        float pushUp     = Mathf.Max(0f, minCenter - centerFrac) * RefH;
        _hintPanel.anchoredPosition = new Vector2(0f, pushUp);

        // Typewriter reveal via TMP's visible-character clip (keeps rich text intact).
        int  shown = hintCharsPerSecond > 0f
            ? Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - _typeStart) * hintCharsPerSecond), 0, _hintCharCount)
            : _hintCharCount;
        _hintText.maxVisibleCharacters = shown;
        bool typingDone = shown >= _hintCharCount;

        // Continue light bar: pulse once the text is done on a click-to-continue Wait step.
        bool wantBar = typingDone && step.kind == TutorialStepKind.Wait && step.waitSeconds <= 0f;
        _continueBar.enabled = wantBar;
        if (wantBar)
        {
            float pulse = 0.35f + 0.65f * Mathf.PingPong(Time.unscaledTime * 2f, 1f);
            Color sig   = GeoPalette.Signal;
            _continueBar.color = new Color(sig.r, sig.g, sig.b, pulse);
        }
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
