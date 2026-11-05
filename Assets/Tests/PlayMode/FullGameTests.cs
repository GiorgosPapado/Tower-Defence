using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

public class FullGameTests
{
    [TearDown]
    public void ResetTime()
    {
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator Without_Turrets_The_Game_Is_Lost()
    {
        yield return SceneManager.LoadSceneAsync("Main");
        yield return null;

        Time.timeScale = 15f;
        float elapsed = 0f;
        while (!GameManager.gameEnded && elapsed < 240f)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Assert.IsTrue(GameManager.gameEnded, "Game never ended with no defence");
        Assert.LessOrEqual(PlayerStats.Lives, 0, "With no turrets the player must lose");
    }

    [UnityTest]
    public IEnumerator Main_Scene_Plays_To_An_End_State_Without_Errors()
    {
        yield return SceneManager.LoadSceneAsync("Main");
        yield return null;
        yield return null;

        var shop = Object.FindFirstObjectByType<Shop>();
        var spawner = Object.FindFirstObjectByType<WaveSpawner>();
        Assert.IsNotNull(shop, "Shop missing in scene");
        Assert.IsNotNull(spawner, "WaveSpawner missing in scene");
        Assert.Greater(spawner.TotalWaves, 0, "No waves assigned");

        // Nodes sorted by distance to the path: the best spots first.
        var nodes = new List<Node>(Object.FindObjectsByType<Node>(FindObjectsSortMode.None));
        Assert.Greater(nodes.Count, 10, "Expected build nodes");
        nodes.Sort((a, b) => PathDistance(a).CompareTo(PathDistance(b)));

        int next = 0;
        Time.timeScale = 15f;
        float elapsed = 0f;

        while (!GameManager.gameEnded && elapsed < 240f)
        {
            // Spend money as it comes in: laser, then missile, then standard.
            while (next < nodes.Count)
            {
                TurretBlueprint pick = null;
                if (PlayerStats.Money >= shop.standardTurret.cost) pick = shop.standardTurret;
                if (next % 4 == 1 && PlayerStats.Money >= shop.missileLauncher.cost) pick = shop.missileLauncher;
                if (next % 4 == 3 && PlayerStats.Money >= shop.LaserLauncher.cost) pick = shop.LaserLauncher;
                if (pick == null) break;

                BuildManager.instance.SelectTurretToBuild(pick);
                BuildManager.instance.BuildTurretOn(nodes[next]);
                next++;
            }

            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        Time.timeScale = 1f;
        Debug.Log("RESULT rounds=" + PlayerStats.Rounds + " lives=" + PlayerStats.Lives + " turrets=" + next);

        Assert.IsTrue(GameManager.gameEnded, "Game did not reach win or lose within the time limit");
        Assert.Greater(PlayerStats.Lives, 0, "A competent bot should win; the game is mis-balanced or broken");
        Assert.GreaterOrEqual(PlayerStats.Rounds, 1, "No wave was spawned");
        Assert.Greater(next, 0, "No turret was built");
        Debug.Log("RESULT rounds=" + PlayerStats.Rounds + " lives=" + PlayerStats.Lives + " turrets=" + next);
    }

    private static float PathDistance(Node n)
    {
        float best = float.MaxValue;
        foreach (Transform p in WayPoints.points)
        {
            float d = Vector3.Distance(n.transform.position, p.position);
            if (d < best) best = d;
        }
        return best;
    }
}
