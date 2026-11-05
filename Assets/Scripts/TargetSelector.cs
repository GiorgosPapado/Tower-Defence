using System.Collections.Generic;
using UnityEngine;

public enum TargetPriority
{
    Closest,    // nearest to the turret
    First,      // closest to the goal
    Last,       // furthest from the goal
    Strongest   // most health
}

public static class TargetSelector
{
    public static Enemies Select(IReadOnlyList<Enemies> enemies, Vector3 from, float range, TargetPriority priority)
    {
        Enemies best = null;
        float bestScore = float.NegativeInfinity;
        float rangeSqr = range * range;

        for (int i = 0; i < enemies.Count; i++)
        {
            Enemies e = enemies[i];
            if (e == null || e.IsDead) continue;

            float distSqr = (e.transform.position - from).sqrMagnitude;
            if (distSqr > rangeSqr) continue;

            float score;
            switch (priority)
            {
                case TargetPriority.First:     score = -e.distanceToGoal; break;
                case TargetPriority.Last:      score = e.distanceToGoal;  break;
                case TargetPriority.Strongest: score = e.health;          break;
                default:                       score = -distSqr;          break;
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = e;
            }
        }
        return best;
    }
}
