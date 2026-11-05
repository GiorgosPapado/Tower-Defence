using System;

/// <summary>Turret kinds the builder can place. Values are the "type" index inside an action.</summary>
public enum TurretKind
{
    Standard = 0,
    Missile = 1,
    Laser = 2,
}

/// <summary>Allowed-type filter for ablations (balance tester).</summary>
[Flags]
public enum TurretTypes
{
    None = 0,
    Standard = 1,
    Missile = 2,
    Laser = 4,
    All = Standard | Missile | Laser,
}

/// <summary>
/// Single source of truth for the builder agent's interface: slot count, action layout and observation layout.
/// Keep this stable so another trainer (e.g. ML-Agents) can plug into the same interface later.
/// </summary>
public static class BuilderSpec
{
    public const int SpecVersion = 1;

    /// <summary>Number of build slots (the K nodes closest to the path).</summary>
    public const int SlotCount = 24;
    public const int TypeCount = 3;

    /// <summary>Action 0 = wait, otherwise 1 + slot * TypeCount + type.</summary>
    public const int ActionCount = 1 + SlotCount * TypeCount;
    public const int WaitAction = 0;

    // Observation layout.
    public const int ObsMoney = 0;            // money / 1000
    public const int ObsLives = 1;            // lives / 10
    public const int ObsWave = 2;             // wave / total waves
    public const int ObsSpawning = 3;         // 1 while a wave is spawning
    public const int ObsEnemies = 4;          // enemies alive / 50
    public const int ObsEnemyHp = 5;          // remaining enemy HP / 5000
    public const int ObsTypeCounts = 6;       // TypeCount values: built turrets of each type / 20
    public const int ObsAfford = ObsTypeCounts + TypeCount;   // TypeCount flags: money >= cost
    public const int ObsOccupancy = ObsAfford + TypeCount;    // SlotCount flags: 1 = occupied or missing
    public const int ObsSize = ObsOccupancy + SlotCount;

    public static int Encode(int slot, int type)
    {
        return 1 + slot * TypeCount + type;
    }

    /// <summary>Decode a build action. Returns false for wait or an out-of-range action.</summary>
    public static bool Decode(int action, out int slot, out int type)
    {
        slot = -1;
        type = -1;
        if (action <= WaitAction || action >= ActionCount) return false;
        int a = action - 1;
        slot = a / TypeCount;
        type = a % TypeCount;
        return true;
    }

    public static bool IsAllowed(TurretTypes allowed, int type)
    {
        return (allowed & (TurretTypes)(1 << type)) != 0;
    }
}
