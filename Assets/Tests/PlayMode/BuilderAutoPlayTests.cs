using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

public class BuilderAutoPlayTests
{
    [TearDown]
    public void Restore()
    {
        SimClock.ForceRestore();
        Difficulty.Reset();
        Time.timeScale = 1f;
    }

    [UnityTest]
    public IEnumerator Main_Has_One_Controller_And_Wired_Toggle_Button()
    {
        yield return SceneManager.LoadSceneAsync("Main");
        yield return null;

        var controllers = Object.FindObjectsByType<BuilderController>(FindObjectsSortMode.None);
        Assert.AreEqual(1, controllers.Length);

        bool wired = false;
        foreach (Button b in Object.FindObjectsByType<Button>(FindObjectsSortMode.None))
        {
            for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                if (b.onClick.GetPersistentTarget(i) == controllers[0] && b.onClick.GetPersistentMethodName(i) == "Toggle")
                    wired = true;
        }
        Assert.IsTrue(wired, "No button has a persistent Toggle listener on the BuilderController");
        Assert.IsNotNull(controllers[0].ShownLabel, "labelText not assigned");
        Assert.AreEqual("AI: OFF", controllers[0].ShownLabel);
    }

    [UnityTest]
    public IEnumerator AutoPlay_Plays_A_Full_Game_With_The_Trained_Policy()
    {
        yield return SceneManager.LoadSceneAsync("Main");
        yield return null;
        yield return null;

        var ctl = Object.FindFirstObjectByType<BuilderController>();
        Assert.IsNotNull(ctl);
        ctl.Enabled = true;
        Assert.AreEqual("AI: ON", ctl.ShownLabel);

        SimClock.Begin();
        float elapsed = 0f;
        while (!GameManager.gameEnded && elapsed < 600f)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        SimClock.End();

        int turrets = Object.FindObjectsByType<Turret>(FindObjectsSortMode.None).Length;
        Debug.Log("AUTOPLAY policy=" + ctl.PolicyName + " ended=" + GameManager.gameEnded + " lives=" + PlayerStats.Lives
            + " turrets=" + turrets + " sim=" + elapsed.ToString("F1"));

        Assert.AreEqual("MlpPolicy", ctl.PolicyName);
        Assert.IsTrue(GameManager.gameEnded, "Game did not end within 600 sim s");
        Assert.Greater(turrets, 0, "Controller built nothing");
        Assert.Greater(PlayerStats.Lives, 0, "Trained policy lost the game at default HP");
        Assert.IsFalse(SimClock.IsActive);
    }
}
