namespace PetTracker.Core;

/// <summary>
/// Learns which NPC drops which pet's soul from play: a soul drop arrives ~0.35 s after the kill that gave it, so the
/// NPC deaths just before it name the source. Only unambiguous windows count: a single NPC code died, and no NPC whose
/// code is unknown (spawned before the tracker saw it) died too. A link is learned once it has been seen
/// <see cref="Confirmations"/> times, so one stray death never teaches a wrong source.
/// </summary>
public sealed class SoulSourceLearner(PetCatalog catalog)
{
    public const long WindowMs = 2_000;
    public const int Confirmations = 2;
    public const int UnknownNpc = 0;

    private readonly List<(long TimeMs, int NpcCode)> _deaths = new();
    private readonly Dictionary<(int NpcCode, int PetId), int> _seen = new();

    /// <summary>NPC → pet links learned this run (not in the catalog before).</summary>
    public List<(int NpcCode, int PetId)> Learned { get; } = new();

    public event Action<int, int>? Linked;

    /// <param name="npcCode"><see cref="UnknownNpc"/> for an entity whose spawn was never seen.</param>
    public void NpcDied(long timeMs, int npcCode)
    {
        _deaths.RemoveAll(d => timeMs - d.TimeMs > WindowMs);
        _deaths.Add((timeMs, npcCode));
    }

    /// <summary>The NPC code a soul came from, when it can be told; learns the link once it is confirmed.</summary>
    public int? SoulDropped(long timeMs, int petId)
    {
        var codes = _deaths.Where(d => d.TimeMs <= timeMs && timeMs - d.TimeMs <= WindowMs).Select(d => d.NpcCode).Distinct().ToList();
        var known = codes.Where(c => catalog.PetsDroppedBy(c).Contains(petId)).ToList();
        if (known.Count == 1) return known[0];
        if (codes.Count != 1 || codes[0] == UnknownNpc) return null;

        var code = codes[0];
        var key = (code, petId);
        _seen[key] = _seen.GetValueOrDefault(key) + 1;
        if (_seen[key] < Confirmations || !catalog.Link(code, petId)) return null;
        Learned.Add(key);
        Linked?.Invoke(code, petId);
        return code;
    }
}
