using System.Collections.Generic;
using UnityEngine;

// Non-level points of interest on the map — NPCs, minigame entrances (see
// MapInteractable / MapInteractableSpot). They reuse the level info panel
// wholesale: clicking one walks the pawn there exactly like clicking a level
// block does, and the panel that opens is the same panel with different content
// and a different action button.
//
// The action stays disabled until the pawn actually ARRIVES, which is what makes
// "walk over to talk" a rule rather than a special case: OpenPanel already runs
// once on click and again on arrival (see WalkCells), so the button enables
// itself on the second pass with no extra state machine.
public partial class LevelMapController : MonoBehaviour
{
    readonly Dictionary<Vector2Int, MapInteractableSpot> _spots = new();

    // Called from Start() after BuildSurface — the spots bind to the surface, so
    // there has to be one.
    //
    // INACTIVE ones are included deliberately. On the one visit right after a decor
    // plot's gate level is cleared, BuildDecor hides that plot's residents so they
    // don't hover over an empty field while it's still underground, and the grow-in
    // cutscene switches them back on afterwards. But this indexing pass runs in
    // Start, before the cutscene — so with the default (active-only) search the
    // residents that were mid-reveal never made it into _spots, and stayed
    // unclickable for the entire session even once they were visible.
    void CollectInteractableSpots()
    {
        _spots.Clear();
        foreach (var s in FindObjectsByType<MapInteractableSpot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (s == null || s.data == null) continue;
            _spots[s.Column] = s;
            if (_columnTop.TryGetValue(s.Column, out var top)) s.PlaceOn(BlockTop(top));
        }
    }

    MapInteractableSpot SpotAt(Vector3Int cell) =>
        _spots.TryGetValue(new Vector2Int(cell.x, cell.z), out var s) ? s : null;

    // True when this cell had an interactable and the panel now shows it, so the
    // caller skips the level panel it would otherwise have opened.
    bool TryOpenInteractablePanel(Vector3Int cell)
    {
        var spot = SpotAt(cell);
        if (spot == null || spot.data == null) return false;

        _selected = null;   // not a level — keeps EnterLevel and the IMGUI fallback inert
        if (infoPanel == null) return true;

        var d = spot.data;
        bool arrived = !_moving && new Vector2Int(_currentCell.x, _currentCell.z) == spot.Column;
        bool canAct  = arrived && HasAction(d);

        // Minigames carry a record; NPCs don't. Shown in the same slot a level uses
        // for its best wave.
        string best = null;
        if (d.kind == MapInteractableKind.Minigame)
        {
            int hi = SaveSystem.Profile.GetMinigameBest(d.id);
            best = hi > 0 ? $"Best score: {hi}" : "No record yet";
        }

        infoPanel.ShowInteractable(
            d.Title,
            d.description,
            arrived ? d.status : "Walk over to interact",
            best,
            arrived ? d.actionLabel : d.approachLabel,
            canAct,
            () => RunInteraction(spot));
        return true;
    }

    static bool HasAction(MapInteractable d) => d.kind switch
    {
        MapInteractableKind.Npc      => d.conversation != null || d.repeatConversation != null,
        MapInteractableKind.Minigame => true,
        _                            => false,
    };

    void RunInteraction(MapInteractableSpot spot)
    {
        var d = spot.data;
        if (d == null) return;

        switch (d.kind)
        {
            case MapInteractableKind.Npc:
                // First visit gets `conversation`; every later one gets
                // repeatConversation if authored, so an NPC can have a one-time
                // introduction without repeating it forever.
                bool seen = !string.IsNullOrEmpty(d.id) && SaveSystem.Profile.HasSeenInteractable(d.id);
                var convo = (seen && d.repeatConversation != null) ? d.repeatConversation
                                                                   : (d.conversation ?? d.repeatConversation);
                if (convo == null) return;

                if (!string.IsNullOrEmpty(d.id) && !seen)
                {
                    SaveSystem.Profile.MarkInteractableSeen(d.id);
                    SaveSystem.Save();
                }
                infoPanel?.Hide();   // the conversation IS the content now — don't stack them
                DialogueRunner.Instance?.Play(convo);
                break;

            case MapInteractableKind.Minigame:
                infoPanel?.Hide();
                switch (d.minigame)
                {
                    case MinigameId.BlockTetris3D: BlockTetris3D.Launch(cubePrefab, d.id); break;
                    case MinigameId.BalanceTower:  BalanceTower.Launch(cubePrefab, d.id);  break;
                    case MinigameId.Clepsydra:     Clepsydra.Launch(cubePrefab, d.id);     break;
                }
                break;
        }
    }
}
