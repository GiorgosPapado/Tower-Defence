using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = System.Random;

public class AiTests
{
    private readonly List<GameObject> created = new List<GameObject>();
    private List<Node> nodes;
    private Shop shop;
    private WaveSpawner spawner;
    private BuildManager buildManager;

    private GameObject Make(string name)
    {
        var go = new GameObject(name);
        created.Add(go);
        return go;
    }

    private TurretBlueprint MakeBlueprint(string prefabName, int cost)
    {
        var prefab = Make(prefabName);
        var bp = Make("bp_" + prefabName).AddComponent<TurretBlueprint>();
        bp.prefab = prefab;
        bp.cost = cost;
        return bp;
    }

    [SetUp]
    public void SetUp()
    {
        EnemyRegistry.Clear();
        Difficulty.Reset();
        PlayerStats.Money = 1000;
        PlayerStats.Lives = 10;

        // Path: two waypoints, 10 apart.
        var wpRoot = Make("WayPoints");
        var wps = new Transform[2];
        for (int i = 0; i < 2; i++)
        {
            wps[i] = Make("WP" + i).transform;
            wps[i].SetParent(wpRoot.transform);
            wps[i].position = new Vector3(i * 10f, 0f, 0f);
        }
        WayPoints.points = wps;

        // 40 nodes (10 x 4) beside the path; every node is called "Node" so name ties must be broken by position.
        nodes = new List<Node>();
        for (int x = 0; x < 10; x++)
            for (int z = 1; z <= 4; z++)
            {
                var go = Make("Node");
                go.transform.position = new Vector3(x, 0f, z);
                nodes.Add(go.AddComponent<Node>());
            }

        var shopGo = Make("Shop");
        shop = shopGo.AddComponent<Shop>();
        shop.standardTurret = MakeBlueprint("TStd", 100);
        shop.missileLauncher = MakeBlueprint("TMis", 250);
        shop.LaserLauncher = MakeBlueprint("TLas", 300);

        var gm = Make("GameMaster");
        buildManager = gm.AddComponent<BuildManager>();
        BuildManager.instance = buildManager;   // Awake does not run in edit mode
        spawner = gm.AddComponent<WaveSpawner>();
        spawner.waves = new WaveDefinition[4];
    }

    [TearDown]
    public void TearDown()
    {
        SimClock.ForceRestore();
        foreach (var n in nodes)
            if (n != null && n.turret != null) Object.DestroyImmediate(n.turret);
        foreach (var go in created)
            if (go != null) Object.DestroyImmediate(go);
        created.Clear();
        foreach (var e in new List<Enemies>(EnemyRegistry.All))
            if (e != null) Object.DestroyImmediate(e.gameObject);
        EnemyRegistry.Clear();
        WayPoints.points = null;
        BuildManager.instance = null;
        Difficulty.Reset();
        PlayerStats.Money = 0;
        PlayerStats.Lives = 10;
    }

    private BuilderContext NewContext(TurretTypes allowed = TurretTypes.All)
    {
        // Feed the nodes in a shuffled order to prove the slot order does not depend on it.
        var shuffled = new List<Node>(nodes);
        var rng = new Random(5);
        for (int i = shuffled.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var t = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = t;
        }
        return new BuilderContext(shuffled, WayPoints.points, shop, spawner) { Allowed = allowed };
    }

    // ---------------------------------------------------------------- spec

    [Test]
    public void Spec_Layout_IsConsistent()
    {
        Assert.AreEqual(24, BuilderSpec.SlotCount);
        Assert.AreEqual(73, BuilderSpec.ActionCount);
        Assert.AreEqual(36, BuilderSpec.ObsSize);
        for (int s = 0; s < BuilderSpec.SlotCount; s++)
            for (int t = 0; t < BuilderSpec.TypeCount; t++)
            {
                int a = BuilderSpec.Encode(s, t);
                int ds, dt;
                Assert.IsTrue(BuilderSpec.Decode(a, out ds, out dt));
                Assert.AreEqual(s, ds);
                Assert.AreEqual(t, dt);
            }
        int x, y;
        Assert.IsFalse(BuilderSpec.Decode(BuilderSpec.WaitAction, out x, out y));
        Assert.IsFalse(BuilderSpec.Decode(BuilderSpec.ActionCount, out x, out y));
    }

    // ---------------------------------------------------------------- slots

    [Test]
    public void Slots_AreTheKClosest_InStableOrder()
    {
        BuilderContext a = NewContext();
        BuilderContext b = new BuilderContext(nodes, WayPoints.points, shop, spawner);   // different input order

        Assert.AreEqual(BuilderSpec.SlotCount, a.Slots.Length);
        for (int i = 0; i < BuilderSpec.SlotCount; i++)
        {
            Assert.IsNotNull(a.Slots[i]);
            Assert.AreSame(a.Slots[i], b.Slots[i], "slot " + i + " differs between input orders");
        }

        // Distances never decrease along the slot list, and no excluded node is closer than the last slot.
        float last = -1f;
        foreach (Node n in a.Slots)
        {
            float d = Dist(n);
            Assert.GreaterOrEqual(d, last);
            last = d;
        }
        var chosen = new HashSet<Node>(a.Slots);
        foreach (Node n in nodes)
            if (!chosen.Contains(n)) Assert.GreaterOrEqual(Dist(n), last);
    }

    [Test]
    public void Slots_FewerNodesThanK_ArePaddedAndBlocked()
    {
        var few = nodes.GetRange(0, 5);
        var ctx = new BuilderContext(few, WayPoints.points, shop, spawner);
        for (int s = 0; s < 5; s++) Assert.IsNotNull(ctx.Slots[s]);
        for (int s = 5; s < BuilderSpec.SlotCount; s++) Assert.IsNull(ctx.Slots[s]);

        float[] obs = BuilderObservation.Build(ctx);
        bool[] mask = BuilderActions.ComputeMask(ctx);
        for (int s = 5; s < BuilderSpec.SlotCount; s++)
        {
            Assert.AreEqual(1f, obs[BuilderSpec.ObsOccupancy + s]);
            for (int t = 0; t < BuilderSpec.TypeCount; t++)
                Assert.IsFalse(mask[BuilderSpec.Encode(s, t)]);
        }
    }

    private static float Dist(Node n)
    {
        float best = float.MaxValue;
        foreach (Transform p in WayPoints.points)
            best = Mathf.Min(best, Vector3.Distance(n.transform.position, p.position));
        return best;
    }

    // ---------------------------------------------------------------- observation

    [Test]
    public void Observation_HasSpecLength_AndNormalisedValues()
    {
        var ctx = NewContext();
        PlayerStats.Money = 500;
        PlayerStats.Lives = 7;

        float[] obs = BuilderObservation.Build(ctx);
        Assert.AreEqual(BuilderSpec.ObsSize, obs.Length);
        foreach (float v in obs)
        {
            Assert.GreaterOrEqual(v, 0f);
            Assert.LessOrEqual(v, 1f);
        }
        Assert.AreEqual(0.5f, obs[BuilderSpec.ObsMoney], 1e-6f);
        Assert.AreEqual(0.7f, obs[BuilderSpec.ObsLives], 1e-6f);
        Assert.AreEqual(0f, obs[BuilderSpec.ObsWave], 1e-6f);
        // 500 money: standard (100) and missile (250) and laser (300) are all affordable.
        for (int t = 0; t < BuilderSpec.TypeCount; t++) Assert.AreEqual(1f, obs[BuilderSpec.ObsAfford + t]);
    }

    [Test]
    public void Observation_ReflectsEnemiesBuildingsAndAffordability()
    {
        var ctx = NewContext();
        PlayerStats.Money = 280;

        for (int i = 0; i < 3; i++)
        {
            var e = Make("enemy").AddComponent<Enemies>();
            e.health = 100;
            EnemyRegistry.Register(e);
        }

        Assert.IsTrue(BuilderActions.Apply(ctx, BuilderSpec.Encode(0, (int)TurretKind.Standard)));     // 280 -> 180
        Assert.IsTrue(BuilderActions.Apply(ctx, BuilderSpec.Encode(3, (int)TurretKind.Standard)));     // 180 -> 80

        float[] obs = BuilderObservation.Build(ctx);
        Assert.AreEqual(3f / 50f, obs[BuilderSpec.ObsEnemies], 1e-6f);
        Assert.AreEqual(300f / 5000f, obs[BuilderSpec.ObsEnemyHp], 1e-6f);
        Assert.AreEqual(2f / 20f, obs[BuilderSpec.ObsTypeCounts + (int)TurretKind.Standard], 1e-6f);
        Assert.AreEqual(0f, obs[BuilderSpec.ObsTypeCounts + (int)TurretKind.Missile]);
        Assert.AreEqual(1f, obs[BuilderSpec.ObsOccupancy + 0]);
        Assert.AreEqual(1f, obs[BuilderSpec.ObsOccupancy + 3]);
        Assert.AreEqual(0f, obs[BuilderSpec.ObsOccupancy + 1]);
        Assert.AreEqual(0f, obs[BuilderSpec.ObsAfford + (int)TurretKind.Standard], "80 < 100");
    }

    // ---------------------------------------------------------------- mask

    [Test]
    public void Mask_FreshState_OnlyAffordableFreeAllowedActions()
    {
        var ctx = NewContext();
        PlayerStats.Money = 260;   // standard (100) and missile (250) affordable, laser (300) not

        bool[] mask = BuilderActions.ComputeMask(ctx);
        Assert.AreEqual(BuilderSpec.ActionCount, mask.Length);
        Assert.IsTrue(mask[BuilderSpec.WaitAction]);
        for (int s = 0; s < BuilderSpec.SlotCount; s++)
        {
            Assert.IsTrue(mask[BuilderSpec.Encode(s, (int)TurretKind.Standard)]);
            Assert.IsTrue(mask[BuilderSpec.Encode(s, (int)TurretKind.Missile)]);
            Assert.IsFalse(mask[BuilderSpec.Encode(s, (int)TurretKind.Laser)], "unaffordable");
        }
    }

    [Test]
    public void Mask_OccupiedSlot_IsForbidden()
    {
        var ctx = NewContext();
        Assert.IsTrue(BuilderActions.Apply(ctx, BuilderSpec.Encode(2, (int)TurretKind.Standard)));

        bool[] mask = BuilderActions.ComputeMask(ctx);
        for (int t = 0; t < BuilderSpec.TypeCount; t++)
        {
            Assert.IsFalse(mask[BuilderSpec.Encode(2, t)], "occupied slot must be masked (type " + t + ")");
            Assert.IsTrue(mask[BuilderSpec.Encode(1, t)] || PlayerStats.Money < ctx.Cost(t));
        }
    }

    [Test]
    public void Mask_DisallowedType_IsForbidden()
    {
        var ctx = NewContext(TurretTypes.Laser);
        PlayerStats.Money = 1000;

        bool[] mask = BuilderActions.ComputeMask(ctx);
        for (int s = 0; s < BuilderSpec.SlotCount; s++)
        {
            Assert.IsFalse(mask[BuilderSpec.Encode(s, (int)TurretKind.Standard)]);
            Assert.IsFalse(mask[BuilderSpec.Encode(s, (int)TurretKind.Missile)]);
            Assert.IsTrue(mask[BuilderSpec.Encode(s, (int)TurretKind.Laser)]);
        }

        ctx.Allowed = TurretTypes.None;
        mask = BuilderActions.ComputeMask(ctx);
        Assert.IsTrue(mask[BuilderSpec.WaitAction]);
        for (int a = 1; a < mask.Length; a++) Assert.IsFalse(mask[a]);
    }

    [Test]
    public void Mask_NoMoney_OnlyWait()
    {
        var ctx = NewContext();
        PlayerStats.Money = 99;
        bool[] mask = BuilderActions.ComputeMask(ctx);
        Assert.IsTrue(mask[BuilderSpec.WaitAction]);
        for (int a = 1; a < mask.Length; a++) Assert.IsFalse(mask[a]);
    }

    [Test]
    public void Mask_WrongLength_Throws()
    {
        var ctx = NewContext();
        Assert.Throws<ArgumentException>(() => BuilderActions.ComputeMask(ctx, new bool[5]));
        Assert.Throws<ArgumentException>(() => BuilderObservation.Build(ctx, new float[5]));
    }

    // ---------------------------------------------------------------- apply

    [Test]
    public void Apply_ValidAction_BuildsAndCharges()
    {
        var ctx = NewContext();
        PlayerStats.Money = 400;
        int slot = 4;
        Assert.IsTrue(BuilderActions.Apply(ctx, BuilderSpec.Encode(slot, (int)TurretKind.Missile)));
        Assert.IsNotNull(ctx.Slots[slot].turret);
        Assert.AreEqual(150, PlayerStats.Money);
        Assert.AreEqual((int)TurretKind.Missile, ctx.TypeOf(ctx.Slots[slot].turret));
    }

    [Test]
    public void Apply_MaskedAction_ChangesNothing()
    {
        var ctx = NewContext(TurretTypes.Standard);
        PlayerStats.Money = 1000;

        Assert.IsFalse(BuilderActions.Apply(ctx, BuilderSpec.Encode(0, (int)TurretKind.Laser)), "disallowed type");
        Assert.IsNull(ctx.Slots[0].turret);
        Assert.AreEqual(1000, PlayerStats.Money);

        Assert.IsTrue(BuilderActions.Apply(ctx, BuilderSpec.Encode(0, (int)TurretKind.Standard)));
        Assert.IsFalse(BuilderActions.Apply(ctx, BuilderSpec.Encode(0, (int)TurretKind.Standard)), "occupied slot");
        Assert.AreEqual(900, PlayerStats.Money);

        PlayerStats.Money = 50;
        Assert.IsFalse(BuilderActions.Apply(ctx, BuilderSpec.Encode(1, (int)TurretKind.Standard)), "unaffordable");
        Assert.IsNull(ctx.Slots[1].turret);

        Assert.IsFalse(BuilderActions.Apply(ctx, BuilderSpec.WaitAction));
        Assert.IsFalse(BuilderActions.Apply(ctx, 9999));
        Assert.IsFalse(BuilderActions.Apply(ctx, -3));
    }

    // ---------------------------------------------------------------- policies

    [Test]
    public void Policies_Act_NeverReturnsAMaskedAction()
    {
        var driver = new Random(11);
        var random = new RandomPolicy(3);
        var heuristic = new HeuristicPolicy();

        for (int trial = 0; trial < 300; trial++)
        {
            var ctx = NewContext((TurretTypes)driver.Next(0, 8));
            PlayerStats.Money = driver.Next(0, 700);

            // Pre-fill a random number of slots so occupancy varies.
            int prefill = driver.Next(0, 10);
            for (int i = 0; i < prefill; i++)
            {
                int slot = driver.Next(BuilderSpec.SlotCount);
                int type = driver.Next(BuilderSpec.TypeCount);
                BuilderActions.Apply(ctx, BuilderSpec.Encode(slot, type));   // may be refused; that is fine
            }
            PlayerStats.Money = driver.Next(0, 700);

            float[] obs = BuilderObservation.Build(ctx);
            bool[] mask = BuilderActions.ComputeMask(ctx);
            var mlp = MlpPolicy.CreateRandom(trial);

            AssertLegal("random", random.Act(obs, mask), mask);
            AssertLegal("heuristic", heuristic.Act(obs, mask), mask);
            AssertLegal("mlp", mlp.Act(obs, mask), mask);

            foreach (var n in nodes)
                if (n.turret != null) { Object.DestroyImmediate(n.turret); n.turret = null; }
        }
    }

    [Test]
    public void MlpPolicy_IgnoresHugeLogitOnMaskedAction()
    {
        var mlp = new MlpPolicy();
        float[] p = mlp.GetParameters();
        int b2 = p.Length - BuilderSpec.ActionCount;
        p[b2 + 5] = 1e6f;   // action 5 is by far the best... if it were allowed
        p[b2 + 7] = 1f;     // best of the valid ones
        mlp.SetParameters(p);

        var obs = new float[BuilderSpec.ObsSize];
        var mask = new bool[BuilderSpec.ActionCount];
        mask[0] = true;
        mask[7] = true;
        Assert.AreEqual(7, mlp.Act(obs, mask), "must pick the best *valid* action");

        mask[7] = false;
        Assert.AreEqual(BuilderSpec.WaitAction, mlp.Act(obs, mask));
    }

    [Test]
    public void RandomPolicy_IsSeededAndDeterministic()
    {
        var obs = new float[BuilderSpec.ObsSize];
        var mask = new bool[BuilderSpec.ActionCount];
        for (int i = 0; i < mask.Length; i += 2) mask[i] = true;

        var a = new RandomPolicy(42);
        var b = new RandomPolicy(42);
        var c = new RandomPolicy(43);
        bool differs = false;
        for (int i = 0; i < 50; i++)
        {
            int x = a.Act(obs, mask);
            Assert.AreEqual(x, b.Act(obs, mask));
            if (x != c.Act(obs, mask)) differs = true;
        }
        Assert.IsTrue(differs, "different seeds should give different action streams");
    }

    [Test]
    public void HeuristicPolicy_FillsNearestSlotsAndCyclesTypes()
    {
        var ctx = NewContext();
        var policy = new HeuristicPolicy();
        PlayerStats.Money = 5000;

        var types = new List<int>();
        for (int i = 0; i < 8; i++)
        {
            float[] obs = BuilderObservation.Build(ctx);
            bool[] mask = BuilderActions.ComputeMask(ctx);
            int action = policy.Act(obs, mask);
            int slot, type;
            Assert.IsTrue(BuilderSpec.Decode(action, out slot, out type));
            Assert.AreEqual(i, slot, "heuristic must fill slots in order, nearest first");
            types.Add(type);
            Assert.IsTrue(BuilderActions.Apply(ctx, action));
        }
        CollectionAssert.AreEqual(new[] { 0, 1, 0, 2, 0, 1, 0, 2 }, types);
    }

    [Test]
    public void HeuristicPolicy_WaitsWhenUnaffordable_AndHonoursAllowedTypes()
    {
        var ctx = NewContext();
        var policy = new HeuristicPolicy();

        PlayerStats.Money = 50;
        Assert.AreEqual(BuilderSpec.WaitAction, policy.Act(BuilderObservation.Build(ctx), BuilderActions.ComputeMask(ctx)));

        ctx.Allowed = TurretTypes.Laser;
        PlayerStats.Money = 1000;
        int slot, type;
        Assert.IsTrue(BuilderSpec.Decode(policy.Act(BuilderObservation.Build(ctx), BuilderActions.ComputeMask(ctx)), out slot, out type));
        Assert.AreEqual((int)TurretKind.Laser, type);
    }

    private static void AssertLegal(string who, int action, bool[] mask)
    {
        Assert.GreaterOrEqual(action, 0, who);
        Assert.Less(action, mask.Length, who);
        Assert.IsTrue(mask[action], who + " returned masked action " + action);
    }

    // ---------------------------------------------------------------- MLP + IO

    [Test]
    public void MlpPolicy_ParameterCount_MatchesLayout()
    {
        var mlp = new MlpPolicy();
        int expected = MlpPolicy.DefaultHidden * BuilderSpec.ObsSize + MlpPolicy.DefaultHidden
                     + BuilderSpec.ActionCount * MlpPolicy.DefaultHidden + BuilderSpec.ActionCount;
        Assert.AreEqual(expected, mlp.ParamCount());
        Assert.AreEqual(expected, mlp.GetParameters().Length);
    }

    [Test]
    public void MlpPolicy_SameSeed_SameWeights_AndActions()
    {
        var a = MlpPolicy.CreateRandom(9);
        var b = MlpPolicy.CreateRandom(9);
        var c = MlpPolicy.CreateRandom(10);
        CollectionAssert.AreEqual(a.GetParameters(), b.GetParameters());
        CollectionAssert.AreNotEqual(a.GetParameters(), c.GetParameters());
    }

    [Test]
    public void PolicyIO_RoundTrip_IsBitIdentical()
    {
        var original = MlpPolicy.CreateRandom(123);
        // Add awkward values to the weights: tiny, huge, negative zero, subnormal.
        float[] w = original.GetParameters();
        w[0] = float.Epsilon;
        w[1] = -0f;
        w[2] = float.MaxValue;
        w[3] = 1f / 3f;
        original.SetParameters(w);

        var meta = PolicyMetadata.Create(77, 30, 24, "sigma=0.1;lr=0.05");
        meta.trainReturn = 12.5f;
        string json = PolicyIO.ToJson(original, meta);

        PolicyMetadata loadedMeta;
        MlpPolicy loaded = PolicyIO.FromJson(json, out loadedMeta);

        float[] a = original.GetParameters();
        float[] b = loaded.GetParameters();
        Assert.AreEqual(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
            Assert.AreEqual(BitConverter.SingleToInt32Bits(a[i]), BitConverter.SingleToInt32Bits(b[i]), "weight " + i);

        // Outputs are bit-identical too.
        var rng = new Random(1);
        var o1 = new float[BuilderSpec.ActionCount];
        var o2 = new float[BuilderSpec.ActionCount];
        for (int trial = 0; trial < 20; trial++)
        {
            var obs = new float[BuilderSpec.ObsSize];
            for (int i = 0; i < obs.Length; i++) obs[i] = (float)rng.NextDouble();
            original.Forward(obs, o1);
            loaded.Forward(obs, o2);
            for (int i = 0; i < o1.Length; i++)
                Assert.AreEqual(BitConverter.SingleToInt32Bits(o1[i]), BitConverter.SingleToInt32Bits(o2[i]));
        }

        Assert.AreEqual(77, loadedMeta.seed);
        Assert.AreEqual(30, loadedMeta.generations);
        Assert.AreEqual(24, loadedMeta.population);
        Assert.AreEqual(12.5f, loadedMeta.trainReturn);
        Assert.AreEqual("sigma=0.1;lr=0.05", loadedMeta.config);
        Assert.AreEqual(Application.unityVersion, loadedMeta.unityVersion);
        Assert.IsFalse(string.IsNullOrEmpty(loadedMeta.createdUtc));
    }

    [Test]
    public void PolicyIO_SaveLoad_RoundTripsThroughAFile()
    {
        var original = MlpPolicy.CreateRandom(5);
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "td_policy_test_" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            PolicyIO.Save(path, original, PolicyMetadata.Create(1, 2, 4, "test"));
            PolicyMetadata meta;
            MlpPolicy loaded = PolicyIO.Load(path, out meta);
            CollectionAssert.AreEqual(original.GetParameters(), loaded.GetParameters());
            Assert.AreEqual(2, meta.generations);

            byte[] bytes = System.IO.File.ReadAllBytes(path);
            Assert.IsFalse(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "policy JSON must be UTF-8 without BOM");
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    [Test]
    public void PolicyIO_RejectsMismatchedOrBrokenFiles()
    {
        Assert.Throws<FormatException>(() => PolicyIO.FromJson("{}"));
        Assert.Throws<FormatException>(() => PolicyIO.FromJson("null"));

        string json = PolicyIO.ToJson(new MlpPolicy(), null);
        string wrongObs = json.Replace("\"obsSize\":" + BuilderSpec.ObsSize, "\"obsSize\":" + (BuilderSpec.ObsSize + 1));
        Assert.AreNotEqual(json, wrongObs);
        Assert.Throws<FormatException>(() => PolicyIO.FromJson(wrongObs));

        string wrongType = json.Replace("\"type\":\"mlp\"", "\"type\":\"cnn\"");
        Assert.AreNotEqual(json, wrongType);
        Assert.Throws<FormatException>(() => PolicyIO.FromJson(wrongType));

        string wrongSpec = json.Replace("\"specVersion\":" + BuilderSpec.SpecVersion, "\"specVersion\":" + (BuilderSpec.SpecVersion + 1));
        Assert.AreNotEqual(json, wrongSpec);
        Assert.Throws<FormatException>(() => PolicyIO.FromJson(wrongSpec));

        Assert.Throws<FormatException>(() => PolicyIO.FromJson("not json at all"));
    }

    // ---------------------------------------------------------------- Evolution Strategy

    private static float Quadratic(float[] x, float[] target)
    {
        float s = 0f;
        for (int i = 0; i < x.Length; i++)
        {
            float d = x[i] - target[i];
            s -= d * d;
        }
        return s;   // maximum 0 at x == target
    }

    [Test]
    public void EvolutionStrategy_MaximisesAToyQuadratic()
    {
        const int dim = 10;
        var target = new float[dim];
        for (int i = 0; i < dim; i++) target[i] = 1f;
        var start = new float[dim];

        var es = new EvolutionStrategy(start, seed: 7, populationSize: 24, sigma: 0.1f, learningRate: 0.05f);
        float initial = Quadratic(es.GetTheta(), target);
        float gap = 0f - initial;   // optimum is 0

        for (int g = 0; g < 50; g++)
            es.Step(x => Quadratic(x, target));

        float final = Quadratic(es.GetTheta(), target);
        float recovered = (final - initial) / gap;
        Assert.AreEqual(50, es.Generation);
        Assert.Greater(recovered, 0.9f, "ES closed only " + recovered * 100f + "% of the gap (initial " + initial + ", final " + final + ")");
    }

    [Test]
    public void EvolutionStrategy_SameSeed_SameTrajectory()
    {
        var target = new float[] { 1f, -2f, 0.5f };
        var a = new EvolutionStrategy(new float[3], seed: 3, populationSize: 8);
        var b = new EvolutionStrategy(new float[3], seed: 3, populationSize: 8);
        var c = new EvolutionStrategy(new float[3], seed: 4, populationSize: 8);
        for (int g = 0; g < 10; g++)
        {
            a.Step(x => Quadratic(x, target));
            b.Step(x => Quadratic(x, target));
            c.Step(x => Quadratic(x, target));
        }
        CollectionAssert.AreEqual(a.GetTheta(), b.GetTheta());
        CollectionAssert.AreNotEqual(a.GetTheta(), c.GetTheta());
    }

    [Test]
    public void EvolutionStrategy_Ask_IsAntithetic()
    {
        var theta0 = new float[] { 1f, 2f, 3f, 4f };
        var es = new EvolutionStrategy(theta0, seed: 1, populationSize: 6, sigma: 0.2f);
        float[][] pop = es.Ask();
        Assert.AreEqual(6, pop.Length);
        for (int k = 0; k < 3; k++)
            for (int j = 0; j < theta0.Length; j++)
                Assert.AreEqual(2f * theta0[j], pop[2 * k][j] + pop[2 * k + 1][j], 1e-5f, "pair " + k + " must mirror around theta");
        Assert.AreNotEqual(pop[0][0], pop[2][0]);
    }

    [Test]
    public void EvolutionStrategy_CenteredRanks_AreScaleInvariantAndHandleTies()
    {
        float[] r = EvolutionStrategy.CenteredRanks(new[] { 10f, -5f, 3f, 1000f });
        float[] expected = { 1f / 6f, -0.5f, -1f / 6f, 0.5f };   // ranks of 4 values: -0.5, -1/6, 1/6, 0.5
        for (int i = 0; i < expected.Length; i++) Assert.AreEqual(expected[i], r[i], 1e-6f);

        float[] ties = EvolutionStrategy.CenteredRanks(new[] { 1f, 1f, 1f });
        foreach (float v in ties) Assert.AreEqual(0f, v, 1e-6f);

        Assert.Throws<ArgumentException>(() => new EvolutionStrategy(new float[2], 1, populationSize: 3));
        var es = new EvolutionStrategy(new float[2], 1, populationSize: 4);
        es.Ask();
        Assert.Throws<ArgumentException>(() => es.Tell(new float[3]));
    }

    // ---------------------------------------------------------------- Difficulty + Enemies hook

    private static void RunAwake(Enemies e)
    {
        MethodInfo awake = typeof(Enemies).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(awake, "Enemies.Awake not found");
        awake.Invoke(e, null);
    }

    [Test]
    public void Difficulty_DefaultsToOne_AndResets()
    {
        Assert.AreEqual(1f, Difficulty.HealthMultiplier);
        Difficulty.HealthMultiplier = 1.5f;
        Assert.AreEqual(1.5f, Difficulty.HealthMultiplier);
        Difficulty.HealthMultiplier = -2f;
        Assert.AreEqual(1f, Difficulty.HealthMultiplier, "non-positive values are rejected");
        Difficulty.HealthMultiplier = 0.8f;
        Difficulty.Reset();
        Assert.AreEqual(1f, Difficulty.HealthMultiplier);
    }

    [Test]
    public void Difficulty_JitterFromSeed_IsDeterministicAndInRange()
    {
        for (int seed = 1000; seed < 1100; seed++)
        {
            float v = Difficulty.JitterFromSeed(seed, 0.85f, 1.25f);
            Assert.AreEqual(v, Difficulty.JitterFromSeed(seed, 0.85f, 1.25f));
            Assert.GreaterOrEqual(v, 0.85f);
            Assert.LessOrEqual(v, 1.25f);
        }
    }

    [Test]
    public void Enemies_Awake_AppliesHealthMultiplier()
    {
        var plain = Make("e1").AddComponent<Enemies>();
        plain.health = 100;
        RunAwake(plain);
        Assert.AreEqual(100f, plain.health, 1e-4f);
        Assert.AreEqual(100f, plain.MaxHealth, 1e-4f);

        Difficulty.HealthMultiplier = 1.5f;
        var tough = Make("e2").AddComponent<Enemies>();
        tough.health = 100;
        RunAwake(tough);
        Assert.AreEqual(150f, tough.health, 1e-4f);
        Assert.AreEqual(150f, tough.MaxHealth, 1e-4f);

        Difficulty.Reset();
        var again = Make("e3").AddComponent<Enemies>();
        again.health = 100;
        RunAwake(again);
        Assert.AreEqual(100f, again.health, 1e-4f, "multiplier must not leak after Reset");
    }

    // ---------------------------------------------------------------- SimClock

    [Test]
    public void SimClock_BeginEnd_RestoresPreviousSettings()
    {
        float capture = Time.captureDeltaTime;
        float scale = Time.timeScale;
        int fps = Application.targetFrameRate;

        SimClock.Begin();
        Assert.IsTrue(SimClock.IsActive);
        Assert.AreEqual(1f / 30f, Time.captureDeltaTime, 1e-6f);
        Assert.AreEqual(4f, Time.timeScale, 1e-6f);
        Assert.AreEqual(-1, Application.targetFrameRate);
        SimClock.End();

        Assert.IsFalse(SimClock.IsActive);
        Assert.AreEqual(capture, Time.captureDeltaTime);
        Assert.AreEqual(scale, Time.timeScale);
        Assert.AreEqual(fps, Application.targetFrameRate);
    }

    [Test]
    public void SimClock_RestoresInFinally_AfterAnException()
    {
        float capture = Time.captureDeltaTime;
        float scale = Time.timeScale;
        int fps = Application.targetFrameRate;

        Assert.Throws<InvalidOperationException>(() =>
        {
            SimClock.Begin();
            try { throw new InvalidOperationException("boom"); }
            finally { SimClock.End(); }
        });

        Assert.IsFalse(SimClock.IsActive);
        Assert.AreEqual(capture, Time.captureDeltaTime);
        Assert.AreEqual(scale, Time.timeScale);
        Assert.AreEqual(fps, Application.targetFrameRate);
    }

    [Test]
    public void SimClock_NestedAndRepeatedEnd_AreSafe()
    {
        float capture = Time.captureDeltaTime;
        float scale = Time.timeScale;

        SimClock.Begin();
        SimClock.Begin(1f / 60f, 2f);
        Assert.AreEqual(2f, Time.timeScale, 1e-6f);
        SimClock.End();
        Assert.IsTrue(SimClock.IsActive, "outer Begin is still open");
        SimClock.End();
        SimClock.End();   // extra End must be a no-op

        Assert.IsFalse(SimClock.IsActive);
        Assert.AreEqual(capture, Time.captureDeltaTime);
        Assert.AreEqual(scale, Time.timeScale);

        SimClock.Begin();
        SimClock.ForceRestore();
        Assert.IsFalse(SimClock.IsActive);
        Assert.AreEqual(capture, Time.captureDeltaTime);
        Assert.AreEqual(scale, Time.timeScale);
    }
}
