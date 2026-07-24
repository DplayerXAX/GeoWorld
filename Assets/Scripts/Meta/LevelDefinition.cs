using System.Collections.Generic;
using UnityEngine;

// One playable level. Authored as an asset and referenced by LevelNode on the
// level-select map. Reuses the existing wave / pacing systems — GameFlowManager
// applies these on Start when RunConfig.Mode == Level.
[CreateAssetMenu(menuName = "GeoWorld/Level", fileName = "Level")]
public class LevelDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable key used in the save file (unlocks / records). Don't rename casually.")]
    public string levelId;
    public string displayName;
    [TextArea] public string description;

    [Header("Run setup")]
    [Tooltip("Fixed seed so the level plays the same each attempt. 0 = randomize.")]
    public ulong runSeed;
    [Min(1)] public int blocksPerTurn  = 8;
    [Min(0)] public int turretsPerTurn = 3;

    [Tooltip("Whether synergies exist at all on this level. Off = placing/picking up a block never touches SynergyEvaluator: no board tracking, no rule checks, no activation FX. For an early tutorial level that hasn't introduced synergies yet, or a mechanic-focused level that deliberately doesn't use them. On by default so existing levels are unaffected.")]
    public bool synergyEnabled = true;

    [Tooltip("Synergy colors allowed to roll in THIS level's shop (ColorDistribution). Fixed per level, independent of any other level's progress — replaying an earlier level always sees the same pool its designer set. Empty = no restriction (every color in ColorDistribution.weights can roll). Author later levels with a longer list to hand-tune a difficulty/variety curve, e.g. 1-1 = [Abundance], 1-2 = [Abundance, Order], ...")]
    public BlockColor[] allowedColors;

    // True if `c` can appear in this level — same "empty = unrestricted" rule
    // ColorDistribution.BeginRound already applies to allowedColors. Also used
    // to decide whether the turret-upgrade UI should even be shown (no point
    // if Enlightenment can never roll this level).
    public bool AllowsColor(BlockColor c) =>
        allowedColors == null || allowedColors.Length == 0 || System.Array.IndexOf(allowedColors, c) >= 0;

    [Tooltip("Blocks GUARANTEED in the very first build phase's shop — the rest of the slots still roll randomly, and every later round is fully random. Turret entries fill the turret row, everything else the block row; extras beyond a row's size are dropped. Use it to hand-set an opening hand (a tutorial's ORANGE block, a level that must open with an AOE turret, ...) instead of leaning on runSeed to luck into it. Empty = fully random opening shop.")]
    public ShopEntry[] startingShop;

    [Tooltip("Authored waves (fed into GameFlowManager.waves). Leave empty to use the procedural generator.")]
    public List<WaveDefinition> waves = new();

    [Header("Victory")]
    [Tooltip("Waves the player must survive to clear the level. 0 = endless within this level.")]
    [Min(0)] public int wavesToClear = 5;

    [Header("Objectives (shown top-left; tracked by LevelObjectivesTracker)")]
    [Tooltip("Special goals for this level. Non-optional ones can gate the clear if requireAllObjectives is on.")]
    public List<LevelObjective> objectives = new();
    [Tooltip("If on, ALL non-optional objectives must be satisfied (in addition to wavesToClear) to count as cleared.")]
    public bool requireAllObjectives = false;
    [Tooltip("OBJECTIVE-DRIVEN clear: the level is cleared the moment every non-optional objective is satisfied (checked any phase), regardless of wavesToClear. Use for build/puzzle levels.")]
    public bool clearOnObjectivesComplete = false;

    [Header("Progression")]
    [Tooltip("This level starts unlocked (e.g. the first level).")]
    public bool unlockedByDefault;

    [Tooltip("Levels unlocked when this one is first cleared.")]
    public LevelDefinition[] unlocks;

    [Tooltip("Tech points granted on first clear.")]
    [Min(0)] public int techReward = 1;

    [Tooltip("Blocks granted (once, on first clear) that the player can place on the LevelSelect map (Build mode) to extend the walkable network toward other levels — must also appear in LevelMapController.buildableBlocks to be placeable.")]
    public BlockData[] mapBlockRewards;

    [Tooltip("Played once, back on LevelSelect, right after this level's FIRST clear (same one-time gate as the tech/unlock/map-block rewards above). Author it like any other DialogueConversation asset. Leave null for no conversation.")]
    public DialogueConversation rewardConversation;

    [Header("Tutorial")]
    [Tooltip("Marks this level as a tutorial — TutorialDirector takes over (fixed endpoints + ghost-guided placement).")]
    public bool isTutorial;
    [Tooltip("Use the fixed start/end cells below instead of the random endpoint generator.")]
    public bool fixedEndpoints;
    public Vector3Int startCell;
    public Vector3Int endCell;
    [Tooltip("Ordered guided placements. Each shows a ghost the player must match exactly before they can place.")]
    public List<TutorialStep> tutorialSteps = new();

    [Header("Starting layout")]
    [Tooltip("Optional pre-built blocks placed on the grid at level start, authored with LevelMapAuthor "
           + "in any scene with a GridSystem + PlacementController (save under a level-specific map name, "
           + "then bake via GeoWorld ▸ Level Map ▸ Bake JSON → Asset).")]
    public LevelMapAsset startingLayout;

    [Header("Endpoint spacing (overrides GameFlowManager's defaults)")]
    [Tooltip("ON: use this level's minDistance/maxDistance below for EVERY start/end point this level generates (the opening pair AND every later added endpoint) — overrides GameFlowManager's blocksPerTurn-derived auto bounds and the LevelEndpointGenerator's own Inspector defaults. OFF: this level behaves exactly as before (whatever GameFlowManager/the generator would otherwise use).")]
    public bool overrideEndpointDistance;

    [Tooltip("Minimum grid-cell distance between a new endpoint and the existing point it's anchored from.")]
    [Min(0f)] public float endpointMinDistance = 5f;

    [Tooltip("Maximum grid-cell distance between a new endpoint and the existing point it's anchored from.")]
    [Min(0f)] public float endpointMaxDistance = 10f;

    [Header("Level Mechanics")]
    [Tooltip("Optional systemic mechanics for this level (Chaos Block, Enlightenment Shrine, ...). Add an asset instance to enable that mechanic for this level with the parameters that instance carries; remove it to disable. Each mechanic's own controller looks itself up via GetMechanic<T>() — see ChaosBlockController / ShrineController.")]
    public List<LevelMechanicConfig> mechanics = new();

    // First mechanic instance of type T in this level's list, or null if none is
    // present (i.e. that mechanic is disabled for this level). A level mechanic's
    // controller (e.g. ChaosBlockController) calls this instead of reading a
    // level-specific bool/param field directly — that's what lets new mechanics
    // be added without ever touching LevelDefinition again.
    public T GetMechanic<T>() where T : LevelMechanicConfig
    {
        if (mechanics == null) return null;
        for (int i = 0; i < mechanics.Count; i++)
            if (mechanics[i] is T match) return match;
        return null;
    }
}

// ── Special objectives ──────────────────────────────────────────────────────
public enum ObjectiveType
{
    ClearWaves,         // clear `target` waves
    ReachWave,          // reach wave number `target`
    KillEnemies,        // defeat `target` enemies
    PlaceBlocks,        // place `target` blocks
    ActivateSynergies,  // have `target` synergies active at the same time
    LimitLeaks,         // let AT MOST `target` enemies reach the end (fails if exceeded)
    KeepLivesAtLeast,   // finish with AT LEAST `target` lives
    BuildPathLength,    // build an enemy path of AT LEAST `target` faces long (each block face = 1)
    UpgradeTurretToLevel, // upgrade any turret branch to AT LEAST `target`
}

[System.Serializable]
public class LevelObjective
{
    public ObjectiveType type = ObjectiveType.ClearWaves;
    [Min(0)] public int target = 1;
    [Tooltip("Shown text. Leave empty to auto-generate from the type + target.")]
    public string description;
    [Tooltip("Bonus objective — never blocks the clear, just shown as extra.")]
    public bool optional;

    public string AutoText() => type switch
    {
        ObjectiveType.ClearWaves        => $"Clear {target} waves",
        ObjectiveType.ReachWave         => $"Reach wave {target}",
        ObjectiveType.KillEnemies       => $"Defeat {target} enemies",
        ObjectiveType.PlaceBlocks       => $"Place {target} blocks",
        ObjectiveType.ActivateSynergies => $"Activate {target} synergies at once",
        ObjectiveType.LimitLeaks        => $"Let through at most {target}",
        ObjectiveType.KeepLivesAtLeast  => $"Finish with {target}+ lives",
        ObjectiveType.BuildPathLength   => $"Build a path {target}+ long",
        ObjectiveType.UpgradeTurretToLevel => $"Upgrade a turret to level {target}",
        _                               => "",
    };

    public string Label => string.IsNullOrEmpty(description) ? AutoText() : description;
    public bool IsLimit => type == ObjectiveType.LimitLeaks || type == ObjectiveType.KeepLivesAtLeast;
}

public enum TutorialStepKind
{
    Place,      // place a block so it covers `cells` (shape + position)
    Rotate,     // wait for the player to rotate the held block
    Run,        // wait for the player to start the wave (combat begins)
    Wait,       // show the hint; auto-advance after waitSeconds (0 = wait for a click)
    FreePlace,  // free placement (no ghost); advance after `count` blocks placed anywhere
    Input,      // wait for the player to press `inputKey` (e.g. W / Q / 1 / Space / Mouse0)
    Purchase,   // wait for the player to buy a block from the shop (optionally a specific `block`)
    PurchaseBlock,  // buy a regular (non-turret) block; block==null = any block, else match blockShape
    PurchaseTurret, // buy a turret; block==null = any turret, else match blockType
    Select,     // wait for the player to click/select a placed block (optionally a specific `block`)
    Sell,       // wait for the player to sell a block
    Refresh,    // wait for the player to refresh the shop
    Upgrade,    // wait for the player to upgrade a turret
    PathLength, // wait until the current enemy path is at least `pathLength` faces long
}

// Optional camera focus for a step — glides the orbit camera to a point of interest.
// What TutorialStep.cameraFocus points the camera at.
//
// NOTE: serialized by INDEX (cameraFocus: 3 == ChaosBlock in existing level
// assets), so only ever APPEND here — reordering silently rewrites every
// authored tutorial step.
//
// StepOrigin is the general escape hatch: it aims at the step's own
// origin/cellsOverride cells, so anything that appears at a location you can
// author is coverable without inventing a new enum entry for it.
// One guaranteed slot in a level's opening shop.
[System.Serializable]
public class ShopEntry
{
    [Tooltip("Block to force into the shop. Turret blocks fill the turret row; everything else fills the block row.")]
    public BlockData block;

    [Tooltip("Synergy colour to force on it. None = roll it from the level's pool like any other token (turrets normally stay None).")]
    public BlockColor color = BlockColor.None;
}

public enum TutorialFocus
{
    None,
    StartPoint,
    EndPoint,
    ChaosBlock,
    StepOrigin,       // this step's `origin` (or the centre of `cellsOverride`)
    Synergy,          // centre of a live synergy's claimed cells
    Enemy,            // first live enemy on the board
    LastPlacedBlock,  // the block the player most recently placed
}

// One tutorial step. For Place: the player must place `block` at `origin` — the
// block's WHOLE shape is shown as a ghost. (Advanced: set `cellsOverride` to an
// arbitrary absolute cell set instead.) Rotate / Run steps just wait for the action.
[System.Serializable]
public class TutorialStep
{
    public TutorialStepKind kind = TutorialStepKind.Place;

    [Header("Place step")]
    [Tooltip("The block whose SHAPE the player must place — the ghost shows this whole shape.")]
    public BlockData block;
    [Tooltip("Grid cell the block's origin (its 0,0,0 cell) lands on. Ghost = (rotated) block.cells + this.")]
    public Vector3Int origin;
    [Tooltip("Required rotation in 90° turns around X / Y / Z. (0,0,0) = default. The ghost shows this orientation; the player must rotate (1/2/3) to match.")]
    public Vector3Int rotation90;
    [Tooltip("Advanced: explicit absolute cells; overrides block+origin+rotation when set.")]
    public Vector3Int[] cellsOverride;

    [Header("Wait / FreePlace step")]
    [Tooltip("Wait step: auto-advance after this many seconds. 0 = wait for a click.")]
    public float waitSeconds = 0f;
    [Tooltip("FreePlace step: how many blocks the player must place (anywhere) to advance.")]
    [Min(1)] public int count = 1;
    [Tooltip("Input step: the key the player must press to advance (e.g. W, Q, Alpha1, Space, Mouse0).")]
    public KeyCode inputKey = KeyCode.Space;
    [Tooltip("PathLength step: advance once the current enemy path is at least this many faces long (each block face = 1).")]
    [Min(1)] public int pathLength = 15;
    [Tooltip("Allow ALL operations during this step (no tutorial gating). Use for sandbox / FreePlace steps.")]
    public bool freeOperations = false;
    [Tooltip("Hide & disable this step during combat (Running phase): its dialogue/hint hides, gating lifts, and it won't advance until combat ends.")]
    public bool hideInCombat = false;

    [Header("Wave gating")]
    [Tooltip("If > 0, this step won't begin (ghost/camera/dialogue/hint) until GameFlowManager.UpcomingWaveNumber reaches this value — i.e. the player has already cleared enough earlier waves. 0 = no gate, step begins as soon as the previous one completes.")]
    [Min(0)] public int requiredWave = 0;

    [Header("Unlocked operations")]
    [Tooltip("Once the player reaches this step, these operations become permanently allowed from then on — even once a LATER step is current and teaching something else. Unlike freeOperations (which lifts ALL gating for one step), this only unlocks the listed ops, and they stay unlocked for the rest of the tutorial. E.g. put Sell here once you've taught it, so it stays usable even while a later step is teaching Upgrade.")]
    public TutorialStepKind[] unlocksOps;

    [Header("Camera")]
    [Tooltip("When the step begins, glide the camera to focus on the start or end point. None = leave the camera where it is.")]
    public TutorialFocus cameraFocus = TutorialFocus.None;
    [Tooltip("Zoom level applied with cameraFocus (ortho size / orbit distance). SMALLER = more zoomed in. 0 = keep current zoom.")]
    public float focusZoom = 0f;

    [TextArea] public string hint;

    [Header("Suggestion highlight + aside (optional)")]
    [Tooltip("If > 0, shows a translucent suggestion box (side³ cells, minimum corner at suggestOrigin) when this step begins — a non-binding hint, not a placement requirement. 0 = no box.")]
    public int suggestCubeSide = 0;
    [Tooltip("Grid cell at the suggestion box's minimum corner.")]
    public Vector3Int suggestOrigin;
    [Tooltip("Optional second suggestion box, layered with the first (e.g. a 3³ tier nested inside a 4³ tier — same idea, different size). 0 = no second box.")]
    public int suggestCubeSide2 = 0;
    [Tooltip("Grid cell at the second suggestion box's minimum corner.")]
    public Vector3Int suggestOrigin2;
    [Tooltip("If set, pops a small AsideBubble (using `speaker`) when this step begins — separate from the main hint box.")]
    [TextArea] public string asideText;

    [Header("Dialogue (optional — delivers the hint as a conversation instead of the hint box)")]
    [Tooltip("Full conversation played when the step begins. Overrides `speaker` + `hint`.")]
    public DialogueConversation conversation;
    [Tooltip("If set (and no `conversation`), the hint text is spoken by THIS character as a one-line conversation.")]
    public DialogueCharacter speaker;
    [Tooltip("Portrait slot for the speaker (one-line mode).")]
    public PortraitSlot speakerSlot = PortraitSlot.Left;
    [Tooltip("Portrait / expression key for the speaker (blank = default).")]
    public string speakerPortrait = "";

    // True when this step delivers its hint through the dialogue system.
    public bool UsesDialogue =>
        conversation != null || (speaker != null && !string.IsNullOrEmpty(hint));

    // Absolute target cells = the ghost shape (rotated) AND the required placement.
    public Vector3Int[] TargetCells()
    {
        if (cellsOverride != null && cellsOverride.Length > 0) return cellsOverride;
        if (block != null && block.cells != null)
        {
            var rot = Quaternion.Euler(90f * rotation90.x, 90f * rotation90.y, 90f * rotation90.z);
            var r = new Vector3Int[block.cells.Length];
            for (int i = 0; i < block.cells.Length; i++)
                r[i] = origin + Vector3Int.RoundToInt(rot * (Vector3)block.cells[i]);
            return r;
        }
        return System.Array.Empty<Vector3Int>();
    }
}
