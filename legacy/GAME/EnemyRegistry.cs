using System.Collections.Generic;
using UnityEngine;

/// <summary>Shared list of live enemies so turrets do not need FindGameObjectsWithTag.</summary>
public static class EnemyRegistry
{
    private static readonly List<Enemies> all = new List<Enemies>();

    public static IReadOnlyList<Enemies> All { get { return all; } }
    public static int Count { get { return all.Count; } }

    public static void Register(Enemies e)
    {
        if (!all.Contains(e)) all.Add(e);
    }

    public static void Unregister(Enemies e)
    {
        all.Remove(e);
    }

    public static void Clear()
    {
        all.Clear();
    }

    // Keeps the list clean when "Enter Play Mode without domain reload" is enabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnLoad()
    {
        all.Clear();
    }
}
