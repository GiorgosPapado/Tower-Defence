using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The scene objects a builder policy acts on: the K build slots (nodes closest to the path, stable order),
/// the shop (costs) and the wave spawner. Also carries the allowed-turret-type filter.
/// </summary>
public sealed class BuilderContext
{
    /// <summary>Exactly BuilderSpec.SlotCount entries; trailing entries are null if the map has fewer nodes.</summary>
    public readonly Node[] Slots = new Node[BuilderSpec.SlotCount];
    public readonly Shop Shop;
    public readonly WaveSpawner Spawner;

    /// <summary>Turret types the policy may build. Default: all.</summary>
    public TurretTypes Allowed = TurretTypes.All;

    public BuilderContext(IEnumerable<Node> nodes, Transform[] path, Shop shop, WaveSpawner spawner)
    {
        Shop = shop;
        Spawner = spawner;
        Node[] chosen = SelectSlots(nodes, path);
        Array.Copy(chosen, Slots, chosen.Length);
    }

    /// <summary>Build a context from the currently loaded scene (call after the scene's Awake/Start have run).</summary>
    public static BuilderContext FromScene()
    {
        Node[] nodes = UnityEngine.Object.FindObjectsByType<Node>(FindObjectsSortMode.None);
        Shop shop = UnityEngine.Object.FindFirstObjectByType<Shop>();
        WaveSpawner spawner = UnityEngine.Object.FindFirstObjectByType<WaveSpawner>();
        return new BuilderContext(nodes, WayPoints.points, shop, spawner);
    }

    /// <summary>
    /// The up-to-K nodes closest to the path, ordered by (distance to the nearest waypoint, name, x, y, z).
    /// The order does not depend on the input order, so it is identical across runs.
    /// </summary>
    public static Node[] SelectSlots(IEnumerable<Node> nodes, Transform[] path)
    {
        var entries = new List<SlotEntry>();
        foreach (Node n in nodes)
        {
            if (n == null) continue;
            entries.Add(new SlotEntry
            {
                node = n,
                dist = PathDistance(n.transform.position, path),
                name = n.name,
                pos = n.transform.position,
            });
        }
        entries.Sort(CompareEntries);

        int count = Math.Min(BuilderSpec.SlotCount, entries.Count);
        var result = new Node[count];
        for (int i = 0; i < count; i++) result[i] = entries[i].node;
        return result;
    }

    private struct SlotEntry
    {
        public Node node;
        public float dist;
        public string name;
        public Vector3 pos;
    }

    private static int CompareEntries(SlotEntry a, SlotEntry b)
    {
        int c = a.dist.CompareTo(b.dist);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.name, b.name);
        if (c != 0) return c;
        c = a.pos.x.CompareTo(b.pos.x);
        if (c != 0) return c;
        c = a.pos.y.CompareTo(b.pos.y);
        if (c != 0) return c;
        return a.pos.z.CompareTo(b.pos.z);
    }

    private static float PathDistance(Vector3 p, Transform[] path)
    {
        if (path == null || path.Length == 0) return float.MaxValue;
        float best = float.MaxValue;
        foreach (Transform t in path)
        {
            if (t == null) continue;
            float d = Vector3.Distance(p, t.position);
            if (d < best) best = d;
        }
        return best;
    }

    public TurretBlueprint Blueprint(int type)
    {
        if (Shop == null) return null;
        switch (type)
        {
            case (int)TurretKind.Standard: return Shop.standardTurret;
            case (int)TurretKind.Missile: return Shop.missileLauncher;
            case (int)TurretKind.Laser: return Shop.LaserLauncher;
            default: return null;
        }
    }

    /// <summary>Cost of a turret type; int.MaxValue if the shop has no such blueprint.</summary>
    public int Cost(int type)
    {
        TurretBlueprint bp = Blueprint(type);
        return bp == null ? int.MaxValue : bp.cost;
    }

    /// <summary>Which turret type a built turret is (matched by prefab name, longest match wins); -1 if unknown.</summary>
    public int TypeOf(GameObject turret)
    {
        if (turret == null) return -1;
        int best = -1;
        int bestLen = -1;
        for (int t = 0; t < BuilderSpec.TypeCount; t++)
        {
            TurretBlueprint bp = Blueprint(t);
            if (bp == null || bp.prefab == null) continue;
            string prefabName = bp.prefab.name;
            if (prefabName.Length > bestLen && turret.name.StartsWith(prefabName, StringComparison.Ordinal))
            {
                best = t;
                bestLen = prefabName.Length;
            }
        }
        return best;
    }

    public bool IsSlotFree(int slot)
    {
        Node n = Slots[slot];
        return n != null && n.turret == null;
    }
}

/// <summary>Builds the fixed-size observation vector from live game state.</summary>
public static class BuilderObservation
{
    public static float[] Create()
    {
        return new float[BuilderSpec.ObsSize];
    }

    public static float[] Build(BuilderContext ctx)
    {
        var obs = Create();
        Build(ctx, obs);
        return obs;
    }

    /// <summary>Fill obs (length BuilderSpec.ObsSize) in place. All values are clamped to [0, 1].</summary>
    public static void Build(BuilderContext ctx, float[] obs)
    {
        if (obs == null || obs.Length != BuilderSpec.ObsSize)
            throw new ArgumentException("obs must have length " + BuilderSpec.ObsSize);

        Array.Clear(obs, 0, obs.Length);

        obs[BuilderSpec.ObsMoney] = Norm(PlayerStats.Money, 1000f);
        obs[BuilderSpec.ObsLives] = Norm(PlayerStats.Lives, 10f);

        WaveSpawner sp = ctx.Spawner;
        if (sp != null)
        {
            obs[BuilderSpec.ObsWave] = Norm(sp.WaveNumber, Mathf.Max(1, sp.TotalWaves));
            obs[BuilderSpec.ObsSpawning] = sp.IsSpawning ? 1f : 0f;
        }

        obs[BuilderSpec.ObsEnemies] = Norm(EnemyRegistry.Count, 50f);
        float hp = 0f;
        IReadOnlyList<Enemies> all = EnemyRegistry.All;
        for (int i = 0; i < all.Count; i++)
            if (all[i] != null && !all[i].IsDead) hp += Mathf.Max(0f, all[i].health);
        obs[BuilderSpec.ObsEnemyHp] = Norm(hp, 5000f);

        var counts = new int[BuilderSpec.TypeCount];
        for (int s = 0; s < BuilderSpec.SlotCount; s++)
        {
            Node n = ctx.Slots[s];
            if (n == null)
            {
                obs[BuilderSpec.ObsOccupancy + s] = 1f;   // missing slot behaves like an occupied one
                continue;
            }
            if (n.turret == null) continue;
            obs[BuilderSpec.ObsOccupancy + s] = 1f;
            int type = ctx.TypeOf(n.turret);
            if (type >= 0) counts[type]++;
        }

        for (int t = 0; t < BuilderSpec.TypeCount; t++)
        {
            obs[BuilderSpec.ObsTypeCounts + t] = Norm(counts[t], 20f);
            obs[BuilderSpec.ObsAfford + t] = PlayerStats.Money >= ctx.Cost(t) ? 1f : 0f;
        }
    }

    private static float Norm(float value, float scale)
    {
        return Mathf.Clamp01(value / scale);
    }
}
