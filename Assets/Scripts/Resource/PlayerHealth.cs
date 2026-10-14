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
    // Mid-level resume: set the count with none of the damage feedback a drop in
    // ApplyRemoteLives would play — nobody was just hit.
    public void RestoreLives(int lives)
    {
        _lives = Mathf.Clamp(lives, 1, maxLives);
        OnLivesChanged?.Invoke(_lives);
    }

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
            AudioManager.Instance?.PulseHurt();
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

        // A held item can turn the last life lost into a second chance.
        if (_lives == 0 && RogueRun.Active && RogueRun.TryDeathSave(out int saved))
            _lives = Mathf.Clamp(saved, 1, maxLives);

        OnLivesChanged?.Invoke(_lives);
        AudioManager.Instance?.PlayDamage();
        AudioManager.Instance?.PulseHurt();
        CameraShake.Damage();

        // Not shaken here on the killing blow — HandleGameOver's own, much harder
        // jolt lands in the same frame, and OrbitCamera.Shake keeps the stronger
        // of the two rather than letting this small one dilute it.
        if (_lives == 0) OnGameOver?.Invoke();
    }

    // ── Roguelite: healing, max-HP, soft losses ──────────────────────────────

    /// <summary>Restores up to `amount` lives (never past max, never revives). Returns how many.</summary>
    public int Heal(int amount)
    {
        if (CombatSync.IsSpectator || amount <= 0 || _lives <= 0 || _lives >= maxLives) return 0;
        int before = _lives;
        _lives = Mathf.Min(maxLives, _lives + amount);
        if (_lives != before) OnLivesChanged?.Invoke(_lives);
        return _lives - before;
    }

    /// <summary>Raises (or lowers) the maximum. A raise also fills the new slots when `fill`.</summary>
    public void AddMaxLives(int amount, bool fill = false)
    {
        maxLives = Mathf.Max(1, maxLives + amount);
        if (amount > 0 && fill) _lives = Mathf.Min(maxLives, _lives + amount);
        _lives = Mathf.Clamp(_lives, Mathf.Min(1, maxLives), maxLives);
        OnLivesChanged?.Invoke(_lives);
    }

    /// <summary>
    /// A price paid in lives that cannot kill: lowers the count by up to `amount`,
    /// stopping at `floor`. Returns how many were lost. Hurts like a hit (sound,
    /// shake) but never reaches game over.
    /// </summary>
    public int Sacrifice(int amount, int floor = 1)
    {
        if (CombatSync.IsSpectator || amount <= 0 || _lives <= floor) return 0;
        int before = _lives;
        _lives = Mathf.Max(floor, _lives - amount);
        OnLivesChanged?.Invoke(_lives);
        AudioManager.Instance?.PlayDamage();
        CameraShake.Damage();
        return before - _lives;
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
