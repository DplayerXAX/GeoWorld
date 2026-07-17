using UnityEngine;
using UnityEngine.UI;

// The title brain. Three faces: TITLE → MAIN MENU → SAVE SELECT. It shows/hides
// the matching UGUI panel (smooth via UIPanel) and slides the cube (TitleCube).
// Buttons inside the panels just call the public methods below — that's ALL the
// wiring you need.
public class TitleFlow : MonoBehaviour
{
    public static TitleFlow Instance;

    public enum Face { Title, MainMenu, SaveSelect }
    public Face CurrentFace { get; private set; }

    [Header("Cube")]
    public TitleCube cube;
    public TitleShaderSwap shaderSwap;   // cube ↔ skybox material swap on the title→menu transition

    [Header("UGUI panels")]
    public UIPanel titlePanel;
    public UIPanel mainMenuPanel;
    public UIPanel savePanel;

    [Header("Scenes (Build Settings)")]
    public string levelSelectScene = "LevelSelect";
    public string gameplayScene    = "gamePlay";

    [Header("Save select")]
    [Tooltip("Level registry — used to compute each slot's completion % in the hover tooltip. Drag the same LevelDatabase the map uses.")]
    public LevelDatabase database;

    void Awake() => Instance = this;
    void Start()
    {
        WireSaveSlots();
        GoToTitle();
    }

    // Attach a SaveSlotButton to each save button under savePanel, in order, so
    // hovering shows that slot's stats and clicking selects it. The buttons keep
    // their existing onClick (LoadLevelSelect) — the slot is selected on the
    // pointer-DOWN that precedes it.
    void WireSaveSlots()
    {
        if (savePanel == null) return;
        var buttons = savePanel.GetComponentsInChildren<Button>(true);
        int n = Mathf.Min(buttons.Length, SaveSystem.SlotCount);
        for (int i = 0; i < n; i++)
        {
            var go = buttons[i].gameObject;
            var comp = go.GetComponent<SaveSlotButton>();
            if (comp == null) comp = go.AddComponent<SaveSlotButton>();
            comp.Init(i);
        }
    }

    void Update()
    {
        if (CurrentFace == Face.Title && (Input.anyKeyDown || Input.GetMouseButtonDown(0) || GamepadInput.ConfirmDown))
            GoToMainMenu();
        else if (CurrentFace == Face.SaveSelect && (Input.GetKeyDown(KeyCode.Escape) || GamepadInput.CancelDown))
            GoToMainMenu();
        else if (CurrentFace == Face.MainMenu && (Input.GetKeyDown(KeyCode.Escape) || GamepadInput.CancelDown))
            GoToTitle();
    }

    // ── Faces (wire Back / Play buttons to these) ──────────────────────────────
    public void GoToTitle()    { CurrentFace = Face.Title;      Apply(titlePanel);    cube?.MoveToCenter();   shaderSwap?.ApplyTitle();   SaveSlotInfoDisplay.Hide(); }
    public void GoToMainMenu() { CurrentFace = Face.MainMenu;   Apply(mainMenuPanel); cube?.MoveToMenuFace(); shaderSwap?.ApplySwapped(); SaveSlotInfoDisplay.Hide(); }
    public void GoToSave()     { CurrentFace = Face.SaveSelect; Apply(savePanel);     cube?.MoveToSaveFace(); shaderSwap?.ApplySwapped(); }

    // ── Menu actions (wire the menu buttons to these) ──────────────────────────
    public void PlayEndless()    { RunConfig.SetEndless(); LoadingScreen.Go(gameplayScene); }
    public void OpenSettings()   { SettingsScreen.Open = true; }
    public void LoadLevelSelect(){ LoadingScreen.Go(levelSelectScene); }   // save slots → world select
    public void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    void Apply(UIPanel active)
    {
        if (titlePanel)    titlePanel.Set(titlePanel == active);
        if (mainMenuPanel) mainMenuPanel.Set(mainMenuPanel == active);
        if (savePanel)     savePanel.Set(savePanel == active);
    }
}
