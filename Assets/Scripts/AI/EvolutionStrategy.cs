using System;

/// <summary>
/// OpenAI-style Evolution Strategies (Salimans et al. 2017) for maximising a black-box objective.
/// Antithetic (mirrored) sampling, centered-rank fitness shaping, seeded System.Random.
/// Pure C#: no UnityEngine dependency, so it can be unit-tested and reused outside the game.
///
/// Usage per generation: <c>float[][] pop = es.Ask();</c> evaluate each candidate, then <c>es.Tell(fitness);</c>
/// or simply <c>es.Step(objective)</c>.
/// </summary>
public sealed class EvolutionStrategy
{
    public struct GenerationStats
    {
        public int generation;
        public float meanFitness;
        public float bestFitness;
        public float worstFitness;
    }

    private readonly float[] theta;
    private readonly int dim;
    private readonly int population;
    private readonly float sigma;
    private readonly float learningRate;
    private readonly Random rng;

    private float[][] noise;       // one perturbation per antithetic pair (population / 2)
    private float[][] candidates;  // population candidates handed out by Ask()
    private int generation;

    /// <param name="initial">Starting parameters (copied).</param>
    /// <param name="populationSize">Even number of candidates per generation (pairs of +/- noise).</param>
    public EvolutionStrategy(float[] initial, int seed, int populationSize = 24, float sigma = 0.1f, float learningRate = 0.05f)
    {
        if (initial == null || initial.Length == 0) throw new ArgumentException("initial parameters required");
        if (populationSize < 2 || populationSize % 2 != 0) throw new ArgumentException("populationSize must be even and >= 2");
        if (sigma <= 0f) throw new ArgumentException("sigma must be > 0");

        dim = initial.Length;
        theta = (float[])initial.Clone();
        population = populationSize;
        this.sigma = sigma;
        this.learningRate = learningRate;
        rng = new Random(seed);

        noise = new float[population / 2][];
        candidates = new float[population][];
        for (int i = 0; i < noise.Length; i++) noise[i] = new float[dim];
        for (int i = 0; i < population; i++) candidates[i] = new float[dim];
    }

    public int Dimension { get { return dim; } }
    public int PopulationSize { get { return population; } }
    public int Generation { get { return generation; } }

    /// <summary>Copy of the current search centre.</summary>
    public float[] GetTheta()
    {
        return (float[])theta.Clone();
    }

    /// <summary>
    /// Sample the next population: candidate 2k = theta + sigma*eps_k, candidate 2k+1 = theta - sigma*eps_k.
    /// The returned arrays are reused by the next Ask(); copy what you need to keep.
    /// </summary>
    public float[][] Ask()
    {
        for (int k = 0; k < noise.Length; k++)
        {
            float[] eps = noise[k];
            float[] plus = candidates[2 * k];
            float[] minus = candidates[2 * k + 1];
            for (int j = 0; j < dim; j++)
            {
                eps[j] = NextGaussian(rng);
                plus[j] = theta[j] + sigma * eps[j];
                minus[j] = theta[j] - sigma * eps[j];
            }
        }
        return candidates;
    }

    /// <summary>Update theta from the fitness (higher is better) of the candidates returned by the last Ask().</summary>
    public GenerationStats Tell(float[] fitness)
    {
        if (fitness == null || fitness.Length != population)
            throw new ArgumentException("Expected " + population + " fitness values");

        var stats = new GenerationStats { generation = generation, bestFitness = float.NegativeInfinity, worstFitness = float.PositiveInfinity };
        double sum = 0.0;
        for (int i = 0; i < population; i++)
        {
            sum += fitness[i];
            if (fitness[i] > stats.bestFitness) stats.bestFitness = fitness[i];
            if (fitness[i] < stats.worstFitness) stats.worstFitness = fitness[i];
        }
        stats.meanFitness = (float)(sum / population);

        float[] shaped = CenteredRanks(fitness);

        // Gradient estimate: 1 / (n * sigma) * sum_k (F(+eps_k) - F(-eps_k)) * eps_k
        float scale = learningRate / (population * sigma);
        for (int k = 0; k < noise.Length; k++)
        {
            float diff = shaped[2 * k] - shaped[2 * k + 1];
            if (diff == 0f) continue;
            float[] eps = noise[k];
            for (int j = 0; j < dim; j++) theta[j] += scale * diff * eps[j];
        }

        generation++;
        return stats;
    }

    /// <summary>Run one full generation against an objective and return its statistics.</summary>
    public GenerationStats Step(Func<float[], float> objective)
    {
        float[][] pop = Ask();
        var fitness = new float[population];
        for (int i = 0; i < population; i++) fitness[i] = objective(pop[i]);
        return Tell(fitness);
    }

    /// <summary>
    /// Centered ranks: values mapped to [-0.5, 0.5] by rank (lowest -0.5, highest +0.5). Ties share the average rank.
    /// Makes the update invariant to the scale and outliers of the raw fitness.
    /// </summary>
    public static float[] CenteredRanks(float[] values)
    {
        int n = values.Length;
        var result = new float[n];
        if (n == 1) return result;

        var order = new int[n];
        for (int i = 0; i < n; i++) order[i] = i;
        // Stable insertion order on ties is irrelevant because tied entries get the same average rank.
        Array.Sort(order, (a, b) =>
        {
            int c = values[a].CompareTo(values[b]);
            return c != 0 ? c : a.CompareTo(b);
        });

        int start = 0;
        while (start < n)
        {
            int end = start;
            while (end + 1 < n && values[order[end + 1]] == values[order[start]]) end++;
            float avgRank = (start + end) * 0.5f;
            float centered = avgRank / (n - 1) - 0.5f;
            for (int i = start; i <= end; i++) result[order[i]] = centered;
            start = end + 1;
        }
        return result;
    }

    /// <summary>Standard normal sample via Box-Muller from a seeded System.Random.</summary>
    public static float NextGaussian(Random rng)
    {
        double u1 = 1.0 - rng.NextDouble();   // (0, 1]
        double u2 = rng.NextDouble();
        return (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }
}
