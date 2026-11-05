/// <summary>
/// Baseline that mirrors the existing PlayMode bot: fill the slot nearest to the path first, cycling the turret
/// kind with the number of turrets already built (standard, missile, standard, laser, repeat), and spend as soon as the
/// choice is affordable. If the preferred kind is not valid it falls back to the first valid kind
/// (standard, missile, laser), which only differs from the bot when a type is disallowed.
/// </summary>
public sealed class HeuristicPolicy : IBuilderPolicy
{
    private static readonly int[] Cycle = { 0, 1, 0, 2 };   // standard, missile, standard, laser

    public int Act(float[] obs, bool[] mask)
    {
        int firstFree = -1;
        for (int s = 0; s < BuilderSpec.SlotCount; s++)
        {
            if (obs[BuilderSpec.ObsOccupancy + s] < 0.5f)
            {
                firstFree = s;
                break;
            }
        }
        if (firstFree < 0) return BuilderSpec.WaitAction;

        // Turrets built so far, recovered from the per-type counts (each is count / 20).
        float builtF = 0f;
        for (int t = 0; t < BuilderSpec.TypeCount; t++)
            builtF += obs[BuilderSpec.ObsTypeCounts + t] * 20f;
        int built = (int)(builtF + 0.5f);

        int preferred = Cycle[built % Cycle.Length];
        int action = BuilderSpec.Encode(firstFree, preferred);
        if (mask[action]) return action;

        for (int t = 0; t < BuilderSpec.TypeCount; t++)
        {
            action = BuilderSpec.Encode(firstFree, t);
            if (mask[action]) return action;
        }
        return BuilderSpec.WaitAction;
    }
}
