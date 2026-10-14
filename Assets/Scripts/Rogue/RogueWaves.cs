using System.Collections.Generic;
using UnityEngine;

// The roguelite's wave builder. A pure function of (wave, routes, run seed): the
// forecast the player reads in the build phase and the wave that then spawns come from
// the SAME call, so what the intel panel promises is what arrives.
//
//   * budget curve → spent on roster entries, weighted, like the endless generator;
//   * elite waves put a share of the budget into promoted groups (HP, size, a rule);
//   * boss waves send an escort and then the act's boss, last.
//
// What a group's enemies actually ARE (hit points, speed, reward) is computed in
// StatsFor, which both the forecast and the spawn hook use — so the two cannot drift.
public static class RogueWaves
{
    public struct Stats
    {
        public int   hp;
        public float speed, scale;
        public int   reward, leakBonus;
        public RogueAffix affix;
        public string name;
    }

    static readonly RogueAffix[] EliteAffixes =
        { RogueAffix.Armored, RogueAffix.Swift, RogueAffix.Regenerating, RogueAffix.Vengeful };

    // ── Build ────────────────────────────────────────────────────────────────

    public static List<SpawnGroup> Build(int wave, int routes, ulong seed,
                                         List<WaveGenerator.WaveForecastGroup> forecast = null)
    {
        var groups = new List<SpawnGroup>();
        var cfg    = RogueRun.Config;
        var roster = cfg.Roster;
        if (roster == null || roster.Count == 0) return groups;

        var rng = new Xoshiro256StarStar(seed ^ ((ulong)(wave + 1) * 0x9E3779B97F4A7C15UL));
        routes = Mathf.Max(1, routes);

        bool boss  = RogueRun.IsBossWave(wave);
        bool elite = RogueRun.IsEliteWave(wave);
        int  act   = RogueRun.ActOf(wave);

        float budget = cfg.Budget(wave) * (1f + (rng.NextFloat() * 2f - 1f) * cfg.budgetVariance);
        if (routes > 1) budget /= Mathf.Pow(routes, cfg.perRouteExponent);
        if (boss) budget *= cfg.bossEscortShare;

        // What may appear: introduced by now. A roster whose every entry is "later"
        // (a misauthored minWave) still has to send something.
        var pool = new List<int>();
        for (int i = 0; i < roster.Count; i++)
            if (roster[i] != null && roster[i].prefab != null && roster[i].minWave <= wave && roster[i].weight > 0f) pool.Add(i);
        if (pool.Count == 0)
            for (int i = 0; i < roster.Count; i++) if (roster[i] != null && roster[i].prefab != null) pool.Add(i);
        if (pool.Count == 0) return groups;

        bool first = true;

        // ── Elite groups first, so the budget they cost is never what is left over ──
        if (elite)
        {
            float eb = budget * cfg.eliteBudgetShare;
            budget -= eb;

            var affixes = new List<RogueAffix>(EliteAffixes);
            rng.Shuffle(affixes);
            int made = 0;
            for (int guard = 0; guard < 4 && eb > 0f; guard++)
            {
                int idx = PickWeighted(roster, pool, eb * cfg.affordSlack, rng, eliteOnly: true);
                if (idx < 0) break;
                var e = roster[idx];

                int size = Mathf.Clamp(rng.NextIntInclusive(2, 3), 1, Mathf.Max(1, Mathf.FloorToInt(eb * cfg.affordSlack / e.cost)));
                var affix = affixes[made % affixes.Count];

                groups.Add(new SpawnGroup
                {
                    prefab = e.prefab, count = size, interval = Mathf.Max(0.6f, e.interval),
                    preDelay = first ? 0f : cfg.groupSpacing,
                    rogueEntry = idx + 1, affix = (int)affix,
                });
                first = false;
                eb -= e.cost * size;
                made++;
            }
        }

        // ── The ordinary spend ────────────────────────────────────────────────
        for (int safety = 0; budget > 0f && safety < 40; safety++)
        {
            int idx = PickWeighted(roster, pool, budget * cfg.affordSlack, rng, eliteOnly: false);
            if (idx < 0) break;
            var e = roster[idx];

            int size = rng.NextIntInclusive(Mathf.Max(1, e.groupMin), Mathf.Max(e.groupMin, e.groupMax));
            int maxSize = Mathf.FloorToInt(budget * cfg.affordSlack / Mathf.Max(1, e.cost));
            if (maxSize > 0 && size > maxSize) size = maxSize;
            if (size <= 0) break;

            groups.Add(new SpawnGroup
            {
                prefab = e.prefab, count = size, interval = e.interval,
                preDelay = first ? 0f : cfg.groupSpacing,
                rogueEntry = idx + 1,
            });
            first = false;
            budget -= e.cost * size;
        }

        // The boss comes last, after a breath.
        if (boss)
        {
            var template = RogueBoss.Template();
            if (template != null)
                groups.Add(new SpawnGroup
                {
                    prefab = template, count = 1, interval = 1f,
                    preDelay = first ? 0f : cfg.groupSpacing + 1.5f,
                    rogueEntry = act + 1, affix = (int)RogueAffix.Boss,
                });
        }

        // Tanks and fast things first inside a wave reads as a rush; keeping the
        // builder's own order keeps the elites in the middle and the boss at the back.
        if (forecast != null) FillForecast(groups, wave, forecast);
        return groups;
    }

    // Weighted pick among `pool` entries cheap enough for `ceiling`. -1 when none is.
    static int PickWeighted(List<RogueConfig.EnemyEntry> roster, List<int> pool, float ceiling,
                            Xoshiro256StarStar rng, bool eliteOnly)
    {
        float total = 0f;
        for (int k = 0; k < pool.Count; k++)
        {
            var e = roster[pool[k]];
            if (e.cost > ceiling) continue;
            if (eliteOnly && (!e.eliteOk || e.cost < 2)) continue;
            total += e.weight;
        }
        if (total <= 0f) return -1;

        float r = rng.NextFloat() * total, acc = 0f;
        for (int k = 0; k < pool.Count; k++)
        {
            var e = roster[pool[k]];
            if (e.cost > ceiling) continue;
            if (eliteOnly && (!e.eliteOk || e.cost < 2)) continue;
            acc += e.weight;
            if (r < acc) return pool[k];
        }
        return -1;
    }

    // ── What a group's enemies are ───────────────────────────────────────────

    public static Stats StatsFor(SpawnGroup g, int wave)
    {
        var cfg = RogueRun.Config;
        var s = new Stats { hp = 1, speed = 1f, scale = 1f, reward = 1, leakBonus = 0, affix = RogueAffix.None, name = "Enemy" };
        if (g == null) return s;

        var affix = (RogueAffix)g.affix;
        s.affix = affix;

        if (affix == RogueAffix.Boss)
        {
            int act = Mathf.Max(0, g.rogueEntry - 1);
            s.name       = "The Watcher";
            s.hp         = cfg.BossHp(act);
            s.speed      = cfg.bossSpeed;
            s.scale      = cfg.bossScale;
            s.reward     = cfg.bossReward;
            s.leakBonus  = Mathf.Max(0, cfg.bossLeakDamage - 1);
            return s;
        }

        var roster = cfg.Roster;
        int i = g.rogueEntry - 1;
        if (roster == null || i < 0 || i >= roster.Count || roster[i] == null)
        {
            if (g.prefab != null)
            {
                s.hp = Mathf.Max(1, Mathf.RoundToInt(g.prefab.maxHealth * cfg.HpMult(wave)));
                s.speed = g.prefab.baseSpeedMultiplier;
                s.reward = g.prefab.rewardOnKill;
                s.name = g.prefab.name;
            }
            return s;
        }

        var e = roster[i];
        float hp    = e.hp * cfg.HpMult(wave);
        float speed = e.speed * cfg.SpeedMult(wave);
        float scale = e.scale;
        float reward = e.reward;
        int leak = 0;

        if (affix != RogueAffix.None)
        {
            hp *= cfg.eliteHpMult;
            scale *= cfg.eliteScale;
            reward *= cfg.eliteRewardMult;
            switch (affix)
            {
                case RogueAffix.Armored:  hp *= 1.4f; break;
                case RogueAffix.Swift:    speed *= cfg.swiftMult; break;
                case RogueAffix.Vengeful: leak = Mathf.Max(0, cfg.vengefulLeak); break;
            }
        }

        s.name       = e.name;
        s.hp         = Mathf.Max(1, Mathf.RoundToInt(hp));
        s.speed      = Mathf.Max(0.05f, speed);
        s.scale      = scale;
        s.reward     = Mathf.Max(0, Mathf.RoundToInt(reward));
        s.leakBonus  = leak;
        return s;
    }

    // Called by EnemyBaseManager for every enemy it spawns (SpawnModifier).
    public static void ApplySpawn(EnemySurfaceUnit enemy, SpawnGroup group)
    {
        if (enemy == null || group == null || !RogueRun.Active) return;
        if (group.rogueEntry <= 0 && group.affix == 0) return;

        int wave = GameFlowManager.Instance != null ? GameFlowManager.Instance.WavesCleared : 0;
        var s = StatsFor(group, wave);

        enemy.SetMaxHealth(s.hp);
        enemy.baseSpeedMultiplier = s.speed;
        enemy.rewardOnKill        = s.reward;
        enemy.leakBonus           = s.leakBonus;
        if (!Mathf.Approximately(s.scale, 1f)) enemy.transform.localScale *= s.scale;

        if (s.affix != RogueAffix.None) RogueEnemyAffix.Attach(enemy, s.affix);
    }

    // ── Forecast ─────────────────────────────────────────────────────────────

    static void FillForecast(List<SpawnGroup> groups, int wave, List<WaveGenerator.WaveForecastGroup> into)
    {
        var cfg = RogueRun.Config;
        var seen = new Dictionary<string, int>();
        foreach (var g in groups)
        {
            if (g == null || g.prefab == null || g.count <= 0) continue;
            var s = StatsFor(g, wave);

            string label = s.name;
            string note = null;
            switch (s.affix)
            {
                case RogueAffix.Boss:         label = s.name + "  (BOSS)"; note = $"Blinds one synergy every {cfg.bossAbilityInterval:0}s. Costs {s.leakBonus + 1} lives if it gets through."; break;
                case RogueAffix.Armored:      label += "  [ELITE · Armored]";      note = "Elite: heavily armoured."; break;
                case RogueAffix.Swift:        label += "  [ELITE · Swift]";        note = "Elite: much faster."; break;
                case RogueAffix.Regenerating: label += "  [ELITE · Regenerating]"; note = "Elite: heals itself."; break;
                case RogueAffix.Vengeful:     label += "  [ELITE · Vengeful]";     note = $"Elite: costs {1 + s.leakBonus} lives if it gets through."; break;
            }

            string key = label;
            if (seen.TryGetValue(key, out int at))
            {
                var e = into[at]; e.count += g.count; into[at] = e;
            }
            else
            {
                seen[key] = into.Count;
                into.Add(new WaveGenerator.WaveForecastGroup { name = label, count = g.count, prefab = g.prefab, hp = s.hp, note = note });
            }
        }
    }
}

// What an elite carries on its back: a crown of gold shards that says "this one is
// different", the regeneration, and the extra leak damage (set by ApplySpawn).
[DisallowMultipleComponent]
public class RogueEnemyAffix : MonoBehaviour
{
    public RogueAffix affix;
    float _regen;

    static readonly Color Gold = new(0.95f, 0.76f, 0.26f);

    public static void Attach(EnemySurfaceUnit enemy, RogueAffix a)
    {
        if (enemy == null) return;
        var c = enemy.GetComponent<RogueEnemyAffix>();
        if (c == null) c = enemy.gameObject.AddComponent<RogueEnemyAffix>();
        c.affix = a;
        if (a != RogueAffix.Boss) c.BuildCrown(enemy);
    }

    void BuildCrown(EnemySurfaceUnit e)
    {
        var sh = Shader.Find("GeoWorld/SilkscreenFlat") ?? Shader.Find("Universal Render Pipeline/Unlit");
        if (sh == null) return;
        var mat = new Material(sh) { name = "EliteCrown (runtime)", hideFlags = HideFlags.DontSave };

        Color tint = affix switch
        {
            RogueAffix.Armored      => new Color(0.55f, 0.62f, 0.78f),
            RogueAffix.Swift        => new Color(0.35f, 0.92f, 0.95f),
            RogueAffix.Regenerating => new Color(0.42f, 0.95f, 0.45f),
            RogueAffix.Vengeful     => new Color(0.95f, 0.28f, 0.22f),
            _                       => Gold,
        };

        var root = new GameObject("EliteCrown").transform;
        root.SetParent(transform, false);
        root.localPosition = new Vector3(0f, 0.34f, 0f);
        root.localScale = Vector3.one * 0.5f;

        const int n = 5;
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(shard.GetComponent<Collider>());
            var t = shard.transform;
            t.SetParent(root, false);
            t.localPosition = new Vector3(Mathf.Cos(a) * 0.32f, (i % 2) * 0.1f, Mathf.Sin(a) * 0.32f);
            t.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 22f);
            t.localScale = new Vector3(0.1f, 0.34f, 0.1f);
            var r = shard.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            MpbColor.Set(r, i % 2 == 0 ? Gold : tint);
        }
        _crown = root;
    }

    Transform _crown;

    void Update()
    {
        if (_crown != null) _crown.Rotate(0f, 70f * Time.deltaTime, 0f, Space.Self);

        if (affix != RogueAffix.Regenerating) return;
        var e = GetComponent<EnemySurfaceUnit>();
        if (e == null || e.CurrentHealth <= 0) return;
        _regen -= Time.deltaTime;
        if (_regen > 0f) return;
        _regen = Mathf.Max(0.3f, RogueRun.Config.regenSeconds);
        e.Heal(Mathf.Max(1, Mathf.RoundToInt(e.maxHealth * 0.04f)));
    }
}
