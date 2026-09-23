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

    /// <summary>
    /// Adopt the host's life count. Whether the run is over is the one question that
    /// absolutely cannot have four answers, so on a client this is the ONLY thing
    /// that moves the number — local damage is ignored entirely.
    /// </summary>
    public void ApplyRemoteLives(int lives)
    {
        if (lives == _lives) return;
        bool dropped = lives < _lives;
        _lives = Mathf.Clamp(lives, 0, maxLives);
        OnLivesChanged?.Invoke(_lives);

        // The feedback still fires locally — a spectator should feel the hit even
        // though it did not compute it.
        if (dropped)
        {
            AudioManager.Instance?.PlayDamage();
            CameraShake.Damage();
        }
        if (_lives == 0) OnGameOver?.Invoke();
    }

    public void TakeDamage(int amount = 1)
    {
        // On a spectator the host's snapshot is the only thing that may change this.
        if (CombatSync.IsSpectator) return;
        if (amount <= 0 || _lives <= 0) return;

        _lives = Mathf.Max(0, _lives - amount);
        OnLivesChanged?.Invoke(_lives);
        AudioManager.Instance?.PlayDamage();
        CameraShake.Damage();

        // Not shaken here on the killing blow — HandleGameOver's own, much harder
        // jolt lands in the same frame, and OrbitCamera.Shake keeps the stronger
        // of the two rather than letting this small one dilute it.
        if (_lives == 0) OnGameOver?.Invoke();
    }

    public void ResetLives()
    {
        _lives = maxLives;
        OnLivesChanged?.Invoke(_lives);
    }

    // The game-over screen (dim overlay + label + Restart button) used to be drawn
    // here in IMGUI. It's now GameOverScreen, driven from
    // GameFlowManager.HandleGameOver — IMGUI draws over every UGUI canvas
    // unconditionally, which would have punched a sharp, unblurred panel straight
    // through the new blur-out. The HP readout lives in TopLeftHUD.
}
