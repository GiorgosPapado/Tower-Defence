using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

/// <summary>
/// Env-gated drivers (Calibration: TD_CALIBRATE=1, Training: TD_TRAIN=1). Without the env var they Assert.Ignore at once,
/// so the default suite is not slowed down.
/// </summary>
public class AiTrainTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int TrainSeedLo = 1000;
    private const int TrainSeedHi = 1999;
    private const int EvalSeedLo = 1;
    private const int EvalSeedHi = 30;
    private const int CenterSeedBase = 1900;   // 6 fixed center-tracking seeds: 1900..1905
    private const int CenterSeedCount = 6;
    private const float DefaultLo = 1.3f;
    private const float DefaultHi = 1.4f;

    [TearDown]
    public void Restore()
    {
        SimClock.ForceRestore();
        Difficulty.Reset();
        Time.timeScale = 1f;
    }

    private static string ProjectRoot()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    }

    private static string Env(string name, string def)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v) ? def : v;
    }

    private static int EnvInt(string name, int def)
    {
        return int.Parse(Env(name, def.ToString(Inv)), Inv);
    }

    private static float EnvFloat(string name, float def)
    {
        return float.Parse(Env(name, def.ToString("R", Inv)), Inv);
    }

    private static string F(double v)
    {
        return v.ToString("0.###", Inv);
    }

    // ------------------------------------------------------------------ Calibration

    [UnityTest]
    [Category("Calibration")]
    [Timeout(3600000)]
    public IEnumerator Calibration()
    {
        if (Environment.GetEnvironmentVariable("TD_CALIBRATE") != "1") Assert.Ignore("TD_CALIBRATE != 1");

        float[] mults = { 1.0f, 1.1f, 1.2f, 1.3f, 1.4f, 1.5f, 2.0f, 2.5f, 3.0f, 4.0f };
        int seeds = EnvInt("TD_CAL_SEEDS", 10);
        string[] names = { "Random", "Heuristic" };
        var rows = new List<string>();
        var table = new StringBuilder();
        table.AppendLine("| HP x | Policy | Episodes | Win rate | Mean lives | Mean R |");
        table.AppendLine("|---|---|---|---|---|---|");
        var sw = Stopwatch.StartNew();

        foreach (float m in mults)
        {
            foreach (string name in names)
            {
                int wins = 0;
                double lives = 0, ret = 0;
                for (int s = 1; s <= seeds; s++)
                {
                    IBuilderPolicy pol = name == "Random" ? (IBuilderPolicy)new RandomPolicy(s) : new HeuristicPolicy();
                    var cfg = new EpisodeConfig { HealthMultiplier = m, SimCap = 600f };
                    EpisodeResult r = null;
                    yield return EpisodeRunner.Run(pol, s, cfg, x => r = x);
                    Assert.IsNotNull(r);
                    if (r.win) wins++;
                    lives += r.livesLeft;
                    ret += r.Return;
                }
                double wr = (double)wins / seeds, ml = lives / seeds, mr = ret / seeds;
                rows.Add("    {\"hp\": " + F(m) + ", \"policy\": \"" + name + "\", \"episodes\": " + seeds
                    + ", \"winRate\": " + F(wr) + ", \"meanLives\": " + F(ml) + ", \"meanR\": " + F(mr) + "}");
                table.AppendLine("| " + F(m) + " | " + name + " | " + seeds + " | " + F(wr) + " | " + F(ml) + " | " + F(mr) + " |");
                Debug.Log("CAL hp=" + F(m) + " " + name + " win=" + F(wr) + " lives=" + F(ml) + " R=" + F(mr));
            }
        }

        string dir = Path.Combine(ProjectRoot(), "docs", "evaluation");
        Directory.CreateDirectory(dir);
        string json = "{\n  \"unity\": \"" + Application.unityVersion + "\",\n  \"seeds\": \"1.." + seeds
            + "\",\n  \"wallSeconds\": " + F(sw.Elapsed.TotalSeconds) + ",\n  \"cells\": [\n" + string.Join(",\n", rows.ToArray()) + "\n  ]\n}\n";
        File.WriteAllText(Path.Combine(dir, "calibration.json"), json, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(dir, "calibration.md"),
            "# Difficulty calibration (fixed enemy-HP multiplier, no jitter)\n\nSeeds 1.." + seeds + " per cell, Unity "
            + Application.unityVersion + ", wall " + F(sw.Elapsed.TotalSeconds) + " s.\n\n" + table, new UTF8Encoding(false));
        Assert.Pass();
    }

    // ------------------------------------------------------------------ Training

    [Serializable]
    private class Checkpoint
    {
        public int generation;          // generations completed
        public int[] thetaBits;
        public int[] bestBits;
        public float bestCenterR = float.NegativeInfinity;
        public int bestGeneration;
        public double wallSeconds;
        public int seed, population;
        public float sigma, lr, lo, hi;
    }

    private static int[] ToBits(float[] w)
    {
        var b = new int[w.Length];
        for (int i = 0; i < w.Length; i++) b[i] = BitConverter.SingleToInt32Bits(w[i]);
        return b;
    }

    private static float[] FromBits(int[] b)
    {
        var w = new float[b.Length];
        for (int i = 0; i < b.Length; i++) w[i] = BitConverter.Int32BitsToSingle(b[i]);
        return w;
    }

    private static EpisodeConfig TrainCfg(float lo, float hi)
    {
        return new EpisodeConfig { JitterHp = true, JitterLo = lo, JitterHi = hi, SimCap = 600f };
    }

    private static IEnumerator MeanReturn(Func<IBuilderPolicy> make, int[] seeds, float lo, float hi, Action<float> done)
    {
        double sum = 0;
        foreach (int s in seeds)
        {
            EpisodeResult r = null;
            yield return EpisodeRunner.Run(make(), s, TrainCfg(lo, hi), x => r = x);
            sum += r.Return;
        }
        done((float)(sum / seeds.Length));
    }

    [UnityTest]
    [Category("Training")]
    [Timeout(3600000)]
    public IEnumerator Training()
    {
        if (Environment.GetEnvironmentVariable("TD_TRAIN") != "1") Assert.Ignore("TD_TRAIN != 1");

        int generations = EnvInt("TD_GENERATIONS", 30);
        int pop = EnvInt("TD_POP", 24);
        int seed = EnvInt("TD_SEED", 1);
        float sigma = EnvFloat("TD_SIGMA", 0.1f);
        float lr = EnvFloat("TD_LR", 0.05f);
        float lo = EnvFloat("TD_HP_LO", DefaultLo);
        float hi = EnvFloat("TD_HP_HI", DefaultHi);
        double budget = EnvFloat("TD_BUDGET", 420f);     // wall seconds of work per invocation
        bool resume = Environment.GetEnvironmentVariable("TD_RESUME") == "1";
        const int episodesPerCandidate = 2;

        // Seed separation: training seeds must never touch the evaluation seeds.
        Assert.Greater(TrainSeedLo, EvalSeedHi, "training seeds overlap evaluation seeds");
        Assert.Less(EvalSeedLo, TrainSeedLo);
        Assert.Greater(CenterSeedBase, TrainSeedLo - 1);
        Assert.LessOrEqual(CenterSeedBase + CenterSeedCount - 1, TrainSeedHi);

        string root = ProjectRoot();
        string evalDir = Path.Combine(root, "docs", "evaluation");
        string ckptDir = Path.Combine(root, "Library", "TdTraining");
        string ckptPath = Path.Combine(ckptDir, "checkpoint.json");
        string csvPath = Path.Combine(evalDir, "builder-training.csv");
        Directory.CreateDirectory(evalDir);
        Directory.CreateDirectory(ckptDir);

        var centerSeeds = new int[CenterSeedCount];
        for (int i = 0; i < CenterSeedCount; i++) centerSeeds[i] = CenterSeedBase + i;

        var template = new MlpPolicy();
        Checkpoint ck;
        if (resume && File.Exists(ckptPath))
        {
            ck = JsonUtility.FromJson<Checkpoint>(File.ReadAllText(ckptPath));
            Assert.AreEqual(seed, ck.seed, "resume: seed mismatch");
            Assert.AreEqual(pop, ck.population, "resume: population mismatch");
            Debug.Log("TRAIN resume at generation " + ck.generation);
        }
        else
        {
            template.Randomize(seed);
            ck = new Checkpoint
            {
                seed = seed, population = pop, sigma = sigma, lr = lr, lo = lo, hi = hi,
                thetaBits = ToBits(template.GetParameters()),
            };
            File.WriteAllText(csvPath, "generation,mean,best,center_R,wall_seconds\n", new UTF8Encoding(false));
        }
        Assert.AreEqual(lo, ck.lo, "resume: HP band changed");
        Assert.AreEqual(hi, ck.hi, "resume: HP band changed");

        var es = new EvolutionStrategy(FromBits(ck.thetaBits), seed, pop, sigma, lr);
        var worker = new MlpPolicy();
        var inv = Inv;
        var sw = Stopwatch.StartNew();
        double wallBase = ck.wallSeconds;

        // Generation 0 row: the untrained center.
        if (ck.generation == 0)
        {
            float c0 = 0;
            worker.SetParameters(es.GetTheta());
            yield return MeanReturn(() => worker, centerSeeds, lo, hi, v => c0 = v);
            ck.bestCenterR = c0;
            ck.bestGeneration = 0;
            ck.bestBits = ToBits(es.GetTheta());
            File.AppendAllText(csvPath, "0,0,0," + c0.ToString("0.###", inv) + "," + (wallBase + sw.Elapsed.TotalSeconds).ToString("0.#", inv) + "\n");
            Debug.Log("TRAIN gen 0 center_R=" + c0);
        }

        while (ck.generation < generations)
        {
            if (sw.Elapsed.TotalSeconds > budget)
            {
                ck.wallSeconds = wallBase + sw.Elapsed.TotalSeconds;
                File.WriteAllText(ckptPath, JsonUtility.ToJson(ck));
                Debug.Log("TRAIN PAUSED at generation " + ck.generation + " of " + generations + " (budget). Re-run with TD_RESUME=1");
                Assert.Pass("paused");
                yield break;
            }

            int g = ck.generation;
            // Per-generation RNG stream so a resumed run is identical to an uninterrupted one.
            es.Restore(FromBits(ck.thetaBits), g, seed * 100003 + g);
            float[][] cand = es.Ask();
            var fit = new float[pop];

            // Common random numbers: all candidates of a generation face the same training episodes.
            var epSeeds = new int[episodesPerCandidate];
            for (int e = 0; e < episodesPerCandidate; e++)
            {
                epSeeds[e] = TrainSeedLo + (int)(((long)seed * 7919 + g * 131 + e * 37) % (TrainSeedHi - TrainSeedLo + 1));
                Assert.GreaterOrEqual(epSeeds[e], TrainSeedLo);
                Assert.LessOrEqual(epSeeds[e], TrainSeedHi);
            }
            for (int i = 0; i < pop; i++)
            {
                worker.SetParameters(cand[i]);
                float f = 0;
                yield return MeanReturn(() => worker, epSeeds, lo, hi, v => f = v);
                fit[i] = f;
            }
            EvolutionStrategy.GenerationStats st = es.Tell(fit);
            float[] theta = es.GetTheta();
            ck.thetaBits = ToBits(theta);

            float center = 0;
            worker.SetParameters(theta);
            yield return MeanReturn(() => worker, centerSeeds, lo, hi, v => center = v);

            ck.generation = g + 1;
            if (center > ck.bestCenterR)
            {
                ck.bestCenterR = center;
                ck.bestGeneration = ck.generation;
                ck.bestBits = ToBits(theta);
            }
            ck.wallSeconds = wallBase + sw.Elapsed.TotalSeconds;
            File.AppendAllText(csvPath, ck.generation + "," + st.meanFitness.ToString("0.###", inv) + "," + st.bestFitness.ToString("0.###", inv)
                + "," + center.ToString("0.###", inv) + "," + ck.wallSeconds.ToString("0.#", inv) + "\n");
            File.WriteAllText(ckptPath, JsonUtility.ToJson(ck));
            Debug.Log("TRAIN gen " + ck.generation + " mean=" + st.meanFitness + " best=" + st.bestFitness + " center_R=" + center
                + " bestSoFar=" + ck.bestCenterR + "@" + ck.bestGeneration + " wall=" + ck.wallSeconds.ToString("F0"));
        }

        // Finished: save the best-by-center checkpoint plus a comparison against the baselines on the same 6 seeds.
        var best = new MlpPolicy();
        best.SetParameters(FromBits(ck.bestBits));
        float trainedR = 0, heuR = 0, rndR = 0;
        yield return MeanReturn(() => best, centerSeeds, lo, hi, v => trainedR = v);
        yield return MeanReturn(() => new HeuristicPolicy(), centerSeeds, lo, hi, v => heuR = v);
        int rs = 0;
        yield return MeanReturn(() => new RandomPolicy(900 + rs++), centerSeeds, lo, hi, v => rndR = v);

        string cfgText = "algo=OpenAI-ES;sigma=" + sigma.ToString("R", inv) + ";lr=" + lr.ToString("R", inv)
            + ";hpBand=" + lo.ToString("R", inv) + "-" + hi.ToString("R", inv)
            + ";episodesPerCandidate=" + episodesPerCandidate + ";trainSeeds=" + TrainSeedLo + "-" + TrainSeedHi
            + ";centerSeeds=" + centerSeeds[0] + "-" + centerSeeds[CenterSeedCount - 1]
            + ";bestGeneration=" + ck.bestGeneration + ";planCommit=" + Env("TD_PLAN_COMMIT", "unknown");
        PolicyMetadata meta = PolicyMetadata.Create(seed, ck.generation, pop, cfgText);
        meta.trainReturn = trainedR;
        string policyPath = Path.Combine(root, "Assets", "Resources", "builder_policy.json");
        PolicyIO.Save(policyPath, best, meta);

        string summary = "{\n  \"trainedCenterR\": " + F(trainedR) + ",\n  \"heuristicR\": " + F(heuR) + ",\n  \"randomR\": " + F(rndR)
            + ",\n  \"seeds\": \"" + centerSeeds[0] + "-" + centerSeeds[CenterSeedCount - 1] + "\",\n  \"hpBand\": [" + F(lo) + ", " + F(hi)
            + "],\n  \"bestGeneration\": " + ck.bestGeneration + ",\n  \"totalWallSeconds\": " + F(ck.wallSeconds) + "\n}\n";
        File.WriteAllText(Path.Combine(evalDir, "builder-training-summary.json"), summary, new UTF8Encoding(false));
        Debug.Log("TRAIN DONE trained=" + trainedR + " heuristic=" + heuR + " random=" + rndR + " bestGen=" + ck.bestGeneration
            + " totalWall=" + ck.wallSeconds.ToString("F0"));

        // Round trip sanity: the saved file loads and reproduces the weights.
        PolicyMetadata back;
        MlpPolicy loaded = PolicyIO.Load(policyPath, out back);
        CollectionAssert.AreEqual(best.GetParameters(), loaded.GetParameters());
        Assert.Pass();
    }
}
