using NUnit.Framework;

public class EvalStatsTests
{
    [Test]
    public void Wilson_KnownValues()
    {
        double lo, hi;
        EvalStats.Wilson(15, 30, out lo, out hi);
        Assert.AreEqual(0.3323, lo, 1e-3);
        Assert.AreEqual(0.6677, hi, 1e-3);

        EvalStats.Wilson(0, 30, out lo, out hi);
        Assert.AreEqual(0.0, lo, 1e-9);
        Assert.AreEqual(0.1135, hi, 1e-3);

        EvalStats.Wilson(30, 30, out lo, out hi);
        Assert.AreEqual(0.8865, lo, 1e-3);
        Assert.AreEqual(1.0, hi, 1e-9);

        EvalStats.Wilson(0, 0, out lo, out hi);
        Assert.AreEqual(0.0, lo);
        Assert.AreEqual(1.0, hi);
    }

    [Test]
    public void Wilson_ContainsEstimateAndNarrowsWithN()
    {
        double lo1, hi1, lo2, hi2;
        EvalStats.Wilson(6, 10, out lo1, out hi1);
        EvalStats.Wilson(60, 100, out lo2, out hi2);
        Assert.Less(lo1, 0.6);
        Assert.Greater(hi1, 0.6);
        Assert.Less(hi2 - lo2, hi1 - lo1);
    }

    [Test]
    public void MeanAndStdError()
    {
        var v = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 };
        Assert.AreEqual(5.0, EvalStats.Mean(v), 1e-12);
        // sample sd = sqrt(32/7), se = sd / sqrt(8)
        Assert.AreEqual(System.Math.Sqrt(32.0 / 7.0) / System.Math.Sqrt(8.0), EvalStats.StdError(v), 1e-12);
        Assert.AreEqual(0.0, EvalStats.StdError(new double[] { 3 }));
        Assert.AreEqual(0.0, EvalStats.Mean(new double[0]));
    }

    [Test]
    public void SelectBest_PicksMaxAndBreaksTiesByOrder()
    {
        Assert.AreEqual(2, EvalStats.SelectBest(new double[] { 1, 5, 9, 3 }));
        Assert.AreEqual(1, EvalStats.SelectBest(new double[] { 1, 9, 9, 3 }));
        Assert.AreEqual(0, EvalStats.SelectBest(new double[] { 7 }));
        Assert.Throws<System.ArgumentException>(() => EvalStats.SelectBest(new double[0]));
    }

    [Test]
    public void Overlap_Works()
    {
        Assert.IsTrue(EvalStats.Overlap(0.1, 0.4, 0.3, 0.6));
        Assert.IsFalse(EvalStats.Overlap(0.1, 0.2, 0.3, 0.6));
    }

    [Test]
    public void RelativeJitter_IsDeterministicInRangeAndSeedDependent()
    {
        var seen = new System.Collections.Generic.HashSet<float>();
        for (int seed = 1; seed <= 30; seed++)
        {
            float u = Difficulty.RelativeJitterFromSeed(seed, 0.05f);
            Assert.AreEqual(u, Difficulty.RelativeJitterFromSeed(seed, 0.05f));
            Assert.GreaterOrEqual(u, -0.05f);
            Assert.LessOrEqual(u, 0.05f);
            seen.Add(u);
        }
        Assert.Greater(seen.Count, 25, "jitter must vary with the seed");
        Assert.AreEqual(0f, Difficulty.RelativeJitterFromSeed(5, 0f));
    }

    [Test]
    public void RelativeJitter_SpansBothSidesOfZero()
    {
        float min = 1f, max = -1f;
        for (int seed = 1; seed <= 200; seed++)
        {
            float u = Difficulty.RelativeJitterFromSeed(seed, 0.05f);
            if (u < min) min = u;
            if (u > max) max = u;
        }
        Assert.Less(min, -0.04f);
        Assert.Greater(max, 0.04f);
    }

    [Test]
    public void HeuristicVariants_AllConstructAndUnknownThrows()
    {
        foreach (string n in HeuristicPolicy.VariantNames)
            Assert.IsNotNull(HeuristicPolicy.Variant(n));
        Assert.Throws<System.ArgumentException>(() => HeuristicPolicy.Variant("nope"));
    }

    [Test]
    public void HeuristicVariant_Standard_AlwaysChoosesStandard()
    {
        var p = HeuristicPolicy.Variant("standard");
        var obs = new float[BuilderSpec.ObsSize];
        var mask = new bool[BuilderSpec.ActionCount];
        for (int i = 1; i < mask.Length; i++) mask[i] = true;
        for (int built = 0; built < 6; built++)
        {
            obs[BuilderSpec.ObsTypeCounts + 1] = built / 20f;   // pretend missiles were built
            int a = p.Act(obs, mask);
            int slot, type;
            Assert.IsTrue(BuilderSpec.Decode(a, out slot, out type));
            Assert.AreEqual(0, type);
        }
    }
}
