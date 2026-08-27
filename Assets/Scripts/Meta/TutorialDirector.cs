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
    [Tooltip("Screen position (normalized): x = horizontal centre (0..1), y = distance from the TOP (0..1). Kept clear of the very bottom — PlacementHintBar's WASDQE card lives there whenever a block is held, and at 0.92 the two drew on top of each other.")]
    public Vector2 hintScreenPos = new Vector2(0.5f, 0.845f);
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
    public static bool CanSell()    => (_active == null || _active.AllowOp(TutorialStepKind.Sell, null)) && !IsFirstRoundLocked;
    public static bool CanRefresh() => _active == null || _active.AllowOp(TutorialStepKind.Refresh, null);
    public static bool CanUpgrade() => _active == null || _active.AllowOp(TutorialStepKind.Upgrade, null);
    public static bool CanRun()     => _active == null || _active.AllowOp(TutorialStepKind.Run,     null);
    // Undo isn't a taught step — it's just locked outright during the tutorial's first
    // round so players can't unwind the guided placements before they've cleared a wave.
    public static bool CanUndo()    => !IsFirstRoundLocked;

    static bool IsFirstRoundLocked =>
        _active != null && GameFlowManager.Instance != null && GameFlowManager.Instance.WavesCleared == 0;

    // Which BlockData the shop should visually highlight right now — the exact item
    // (or item category, if the step doesn't pin one down) the current Purchase-kind
    // step is asking for. Used by ShopController to glow the target and dim the rest.
    public static bool IsPurchaseTarget(BlockData d)
    {
        if (_active == null || _active._pendingStepIndex >= 0) return false;
        var step = _active.Cur;
        if (step == null || step.freeOperations || !IsPurchaseKind(step.kind)) return false;
        return PurchaseMatches(step, d);
    }

    // True while the shop should be dimming everything that isn't the current
    // purchase target (i.e. a Purchase-kind step is actively being taught).
    public static bool IsPurchaseStepActive
    {
        get
        {
            if (_active == null || _active._pendingStepIndex >= 0) return false;
            var step = _active.Cur;
            return step != null && !step.freeOperations && IsPurchaseKind(step.kind);
        }
    }

    bool AllowOp(TutorialStepKind op, BlockData d)
    {
        // While the NEXT step is held back by a wave gate (requiredWave), the
        // tutorial is between steps and waiting for the player to fight more waves
        // — so lift ALL gating. Otherwise the previous step's restrictions would
        // block Run, the wave count could never advance, and the gated step would
        // never appear (a deadlock).
        if (_pendingStepIndex >= 0) return true;

        var step = Cur;
        if (step == null) return true;            // tutorial finished → no gating
        // freeOperations means "no restrictions at all" (a true sandbox/review step) —
        // if a step wants to restrict WHAT can be bought/run, it must not set this;
        // see the two PurchaseTurret steps in Level_tuto for the intended pattern.
        if (step.freeOperations) return true;     // sandbox step
        // Hidden-in-combat steps lift their gating during the wave so the tutorial
        // never blocks combat actions.
        if (step.hideInCombat && GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running) return true;
        // An earlier step can permanently unlock an op (TutorialStep.unlocksOps) —
        // once reached, it stays allowed regardless of what the CURRENT step teaches.
        if (IsOpUnlockedByPastStep(op)) return true;
        // Buying: CanPurchase passes the generic Purchase intent. Allow it when the
        // current step is ANY purchase kind and the bought item fits that kind.
        if (op == TutorialStepKind.Purchase)
            return IsPurchaseKind(step.kind) && PurchaseMatches(step, d);
        if (step.kind != op) return false;        // this op isn't the current step → locked
        return true;
    }

    // Scans every step up to (and including) the current one for an unlocksOps entry
    // matching `op`. Step lists are short (tens of entries), so a linear scan each call
    // is cheap — no need to cache/accumulate a running set.
    bool IsOpUnlockedByPastStep(TutorialStepKind op)
    {
        var steps = _lv != null ? _lv.tutorialSteps : null;
        if (steps == null) return false;
        int upTo = Mathf.Min(_step, steps.Count - 1);
        for (int i = 0; i <= upTo; i++)
        {
            var s = steps[i];
            if (s?.unlocksOps == null) continue;
            for (int j = 0; j < s.unlocksOps.Length; j++)
            {
                var u = s.unlocksOps[j];
                if (u == op) return true;
                // CanPurchase always queries the generic Purchase (never PurchaseBlock/
                // PurchaseTurret directly — see CanPurchase above), so a designer marking
                // PurchaseBlock or PurchaseTurret as unlocked clearly means "buying is
                // unlocked" too — treat the whole purchase family as interchangeable here.
                if (op == TutorialStepKind.Purchase && IsPurchaseKind(u)) return true;
            }
        }
        return false;
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
    int        _pendingStepIndex = -1;   // -1 = none; else next step index held back by a wave gate
    float      _stepTimer;     // for Wait steps
    int        _freePlaced;    // for FreePlace steps
    GameObject _ghost;
    Material   _ghostMat;
    readonly List<Renderer> _ghostRends = new();

    [Tooltip("Suggestion box tint — a non-binding hint (see TutorialStep.suggestCubeSide), distinct from ghostColor's binding-placement look.")]
    public Color suggestColor = new Color(0.25f, 0.55f, 1f, 0.35f);
    [Tooltip("Suggested-route tiles (TutorialStep.suggestSpanEndpoints). White like the placement ghost, not the blue box tint — it's marking where blocks should GO, which is the ghost's language, not the 'somewhere in this region' one.")]
    public Color suggestRouteColor = new Color(1f, 1f, 1f, 0.5f);

    // Suggestion volumes sit just INSIDE their cells. At exactly one cell they end
    // up coplanar with any block placed there, and the two surfaces z-fight into a
    // half-through-the-block look. A hair smaller reads as a marker the block drops
    // into instead.
    const float SuggestCellInset = 0.92f;
    [Tooltip("Tint for the optional second, layered suggestion box (see TutorialStep.suggestCubeSide2).")]
    public Color suggestColor2 = new Color(0.95f, 0.75f, 0.25f, 0.3f);
    GameObject _suggestBox, _suggestBox2;
    Renderer   _suggestRend, _suggestRend2;

    // ── Hint UI (UGUI) ────────────────────────────────────────────────────────
    Canvas        _hintCanvas;
    CanvasGroup   _hintGroup;

    [Tooltip("Opacity the hint box drops to while the player is mid-placement — matches DialogueRunner.editDim so a speakerless step gets out of the way exactly like a spoken one.")]
    [Range(0f, 1f)] public float hintEditDim = 0.25f;
    RectTransform _hintPanel;
    TMP_Text      _hintText;
    Image         _continueBar;

    // Typewriter state — typing starts the first frame the hint is actually shown
    // (so it doesn't "pre-type" while hidden during the intro / pause).
    int    _typedStep = -1;
    float  _typeStart;
    string _hintMsg = "";
    TMP_FontAsset hintFont;
    int    _hintCharCount;
    int    _hintShownPrev;

    // ── Dialogue delivery ───────────────────────────────────────────────────────
    int                  _dialoguePlayedStep = -1;   // which step has had its convo kicked off
    bool                 _dialogueActive;            // a tutorial conversation is on screen
    DialogueConversation _runtimeConvo;              // built for one-line speaker hints
    DialogueRunner       _runner;                    // cached so OnDestroy doesn't spawn one

    // ShowStep() also builds the placement ghost and moves the camera (FocusCameraForStep) —
    // not just dialogue — so the FIRST step must wait for the intro too, not fire at Start().
    bool _firstStepShown;

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
        // ShowStep() also spawns the ghost + moves the camera — deferred to Update() until
        // the intro finishes (see _firstStepShown), so it doesn't fire mid-intro.
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
        if (_suggestBox != null) Destroy(_suggestBox);
        if (_suggestBox2 != null) Destroy(_suggestBox2);
        if (_runner != null) _runner.OnFinished -= OnDialogueFinished;
        if (_runtimeConvo != null) Destroy(_runtimeConvo);
    }

    TutorialStep Cur =>
        (_lv != null && _lv.tutorialSteps != null && _step < _lv.tutorialSteps.Count)
            ? _lv.tutorialSteps[_step] : null;

    void Update()
    {
        if (IntroDirector.Playing) return;   // wait for the entrance animation before running steps

        // The level is over — retire the tutorial whether or not it finished.
        //
        // A level can be cleared while steps are still queued: objective-driven
        // clears fire the moment the goals are met, and a player who works it out
        // early gets there before the script does. Carrying on would mean hints for
        // a level that's already won, still gating operations, still holding a ghost
        // over a board nobody is playing.
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.LevelCleared)
        {
            RetireTutorial();
            return;
        }

        if (!_firstStepShown)
        {
            _firstStepShown = true;
            if (IsWaveGated(_step)) _pendingStepIndex = _step;
            else ShowStep();
        }

        // A wave-gated step is held here until the player reaches the required wave —
        // frozen (no ghost/dialogue/hint, no per-frame completion checks below) rather
        // than re-running the just-completed step's triggers every frame.
        if (_pendingStepIndex >= 0)
        {
            if (IsWaveGated(_pendingStepIndex)) return;
            _step = _pendingStepIndex;
            _pendingStepIndex = -1;
            ShowStep();
        }

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

        // A step-owned suggestion region is a BUILD-phase hint. Once the wave is
        // running there's nothing to act on it with, and it would just sit under
        // the fight.
        if (_suggestTransient && GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running)
            ClearSuggestBox();

        if (step != null && step.kind == TutorialStepKind.Run
            && GameFlowManager.Instance != null
            && GameFlowManager.Instance.phase == GamePhase.Running)
            Advance();

        // Wait step: auto-advance after waitSeconds, or on a click if waitSeconds <= 0.
        if (step != null && step.kind == TutorialStepKind.Wait)
        {
            _stepTimer += Time.deltaTime;
            bool timed   = step.waitSeconds > 0f && _stepTimer >= step.waitSeconds;
            bool clicked = step.waitSeconds <= 0f && InputReady
                        && (Input.GetMouseButtonDown(0) || GamepadInput.ConfirmDown);
            if (timed || clicked) { Advance(); return; }
        }

        // Input step: advance when the required key (or its gamepad equivalent) is pressed.
        if (step != null && step.kind == TutorialStepKind.Input && InputReady
            && (Input.GetKeyDown(step.inputKey) || GamepadInput.MatchesKeyCode(step.inputKey)))
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

        if (_suggestRend != null)
        {
            float a = suggestColor.a * (0.5f + 0.5f * Mathf.PingPong(Time.time * 0.8f, 1f));
            MpbColor.Set(_suggestRend, new Color(suggestColor.r, suggestColor.g, suggestColor.b, a));
        }

        if (_routeRends.Count > 0)
        {
            float a = suggestRouteColor.a * (0.55f + 0.45f * Mathf.PingPong(Time.time * 0.8f, 1f));
            var c = new Color(suggestRouteColor.r, suggestRouteColor.g, suggestRouteColor.b, a);
            for (int i = _routeRends.Count - 1; i >= 0; i--)
            {
                if (_routeRends[i] == null) { _routeRends.RemoveAt(i); continue; }
                MpbColor.Set(_routeRends[i], c);
            }
        }
        if (_suggestRend2 != null)
        {
            // Slightly out of phase with the first box so the two tiers read as
            // distinct layers rather than pulsing in lockstep.
            float a = suggestColor2.a * (0.5f + 0.5f * Mathf.PingPong(Time.time * 0.8f + 0.5f, 1f));
            MpbColor.Set(_suggestRend2, new Color(suggestColor2.r, suggestColor2.g, suggestColor2.b, a));
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
        // Remembered for TutorialFocus.LastPlacedBlock — recorded before the
        // step checks below, which can return early or advance the step.
        if (worldCells != null && worldCells.Length > 0) _lastPlacedCells = worldCells;

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

    void Advance()
    {
        int next = _step + 1;
        if (IsWaveGated(next))
        {
            // Hold at the just-completed step's index (don't touch _step) — Update()
            // polls IsWaveGated each frame and actually advances once it opens.
            _pendingStepIndex = next;
            HideStepVisuals();
            return;
        }
        _step = next;
        ShowStep();
    }

    // True while `stepIndex` exists, has a requiredWave, and the player hasn't reached
    // that wave yet (GameFlowManager.UpcomingWaveNumber is the 1-based "which wave is
    // this" counter — see its own doc comment). No GameFlowManager / level fallback:
    // no way to check, so don't gate.
    bool IsWaveGated(int stepIndex)
    {
        var steps = _lv != null ? _lv.tutorialSteps : null;
        if (steps == null || stepIndex < 0 || stepIndex >= steps.Count) return false;
        var step = steps[stepIndex];
        return step != null && step.requiredWave > 0
            && GameFlowManager.Instance != null
            && GameFlowManager.Instance.UpcomingWaveNumber < step.requiredWave;
    }

    // Shuts the tutorial down for good: everything it put on screen goes, the
    // placement constraint is lifted so nothing stays gated, and the component
    // disables itself so Update stops running. Not Destroy — OnDestroy's unsubscribe
    // still needs to happen when the scene unloads, and re-enabling is a debugging
    // affordance worth keeping.
    void RetireTutorial()
    {
        HideStepVisuals();
        ClearSuggestBox();
        if (_suggestBox2 != null) { Destroy(_suggestBox2); _suggestBox2 = null; _suggestRend2 = null; }

        var pc = PlacementController.Instance;
        if (pc != null) pc.placementConstraint = null;

        _step = _lv != null && _lv.tutorialSteps != null ? _lv.tutorialSteps.Count : 0;
        _pendingStepIndex = -1;
        enabled = false;
    }

    // Clears whatever the just-completed step left on screen (ghost / dialogue / hint)
    // while we wait for a wave-gated next step to open up.
    void HideStepVisuals()
    {
        if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostRends.Clear(); }
        // Suggestion box is intentionally NOT cleared here — it's a standing hint,
        // not tied to any one step's lifecycle (see ShowStep()).
        CloseDialogue();
        if (_hintCanvas != null) _hintCanvas.enabled = false;
    }

    // Frame the current step began on. A click-advanced step must not eat the SAME
    // click that finished the previous one: the previous step completes mid-frame
    // (a Select event, say), ShowStep runs immediately, and GetKeyDown is still
    // true for the rest of that frame — so two click-steps in a row collapsed into
    // one and the second line was never on screen for a single frame.
    int  _stepFrame = -1;

    // True once the player has skipped (or the typewriter has finished) the current
    // step's hint. A click that lands mid-type is spent COMPLETING the text, so it
    // must not also count as the click that satisfies a Wait/Input step — otherwise
    // one press does both and the player never sees the second half of the line.
    bool _hintTypeSkipped;

    bool HintStillTyping =>
        _hintCanvas != null && _hintCanvas.enabled
        && !_hintTypeSkipped && _hintCharCount > 0 && hintCharsPerSecond > 0f
        && (Time.unscaledTime - _typeStart) * hintCharsPerSecond < _hintCharCount;

    // Same rule for the DIALOGUE-backed steps (a step with a speaker delivers its
    // hint through DialogueRunner, not the hint box). Without this the click that
    // completes the spoken line also satisfies the Wait/Input step in the same
    // frame, so a speaker-backed step could never be read to the end.
    static bool DialogueStillTyping =>
        DialogueRunner.Instance != null && DialogueRunner.Instance.IsTyping;

    bool InputReady => Time.frameCount > _stepFrame && !HintStillTyping && !DialogueStillTyping;

    void ShowStep()
    {
        if (_ghost != null) { Destroy(_ghost); _ghost = null; _ghostRends.Clear(); }
        _stepTimer = 0f;
        _freePlaced = 0;
        _stepFrame = Time.frameCount;

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
        // A transient box belongs to the step that asked for it — drop the previous
        // one before this step gets a chance to build its own.
        if (_suggestTransient) ClearSuggestBox();

        if (step.suggestSpanEndpoints && _suggestBox == null)
        {
            _suggestBox       = BuildSpanRoute(step);
            _suggestTransient = step.suggestTransient;
        }
        else if (step.suggestCubeSide > 0 && _suggestBox == null)
        {
            _suggestRend = BuildSuggestBox(step.suggestOrigin, step.suggestCubeSide, suggestColor, out _suggestBox);
            _suggestTransient = step.suggestTransient;
        }
        if (step.suggestCubeSide2 > 0 && _suggestBox2 == null)
            _suggestRend2 = BuildSuggestBox(step.suggestOrigin2, step.suggestCubeSide2, suggestColor2, out _suggestBox2);
        if (!string.IsNullOrEmpty(step.asideText))
            AsideBubble.Show(step.AsideSpeaker, step.AsidePortrait, step.asideText);

        FocusCameraForStep(step);
        HudSidePanels.Open(step.openPanel);   // no-op when the step doesn't ask for one

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

    [Header("Camera focus (steps with cameraFocus set)")]
    [Tooltip("Vertical viewport position the focused subject sits at while a step directs the camera at it — a little above dead-centre so the subject doesn't end up sitting behind the dialogue box's bottom band. 0.5 = screen centre.")]
    [Range(0.5f, 0.75f)] public float focusViewportY = 0.65f;

    // Glide the orbit camera to the start / end point / a live chaos block when
    // the step requests it.
    void FocusCameraForStep(TutorialStep step)
    {
        var orbit = FindFirstObjectByType<OrbitCamera>();
        if (orbit == null) return;

        if (step == null || step.cameraFocus == TutorialFocus.None)
        {
            // No directed focus this step — clear any bias a PREVIOUS step left
            // behind so ordinary player-driven orbiting isn't stuck off-centre.
            orbit.focusViewport = new Vector2(0.5f, 0.5f);
            return;
        }

        // Every resolver may return null when its subject doesn't exist yet (no
        // synergy active, no enemy alive, ...). That's not an error — the camera
        // just stays where it is rather than snapping somewhere meaningless.
        Vector3? p = step.cameraFocus switch
        {
            TutorialFocus.ChaosBlock      => ChaosBlockFocusPoint(),
            TutorialFocus.StepOrigin      => StepOriginFocusPoint(step),
            TutorialFocus.Synergy         => SynergyFocusPoint(),
            TutorialFocus.Enemy           => EnemyFocusPoint(),
            TutorialFocus.LastPlacedBlock => LastPlacedBlockFocusPoint(),
            _                             => StartOrEndFocusPoint(step.cameraFocus),
        };
        if (p == null) return;

        // Locked dead-centre horizontally, nudged up vertically — see the
        // tooltip above for why (keeps the subject clear of the dialogue box).
        orbit.focusViewport = new Vector2(0.5f, focusViewportY);
        orbit.FocusOnPoint(p.Value, snap: false);          // smooth glide
        if (step.focusZoom > 0f) orbit.SetZoom(step.focusZoom);   // optional zoom-in
    }

    Vector3Int[] _lastPlacedCells;

    // The step's own authored cells. This is the general-purpose focus: anything
    // that shows up at a spot you can write down (a guide ghost, a block you're
    // about to make appear, a landmark) is reachable without a bespoke enum entry.
    Vector3? StepOriginFocusPoint(TutorialStep step)
    {
        var cells = step.cellsOverride != null && step.cellsOverride.Length > 0
            ? step.cellsOverride
            : new[] { step.origin };
        return CellsCentre(cells);
    }

    // Centre of the first active synergy's participating cells — for "look, this
    // is what you just formed" beats.
    Vector3? SynergyFocusPoint()
    {
        var ev = SynergyEvaluator.Instance;
        if (ev == null) return null;

        var actives = ev.Actives;
        for (int i = 0; i < actives.Count; i++)
        {
            var a = actives[i];
            if (a?.claimedPieces == null) continue;

            var cells = new List<Vector3Int>();
            foreach (var p in a.claimedPieces)
            {
                if (p?.cells == null) continue;
                for (int k = 0; k < p.cells.Length; k++)
                {
                    // Respect the rule's own filter, so this frames the actual
                    // loop / cube rather than its inert claimed tail.
                    if (a.highlightCells != null && !a.highlightCells.Contains(p.cells[k])) continue;
                    cells.Add(p.cells[k]);
                }
            }
            var c = CellsCentre(cells.ToArray());
            if (c != null) return c;
        }
        return null;
    }

    Vector3? EnemyFocusPoint()
    {
        var mgr = EnemyBaseManager.Instance;
        if (mgr == null) return null;

        var enemies = mgr.ActiveEnemies;
        for (int i = 0; i < enemies.Count; i++)
            if (enemies[i] != null && enemies[i].CurrentHealth > 0)
                return enemies[i].transform.position;
        return null;
    }

    Vector3? LastPlacedBlockFocusPoint() => CellsCentre(_lastPlacedCells);

    static Vector3? CellsCentre(Vector3Int[] cells)
    {
        var grid = GridSystem.instance;
        if (grid == null || cells == null || cells.Length == 0) return null;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < cells.Length; i++) sum += grid.GridToWorld(cells[i]);
        return sum / cells.Length + Vector3.up * (grid.cellSize * 0.5f);
    }

    Vector3? StartOrEndFocusPoint(TutorialFocus focus)
    {
        var gfm  = GameFlowManager.Instance;
        var grid = GridSystem.instance;
        if (gfm == null || grid == null) return null;

        var cells = focus == TutorialFocus.StartPoint ? gfm.AllStarts : gfm.AllEnds;
        if (cells == null || cells.Count == 0) return null;

        return grid.GridToWorld(cells[0]) + Vector3.up * (grid.cellSize * 0.5f);
    }

    // First currently-alive Chaos Block (see ChaosBlockController). Null if the
    // mechanic isn't enabled for this level, or every block spawned so far has
    // already been killed — the camera just stays put in that case.
    Vector3? ChaosBlockFocusPoint()
    {
        var ctrl = ChaosBlockController.Instance;
        if (ctrl == null) return null;

        var alive = ctrl.Alive;
        for (int i = 0; i < alive.Count; i++)
            if (alive[i] != null && alive[i].CurrentHealth > 0)
                return alive[i].transform.position;
        return null;
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

    // A single translucent box spanning an N×N×N region — a non-binding suggestion,
    // not a placement requirement (unlike BuildGhost's per-cell Place-step ghost).
    // Persists once built regardless of step changes — see ShowStep()/HideStepVisuals().
    // Two of these can be layered (e.g. a 3³ tier nested inside a 4³ tier) — see
    // suggestCubeSide/suggestCubeSide2 in ShowStep().
    // True while the box on screen was built by a step that owns it (see
    // TutorialStep.suggestTransient). The original boxes are standing hints that
    // outlive their step, so this can't just be assumed.
    bool _suggestTransient;

    // Route tiles are pulsed as a set — they're one hint, not N of them.
    readonly List<Renderer> _routeRends = new();

    void ClearSuggestBox()
    {
        if (_suggestBox != null) Destroy(_suggestBox);
        _suggestBox       = null;
        _suggestRend      = null;
        _suggestTransient = false;
        _routeRends.Clear();
    }

    // A suggested ROUTE — one flat marker per cell, tracing a detour from the start
    // point to the end point that's at least `pathLength` long.
    //
    // A chain of tiles rather than one slab: the step is asking the player to lay a
    // longer ROAD, and a filled rectangle says "build anywhere in here", which is
    // both wrong and unhelpfully vague. The shape itself is the instruction.
    //
    // Derived from the live endpoints rather than authored cells — that region is a
    // property of where the start and end actually are, and hardcoded coordinates
    // would go stale the first time a layout or an endpoint moved.
    GameObject BuildSpanRoute(TutorialStep step)
    {
        var grid = GridSystem.instance;
        var flow = GameFlowManager.Instance;
        if (grid == null || flow == null) return null;
        if (flow.AllStarts.Count == 0 || flow.AllEnds.Count == 0) return null;

        Vector3Int s = flow.AllStarts[0], e = flow.AllEnds[0];

        // The straight run is `span` cells; the step wants `pathLength`. A detour
        // out and back adds twice its depth, so that's what has to cover the gap.
        int span = Mathf.Abs(e.x - s.x) + Mathf.Abs(e.z - s.z);
        int need = Mathf.Max(0, step.pathLength - span);
        int d    = Mathf.Max(1, Mathf.CeilToInt(need * 0.5f)) + Mathf.Max(0, step.suggestSpanMargin);

        // Bump perpendicular to the dominant run — pushed along the same axis it
        // already travels, the detour would fold back onto the straight line and
        // read as nothing at all.
        bool xDominant = Mathf.Abs(e.x - s.x) >= Mathf.Abs(e.z - s.z);
        Vector3Int off = xDominant ? new Vector3Int(0, 0, d) : new Vector3Int(d, 0, 0);

        var cells = new List<Vector3Int>();
        AddLeg(cells, s,       s + off);
        AddLeg(cells, s + off, e + off);
        AddLeg(cells, e + off, e);
        if (cells.Count == 0) return null;

        float cs   = grid.cellSize;
        var   root = new GameObject("TutorialSuggestRoute");
        _routeRends.Clear();

        foreach (var c in cells)
        {
            var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
            tile.name = "RouteTile";
            tile.transform.SetParent(root.transform, false);
            tile.transform.position = grid.GridToWorld(c);
            // A FULL cell, same as BuildSuggestBox — this marks where a block goes,
            // so it has to be the size of the block that goes there. The flat inset
            // tile this started as read as a floor decal instead.
            tile.transform.localScale = Vector3.one * (cs * SuggestCellInset);

            var col = tile.GetComponent<Collider>(); if (col != null) Destroy(col);
            var r = tile.GetComponent<Renderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.sharedMaterial    = GhostMat();
            MpbColor.Set(r, suggestRouteColor);
            _routeRends.Add(r);
        }
        return root;
    }

    // Grid-walks one leg, X first then Z, skipping the cell it starts on so joins
    // don't double up.
    static void AddLeg(List<Vector3Int> into, Vector3Int from, Vector3Int to)
    {
        if (into.Count == 0) into.Add(from);

        var c = from;
        while (c.x != to.x) { c.x += to.x > c.x ? 1 : -1; into.Add(c); }
        while (c.z != to.z) { c.z += to.z > c.z ? 1 : -1; into.Add(c); }
    }

    Renderer BuildSuggestBox(Vector3Int origin, int side, Color tint, out GameObject box)
    {
        box = null;
        var grid = GridSystem.instance;
        if (grid == null) return null;

        side = Mathf.Max(1, side);
        float cs = grid.cellSize;
        Vector3 min = grid.GridToWorld(origin) - Vector3.one * (cs * 0.5f);
        Vector3 center = min + Vector3.one * (cs * side * 0.5f);

        box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = "TutorialSuggestBox";
        box.transform.position   = center;
        box.transform.localScale = Vector3.one * (cs * side * SuggestCellInset);
        var col = box.GetComponent<Collider>(); if (col != null) Destroy(col);
        var rend = box.GetComponent<Renderer>();
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.sharedMaterial    = GhostMat();
        MpbColor.Set(rend, tint);
        return rend;
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

        // Lets the box FADE rather than only switch on and off, which is what the
        // edit-mode dim needs — a speakerless step was previously the one kind of
        // tutorial text that stayed fully opaque over a block being placed, while
        // every DialogueRunner line got out of the way.
        _hintGroup = go.AddComponent<CanvasGroup>();
        _hintGroup.blocksRaycasts = false;
        _hintGroup.interactable   = false;

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

        // Same round pulsing ball as DialogueRunner, planted after the last visible
        // character (see PositionContinueBall). Parented to the text itself.
        var barRT = NewRect("ContinueBall", _hintText.rectTransform);
        barRT.anchorMin = barRT.anchorMax = Vector2.zero;
        barRT.pivot     = new Vector2(0f, 0.5f);
        barRT.sizeDelta = new Vector2(24f, 24f);
        _continueBar = barRT.gameObject.AddComponent<Image>();
        _continueBar.sprite = UIRoundedRect.Get(16);
        _continueBar.type   = Image.Type.Simple;
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
                      && !SettingsScreen.Open && !PauseMenu.Paused && !IntroDirector.Playing
                      && !PeekWorld.Held;

        _hintCanvas.enabled = show;
        if (!show)
        {
            AudioManager.Instance?.StopTextBlip();   // guard: hint hidden (pause/step change) mid-type
            return;
        }

        // Dim (don't hide) while a block is being placed — same rule and same
        // reason as DialogueRunner's editDim: the box sits over the board the
        // player is aiming at, and reading it isn't what they're doing right now.
        if (_hintGroup != null)
        {
            var pc = PlacementController.Instance;
            bool editing = (pc != null && pc.mode == PlacementMode.Edit)
                        || (LevelMapController.Instance != null && LevelMapController.Instance.BuildMode);
            _hintGroup.alpha = Mathf.MoveTowards(_hintGroup.alpha, editing ? hintEditDim : 1f,
                                                 6f * Time.unscaledDeltaTime);
        }

        // (Re)set text & typewriter clock when the step's message changes.
        if (_typedStep != _step || _hintMsg != msg)
        {
            _typedStep = _step;
            _hintMsg   = msg;
            if(hintFont!=null)_hintText.font = hintFont;
            _typeStart = Time.unscaledTime;
            _hintText.text = msg;
            _hintText.ForceMeshUpdate();
            _hintCharCount = _hintText.textInfo.characterCount;
            _hintShownPrev = 0;
            _hintTypeSkipped = false;                 // new line — it types again
            AudioManager.Instance?.StartTextBlip();   // TextBlip is one continuous segment — start once per line
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
        int  shown = _hintTypeSkipped || hintCharsPerSecond <= 0f
            ? _hintCharCount
            : Mathf.Clamp(Mathf.FloorToInt((Time.unscaledTime - _typeStart) * hintCharsPerSecond), 0, _hintCharCount);
        _hintText.maxVisibleCharacters = shown;
        bool typingDone = shown >= _hintCharCount;

        // A click while the hint is still typing COMPLETES it instead of counting
        // toward whatever the step is waiting for. Same rule as DialogueRunner: the
        // first press means "I read faster than this", not "I'm done with it".
        // HintTypeSkipped is what the step-advance checks consult (see InputReady).
        if (!typingDone && !_hintTypeSkipped && Time.frameCount != _stepFrame
            && (Input.GetMouseButtonDown(0) || GamepadInput.ConfirmDown))
        {
            _hintTypeSkipped = true;
            AudioManager.Instance?.StopTextBlip();
        }
        if (typingDone && _hintShownPrev < _hintCharCount) AudioManager.Instance?.StopTextBlip();
        _hintShownPrev = shown;

        // Continue light: pulse once the text is done on a click-to-continue Wait step.
        bool wantBar = typingDone && step.kind == TutorialStepKind.Wait && step.waitSeconds <= 0f;
        _continueBar.enabled = wantBar;
        if (wantBar)
        {
            float pulse = 0.35f + 0.65f * Mathf.PingPong(Time.unscaledTime * 2f, 1f);
            Color sig   = GeoPalette.Signal;
            _continueBar.color = new Color(sig.r, sig.g, sig.b, pulse);
            float d = Mathf.Lerp(20f, 32f, pulse);   // pulses as a circle, same as DialogueRunner's
            _continueBar.rectTransform.sizeDelta = new Vector2(d, d);
            PositionContinueBall(shown);
        }
    }

    // Mirrors DialogueRunner.PositionContinueBar.
    void PositionContinueBall(int shown)
    {
        var ti = _hintText.textInfo;
        if (ti.characterCount == 0 || shown <= 0) return;

        int idx = Mathf.Clamp(shown - 1, 0, ti.characterCount - 1);
        var ci  = ti.characterInfo[idx];
        while (idx > 0 && !ci.isVisible) { idx--; ci = ti.characterInfo[idx]; }
        if (!ci.isVisible) return;

        const float gapX = 14f, dropY = -2f;
        _continueBar.rectTransform.localPosition =
            new Vector3(ci.bottomRight.x + gapX, ci.bottomRight.y + dropY, 0f);
    }

    RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
}
