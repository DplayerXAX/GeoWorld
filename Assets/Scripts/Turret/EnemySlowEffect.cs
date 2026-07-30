using UnityEngine;

public class EnemySlowEffect : MonoBehaviour
{
    // Frost feedback aura shown while an enemy is slowed (Order synergy / slow turret).
    static readonly Color SlowColor = new Color(0.55f, 0.8f, 1f);

    EnemySurfaceUnit _enemy;
    float _timer;
    SynergyAura _aura;

    public static void Apply(EnemySurfaceUnit enemy, float duration, float multiplier)
    {
        if (enemy == null || duration <= 0f) return;

        var effect = enemy.GetComponent<EnemySlowEffect>();
        if (effect == null) effect = enemy.gameObject.AddComponent<EnemySlowEffect>();

        effect._enemy = enemy;
        effect._timer = Mathf.Max(effect._timer, duration);
        enemy.SetSpeedMultiplier(Mathf.Clamp(multiplier, 0.01f, 1f));

        // Frost aura — refreshes with the slow, so it lingers exactly as long.
        float size = GridSystem.instance != null ? GridSystem.instance.cellSize * 0.9f : 1f;
        effect._aura = SynergyBuffFx.AttachAura(enemy.transform, SlowColor, size);
        if (effect._aura != null) effect._aura.Refresh(effect._timer);
    }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        if (_enemy != null)
            _enemy.SetSpeedMultiplier(1f);

        if (_aura != null) _aura.Remove();
        Destroy(this);
    }
}
