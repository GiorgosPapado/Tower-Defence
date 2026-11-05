using System;

/// <summary>Action validity mask and action execution through the existing BuildManager.</summary>
public static class BuilderActions
{
    public static bool[] CreateMask()
    {
        return new bool[BuilderSpec.ActionCount];
    }

    public static bool[] ComputeMask(BuilderContext ctx)
    {
        var mask = CreateMask();
        ComputeMask(ctx, mask);
        return mask;
    }

    /// <summary>
    /// Fill mask (length BuilderSpec.ActionCount) in place. Wait is always valid; a build action is valid only if
    /// the slot exists and is free, the turret type is allowed and in the shop, and the player can afford it.
    /// </summary>
    public static void ComputeMask(BuilderContext ctx, bool[] mask)
    {
        if (mask == null || mask.Length != BuilderSpec.ActionCount)
            throw new ArgumentException("mask must have length " + BuilderSpec.ActionCount);

        mask[BuilderSpec.WaitAction] = true;
        for (int s = 0; s < BuilderSpec.SlotCount; s++)
        {
            bool free = ctx.IsSlotFree(s);
            for (int t = 0; t < BuilderSpec.TypeCount; t++)
                mask[BuilderSpec.Encode(s, t)] = free && TypeBuildable(ctx, t);
        }
    }

    public static bool IsValid(BuilderContext ctx, int action)
    {
        if (action == BuilderSpec.WaitAction) return true;
        int slot, type;
        if (!BuilderSpec.Decode(action, out slot, out type)) return false;
        return ctx.IsSlotFree(slot) && TypeBuildable(ctx, type);
    }

    private static bool TypeBuildable(BuilderContext ctx, int type)
    {
        return BuilderSpec.IsAllowed(ctx.Allowed, type)
            && ctx.Blueprint(type) != null
            && PlayerStats.Money >= ctx.Cost(type);
    }

    /// <summary>
    /// One decision step shared by EpisodeRunner and BuilderController: observe, mask, ask the policy, apply.
    /// Returns true if a turret was built (type and cost describe it); false otherwise or if there is no BuildManager.
    /// </summary>
    public static bool Step(IBuilderPolicy policy, BuilderContext ctx, float[] obs, bool[] mask, out int type, out int cost)
    {
        type = -1;
        cost = 0;
        if (BuildManager.instance == null) return false;
        BuilderObservation.Build(ctx, obs);
        ComputeMask(ctx, mask);
        int action = policy.Act(obs, mask);

        int slot;
        if (!BuilderSpec.Decode(action, out slot, out type)) return false;
        cost = ctx.Cost(type);
        return Apply(ctx, action);
    }

    /// <summary>Execute an action. Returns true only if a turret was actually built; invalid actions change nothing.</summary>
    public static bool Apply(BuilderContext ctx, int action)
    {
        if (action == BuilderSpec.WaitAction || !IsValid(ctx, action)) return false;
        if (BuildManager.instance == null) return false;

        int slot, type;
        BuilderSpec.Decode(action, out slot, out type);
        Node node = ctx.Slots[slot];

        BuildManager.instance.SelectTurretToBuild(ctx.Blueprint(type));
        BuildManager.instance.BuildTurretOn(node);
        return node.turret != null;
    }
}
