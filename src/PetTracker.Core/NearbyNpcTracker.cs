namespace PetTracker.Core;

public readonly record struct NearbyNpc(uint ActorId, int NpcCode, float X, float Y, float Z, long LastSeenMs);

/// <summary>
/// NPCs currently around the character: added when they spawn into view, refreshed whenever a packet mentions them,
/// removed when they die, on a map change, or after <see cref="StaleMs"/> without any sign of them (the game sends no
/// packet when an NPC merely leaves view).
/// </summary>
public sealed class NearbyNpcTracker
{
    public const long StaleMs = 180_000;

    private readonly Dictionary<uint, NearbyNpc> _npcs = new();

    public IReadOnlyCollection<NearbyNpc> Npcs => _npcs.Values;

    public int? NpcCodeOf(uint actorId) => _npcs.TryGetValue(actorId, out var n) ? n.NpcCode : null;

    public void Seen(long timeMs, uint actorId, int npcCode, float x, float y, float z) =>
        _npcs[actorId] = new NearbyNpc(actorId, npcCode, x, y, z, timeMs);

    public void Touch(long timeMs, uint actorId)
    {
        if (_npcs.TryGetValue(actorId, out var n)) _npcs[actorId] = n with { LastSeenMs = timeMs };
    }

    /// <summary>Removes a dead NPC; returns its code when it was tracked.</summary>
    public int? Died(uint actorId) => _npcs.Remove(actorId, out var n) ? n.NpcCode : null;

    public void Clear() => _npcs.Clear();

    public void Expire(long nowMs)
    {
        foreach (var id in _npcs.Where(kv => nowMs - kv.Value.LastSeenMs > StaleMs).Select(kv => kv.Key).ToList())
            _npcs.Remove(id);
    }
}
