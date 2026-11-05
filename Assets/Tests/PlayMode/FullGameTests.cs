using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class FullGameTests
{
    [TearDown]
    public void ResetTime()
    {
        SimClock.ForceRestore();
        Difficulty.Reset();
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator Without_Turrets_The_Game_Is_Lost()
    {
        yield return SceneManager.LoadSceneAsync("Main");
        yield return null;

        SimClock.Begin();   // fixed 1/30 s frames x4: deterministic and fast
        float elapsed = 0f;
        while (!GameManager.gameEnded && elapsed < 600f)
        {
            elapsed += Time.deltaTime;   // simulated seconds
            yield return null;
        }
        SimClock.End();

        Assert.IsTrue(GameManager.gameEnded, "Game never ended with no defence");
        Assert.LessOrEqual(PlayerStats.Lives, 0, "With no turrets the player must lose");
    }

    // A competent policy must be able to win the unmodified game. Uses the same deterministic
    // episode runner as training and evaluation, so the result does not depend on frame timing.
    [UnityTest]
    public IEnumerator Heuristic_Policy_Wins_The_Default_Game()
    {
        EpisodeResult r = null;
        yield return EpisodeRunner.Run(new HeuristicPolicy(), 1, new EpisodeConfig { HealthMultiplier = 1f }, x => r = x);

        Assert.IsNotNull(r, "Episode did not complete");
        Assert.IsFalse(r.hitCap, "Episode hit the simulation cap");
        Assert.IsTrue(r.win, "A competent policy should win the default game; it is mis-balanced or broken");
        Assert.Greater(r.livesLeft, 0);
        Assert.Greater(r.turretsByType[0] + r.turretsByType[1] + r.turretsByType[2], 0, "No turret was built");
        Debug.Log("RESULT win=" + r.win + " lives=" + r.livesLeft + " waves=" + r.wavesReached);
    }
}
