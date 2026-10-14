using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs a roguelite run: the structure around the fighting.
//
//   run start    own purse and lives; choose a starting relic
//   every wave   the relic market restocks
//   elite wave   cleared → choose 1 of 3 relics (uncommon-leaning)
//   act boss     cleared → heal, choose 1 of 3 relics (rare-leaning); the last act's
//                boss ends the run in victory — and the run may go on, endless
//   defeat       the existing GameOverScreen, with the run's tally
//
// The fighting itself is untouched: GameFlowManager runs build → wave → build as it
// always did, asking RogueWaves for the wave and telling RogueRun when one is won.
// This class only decides what a won wave is worth.
//
// Spawns itself on a gameplay scene when RunConfig.Mode is Rogue — the same hook
// the level bosses use — so there is no scene wiring.
[DisallowMultipleComponent]
public class RogueDirector : MonoBehaviour
{
    public static RogueDirector Instance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        TrySpawn();
    }

    static void OnLoaded(Scene s, LoadSceneMode m) => TrySpawn();

    static void TrySpawn()
    {
        if (RunConfig.Mode != GameMode.Rogue) return;
        if (PlacementController.Instance == null) return;          // gameplay scene only
        if (FindFirstObjectByType<RogueDirector>() != null) return;
        new GameObject("RogueDirector").AddComponent<RogueDirector>();
    }

    RogueConfig    _cfg;
    UpgradeManager _um;
    ulong          _seed;
    int            _bestAtStart;
    bool           _won;
    bool           _ready;
    System.Action  _afterReward;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    void Awake()
    {
        Instance = this;
        // Statics survive a scene load: wipe whatever the last run left set.
        RogueRun.Reset();
        BoardSnapshot.ClearAliases();

        EnemyBaseManager.SpawnModifier = RogueWaves.ApplySpawn;
        RogueRun.WaveCleared += OnWaveCleared;
        EnemySurfaceUnit.AnyDied += OnAnyDied;
    }

    void OnDestroy()
    {
        RogueRun.WaveCleared -= OnWaveCleared;
        EnemySurfaceUnit.AnyDied -= OnAnyDied;
        if (EnemyBaseManager.SpawnModifier == (System.Action<EnemySurfaceUnit, SpawnGroup>)RogueWaves.ApplySpawn)
            EnemyBaseManager.SpawnModifier = null;
        if (Instance == this) Instance = null;
        RogueRun.Reset();
        BoardSnapshot.ClearAliases();
    }

    IEnumerator Start()
    {
        _cfg = RogueRun.Config;

        // GameFlowManager.Start does the scene's own setup (wallets, lives, clearing
        // the modifier stack) — the run's values go on top of that, not under it.
        while (GameFlowManager.Instance == null || ResourceManager.Instance == null || PlayerHealth.Instance == null)
            yield return null;
        yield return null;

        var gfm = GameFlowManager.Instance;
        var rng = gfm.Rng;                        // forces the run seed to exist
        _seed = gfm.runSeed != 0UL ? gfm.runSeed : 1UL;
        _bestAtStart = SaveSystem.Profile.rogueBestWave;

        SetupUpgrades();

        PlayerHealth.Instance.maxLives = Mathf.Max(1, _cfg.startLives);
        PlayerHealth.Instance.ResetLives();
        ResourceManager.Instance.SetStartingWallets(_cfg.startBlockCurrency, _cfg.startTurretCurrency);
        SaveSystem.RecordRogue(0, 0, false, newRun: true);

        while (IntroDirector.Playing) yield return null;
        yield return new WaitForSeconds(0.35f);

        RogueMarket.Restock(0, _seed);
        _ready = true;
        OfferStarter();
    }

    void SetupUpgrades()
    {
        _um = UpgradeManager.Instance;
        if (_um == null) _um = new GameObject("UpgradeManager").AddComponent<UpgradeManager>();

        RogueItems.Build();
        _um.cardPool    = new List<UpgradeCard>(RogueItems.Cards);
        _um.autoOffer   = false;
        _um.eligibility = RogueItems.IsUnlocked;
        _um.ResetForNewRun();

        UpgradeChoiceUI.Ensure();
    }

    // ── Offers ───────────────────────────────────────────────────────────────

    void OfferStarter()
    {
        if (_um == null) return;
        var rng = new Xoshiro256StarStar(_seed ^ 0x5354415254UL);
        var w = _cfg.WeightsFor(RogueSource.Starter);
        var cards = _um.RollOffer(rng, Mathf.Max(2, _cfg.starterChoices), c => c.starter ? RogueMarket.RarityWeight(w, c) : 0f);

        UpgradeChoiceUI.Instance.Show(cards, card => { if (card != null) _um.Grant(card); },
                                      "CHOOSE A STARTING RELIC",
                                      "It stays with you for the whole run.", false);
    }

    // Free relic choice. Returns false when the pool had nothing to give.
    bool OfferReward(RogueSource source, string title, string subtitle, System.Action done)
    {
        if (_um == null) return false;
        var rng = new Xoshiro256StarStar(_seed ^ (ulong)(GameFlowManager.Instance.WavesCleared * 40503 + (int)source * 977 + 3));
        var w = _cfg.WeightsFor(source);
        var cards = _um.RollOffer(rng, 3, c => RogueMarket.RarityWeight(w, c));
        if (cards.Count == 0) return false;

        UpgradeChoiceUI.Instance.Show(cards,
            card =>
            {
                if (card != null) _um.Grant(card);
                else ResourceManager.Instance?.AddBlockCurrency(Mathf.Max(0, _cfg.skipBonus));   // passed: paid for it
                done?.Invoke();
            },
            title, subtitle, true, $"SKIP  (+{_cfg.skipBonus})");
        return true;
    }

    // ── A wave won ───────────────────────────────────────────────────────────

    void OnWaveCleared(int cleared)
    {
        if (!_ready) return;
        int idx = cleared - 1;
        bool boss  = RogueRun.IsBossWave(idx);
        bool elite = RogueRun.IsEliteWave(idx);
        int  act   = RogueRun.ActOf(idx);

        RogueMarket.Restock(cleared, _seed);

        var pc = PlacementController.Instance;
        bool final = boss && RogueRun.IsFinalWave(idx) && !_won;

        System.Action after = null;
        if (final)
        {
            _won = true;
            SaveSystem.RecordRogue(cleared, RogueRun.Score(cleared), won: true);
            after = () => RogueVictoryScreen.Show(cleared, _bestAtStart);
        }

        if (boss)
        {
            int healed = PlayerHealth.Instance != null ? PlayerHealth.Instance.Heal(Mathf.Max(0, _cfg.bossHeal)) : 0;
            pc?.ShowPlacementPopup(final ? "THE LAST BOSS HAS FALLEN"
                                         : $"ACT {act + 1} COMPLETE{(healed > 0 ? $"   +{healed} life" : "")}", 3f);
            if (!OfferReward(RogueSource.Boss, "BOSS DEFEATED", "Choose a relic.", after)) after?.Invoke();
        }
        else if (elite)
        {
            pc?.ShowPlacementPopup("ELITES DOWN", 2f);
            OfferReward(RogueSource.Elite, "ELITES DEFEATED", "Choose a relic.", null);
        }
    }

    void OnAnyDied(EnemySurfaceUnit e)
    {
        if (e == null) return;
        var a = e.GetComponent<RogueEnemyAffix>();
        if (a == null) return;
        if (a.affix == RogueAffix.Boss) RogueRun.BossesDefeated++;
        else                            RogueRun.ElitesDefeated++;
    }
}
