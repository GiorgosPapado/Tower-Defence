using System;

/// <summary>
/// Small MLP policy: obs -> hidden (tanh) -> action logits, with masked argmax. Parameters live in one flat
/// float array (W1[hidden*obs], b1[hidden], W2[actions*hidden], b2[actions]) so a trainer can perturb it directly.
/// Deterministic: no randomness in Act, ties go to the lowest action index.
/// </summary>
public sealed class MlpPolicy : IBuilderPolicy
{
    public const int DefaultHidden = 24;

    public readonly int ObsSize;
    public readonly int Hidden;
    public readonly int Actions;

    private readonly float[] p;
    private readonly float[] hiddenBuf;
    private readonly float[] logitBuf;

    public MlpPolicy() : this(BuilderSpec.ObsSize, DefaultHidden, BuilderSpec.ActionCount)
    {
    }

    public MlpPolicy(int obsSize, int hidden, int actions)
    {
        if (obsSize <= 0 || hidden <= 0 || actions <= 0)
            throw new ArgumentException("MlpPolicy dimensions must be positive");
        ObsSize = obsSize;
        Hidden = hidden;
        Actions = actions;
        p = new float[ParamCount(obsSize, hidden, actions)];
        hiddenBuf = new float[hidden];
        logitBuf = new float[actions];
    }

    public static int ParamCount(int obsSize, int hidden, int actions)
    {
        return hidden * obsSize + hidden + actions * hidden + actions;
    }

    public int ParamCount()
    {
        return p.Length;
    }

    /// <summary>Seeded random initialisation (scaled normal weights, zero biases).</summary>
    public static MlpPolicy CreateRandom(int seed)
    {
        var m = new MlpPolicy();
        m.Randomize(seed);
        return m;
    }

    public void Randomize(int seed)
    {
        var rng = new Random(seed);
        float s1 = 1f / (float)Math.Sqrt(ObsSize);
        float s2 = 0.5f / (float)Math.Sqrt(Hidden);
        int w1 = Hidden * ObsSize;
        int b1 = w1 + Hidden;
        int w2 = b1 + Actions * Hidden;
        for (int i = 0; i < p.Length; i++)
        {
            if (i < w1) p[i] = EvolutionStrategy.NextGaussian(rng) * s1;
            else if (i < b1) p[i] = 0f;
            else if (i < w2) p[i] = EvolutionStrategy.NextGaussian(rng) * s2;
            else p[i] = 0f;
        }
    }

    /// <summary>Copy of the flat parameter vector.</summary>
    public float[] GetParameters()
    {
        return (float[])p.Clone();
    }

    public void SetParameters(float[] src)
    {
        if (src == null || src.Length != p.Length)
            throw new ArgumentException("Expected " + p.Length + " parameters");
        Array.Copy(src, p, p.Length);
    }

    /// <summary>Compute raw logits for an observation into the supplied buffer (length Actions).</summary>
    public void Forward(float[] obs, float[] logits)
    {
        if (obs == null || obs.Length != ObsSize)
            throw new ArgumentException("obs must have length " + ObsSize);
        if (logits == null || logits.Length != Actions)
            throw new ArgumentException("logits must have length " + Actions);

        int w1 = 0;
        int b1 = Hidden * ObsSize;
        int w2 = b1 + Hidden;
        int b2 = w2 + Actions * Hidden;

        for (int h = 0; h < Hidden; h++)
        {
            float sum = p[b1 + h];
            int row = w1 + h * ObsSize;
            for (int i = 0; i < ObsSize; i++) sum += p[row + i] * obs[i];
            hiddenBuf[h] = (float)Math.Tanh(sum);
        }

        for (int a = 0; a < Actions; a++)
        {
            float sum = p[b2 + a];
            int row = w2 + a * Hidden;
            for (int h = 0; h < Hidden; h++) sum += p[row + h] * hiddenBuf[h];
            logits[a] = sum;
        }
    }

    public int Act(float[] obs, bool[] mask)
    {
        if (mask == null || mask.Length != Actions)
            throw new ArgumentException("mask must have length " + Actions);

        Forward(obs, logitBuf);

        int best = -1;
        float bestLogit = float.NegativeInfinity;
        for (int a = 0; a < Actions; a++)
        {
            if (!mask[a]) continue;
            // NaN logits (diverged weights) never beat a real value; the first valid action is the fallback.
            if (best < 0 || logitBuf[a] > bestLogit || float.IsNaN(bestLogit))
            {
                best = a;
                bestLogit = logitBuf[a];
            }
        }
        return best < 0 ? BuilderSpec.WaitAction : best;
    }
}
