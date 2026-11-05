using System;

/// <summary>Pure statistics helpers for the balance tester.</summary>
public static class EvalStats
{
    /// <summary>Wilson score interval for a binomial proportion (default z = 1.96, 95%).</summary>
    public static void Wilson(int successes, int n, out double lo, out double hi, double z = 1.96)
    {
        if (n <= 0)
        {
            lo = 0;
            hi = 1;
            return;
        }
        if (successes < 0) successes = 0;
        if (successes > n) successes = n;
        double p = (double)successes / n;
        double z2 = z * z;
        double denom = 1.0 + z2 / n;
        double center = (p + z2 / (2.0 * n)) / denom;
        double half = z * Math.Sqrt(p * (1.0 - p) / n + z2 / (4.0 * n * n)) / denom;
        lo = Math.Max(0.0, center - half);
        hi = Math.Min(1.0, center + half);
    }

    public static double Mean(double[] v)
    {
        if (v == null || v.Length == 0) return 0;
        double s = 0;
        for (int i = 0; i < v.Length; i++) s += v[i];
        return s / v.Length;
    }

    /// <summary>Standard error of the mean using the sample (n-1) standard deviation; 0 for n &lt; 2.</summary>
    public static double StdError(double[] v)
    {
        if (v == null || v.Length < 2) return 0;
        double m = Mean(v), ss = 0;
        for (int i = 0; i < v.Length; i++) ss += (v[i] - m) * (v[i] - m);
        return Math.Sqrt(ss / (v.Length - 1)) / Math.Sqrt(v.Length);
    }

    /// <summary>Index of the highest score; ties resolve to the lowest index (so the order of candidates is the tie-break).</summary>
    public static int SelectBest(double[] scores)
    {
        if (scores == null || scores.Length == 0) throw new ArgumentException("no scores");
        int best = 0;
        for (int i = 1; i < scores.Length; i++)
            if (scores[i] > scores[best]) best = i;
        return best;
    }

    /// <summary>True if two intervals share any point.</summary>
    public static bool Overlap(double aLo, double aHi, double bLo, double bHi)
    {
        return aLo <= bHi && bLo <= aHi;
    }
}
