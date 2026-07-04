using System.Collections;
using UnityEngine;

// 丰饶 (Abundance) — periodic currency drip while the synergy is active
// AND the game is in combat (Running phase).
//
// • Combat-gated: ticks during Running only. Build phase = silent.
// • Time-scaled: uses WaitForSeconds, so Time.timeScale (DevPanel speed
//   buttons / future game-speed UI) speeds up the drip naturally.
// • Multiple Apply / Revoke cycles are safe — old coroutine is killed first.
//
// NOTE: shared SO state. If the SAME effect asset is referenced by multiple
// rules that activate simultaneously, only the latest coroutine survives.
// Author one asset per rule that needs independent income (typical case).
[CreateAssetMenu(menuName = "GeoWorld/Synergy/Effects/Abundance Income",
                 fileName = "AbundanceIncomeEffect")]
public class AbundanceIncomeEffect : GameEffect
{
    public enum CurrencyKind { Block, Turret }

    [Header("Drip")]
    [Min(1)] public int   amountPerTick   = 1;
    [Min(0.05f)] public float intervalSeconds = 2f;
    public CurrencyKind   currency        = CurrencyKind.Block;

    [Header("Gating")]
    [Tooltip("Only tick while phase == Running. If false, drips during Build phase too.")]
    public bool combatOnly = true;

    [Header("On-activation bonus (optional)")]
    [Tooltip("Extra one-shot payment dropped the moment the synergy activates (in addition to subsequent ticks).")]
    [Min(0)] public int activationBonus = 0;

    [Header("Visual")]
    [Tooltip("Colour of the currency mote that floats up from the structure each drip.")]
    public Color moteColor = new Color(0.95f, 0.8f, 0.3f);

    // Runtime state — assumes one active coroutine per asset.
    Coroutine _tickRoutine;
    GameFlowManager _runner;

    public override void Apply(GameFlowManager game)
    {
        if (game == null) return;
        StopExisting();

        _runner      = game;
        _tickRoutine = game.StartCoroutine(Tick(game));

        if (activationBonus > 0) Pay(activationBonus);
    }

    public override void Revoke(GameFlowManager game)
    {
        StopExisting();
    }

    void StopExisting()
    {
        if (_tickRoutine != null && _runner != null)
            _runner.StopCoroutine(_tickRoutine);
        _tickRoutine = null;
        _runner      = null;
    }

    IEnumerator Tick(GameFlowManager game)
    {
        // WaitForSeconds is scaled by Time.timeScale → 2× speed drips 2× faster.
        var wait = new WaitForSeconds(intervalSeconds);
        while (true)
        {
            yield return wait;
            if (combatOnly && (game == null || game.phase != GamePhase.Running))
                continue;
            Pay(amountPerTick);
        }
    }

    void Pay(int amount)
    {
        var rm = ResourceManager.Instance;
        if (rm == null) return;

        if (currency == CurrencyKind.Block) rm.AddBlockCurrency(amount);
        else                                rm.AddTurretCurrency(amount);

        if (SynergyEffectUtil.TryGetClaimedCellWorld(this, out var w))
        {
            SynergyBuffFx.Mote(w, moteColor,
                GridSystem.instance != null ? GridSystem.instance.cellSize * 0.7f : 0.7f);
            CurrencyFlyFx.Fly(w, isTurret: currency == CurrencyKind.Turret, amount);
        }
    }
}
