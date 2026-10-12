namespace PetTracker.Core;

/// <summary>
/// Souls needed for the next pet level on Global: unlock (→ 1) is per pet (5 for most), then 25 (→ 2) and 75 (→ 3, max).
/// The game's counter restarts at 0 on every level up.
/// </summary>
public static class SoulThresholds
{
    public const int DefaultUnlock = 5;
    public const int MaxLevel = 3;
    private static readonly int[] AfterUnlock = [25, 75];

    /// <summary>Souls to reach the level after <paramref name="level"/> (0 = not owned); null at max level.</summary>
    public static int? Needed(int level, int unlockSouls = DefaultUnlock) => level switch
    {
        <= 0 => unlockSouls,
        >= MaxLevel => null,
        _ => AfterUnlock[level - 1],
    };
}
