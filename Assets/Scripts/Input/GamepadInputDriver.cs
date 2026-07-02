using UnityEngine;

// Auto-spawned poller that refreshes GamepadInput's static state once per frame, before
// everything else's Update() runs. Same auto-spawn-singleton pattern as SettingsScreen.Spawn().
[DefaultExecutionOrder(-100)]
public class GamepadInputDriver : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Spawn()
    {
        if (FindFirstObjectByType<GamepadInputDriver>() != null) return;
        var go = new GameObject("GamepadInputDriver");
        DontDestroyOnLoad(go);
        go.AddComponent<GamepadInputDriver>();
    }

    void Update() => GamepadInput.Poll();
}
