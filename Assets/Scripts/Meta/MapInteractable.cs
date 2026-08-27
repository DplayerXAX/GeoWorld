using UnityEngine;

public enum MapInteractableKind
{
    Npc,        // action plays `conversation`
    Minigame,   // action launches `minigame`
}

public enum MinigameId
{
    BlockTetris3D,
    BalanceTower,
    Clepsydra,
}

// A non-level point of interest on the LevelSelect map — an NPC to talk to, a
// minigame to enter. Deliberately mirrors LevelDefinition's role: the map shows
// it in the SAME info panel a level uses, just with different content and a
// different action button (see LevelMapController.OpenInteractablePanel).
//
// Placed on the map by a MapInteractableSpot in the LevelSelect scene, not by
// the map file — the map's own nodes are built at runtime, so there'd be nothing
// to author this against in the Inspector otherwise.
[CreateAssetMenu(menuName = "Game/Map Interactable")]
public class MapInteractable : ScriptableObject
{
    [Tooltip("Stable id, used for save flags (e.g. 'already talked to').")]
    public string id;

    public MapInteractableKind kind = MapInteractableKind.Npc;

    [Header("Panel content")]
    public string displayName;
    [TextArea] public string description;
    [Tooltip("Line under the title — the panel's equivalent of a level's Locked/Cleared state.")]
    public string status;
    [Tooltip("Action button label once the pawn is standing here.")]
    public string actionLabel = "Talk";
    [Tooltip("Action button label while the pawn is still walking over.")]
    public string approachLabel = "Walk over";

    [Header("NPC")]
    public DialogueConversation conversation;
    [Tooltip("Played instead of `conversation` on every visit after the first. Leave null to always replay the same one.")]
    public DialogueConversation repeatConversation;

    [Header("Minigame")]
    [Tooltip("Which built-in minigame to launch. See LevelMapController.RunInteraction.")]
    public MinigameId minigame = MinigameId.BlockTetris3D;

    public string Title => string.IsNullOrEmpty(displayName) ? name : displayName;
}
