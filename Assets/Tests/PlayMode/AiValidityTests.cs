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
/// Env-gated validity checks of the evaluation. Each Assert.Ignore()s unless its env var is set:
/// TD_RT=1 (category "RealTimeCheck"): finer step (1/60 s per frame) vs the coarse evaluation step.
/// TD_SPOT=1 (category "EvalSpotCheck"): re-run a few evaluation episodes and compare with builder-eval.json.
/// TD_BUDGET = wall seconds per invocation (default 420), TD_RESUME=1 continues the real-time check.
/// </summary>
public class AiValidityTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const float RelJitter = 0.05f;
    private const string Tuned = "standard";

    [TearDown]
    public void Restore()
    {
        SimClock.ForceRestore();
        Difficulty.Reset();
        Time.timeScale = 1f;
    }

    [Serializable]
    private class Ep
    {
        public int seed;
        public bool win;
        public int lives, wavesCleared;
        public float hp, sim, ret;
    }

    [Serializable]
    private class CellRec
    {
        public string policy, variant, id;
        public float scale;
        public int n;
        public double winRate, meanLives, meanReturn, meanSim, wallSeconds;
        public List<Ep> episodes = new List<Ep>();
    }

    [Serializable]
    private class Store
    {
        public string note;
        public double wallSeconds;
        public List<CellRec> cells = new List<CellRec>();
    }

    private static string F(double v, string fmt = "0.###") { return v.ToString(fmt, Inv); }

    private static string Root() { return Path.GetFullPath(Path.Combine(Application.dataPath, "..")); }

    private static Ep ToEp(EpisodeResult r)
    {
        return new Ep { seed = r.seed, win = r.win, lives = r.livesLeft, wavesCleared = r.wavesCleared, hp = r.hpMultiplier, sim = r.simSeconds, ret = r.Return };
    }

    private static void Fill(CellRec c)
    {
        c.n = c.episodes.Count;
        double w = 0, l = 0, r = 0, s = 0;
        foreach (Ep e in c.episodes) { if (e.win) w++; l += e.lives; r += e.ret; s += e.sim; }
        if (c.n > 0) { c.winRate = w / c.n; c.meanLives = l / c.n; c.meanReturn = r / c.n; c.meanSim = s / c.n; }
    }

    private static Store LoadEval()
    {
        string path = Path.Combine(Root(), "docs", "evaluation", "builder-eval.json");
        Assert.IsTrue(File.Exists(path), "builder-eval.json missing");
        return JsonUtility.FromJson<Store>(File.ReadAllText(path));
    }

    private static CellRec FindEval(Store eval, string policy, float scale)
    {
        return eval.cells.Find(c => c.id == "sweep|" + policy + "|" + F(scale) + "|all");
    }

    private static IBuilderPolicy MakePolicy(string name, MlpPolicy trained)
    {
        return name == "Trained" ? (IBuilderPolicy)trained : HeuristicPolicy.Variant(Tuned);
    }

    // ------------------------------------------------------------------ real-time check

    [UnityTest]
    [Category("RealTimeCheck")]
    [Timeout(3600000)]
    public IEnumerator RealTimeCheck()
    {
        if (Environment.GetEnvironmentVariable("TD_RT") != "1") Assert.Ignore("TD_RT != 1");

        string v = Environment.GetEnvironmentVariable("TD_BUDGET");
        double budget = string.IsNullOrEmpty(v) ? 420 : double.Parse(v, Inv);
        bool resume = Environment.GetEnvironmentVariable("TD_RESUME") == "1";

        string ckDir = Path.Combine(Root(), "Library", "TdEval");
        string ckPath = Path.Combine(ckDir, "rt-checkpoint.json");
        string outDir = Path.Combine(Root(), "docs", "evaluation");
        Directory.CreateDirectory(ckDir);
        Directory.CreateDirectory(outDir);

        Store ck = (resume && File.Exists(ckPath)) ? JsonUtility.FromJson<Store>(File.ReadAllText(ckPath)) : new Store();
        PolicyMetadata meta;
        MlpPolicy trained = PolicyIO.LoadResource("builder_policy", out meta);
        Assert.IsNotNull(trained);

        string[] policies = { "Trained", "HeuristicTuned" };
        float[] scales = { 1.0f, 1.4f };
        var sw = Stopwatch.StartNew();
        double wallBase = ck.wallSeconds;

        foreach (float scale in scales)
            foreach (string pol in policies)
            {
                string id = pol + "|" + F(scale);
                if (ck.cells.Exists(c => c.id == id)) continue;
                if (sw.Elapsed.TotalSeconds > budget)
                {
                    ck.wallSeconds = wallBase + sw.Elapsed.TotalSeconds;
                    File.WriteAllText(ckPath, JsonUtility.ToJson(ck));
                    Debug.Log("RT PAUSED: " + ck.cells.Count + " of 4 cells done. Re-run with TD_RESUME=1");
                    Assert.Pass("paused");
                    yield break;
                }
                var cell = new CellRec { id = id, policy = pol, variant = pol == "Trained" ? "" : Tuned, scale = scale };
                var cs = Stopwatch.StartNew();
                for (int seed = 1; seed <= 10; seed++)
                {
                    var cfg = new EpisodeConfig
                    {
                        HealthMultiplier = scale, RelativeJitter = RelJitter, SimCap = 600f,
                        CaptureDeltaTime = 1f / 60f, TimeScale = 1f,
                    };
                    EpisodeResult r = null;
                    yield return EpisodeRunner.Run(MakePolicy(pol, trained), seed, cfg, x => r = x);
                    Assert.IsNotNull(r);
                    cell.episodes.Add(ToEp(r));
                }
                cell.wallSeconds = cs.Elapsed.TotalSeconds;
                Fill(cell);
                ck.cells.Add(cell);
                ck.wallSeconds = wallBase + sw.Elapsed.TotalSeconds;
                File.WriteAllText(ckPath, JsonUtility.ToJson(ck));
                Debug.Log("RT cell " + id + " win=" + F(cell.winRate) + " R=" + F(cell.meanReturn) + " wall=" + F(cell.wallSeconds, "0.0"));
            }

        WriteRealTime(ck, outDir);
        Assert.Pass();
    }

    private static void WriteRealTime(Store ck, string outDir)
    {
        Store eval = LoadEval();
        ck.note = "Fine step: captureDeltaTime 1/60 s, timeScale 1 (0.0167 game s per frame). Decision interval 1.0 s game time. "
            + "Seeds 1-10, RelativeJitter 0.05. Coarse = evaluation step 1/30 s x 4 = 0.133 s per frame (same seeds 1-10 from builder-eval.json).";
        File.WriteAllText(Path.Combine(outDir, "realtime-check.json"), JsonUtility.ToJson(ck, true), new UTF8Encoding(false));

        var md = new StringBuilder();
        md.AppendLine("# Real-time (fine step) check of the evaluation");
        md.AppendLine();
        md.AppendLine("Generated by `AiValidityTests.RealTimeCheck` (env `TD_RT=1`). Unity " + Application.unityVersion + ", " + DateTime.UtcNow.ToString("o") + ".");
        md.AppendLine();
        md.AppendLine("- Fine step: 1/60 s per frame (captureDeltaTime 1/60, timeScale 1), 0.0167 game s per frame; decision interval stays 1.0 s of game time.");
        md.AppendLine("- Coarse step (evaluation): 0.133 game s per frame (1/30 x 4). Coarse columns use the same eval seeds 1-10 taken from `builder-eval.json`.");
        md.AppendLine("- HeuristicTuned = the `" + Tuned + "` variant (standard turrets only). Seeds 1-10, HP jitter +-5%. N = 10 per cell: small, treat differences with caution.");
        md.AppendLine();
        md.AppendLine("| HP x | Policy | N | Win rate (fine) | Mean lives (fine) | Mean return (fine) | Win rate (coarse) | Mean lives (coarse) | Mean return (coarse) |");
        md.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (CellRec c in ck.cells)
        {
            CellRec e = FindEval(eval, c.policy, c.scale);
            var coarse = new CellRec();
            if (e != null) foreach (Ep ep in e.episodes) if (ep.seed >= 1 && ep.seed <= 10) coarse.episodes.Add(ep);
            Fill(coarse);
            string pol = c.policy == "HeuristicTuned" ? "HeuristicTuned(" + Tuned + ")" : c.policy;
            md.AppendLine("| " + F(c.scale) + " | " + pol + " | " + c.n + " | " + F(c.winRate) + " | " + F(c.meanLives, "0.00") + " | " + F(c.meanReturn, "0.0")
                + " | " + F(coarse.winRate) + " | " + F(coarse.meanLives, "0.00") + " | " + F(coarse.meanReturn, "0.0") + " |");
        }
        md.AppendLine();
        md.AppendLine("Per-episode records are in `realtime-check.json`.");
        File.WriteAllText(Path.Combine(outDir, "realtime-check.md"), md.ToString(), new UTF8Encoding(false));
    }

    // ------------------------------------------------------------------ spot check

    [UnityTest]
    [Category("EvalSpotCheck")]
    [Timeout(3600000)]
    public IEnumerator EvalSpotCheck()
    {
        if (Environment.GetEnvironmentVariable("TD_SPOT") != "1") Assert.Ignore("TD_SPOT != 1");

        string outDir = Path.Combine(Root(), "docs", "evaluation");
        Directory.CreateDirectory(outDir);
        Store eval = LoadEval();
        PolicyMetadata meta;
        MlpPolicy trained = PolicyIO.LoadResource("builder_policy", out meta);
        Assert.IsNotNull(trained);

        string[] pols = { "Trained", "HeuristicTuned" };
        float[] scales = { 1.4f, 1.3f };
        var md = new StringBuilder();
        int matches = 0, mismatches = 0;
        md.AppendLine("# Evaluation spot check");
        md.AppendLine();
        md.AppendLine("Generated by `AiValidityTests.EvalSpotCheck` (env `TD_SPOT=1`), after the EpisodeRunner clock fix. Same settings as the evaluation (default step, RelativeJitter 0.05, SimCap 600, all types). Compared with the per-episode records in `builder-eval.json`.");
        md.AppendLine();
        md.AppendLine("| Policy | HP x | Seed | Return (now / eval) | Win (now / eval) | Lives (now / eval) | Waves cleared (now / eval) | Match |");
        md.AppendLine("|---|---|---|---|---|---|---|---|");
        for (int k = 0; k < pols.Length; k++)
        {
            string pol = pols[k];
            float scale = scales[k];
            CellRec e = FindEval(eval, pol, scale);
            Assert.IsNotNull(e, "eval cell missing for " + pol);
            for (int seed = 1; seed <= 5; seed++)
            {
                Ep old = e.episodes.Find(x => x.seed == seed);
                Assert.IsNotNull(old);
                var cfg = new EpisodeConfig { HealthMultiplier = scale, RelativeJitter = RelJitter, SimCap = 600f };
                EpisodeResult r = null;
                yield return EpisodeRunner.Run(MakePolicy(pol, trained), seed, cfg, x => r = x);
                Assert.IsNotNull(r);
                bool ok = Math.Abs(r.Return - old.ret) < 1e-3f && r.win == old.win && r.livesLeft == old.lives && r.wavesCleared == old.wavesCleared;
                if (ok) matches++; else mismatches++;
                md.AppendLine("| " + pol + " | " + F(scale) + " | " + seed + " | " + F(r.Return, "0.#") + " / " + F(old.ret, "0.#") + " | " + r.win + " / " + old.win
                    + " | " + r.livesLeft + " / " + old.lives + " | " + r.wavesCleared + " / " + old.wavesCleared + " | " + (ok ? "yes" : "NO") + " |");
            }
        }
        md.AppendLine();
        md.AppendLine("Result: **" + matches + " matches, " + mismatches + " mismatches** of " + (matches + mismatches) + " episodes.");
        File.WriteAllText(Path.Combine(outDir, "spot-check.md"), md.ToString(), new UTF8Encoding(false));
        Debug.Log("SPOT matches=" + matches + " mismatches=" + mismatches);
        Assert.AreEqual(0, mismatches, "Spot check mismatches against builder-eval.json (see docs/evaluation/spot-check.md)");
    }
}
