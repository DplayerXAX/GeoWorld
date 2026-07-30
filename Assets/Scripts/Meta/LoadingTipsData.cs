using UnityEngine;

// Editable pool of loading-screen tips. Lives at Resources/LoadingTips.asset —
// LoadingScreen loads it via Resources.Load so it works with zero scene wiring
// (same "no scene setup" convention as the rest of the runtime-built UI), and
// picks one at random each time a load starts. Edit the `tips` list directly in
// the Inspector to add/remove/rewrite tips.
[CreateAssetMenu(menuName = "GeoWorld/Loading Tips", fileName = "LoadingTips")]
public class LoadingTipsData : ScriptableObject
{
    [Tooltip("Pool of tips shown on the loading screen — one is picked at random per load. Add, remove, or rewrite freely.")]
    [TextArea(1, 3)]
    public string[] tips;
}
