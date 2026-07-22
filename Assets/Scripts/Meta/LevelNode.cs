using System.Collections.Generic;
using UnityEngine;

// One block on the level-select map. A node either launches a level (`level`
// set) or is a plain path stepping-stone (`level` null → always passable, not
// enterable). Nodes are linked by `neighbors`; the pawn walks between linked
// nodes. State (Locked / Unlocked / Cleared) is read from the save profile and
// shown by tinting the block.
[DisallowMultipleComponent]
public class LevelNode : MonoBehaviour
{
    [Tooltip("Level this block launches. Null = plain path stone (passable, not enterable).")]
    public LevelDefinition level;

    [Tooltip("Linked nodes the pawn can walk to/from (treated as bidirectional). " +
             "When the map is built from a saved file, these are filled automatically by cell adjacency.")]
    public List<LevelNode> neighbors = new();

    [Tooltip("The pawn starts here. Mark exactly one node.")]
    public bool isStart;

    // Grid cells this node occupies — set when the map is built from a saved file
    // (used to auto-link face-adjacent nodes). Empty for hand-authored nodes.
    [System.NonSerialized] public Vector3Int[] cells;

    // Player-built map-extension pieces only (LevelMapController build mode): which
    // reward BlockData this node was built from, and at what rotation — lets the
    // piece be picked back up and re-edited exactly like gameplay's PickUpSelected.
    // Null on every authored map node (level blocks, decorations, connectors baked
    // into the map asset) — those were never a player's reward, so they're inert.
    [System.NonSerialized] public BlockData  sourceBlock;
    [System.NonSerialized] public Quaternion builtRotation = Quaternion.identity;

    // Is there an unbroken walkable surface from the START node to this one RIGHT
    // NOW? Recomputed by LevelMapController.RefreshNodes() after every map change
    // (build, pick-up, load). A level nobody can walk to cannot be entered — that's
    // what makes the reward blocks matter: you don't unlock the road, you build it.
    // Defaults true so hand-authored scenes with no controller driving them behave
    // exactly as they did before.
    [System.NonSerialized] public bool connectedToStart = true;

    // Face-adjacent (touching) to another node?
    public bool IsAdjacentTo(LevelNode o)
    {
        if (cells == null || o == null || o.cells == null) return false;
        foreach (var a in cells)
            foreach (var b in o.cells)
            {
                int d = Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y) + Mathf.Abs(a.z - b.z);
                if (d == 1) return true;
            }
        return false;
    }

    [Header("Look")]
    public Color themeColor  = new Color(0.40f, 0.90f, 1.00f, 1f);
    public Color lockedColor = new Color(0.32f, 0.33f, 0.36f, 1f);
    [Tooltip("How high the pawn floats above this block's pivot.")]
    public float pawnLift = 0.9f;

    public enum State { Locked, Unlocked, Cleared }
    public State NodeState { get; private set; }

    public bool    IsWaypoint => level == null;
    // Locked specifically because no road reaches it yet (as opposed to locked by
    // progression) — lets the info panel explain WHICH wall the player hit.
    public bool    Unreachable => level != null && !connectedToStart;
    // The pawn may walk THROUGH a node if it's a waypoint or a non-locked level.
    public bool    Passable   => IsWaypoint || NodeState != State.Locked;
    public Vector3 PawnPoint  => transform.position + Vector3.up * pawnLift;

    Renderer[] _rends;

    void Awake() => _rends = GetComponentsInChildren<Renderer>();

    // Recompute state from the save profile and recolor the block.
    //
    // Every reachable node shows its EXACT synergy-palette colour — no brightness
    // modulation. Waypoints used to render at 70% and cleared levels at 115%,
    // which meant two blocks authored the identical palette green showed up on
    // screen as two different greens purely because one was a path stone. Locked
    // is the one state still worth recolouring: it's not a shade of the theme,
    // it's "you can't go here", and it has to read at a glance.
    public void Refresh()
    {
        if (IsWaypoint) { NodeState = State.Unlocked; Tint(themeColor); return; }

        var p   = SaveSystem.Profile;
        var rec = p.GetRecord(level.levelId);
        if      (rec != null && rec.cleared)  NodeState = State.Cleared;
        else if (p.IsUnlocked(level.levelId)) NodeState = State.Unlocked;
        else                                  NodeState = State.Locked;

        // No road home = not activated, whatever the profile says. Applied last so
        // it overrides Unlocked; a CLEARED level stays cleared (its record is
        // history — greying out a level you've already beaten because you moved a
        // bridge would read as losing progress).
        if (NodeState == State.Unlocked && !connectedToStart) NodeState = State.Locked;

        Tint(NodeState == State.Locked ? lockedColor : themeColor);
    }

    void Tint(Color c)
    {
        if (_rends == null) return;
        for (int i = 0; i < _rends.Length; i++)
            if (_rends[i] != null) MpbColor.Set(_rends[i], c);
    }
}
