using System;
using UnityEngine;

// Lives counter. Combat scripts call TakeDamage when enemies reach the
// endpoint; GameFlowManager subscribes to OnGameOver to halt the run.
public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance;

    [Header("Balance")]
    [Tooltip("Central balance asset. When set, startingLives overrides maxLives below at Awake.")]
    public BalanceTable balance;

    [Header("Health")]
    public int maxLives = 10;

    public int  CurrentLives => _lives;
    public bool IsAlive      => _lives > 0;

    public event Action<int> OnLivesChanged;
    public event Action      OnGameOver;

    int _lives;

    void Awake()
    {
        Instance = this;
        if (balance != null) maxLives = Mathf.Max(1, balance.startingLives);
        _lives = maxLives;
    }

    public void TakeDamage(int amount = 1)
    {
        if (amount <= 0 || _lives <= 0) return;

        _lives = Mathf.Max(0, _lives - amount);
        OnLivesChanged?.Invoke(_lives);

        if (_lives == 0) OnGameOver?.Invoke();
    }

    public void ResetLives()
    {
        _lives = maxLives;
        OnLivesChanged?.Invoke(_lives);
    }

    // Minimal OnGUI for HP readout + game-over screen. Replace with real UI
    // once the HUD is built.
    GUIStyle _hpStyle, _overStyle, _btnStyle;

    void OnGUI()
    {
        if (_hpStyle == null) BuildStyles();

        // HP readout now lives in TopLeftHUD's bottom-left status panel.
        if (_lives <= 0)
        {
            // Dim overlay
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var box = new Rect(Screen.width * 0.5f - 160f, Screen.height * 0.5f - 80f, 320f, 160f);
            GUI.Label(box, "GAME OVER", _overStyle);

            var btn = new Rect(Screen.width * 0.5f - 80f, Screen.height * 0.5f + 16f, 160f, 36f);
            if (GUI.Button(btn, "Restart", _btnStyle))
                GameFlowManager.Instance?.RestartGame();
        }
    }

    void BuildStyles()
    {
        _hpStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 16,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _hpStyle.normal.textColor = new Color(1f, 0.40f, 0.40f);

        _overStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize  = 42,
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
        };
        _overStyle.normal.textColor = Color.white;

        _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 16 };
    }
}
