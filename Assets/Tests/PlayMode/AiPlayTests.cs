using System.Collections;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

public class AiPlayTests
{
    private const float Cap = 600f;

    [TearDown]
    public void Restore()
    {
        SimClock.ForceRestore();
        Difficulty.Reset();
        Time.timeScale = 1f;
    }

    private static EpisodeConfig Cfg()
    {
        return new EpisodeConfig { HealthMultiplier = 1f, SimCap = Cap };
    }

    private static void AssertClockRestored()
    {
        Assert.AreEqual(0f, Time.captureDeltaTime, "captureDeltaTime left changed");
        Assert.AreEqual(1f, Time.timeScale, "timeScale left changed");
        Assert.AreEqual(1f, Difficulty.HealthMultiplier, "Difficulty left changed");
        Assert.IsFalse(SimClock.IsActive, "SimClock still active");
    }

    [UnityTest]
    public IEnumerator Same_Policy_And_Seed_Give_Identical_Results()
    {
        EpisodeResult a = null, b = null;
        yield return EpisodeRunner.Run(new HeuristicPolicy(), 7, Cfg(), r => a = r);
        yield return EpisodeRunner.Run(new HeuristicPolicy(), 7, Cfg(), r => b = r);

        Assert.IsNotNull(a);
        Assert.IsNotNull(b);
        Assert.AreEqual(a, b, "Episodes differ:\nA: " + a + "\nB: " + b);
        AssertClockRestored();
    }

    [UnityTest]
    public IEnumerator Heuristic_Episode_Terminates_Within_Cap()
    {
        EpisodeResult r = null;
        yield return EpisodeRunner.Run(new HeuristicPolicy(), 1, Cfg(), x => r = x);

        Assert.IsNotNull(r);
        Debug.Log("EPISODE " + r);
        Assert.IsFalse(r.hitCap, "Episode hit the sim cap");
        Assert.LessOrEqual(r.simSeconds, Cap);
        Assert.GreaterOrEqual(r.wavesReached, 1);
        Assert.Greater(r.turretsByType[0] + r.turretsByType[1] + r.turretsByType[2], 0, "Nothing built");
        AssertClockRestored();
    }

    [UnityTest]
    public IEnumerator Random_And_Heuristic_Both_Terminate_And_State_Is_Restored()
    {
        EpisodeResult rnd = null, heu = null;
        yield return EpisodeRunner.Run(new RandomPolicy(3), 3, Cfg(), x => rnd = x);
        AssertClockRestored();
        yield return EpisodeRunner.Run(new HeuristicPolicy(), 3, Cfg(), x => heu = x);

        Assert.IsNotNull(rnd);
        Assert.IsNotNull(heu);
        Debug.Log("EPISODE " + rnd);
        Debug.Log("EPISODE " + heu);
        Assert.LessOrEqual(rnd.simSeconds, Cap);
        Assert.LessOrEqual(heu.simSeconds, Cap);
        Assert.GreaterOrEqual(rnd.wavesReached, 1);
        Assert.GreaterOrEqual(heu.wavesReached, 1);
        AssertClockRestored();
    }

    [UnityTest]
    public IEnumerator Measure_Episode_Cost()
    {
        double wallTotal = 0, simTotal = 0;
        for (int i = 0; i < 5; i++)
        {
            EpisodeResult r = null;
            var sw = Stopwatch.StartNew();
            yield return EpisodeRunner.Run(new HeuristicPolicy(), 100 + i, Cfg(), x => r = x);
            sw.Stop();
            Assert.IsNotNull(r);
            wallTotal += sw.Elapsed.TotalSeconds;
            simTotal += r.simSeconds;
            Debug.Log("TIMING episode " + i + " wall=" + sw.Elapsed.TotalSeconds.ToString("F2") + "s sim="
                + r.simSeconds.ToString("F1") + "s " + r);
        }
        Debug.Log("TIMING mean wall=" + (wallTotal / 5).ToString("F2") + "s mean sim=" + (simTotal / 5).ToString("F1") + "s");
        AssertClockRestored();
    }

    private static EpisodeConfig JitterCfg()
    {
        return new EpisodeConfig { HealthMultiplier = 1f, RelativeJitter = 0.05f, SimCap = Cap };
    }

    [UnityTest]
    public IEnumerator Trained_Policy_Is_Deterministic_With_Hp_Jitter()
    {
        PolicyMetadata meta;
        MlpPolicy trained = PolicyIO.LoadResource("builder_policy", out meta);
        Assert.IsNotNull(trained);
        EpisodeResult a = null, b = null;
        yield return EpisodeRunner.Run(trained, 4, JitterCfg(), r => a = r);
        yield return EpisodeRunner.Run(trained, 4, JitterCfg(), r => b = r);
        AssertClockRestored();
        Assert.AreEqual(a, b, "Episodes differ:\nA: " + a + "\nB: " + b);
    }

    [UnityTest]
    public IEnumerator Random_Policy_Is_Deterministic_With_Hp_Jitter()
    {
        EpisodeResult a = null, b = null;
        yield return EpisodeRunner.Run(new RandomPolicy(5004), 4, JitterCfg(), r => a = r);
        yield return EpisodeRunner.Run(new RandomPolicy(5004), 4, JitterCfg(), r => b = r);
        AssertClockRestored();
        Assert.AreEqual(a, b, "Episodes differ:\nA: " + a + "\nB: " + b);
    }
}
