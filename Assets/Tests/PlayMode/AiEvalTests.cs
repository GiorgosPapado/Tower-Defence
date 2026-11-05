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
/// Env-gated balance-tester driver (TD_EVAL=1, category "Evaluation"). Without the env var it Assert.Ignore()s at once.
/// TD_BUDGET = wall seconds of work per invocation (default 420), TD_RESUME=1 continues from Library/TdEval/checkpoint.json.
/// Evaluation seeds are 1..30 only; tuned-baseline selection uses training seeds 1000..1049 only.
/// </summary>
public class AiEvalTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private const int EvalSeedLo = 1, EvalSeedHi = 30;
    private const int TrainSeedLo = 1000, TrainSeedHi = 1999;
    private const int SelectSeedLo = 1000, SelectSeedHi = 1049;
    private const float SelectScale = 1.35f;
    private const float RelJitter = 0.05f;
    private const int RandomPolicySeedOffset = 5000;

    private static readonly float[] SweepScales = { 1.0f, 1.2f, 1.3f, 1.4f, 1.5f, 1.7f };
    private static readonly float[] AblationScales = { 1.3f, 1.4f };
    private static readonly string[] Policies = { "Random", "Heuristic", "HeuristicTuned", "Trained" };

    [TearDown]
    public void Restore()
    {
        SimClock.ForceRestore();
        Difficulty.Reset();
        Time.timeScale = 1f;
    }

    // ------------------------------------------------------------------ data

    [Serializable]
    private class EpRec
    {
        public int seed;
        public bool win, hitCap;
        public int lives, wavesReached, wavesCleared, nStandard, nMissile, nLaser, spent;
        public float hp, sim, ret;
    }

    [Serializable]
    private class Cell
    {
        public string id, kind, policy, variant, allowed;
        public float scale;
        public int n;
        public double wallSeconds;
        public double winRate, wilsonLo, wilsonHi;
        public double meanLives, seLives, meanWaves, seWaves, meanReturn, seReturn, meanSpent, meanStandard, meanMissile, meanLaser, meanSim;
        public List<EpRec> episodes = new List<EpRec>();
    }

    private class Job
    {
        public string id, kind, policy, allowedName;
        public TurretTypes allowed;
        public float scale;
        public int seedLo, seedHi;
    }

    [Serializable]
    private class Ckpt
    {
        public List<Cell> cells = new List<Cell>();
        public string tunedVariant = "";
        public double wallSeconds;
    }

    [Serializable]
    private class Report
    {
        public string unity, planCommit, generatedUtc, tunedVariant, policyMetadata, notes;
        public string evalSeeds, selectionSeeds, trainSeeds;
        public float relativeHpJitter, selectionScale;
        public double totalWallSeconds;
        public List<Cell> selection = new List<Cell>();
        public List<Cell> cells = new List<Cell>();
    }

    private static string Env(string name, string def)
    {
        string v = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(v) ? def : v;
    }

    private static string F(double v, string fmt = "0.###")
    {
        return v.ToString(fmt, Inv);
    }

    private static string AllowedName(TurretTypes t)
    {
        return t == TurretTypes.All ? "all" : t.ToString().ToLowerInvariant();
    }

    private static List<Job> BuildJobs()
    {
        var jobs = new List<Job>();
        foreach (string v in HeuristicPolicy.VariantNames)
            jobs.Add(new Job { id = "select|" + v, kind = "select", policy = "variant:" + v, allowed = TurretTypes.All, allowedName = "all", scale = SelectScale, seedLo = SelectSeedLo, seedHi = SelectSeedHi });
        foreach (float s in SweepScales)
            foreach (string p in Policies)
                jobs.Add(new Job { id = "sweep|" + p + "|" + F(s) + "|all", kind = "sweep", policy = p, allowed = TurretTypes.All, allowedName = "all", scale = s, seedLo = EvalSeedLo, seedHi = EvalSeedHi });
        TurretTypes[] abl = { TurretTypes.Standard, TurretTypes.Missile, TurretTypes.Laser };
        foreach (float s in AblationScales)
            foreach (TurretTypes a in abl)
                foreach (string p in Policies)
                    jobs.Add(new Job { id = "ablation|" + p + "|" + F(s) + "|" + AllowedName(a), kind = "ablation", policy = p, allowed = a, allowedName = AllowedName(a), scale = s, seedLo = EvalSeedLo, seedHi = EvalSeedHi });
        return jobs;
    }

    private static void Fill(Cell c)
    {
        int n = c.episodes.Count;
        c.n = n;
        int wins = 0;
        var lives = new double[n];
        var waves = new double[n];
        var ret = new double[n];
        double spent = 0, st = 0, mi = 0, la = 0, sim = 0;
        for (int i = 0; i < n; i++)
        {
            EpRec e = c.episodes[i];
            if (e.win) wins++;
            lives[i] = e.lives;
            waves[i] = e.wavesCleared;
            ret[i] = e.ret;
            spent += e.spent;
            st += e.nStandard;
            mi += e.nMissile;
            la += e.nLaser;
            sim += e.sim;
        }
        c.winRate = n > 0 ? (double)wins / n : 0;
        double lo, hi;
        EvalStats.Wilson(wins, n, out lo, out hi);
        c.wilsonLo = lo;
        c.wilsonHi = hi;
        c.meanLives = EvalStats.Mean(lives);
        c.seLives = EvalStats.StdError(lives);
        c.meanWaves = EvalStats.Mean(waves);
        c.seWaves = EvalStats.StdError(waves);
        c.meanReturn = EvalStats.Mean(ret);
        c.seReturn = EvalStats.StdError(ret);
        if (n > 0)
        {
            c.meanSpent = spent / n;
            c.meanStandard = st / n;
            c.meanMissile = mi / n;
            c.meanLaser = la / n;
            c.meanSim = sim / n;
        }
    }

    private static EpRec ToRec(EpisodeResult r)
    {
        return new EpRec
        {
            seed = r.seed, win = r.win, hitCap = r.hitCap, lives = r.livesLeft, wavesReached = r.wavesReached, wavesCleared = r.wavesCleared,
            nStandard = r.turretsByType[0], nMissile = r.turretsByType[1], nLaser = r.turretsByType[2], spent = r.moneySpent,
            hp = r.hpMultiplier, sim = r.simSeconds, ret = r.Return,
        };
    }

    private static string ChooseTuned(Ckpt ck)
    {
        var scores = new double[HeuristicPolicy.VariantNames.Length];
        for (int i = 0; i < scores.Length; i++)
        {
            string id = "select|" + HeuristicPolicy.VariantNames[i];
            Cell c = ck.cells.Find(x => x.id == id);
            Assert.IsNotNull(c, "missing selection cell " + id);
            scores[i] = c.meanReturn;
        }
        return HeuristicPolicy.VariantNames[EvalStats.SelectBest(scores)];
    }

    // ------------------------------------------------------------------ driver

    [UnityTest]
    [Category("Evaluation")]
    [Timeout(3600000)]
    public IEnumerator Evaluation()
    {
        if (Environment.GetEnvironmentVariable("TD_EVAL") != "1") Assert.Ignore("TD_EVAL != 1");

        double budget = double.Parse(Env("TD_BUDGET", "420"), Inv);
        bool resume = Environment.GetEnvironmentVariable("TD_RESUME") == "1";

        // Seed separation: evaluation seeds never overlap training seeds; selection uses training seeds only.
        Assert.Less(EvalSeedHi, TrainSeedLo, "eval seeds overlap training seeds");
        Assert.GreaterOrEqual(SelectSeedLo, TrainSeedLo);
        Assert.LessOrEqual(SelectSeedHi, TrainSeedHi);
        Assert.Less(EvalSeedHi, SelectSeedLo);

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string ckDir = Path.Combine(root, "Library", "TdEval");
        string ckPath = Path.Combine(ckDir, "checkpoint.json");
        string outDir = Path.Combine(root, "docs", "evaluation");
        Directory.CreateDirectory(ckDir);
        Directory.CreateDirectory(outDir);

        Ckpt ck = (resume && File.Exists(ckPath)) ? JsonUtility.FromJson<Ckpt>(File.ReadAllText(ckPath)) : new Ckpt();
        if (resume) Debug.Log("EVAL resume with " + ck.cells.Count + " cells done");

        PolicyMetadata meta;
        MlpPolicy trained = PolicyIO.LoadResource("builder_policy", out meta);
        Assert.IsNotNull(trained, "Resources/builder_policy.json missing");

        List<Job> jobs = BuildJobs();
        var sw = Stopwatch.StartNew();
        double wallBase = ck.wallSeconds;

        foreach (Job job in jobs)
        {
            if (ck.cells.Exists(c => c.id == job.id)) continue;

            if (sw.Elapsed.TotalSeconds > budget)
            {
                ck.wallSeconds = wallBase + sw.Elapsed.TotalSeconds;
                File.WriteAllText(ckPath, JsonUtility.ToJson(ck));
                Debug.Log("EVAL PAUSED: " + ck.cells.Count + " of " + jobs.Count + " cells done. Re-run with TD_RESUME=1");
                Assert.Pass("paused");
                yield break;
            }

            // Resolve the tuned heuristic once all selection cells exist (training seeds only).
            string variant = "";
            if (job.policy == "HeuristicTuned")
            {
                if (string.IsNullOrEmpty(ck.tunedVariant)) ck.tunedVariant = ChooseTuned(ck);
                variant = ck.tunedVariant;
            }
            else if (job.policy.StartsWith("variant:", StringComparison.Ordinal)) variant = job.policy.Substring(8);

            var cell = new Cell { id = job.id, kind = job.kind, policy = job.policy, variant = variant, allowed = job.allowedName, scale = job.scale };
            var cellSw = Stopwatch.StartNew();
            for (int seed = job.seedLo; seed <= job.seedHi; seed++)
            {
                if (job.kind != "select") { Assert.GreaterOrEqual(seed, EvalSeedLo); Assert.LessOrEqual(seed, EvalSeedHi); }
                else { Assert.GreaterOrEqual(seed, SelectSeedLo); Assert.LessOrEqual(seed, SelectSeedHi); }

                IBuilderPolicy pol;
                if (job.policy == "Random") pol = new RandomPolicy(RandomPolicySeedOffset + seed);
                else if (job.policy == "Heuristic") pol = new HeuristicPolicy();
                else if (job.policy == "Trained") pol = trained;
                else pol = HeuristicPolicy.Variant(variant);

                var cfg = new EpisodeConfig { HealthMultiplier = job.scale, RelativeJitter = RelJitter, Allowed = job.allowed, SimCap = 600f };
                EpisodeResult r = null;
                yield return EpisodeRunner.Run(pol, seed, cfg, x => r = x);
                Assert.IsNotNull(r);
                cell.episodes.Add(ToRec(r));
            }
            cell.wallSeconds = cellSw.Elapsed.TotalSeconds;
            Fill(cell);
            ck.cells.Add(cell);
            if (job.kind == "select" && !string.IsNullOrEmpty(ck.tunedVariant)) Assert.Fail("selection after tuned variant fixed");
            ck.wallSeconds = wallBase + sw.Elapsed.TotalSeconds;
            File.WriteAllText(ckPath, JsonUtility.ToJson(ck));
            Debug.Log("EVAL cell " + ck.cells.Count + "/" + jobs.Count + " " + job.id + " win=" + F(cell.winRate) + " R=" + F(cell.meanReturn)
                + " wall=" + F(cell.wallSeconds, "0.0"));
        }

        // All cells done: write outputs.
        ck.wallSeconds = wallBase + sw.Elapsed.TotalSeconds;
        File.WriteAllText(ckPath, JsonUtility.ToJson(ck));
        WriteOutputs(ck, meta, outDir);
        Assert.Pass();
    }

    // ------------------------------------------------------------------ report

    private static void WriteOutputs(Ckpt ck, PolicyMetadata meta, string outDir)
    {
        var rep = new Report
        {
            unity = Application.unityVersion,
            planCommit = Env("TD_PLAN_COMMIT", "unknown"),
            generatedUtc = DateTime.UtcNow.ToString("o"),
            tunedVariant = ck.tunedVariant,
            policyMetadata = JsonUtility.ToJson(meta),
            evalSeeds = EvalSeedLo + "-" + EvalSeedHi,
            selectionSeeds = SelectSeedLo + "-" + SelectSeedHi,
            trainSeeds = TrainSeedLo + "-" + TrainSeedHi,
            relativeHpJitter = RelJitter,
            selectionScale = SelectScale,
            totalWallSeconds = ck.wallSeconds,
            notes = "HP multiplier of an episode = scale * (1 + u), u uniform in [-0.05, 0.05] drawn from the episode seed. "
                + "Trained is masked in the ablation (out-of-distribution for it). Win-rate CIs are Wilson 95%.",
        };
        foreach (Cell c in ck.cells)
        {
            if (c.kind == "select") rep.selection.Add(c);
            else rep.cells.Add(c);
        }
        File.WriteAllText(Path.Combine(outDir, "builder-eval.json"), JsonUtility.ToJson(rep, true), new UTF8Encoding(false));

        var md = new StringBuilder();
        md.AppendLine("# Builder agent evaluation (balance tester)");
        md.AppendLine();
        md.AppendLine("<!-- Tables below are generated by AiEvalTests; the Findings section is written by hand from them. -->");
        md.AppendLine();
        md.AppendLine("## Setup");
        md.AppendLine();
        md.AppendLine("- Unity " + rep.unity + "; plan/source commit `" + rep.planCommit + "`; generated " + rep.generatedUtc + ".");
        md.AppendLine("- Evaluation seeds: " + rep.evalSeeds + " (N = 30 per cell). Training seeds: " + rep.trainSeeds
            + ". Tuned-baseline selection seeds: " + rep.selectionSeeds + " (training range; never evaluation seeds). Disjointness asserted in the driver.");
        md.AppendLine("- Enemy HP of an episode = scale x (1 + u), u uniform in [-0.05, +0.05] drawn deterministically from the episode seed (so even the deterministic heuristic sees 30 different HP values per cell).");
        md.AppendLine("- Trained policy: `Assets/Resources/builder_policy.json` loaded with `PolicyIO.LoadResource`; metadata: `" + rep.policyMetadata + "`.");
        md.AppendLine("- Random policy RNG seed = 5000 + episode seed. Return R = 100*win + 10*livesLeft + 5*wavesCleared (max 100 + 10*lives + 5*waves).");
        md.AppendLine("- Total wall time of the matrix: " + F(rep.totalWallSeconds / 60.0, "0.0") + " min (sum of work across invocations; per-cell wall seconds are in the JSON).");
        md.AppendLine("- Win-rate interval: Wilson 95%. Lives / waves / return: mean +- standard error (sample SD / sqrt(N)).");
        md.AppendLine();

        md.AppendLine("## HeuristicTuned selection (training seeds only)");
        md.AppendLine();
        md.AppendLine("Variants run on seeds " + rep.selectionSeeds + " at HP x" + F(SelectScale) + " (same +-5% jitter), all turret types; the variant with the highest mean return wins, ties go to the earlier row.");
        md.AppendLine();
        md.AppendLine("| Variant | N | Win rate | Mean return +- SE | Mean lives |");
        md.AppendLine("|---|---|---|---|---|");
        foreach (Cell c in rep.selection)
            md.AppendLine("| " + c.variant + (c.variant == ck.tunedVariant ? " (chosen)" : "") + " | " + c.n + " | " + F(c.winRate)
                + " | " + F(c.meanReturn, "0.0") + " +- " + F(c.seReturn, "0.0") + " | " + F(c.meanLives, "0.0") + " |");
        md.AppendLine();
        md.AppendLine("Chosen variant: **" + ck.tunedVariant + "**.");
        md.AppendLine();

        md.AppendLine("## 1. HP sweep (all turret types allowed)");
        md.AppendLine();
        AppendTable(md, rep.cells, "sweep");
        md.AppendLine();
        md.AppendLine("### Trained versus baselines per HP scale");
        md.AppendLine();
        md.AppendLine("Verdict rule: win rates compared by Wilson 95% intervals, return by mean +- 1.96 SE; a difference is only called when the intervals do not overlap.");
        md.AppendLine();
        md.AppendLine("| HP x | Baseline | Win: Trained vs baseline | Win verdict | Return: Trained vs baseline | Return verdict |");
        md.AppendLine("|---|---|---|---|---|---|");
        foreach (float s in SweepScales)
        {
            Cell t = Find(rep.cells, "sweep", "Trained", s, "all");
            foreach (string b in new[] { "Random", "Heuristic", "HeuristicTuned" })
            {
                Cell o = Find(rep.cells, "sweep", b, s, "all");
                md.AppendLine("| " + F(s) + " | " + b + " | " + F(t.winRate) + " vs " + F(o.winRate) + " | " + Verdict(t.wilsonLo, t.wilsonHi, o.wilsonLo, o.wilsonHi, t.winRate, o.winRate)
                    + " | " + F(t.meanReturn, "0.0") + " vs " + F(o.meanReturn, "0.0") + " | "
                    + Verdict(t.meanReturn - 1.96 * t.seReturn, t.meanReturn + 1.96 * t.seReturn, o.meanReturn - 1.96 * o.seReturn, o.meanReturn + 1.96 * o.seReturn, t.meanReturn, o.meanReturn) + " |");
            }
        }
        md.AppendLine();

        md.AppendLine("## 2. Turret-type ablation (only the listed type may be built)");
        md.AppendLine();
        md.AppendLine("Note: the Trained policy was trained with all types allowed; here its actions are masked, which is out-of-distribution for it. "
            + "Heuristic variants fall back to the first allowed type.");
        md.AppendLine();
        AppendTable(md, rep.cells, "ablation");
        md.AppendLine();
        File.WriteAllText(Path.Combine(outDir, "builder-eval.md"), md.ToString(), new UTF8Encoding(false));
    }

    private static Cell Find(List<Cell> cells, string kind, string policy, float scale, string allowed)
    {
        return cells.Find(c => c.kind == kind && c.policy == policy && Math.Abs(c.scale - scale) < 1e-4f && c.allowed == allowed);
    }

    private static string Verdict(double aLo, double aHi, double bLo, double bHi, double aMean, double bMean)
    {
        if (EvalStats.Overlap(aLo, aHi, bLo, bHi)) return "indistinguishable (CIs overlap)";
        return aMean > bMean ? "Trained better" : "Trained worse";
    }

    private static void AppendTable(StringBuilder md, List<Cell> cells, string kind)
    {
        md.AppendLine("| HP x | Allowed | Policy | N | Win rate [Wilson 95%] | Lives | Waves cleared | Return | Money spent | Turrets std/missile/laser | Wall s |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (Cell c in cells)
        {
            if (c.kind != kind) continue;
            string pol = c.policy == "HeuristicTuned" ? "HeuristicTuned(" + c.variant + ")" : c.policy;
            md.AppendLine("| " + F(c.scale) + " | " + c.allowed + " | " + pol + " | " + c.n + " | " + F(c.winRate) + " [" + F(c.wilsonLo) + ", " + F(c.wilsonHi) + "] | "
                + F(c.meanLives, "0.00") + " +- " + F(c.seLives, "0.00") + " | " + F(c.meanWaves, "0.00") + " +- " + F(c.seWaves, "0.00") + " | "
                + F(c.meanReturn, "0.0") + " +- " + F(c.seReturn, "0.0") + " | " + F(c.meanSpent, "0") + " | "
                + F(c.meanStandard, "0.0") + " / " + F(c.meanMissile, "0.0") + " / " + F(c.meanLaser, "0.0") + " | " + F(c.wallSeconds, "0") + " |");
        }
    }
}
