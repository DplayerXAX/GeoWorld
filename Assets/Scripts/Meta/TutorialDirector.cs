using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

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

    LevelDefinition _lv;
    int        _step;
    float      _stepTimer;     // for Wait steps
    int        _freePlaced;    // for FreePlace steps
    GameObject _ghost;
    Material   _ghostMat;
    GUIStyle   _hintStyle;
    readonly List<Renderer> _ghostRends = new();

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
        ShowStep();
    }

    void OnDestroy()
    {
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
    }

    TutorialStep Cur =>
        (_lv != null && _lv.tutorialSteps != null && _step < _lv.tutorialSteps.Count)
            ? _lv.tutorialSteps[_step] : null;

    void Update()
    {
        if (IntroDirector.Playing) return;   // wait for the entrance animation before running steps

        var step = Cur;
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
        if (step != null && step.kind == TutorialStepKind.Purchase && SameBlock(step.block, d)) Advance();
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
            return;
        }

        if (pc != null) pc.placementConstraint = MatchesStep;   // re-arm (Place restricts; others pass)
        if (step.kind == TutorialStepKind.Place) BuildGhost(step);
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

    void OnGUI()
    {
        var step = Cur;
        string msg = step != null ? step.hint : null;
        if (string.IsNullOrEmpty(msg) || SettingsScreen.Open || PauseMenu.Paused || IntroDirector.Playing) return;

        float s = UiScale.Get();
        if (_hintStyle == null)
            _hintStyle = new GUIStyle { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, wordWrap = true };
        _hintStyle.fontSize = Mathf.RoundToInt(20f * s);

        float w = 560f * s, h = 66f * s;
        var r = new Rect((Screen.width - w) * 0.5f, 24f * s, w, h);
        Color p = GUI.color;
        GUI.color = new Color(0.05f, 0.05f, 0.06f, 0.82f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = GeoPalette.Signal;
        GUI.DrawTexture(new Rect(r.x, r.y, 6f * s, h), Texture2D.whiteTexture);
        GUI.color = p;
        _hintStyle.normal.textColor = GeoPalette.Paper;
        GUI.Label(r, msg, _hintStyle);
    }
}
