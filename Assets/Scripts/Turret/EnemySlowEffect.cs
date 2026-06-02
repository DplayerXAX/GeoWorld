using UnityEngine;

public class EnemySlowEffect : MonoBehaviour
{
    EnemySurfaceUnit _enemy;
    float _timer;

    public static void Apply(EnemySurfaceUnit enemy, float duration, float multiplier)
    {
        if (enemy == null || duration <= 0f) return;

        var effect = enemy.GetComponent<EnemySlowEffect>();
        if (effect == null) effect = enemy.gameObject.AddComponent<EnemySlowEffect>();

        effect._enemy = enemy;
        effect._timer = Mathf.Max(effect._timer, duration);
        enemy.SetSpeedMultiplier(Mathf.Clamp(multiplier, 0.01f, 1f));
    }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        if (_enemy != null)
            _enemy.SetSpeedMultiplier(1f);

        Destroy(this);
    }
}
