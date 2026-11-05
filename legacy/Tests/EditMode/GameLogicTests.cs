using NUnit.Framework;
using UnityEngine;

public class GameLogicTests
{
    private GameObject Make(Vector3 pos, float health, float distanceToGoal)
    {
        var go = new GameObject("enemy");
        go.transform.position = pos;
        var e = go.AddComponent<Enemies>();
        e.health = health;
        e.distanceToGoal = distanceToGoal;
        EnemyRegistry.Register(e);   // OnEnable does not run for normal components in edit mode
        return go;
    }

    [SetUp]
    public void SetUp()
    {
        EnemyRegistry.Clear();
        PlayerStats.Money = 0;
        PlayerStats.Lives = 10;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (var e in new System.Collections.Generic.List<Enemies>(EnemyRegistry.All))
            if (e != null) Object.DestroyImmediate(e.gameObject);
        EnemyRegistry.Clear();
    }

    [Test]
    public void TargetSelector_Closest_PicksNearest()
    {
        Make(new Vector3(10, 0, 0), 100, 5);
        var near = Make(new Vector3(2, 0, 0), 100, 50);
        var t = TargetSelector.Select(EnemyRegistry.All, Vector3.zero, 15f, TargetPriority.Closest);
        Assert.AreEqual(near.GetComponent<Enemies>(), t);
    }

    [Test]
    public void TargetSelector_First_PicksClosestToGoal()
    {
        Make(new Vector3(2, 0, 0), 100, 50);
        var lead = Make(new Vector3(10, 0, 0), 100, 5);
        var t = TargetSelector.Select(EnemyRegistry.All, Vector3.zero, 15f, TargetPriority.First);
        Assert.AreEqual(lead.GetComponent<Enemies>(), t);
    }

    [Test]
    public void TargetSelector_Strongest_PicksMostHealth()
    {
        Make(new Vector3(2, 0, 0), 50, 1);
        var tank = Make(new Vector3(3, 0, 0), 300, 99);
        var t = TargetSelector.Select(EnemyRegistry.All, Vector3.zero, 15f, TargetPriority.Strongest);
        Assert.AreEqual(tank.GetComponent<Enemies>(), t);
    }

    [Test]
    public void TargetSelector_IgnoresOutOfRange()
    {
        Make(new Vector3(100, 0, 0), 100, 1);
        Assert.IsNull(TargetSelector.Select(EnemyRegistry.All, Vector3.zero, 15f, TargetPriority.Closest));
    }

    [Test]
    public void Enemy_Death_AwardsMoneyOnce()
    {
        var e = Make(Vector3.zero, 100, 1).GetComponent<Enemies>();
        e.value = 30;
        e.TakeDamage(60);
        Assert.AreEqual(0, PlayerStats.Money);
        e.TakeDamage(60);   // kills (DestroyImmediate in edit mode)
        Assert.AreEqual(30, PlayerStats.Money);
    }

    [Test]
    public void Enemy_Slow_ExpiresAfterDuration()
    {
        var e = Make(Vector3.zero, 100, 1).GetComponent<Enemies>();
        e.startSpeed = 10f;
        e.Slow(0.5f, 0.25f);
        e.ApplySlowDecay(0.1f);
        Assert.AreEqual(5f, e.speed, 0.001f);
        e.ApplySlowDecay(0.3f);   // timer runs out
        e.ApplySlowDecay(0.1f);
        Assert.AreEqual(10f, e.speed, 0.001f);
    }

    [Test]
    public void WaveDefinition_CountsEnemies()
    {
        var w = ScriptableObject.CreateInstance<WaveDefinition>();
        w.groups = new[]
        {
            new WaveDefinition.Group { count = 3 },
            new WaveDefinition.Group { count = 5 },
        };
        Assert.AreEqual(8, w.TotalEnemies);
        Object.DestroyImmediate(w);
    }
}
