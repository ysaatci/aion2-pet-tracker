using AionMeter.Core.Events;

namespace PetTracker.Core;

/// <param name="NearbyCount">NPCs around that drop this pet's soul.</param>
public sealed record NearbyPet(PetInfo Pet, PetProgress Progress, int NearbyCount, IReadOnlyList<int> NpcCodes);

/// <summary>What the overlay shows: nearby soul sources grouped by genus, and the last soul drop.</summary>
public sealed record PetOverlaySnapshot(
    bool HasCollection,
    IReadOnlyDictionary<Genus, IReadOnlyList<NearbyPet>> NearbyByGenus,
    PetProgress? LastDrop,
    long TimeMs);

/// <summary>
/// Feeds game events into the progress store, the nearby NPC list and the soul source learner, and builds the overlay
/// snapshot. Not thread-safe: call it from one thread (the capture's event thread, or under a lock).
/// </summary>
public sealed class PetTrackerService(PetCatalog catalog, PetProgressStore progress)
{
    private readonly NearbyNpcTracker _nearby = new();
    private PetProgress? _lastDrop;
    private long _nowMs;

    public SoulSourceLearner Learner { get; } = new(catalog);
    public PetCatalog Catalog => catalog;
    public PetProgressStore Progress => progress;
    public NearbyNpcTracker Nearby => _nearby;

    /// <summary>Raised after an event that changed what the overlay shows.</summary>
    public event Action? Changed;

    public void Process(GameEvent e)
    {
        _nowMs = Math.Max(_nowMs, e.TimeMs);
        var changed = true;
        switch (e)
        {
            case PetCollectionEvent c:
                progress.ApplySnapshot(c.TimeMs, c.ActivePetId, c.Levels, c.Souls);
                break;
            case PetSoulGainedEvent s:
                Learner.SoulDropped(s.TimeMs, s.PetId);
                _lastDrop = progress.ApplySoul(s.TimeMs, s.PetId, s.Souls);
                break;
            case NpcSeenEvent n:
                _nearby.Seen(n.TimeMs, n.ActorId, n.NpcCode, n.X, n.Y, n.Z);
                changed = catalog.PetsDroppedBy(n.NpcCode).Count > 0;
                break;
            case DeathEvent d:
                if (_nearby.Died(d.ActorId) is { } code) Learner.NpcDied(d.TimeMs, code);
                else changed = false;
                break;
            case ZoneChangedEvent:
                _nearby.Clear();
                break;
            case NpcHpEvent h:
                _nearby.Touch(h.TimeMs, h.ActorId);
                changed = false;
                break;
            case DamageEvent dmg:
                _nearby.Touch(dmg.TimeMs, dmg.SourceId);
                _nearby.Touch(dmg.TimeMs, dmg.TargetId);
                changed = false;
                break;
            default:
                changed = false;
                break;
        }
        if (changed) Changed?.Invoke();
    }

    public PetOverlaySnapshot Snapshot()
    {
        _nearby.Expire(_nowMs);
        var counts = new Dictionary<int, (int Count, HashSet<int> Codes)>();
        foreach (var npc in _nearby.Npcs)
        {
            foreach (var petId in catalog.PetsDroppedBy(npc.NpcCode))
            {
                var c = counts.GetValueOrDefault(petId, (Count: 0, Codes: new HashSet<int>()));
                c.Codes.Add(npc.NpcCode);
                counts[petId] = (c.Count + 1, c.Codes);
            }
        }
        var pets = counts.Select(kv => new NearbyPet(
                catalog.Pet(kv.Key) ?? new PetInfo(kv.Key, $"Pet {kv.Key}", Genus.Unknown, SoulThresholds.DefaultUnlock, []),
                progress.Get(kv.Key), kv.Value.Count, kv.Value.Codes.Order().ToList()))
            // Pets still to level first, closest to their next level on top; maxed pets last.
            .OrderBy(p => p.Progress.Needed is null)
            .ThenBy(p => (p.Progress.Needed ?? 0) - p.Progress.Souls)
            .ThenBy(p => p.Pet.Name);
        var byGenus = pets.GroupBy(p => p.Pet.Genus)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<NearbyPet>)g.ToList());
        return new PetOverlaySnapshot(progress.HasSnapshot, byGenus, _lastDrop, _nowMs);
    }
}
