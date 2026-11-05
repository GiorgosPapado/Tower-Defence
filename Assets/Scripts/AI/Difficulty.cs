using UnityEngine;

/// <summary>Global difficulty knobs used by the balance tester and the trainer. Defaults leave the game unchanged.</summary>
public static class Difficulty
{
    private static float healthMultiplier = 1f;

    /// <summary>Multiplies enemy health in Enemies.Awake. Always &gt; 0; 1 in normal play.</summary>
    public static float HealthMultiplier
    {
        get { return healthMultiplier; }
        set { healthMultiplier = value > 0f ? value : 1f; }
    }

    public static void Reset()
    {
        healthMultiplier = 1f;
    }

    /// <summary>Deterministic uniform value in [lo, hi] derived from an episode seed (enemy-HP jitter).</summary>
    public static float JitterFromSeed(int seed, float lo, float hi)
    {
        var rng = new System.Random(seed);
        return lo + (float)rng.NextDouble() * (hi - lo);
    }

    /// <summary>
    /// Deterministic relative jitter u in [-halfWidth, +halfWidth] derived from an episode seed; 0 when halfWidth is 0.
    /// The HP multiplier is then scale * (1 + u).
    /// </summary>
    public static float RelativeJitterFromSeed(int seed, float halfWidth)
    {
        if (halfWidth <= 0f) return 0f;
        var rng = new System.Random(unchecked(seed * 7919 + 13));
        return (float)((rng.NextDouble() * 2.0 - 1.0) * halfWidth);
    }

    // Keeps the value clean when "Enter Play Mode without domain reload" is enabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        healthMultiplier = 1f;
    }
}
