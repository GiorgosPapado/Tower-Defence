using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Settings of one simulated episode.</summary>
public sealed class EpisodeConfig
{
    public string SceneName = "Main";
    public TurretTypes Allowed = TurretTypes.All;

    /// <summary>Fixed enemy-health multiplier (used when JitterHp is false).</summary>
    public float HealthMultiplier = 1f;
    /// <summary>If true, the multiplier is Difficulty.JitterFromSeed(seed, JitterLo, JitterHi).</summary>
    public bool JitterHp;
    public float JitterLo = 0.85f;
    public float JitterHi = 1.25f;
    /// <summary>Extra seed-dependent relative jitter: multiplier *= (1 + u), u uniform in [-RelativeJitter, +RelativeJitter]. 0 = off.</summary>
    public float RelativeJitter;

    /// <summary>Simulated seconds between two policy decisions.</summary>
    public float DecisionInterval = 1f;
    /// <summary>Simulated seconds after which the episode is counted as a loss.</summary>
    public float SimCap = 600f;
}

/// <summary>Outcome of one episode.</summary>
public sealed class EpisodeResult : IEquatable<EpisodeResult>
{
    public int seed;
    public string policyName;
    public bool win;
    public int livesLeft;
    public int wavesReached;
    public int wavesCleared;
    public int[] turretsByType = new int[BuilderSpec.TypeCount];
    public int moneySpent;
    public float simSeconds;
    public bool hitCap;
    public float hpMultiplier;

    /// <summary>R = 100*win + 10*livesLeft + 5*wavesCleared.</summary>
    public float Return
    {
        get { return 100f * (win ? 1 : 0) + 10f * livesLeft + 5f * wavesCleared; }
    }

    public bool Equals(EpisodeResult o)
    {
        if (o == null) return false;
        if (seed != o.seed || policyName != o.policyName || win != o.win || livesLeft != o.livesLeft
            || wavesReached != o.wavesReached || wavesCleared != o.wavesCleared || moneySpent != o.moneySpent
            || simSeconds != o.simSeconds || hitCap != o.hitCap || hpMultiplier != o.hpMultiplier)
            return false;
        for (int i = 0; i < turretsByType.Length; i++)
            if (turretsByType[i] != o.turretsByType[i]) return false;
        return true;
    }

    public override bool Equals(object obj) { return Equals(obj as EpisodeResult); }
    public override int GetHashCode() { return seed; }

    public override string ToString()
    {
        return "seed=" + seed + " policy=" + policyName + " win=" + win + " lives=" + livesLeft
            + " reached=" + wavesReached + " cleared=" + wavesCleared
            + " turrets=[" + turretsByType[0] + "," + turretsByType[1] + "," + turretsByType[2] + "]"
            + " spent=" + moneySpent + " sim=" + simSeconds.ToString("F2") + " cap=" + hitCap
            + " hp=" + hpMultiplier.ToString("F3") + " R=" + Return;
    }
}

/// <summary>Runs one headless, fixed-step episode of the Main scene with a builder policy (use from PlayMode tests).</summary>
public static class EpisodeRunner
{
    public static IEnumerator Run(IBuilderPolicy policy, int seed, EpisodeConfig cfg, Action<EpisodeResult> done)
    {
        if (policy == null) throw new ArgumentNullException("policy");
        if (cfg == null) cfg = new EpisodeConfig();

        var result = new EpisodeResult { seed = seed, policyName = policy.GetType().Name };
        result.hpMultiplier = cfg.JitterHp
            ? Difficulty.JitterFromSeed(seed, cfg.JitterLo, cfg.JitterHi)
            : cfg.HealthMultiplier;
        if (cfg.RelativeJitter > 0f)
            result.hpMultiplier *= 1f + Difficulty.RelativeJitterFromSeed(seed, cfg.RelativeJitter);

        BuilderContext ctx = null;
        bool clockOn = false;
        try
        {
            Difficulty.HealthMultiplier = result.hpMultiplier;

            yield return SceneManager.LoadSceneAsync(cfg.SceneName);
            yield return null;   // let Start() run (PlayerStats sets money and lives)
            yield return null;

            ctx = BuilderContext.FromScene();
            ctx.Allowed = cfg.Allowed;
            float[] obs = BuilderObservation.Create();
            bool[] mask = BuilderActions.CreateMask();

            SimClock.Begin();
            clockOn = true;

            float elapsed = 0f;
            float sinceDecision = cfg.DecisionInterval;   // decide on the first frame
            while (!GameManager.gameEnded && elapsed < cfg.SimCap)
            {
                if (sinceDecision >= cfg.DecisionInterval)
                {
                    sinceDecision -= cfg.DecisionInterval;
                    Decide(policy, ctx, obs, mask, result);
                }
                float dt = Time.deltaTime;
                elapsed += dt;
                sinceDecision += dt;
                yield return null;
            }

            result.simSeconds = elapsed;
            result.hitCap = !GameManager.gameEnded;
            result.livesLeft = Mathf.Max(0, PlayerStats.Lives);
            WaveSpawner sp = ctx.Spawner;
            int total = (sp != null) ? sp.TotalWaves : 0;
            result.wavesReached = (sp != null) ? sp.WaveNumber : PlayerStats.Rounds;
            result.win = !result.hitCap && PlayerStats.Lives > 0;
            // The wave in progress is not cleared unless the game was won.
            result.wavesCleared = result.win ? total : Mathf.Max(0, result.wavesReached - 1);
        }
        finally
        {
            if (clockOn) SimClock.End();
            Difficulty.Reset();
            if (BuildManager.instance != null) BuildManager.instance.SelectTurretToBuild(null);
        }

        if (done != null) done(result);
    }

    private static void Decide(IBuilderPolicy policy, BuilderContext ctx, float[] obs, bool[] mask, EpisodeResult result)
    {
        if (BuildManager.instance == null) return;
        BuilderObservation.Build(ctx, obs);
        BuilderActions.ComputeMask(ctx, mask);
        int action = policy.Act(obs, mask);

        int slot, type;
        if (!BuilderSpec.Decode(action, out slot, out type)) return;
        int cost = ctx.Cost(type);
        if (BuilderActions.Apply(ctx, action))
        {
            result.turretsByType[type]++;
            result.moneySpent += cost;
        }
    }
}
