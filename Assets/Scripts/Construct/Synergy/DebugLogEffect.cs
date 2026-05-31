using UnityEngine;

// EXAMPLE / SMOKE-TEST EFFECT — proves the GameEffect pipeline compiles
// end-to-end. Used as a no-op stand-in until real effects are authored.
// Delete or rename once you have actual synergy / card effects.
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Debug Log",
                 fileName = "DebugLogEffect")]
public class DebugLogEffect : GameEffect
{
    [TextArea] public string applyMessage  = "Effect applied.";
    [TextArea] public string revokeMessage = "Effect revoked.";

    public override void Apply(GameFlowManager game)
        => Debug.Log($"[GameEffect] {applyMessage}");

    public override void Revoke(GameFlowManager game)
        => Debug.Log($"[GameEffect] {revokeMessage}");
}
