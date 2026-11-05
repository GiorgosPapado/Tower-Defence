using System;

/// <summary>Baseline: uniformly random among the valid actions (wait included). Seeded and deterministic.</summary>
public sealed class RandomPolicy : IBuilderPolicy
{
    private Random rng;

    public RandomPolicy(int seed)
    {
        rng = new Random(seed);
    }

    public void Reseed(int seed)
    {
        rng = new Random(seed);
    }

    public int Act(float[] obs, bool[] mask)
    {
        int valid = 0;
        for (int i = 0; i < mask.Length; i++)
            if (mask[i]) valid++;
        if (valid == 0) return BuilderSpec.WaitAction;

        int pick = rng.Next(valid);
        for (int i = 0; i < mask.Length; i++)
        {
            if (!mask[i]) continue;
            if (pick == 0) return i;
            pick--;
        }
        return BuilderSpec.WaitAction;
    }
}
