using AionMeter.Core.Events;

namespace PetTracker.Core;

/// <param name="NearbyCount">NPCs around that drop this pet's soul; 0 for a pet kept up after its last one died.</param>
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
    private uint _selfId;
    private readonly Dictionary<uint, long> _deaths = new();
    private long _nowMs;
    private long _lastExpireMs;
    // Pets whose source died or whose soul dropped lately: they stay listed for LingerMs after the last one is gone.
    private readonly Dictionary<int, (long TimeMs, HashSet<int> Codes)> _recent = new();

    /// <summary>How long a pet stays listed after its last nearby source died or its soul dropped.</summary>
    public const long LingerMs = 300_000;

    // Removed by hand (×): hidden until one of the pet's mobs spawns into view again.
    private readonly HashSet<int> _dismissed = new();

    public SoulSourceLearner Learner { get; } = new(catalog);
    public PetCatalog Catalog => catalog;
    public PetProgressStore Progress => progress;
    public NearbyNpcTracker Nearby => _nearby;

    /// <summary>Raised after an event that changed what the overlay shows.</summary>
    public event Action? Changed;

    public void Process(GameEvent e)
    {
        _nowMs = Math.Max(_nowMs, e.TimeMs);
        // Housekeeping on the event clock too, so the lists stay small while no overlay asks for snapshots.
        if (_nowMs - _lastExpireMs > 30_000)
        {
            _lastExpireMs = _nowMs;
            _nearby.Expire(_nowMs);
            foreach (var id in _recent.Where(kv => _nowMs - kv.Value.TimeMs > LingerMs).Select(kv => kv.Key).ToList()) _recent.Remove(id);
        }
        var changed = true;
        switch (e)
        {
            case PetCollectionEvent c:
                progress.ApplySnapshot(c.TimeMs, c.ActivePetId, c.Levels, c.Souls);
                break;
            case PetSoulGainedEvent s:
                Learner.SoulDropped(s.TimeMs, s.PetId);
                _lastDrop = progress.ApplySoul(s.TimeMs, s.PetId, s.Souls) ?? _lastDrop;
                Remember(s.PetId, s.TimeMs, null);
                break;
            case NpcSeenEvent n:
                _nearby.Seen(n.TimeMs, n.ActorId, n.NpcCode, n.X, n.Y, n.Z);
                changed = catalog.PetsDroppedBy(n.NpcCode).Count > 0;
                // One of its mobs came into view: a pet removed by hand is back.
                foreach (var petId in catalog.PetsDroppedBy(n.NpcCode)) _dismissed.Remove(petId);
                break;
            case DeathEvent d:
                // A kill arrives twice (kill credit and death packet): only the first one counts.
                if (_deaths.TryGetValue(d.ActorId, out var last) && d.TimeMs - last < SoulSourceLearner.WindowMs)
                {
                    changed = false;
                    break;
                }
                _deaths[d.ActorId] = d.TimeMs;
                if (_deaths.Count > 512) PruneDeaths(d.TimeMs);
                var died = _nearby.Died(d.ActorId);
                // An entity never seen spawning may still be the kill behind a soul: it makes the window ambiguous.
                if (!d.AlreadyDead && d.ActorId != _selfId) Learner.NpcDied(d.TimeMs, died ?? SoulSourceLearner.UnknownNpc);
                changed = died is { } code && catalog.PetsDroppedBy(code).Count > 0;
                if (changed && !d.AlreadyDead)
                    foreach (var petId in catalog.PetsDroppedBy(died!.Value)) Remember(petId, d.TimeMs, died.Value);
                break;
            case SelfIdentifiedEvent self:
                _selfId = self.ActorId;
                changed = false;
                break;
            case ZoneChangedEvent z:
                _nearby.Clear();
                if (!z.IsTeleport) _recent.Clear(); // another map: the last one's pets are no longer "around"
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

    /// <summary>Takes a pet off the list until one of its mobs comes into view again.</summary>
    public void Dismiss(int petId)
    {
        _dismissed.Add(petId);
        _recent.Remove(petId);
        Changed?.Invoke();
    }

    private void Remember(int petId, long timeMs, int? npcCode)
    {
        var codes = _recent.TryGetValue(petId, out var r) ? r.Codes : new HashSet<int>();
        if (npcCode is { } code) codes.Add(code);
        _recent[petId] = (timeMs, codes);
    }

    private void PruneDeaths(long nowMs)
    {
        foreach (var id in _deaths.Where(kv => nowMs - kv.Value > SoulSourceLearner.WindowMs).Select(kv => kv.Key).ToList())
            _deaths.Remove(id);
    }

    /// <param name="nowMs">Wall clock (Unix ms) for expiry; defaults to the newest event's time.</param>
    public PetOverlaySnapshot Snapshot(long? nowMs = null)
    {
        var now = Math.Max(_nowMs, nowMs ?? 0);
        _nearby.Expire(now);
        foreach (var id in _recent.Where(kv => now - kv.Value.TimeMs > LingerMs).Select(kv => kv.Key).ToList()) _recent.Remove(id);
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
        foreach (var (petId, r) in _recent)
            if (!counts.ContainsKey(petId)) counts[petId] = (0, new HashSet<int>(r.Codes));
        foreach (var petId in _dismissed) counts.Remove(petId);
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
        return new PetOverlaySnapshot(progress.HasSnapshot, byGenus, _lastDrop, now);
    }
}
