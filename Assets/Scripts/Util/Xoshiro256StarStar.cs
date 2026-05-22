using System;
using System.Collections.Generic;
using UnityEngine;

// xoshiro256** PRNG — Blackman & Vigna 2018 (https://prng.di.unimi.it/)
//
// Why this and not System.Random:
//   • Tiny serializable state (4 ulong = 32 bytes), fields are public so
//     JsonUtility / our own snapshot system can save / restore the run's
//     RNG state without reflection hacks.
//   • Fast, well-distributed, passes BigCrush.
//   • Deterministic: same seed → same sequence on any platform / version.
//
// Seed an entire roguelite run from one user-visible seed, keep the same
// instance alive for the whole run (wave gen, loot, shop, events, ...) so
// the sequence is reproducible end-to-end.
[Serializable]
public sealed class Xoshiro256StarStar
{
    // State must not be all zeros. SplitMix64 in the constructor guarantees
    // a non-zero state for any seed; the all-zero guard is belt-and-braces.
    public ulong s0, s1, s2, s3;

    public Xoshiro256StarStar()
        : this(unchecked((ulong)DateTime.UtcNow.Ticks)) { }

    public Xoshiro256StarStar(ulong seed)
    {
        // Expand the 64-bit seed into 256 bits of state using SplitMix64.
        // SplitMix64 is the recommended seeder per the xoshiro paper.
        ulong sm = seed;
        s0 = SplitMix64(ref sm);
        s1 = SplitMix64(ref sm);
        s2 = SplitMix64(ref sm);
        s3 = SplitMix64(ref sm);

        if ((s0 | s1 | s2 | s3) == 0UL) s0 = 1UL;
    }

    // ── Serialization helpers ───────────────────────────────────────────

    public ulong[] SaveState() => new[] { s0, s1, s2, s3 };

    public void LoadState(ulong[] state)
    {
        if (state == null || state.Length != 4)
            throw new ArgumentException("xoshiro state must be 4 ulongs.");
        s0 = state[0]; s1 = state[1]; s2 = state[2]; s3 = state[3];
        if ((s0 | s1 | s2 | s3) == 0UL) s0 = 1UL;
    }

    // Branch off a sub-generator without consuming the parent's stream.
    // Useful if you ever want partitioned RNGs (waveRng / lootRng / ...).
    public Xoshiro256StarStar Fork(ulong salt)
    {
        ulong mix = s0 ^ s1 ^ s2 ^ s3 ^ salt;
        return new Xoshiro256StarStar(mix);
    }

    // ── Core ────────────────────────────────────────────────────────────

    static ulong SplitMix64(ref ulong state)
    {
        ulong z = (state += 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

    public ulong NextUInt64()
    {
        ulong result = Rotl(s1 * 5UL, 7) * 9UL;

        ulong t = s1 << 17;

        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;

        s2 ^= t;
        s3 = Rotl(s3, 45);

        return result;
    }

    // ── Convenience ─────────────────────────────────────────────────────

    // [0, 1) — uses the top 53 bits, the precision of a double mantissa.
    public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

    public float NextFloat() => (float)NextDouble();

    // [0, max). Returns 0 if max <= 0.
    // Small modulo bias (~max / 2^64) — irrelevant for gameplay-scale ints.
    public int NextInt(int max)
    {
        if (max <= 0) return 0;
        return (int)(NextUInt64() % (ulong)max);
    }

    // [min, max).
    public int NextInt(int min, int max)
    {
        if (max <= min) return min;
        return min + NextInt(max - min);
    }

    // [min, max] inclusive.
    public int NextIntInclusive(int min, int max) => NextInt(min, max + 1);

    public float NextRange(float min, float max) => min + (max - min) * NextFloat();

    public bool NextBool() => (NextUInt64() & 1UL) != 0UL;

    // Weighted pick. Negative weights treated as 0. Returns -1 if no positive weight.
    public int PickWeighted(IReadOnlyList<float> weights)
    {
        if (weights == null || weights.Count == 0) return -1;

        float total = 0f;
        for (int i = 0; i < weights.Count; i++) total += Mathf.Max(0f, weights[i]);
        if (total <= 0f) return -1;

        float r = NextFloat() * total;
        float acc = 0f;
        for (int i = 0; i < weights.Count; i++)
        {
            acc += Mathf.Max(0f, weights[i]);
            if (r < acc) return i;
        }
        return weights.Count - 1;
    }

    // In-place Fisher–Yates shuffle.
    public void Shuffle<T>(IList<T> list)
    {
        if (list == null) return;
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
