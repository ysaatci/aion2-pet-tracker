namespace PetTracker.Core;

/// <summary>
/// Learns which NPC drops which pet's soul from play: a soul drop arrives ~0.35 s after the kill that gave it, so the
/// NPC deaths just before it name the source. It learns only when that is unambiguous: one NPC code died in the window,
/// or exactly one of them is already known to drop the pet.
/// </summary>
public sealed class SoulSourceLearner(PetCatalog catalog)
{
    public const long WindowMs = 2_000;

    private readonly List<(long TimeMs, int NpcCode)> _deaths = new();

    /// <summary>NPC → pet links learned this run (not in the catalog before).</summary>
    public List<(int NpcCode, int PetId)> Learned { get; } = new();

    public event Action<int, int>? Linked;

    public void NpcDied(long timeMs, int npcCode)
    {
        _deaths.RemoveAll(d => timeMs - d.TimeMs > WindowMs);
        _deaths.Add((timeMs, npcCode));
    }

    /// <summary>The NPC code a soul came from, when it can be told; learns the link if it is new.</summary>
    public int? SoulDropped(long timeMs, int petId)
    {
        var codes = _deaths.Where(d => d.TimeMs <= timeMs && timeMs - d.TimeMs <= WindowMs).Select(d => d.NpcCode).Distinct().ToList();
        int? source = codes.Count == 1 ? codes[0] : null;
        if (source is null)
        {
            var known = codes.Where(c => catalog.PetsDroppedBy(c).Contains(petId)).ToList();
            if (known.Count == 1) source = known[0];
        }
        if (source is not { } code) return null;
        if (catalog.Link(code, petId))
        {
            Learned.Add((code, petId));
            Linked?.Invoke(code, petId);
        }
        return code;
    }
}
