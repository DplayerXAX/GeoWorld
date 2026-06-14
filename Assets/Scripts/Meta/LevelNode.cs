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
    // The pawn may walk THROUGH a node if it's a waypoint or a non-locked level.
    public bool    Passable   => IsWaypoint || NodeState != State.Locked;
    public Vector3 PawnPoint  => transform.position + Vector3.up * pawnLift;

    Renderer[] _rends;

    void Awake() => _rends = GetComponentsInChildren<Renderer>();

    // Recompute state from the save profile and recolor the block.
    public void Refresh()
    {
        if (IsWaypoint) { NodeState = State.Unlocked; Tint(Dim(themeColor, 0.7f)); return; }

        var p   = SaveSystem.Profile;
        var rec = p.GetRecord(level.levelId);
        if      (rec != null && rec.cleared)  NodeState = State.Cleared;
        else if (p.IsUnlocked(level.levelId)) NodeState = State.Unlocked;
        else                                  NodeState = State.Locked;

        Tint(NodeState switch
        {
            State.Locked  => lockedColor,
            State.Cleared => Dim(themeColor, 1.15f),
            _             => themeColor,
        });
    }

    void Tint(Color c)
    {
        if (_rends == null) return;
        for (int i = 0; i < _rends.Length; i++)
            if (_rends[i] != null) MpbColor.Set(_rends[i], c);
    }

    static Color Dim(Color c, float m) => new Color(c.r * m, c.g * m, c.b * m, 1f);
}
