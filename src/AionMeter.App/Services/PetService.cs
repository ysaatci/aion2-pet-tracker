using System.IO;
using AionMeter.Core.Events;
using PetTracker.Core;

namespace AionMeter.App.Services;

/// <summary>
/// The pet tracker for the app: catalog from <c>data/pets.json</c>, progress and learned soul sources saved under
/// <c>%APPDATA%\AionMeter</c>. Events arrive on the capture thread, the overlay reads on the UI thread: both under a lock.
/// </summary>
public sealed class PetService
{
    private readonly object _gate = new();
    private readonly PetTrackerService _tracker;
    private readonly LearnedSources _learned;

    public PetService(string dataDirectory, bool persistent)
    {
        var catalog = PetCatalog.Load(Path.Combine(dataDirectory, "pets.json"));
        _learned = new LearnedSources(Path.Combine(AppSettings.AppDataDir, "pets-learned.json"));
        if (persistent) _learned.LoadInto(catalog);
        var storage = persistent ? new JsonFilePetStateStorage(Path.Combine(AppSettings.AppDataDir, "pets-state.json")) : null;
        _tracker = new PetTrackerService(catalog, new PetProgressStore(catalog, storage));
        _tracker.Changed += () => Version++;
        _tracker.Learner.Linked += (npc, pet) =>
        {
            Log.Info($"Pet soul source learned: NPC {npc} drops pet {pet}");
            if (persistent) _learned.Add(npc, pet);
        };
        Log.Info($"Pet catalog: {catalog.Pets.Count} pets" + (_tracker.Progress.HasSnapshot ? ", progress restored" : ", no saved progress"));
    }

    /// <summary>Bumped on every change the overlay shows, so it redraws only then (and on its own clock for expiry).</summary>
    public int Version { get; private set; }

    public void Process(GameEvent e)
    {
        lock (_gate)
        {
            _tracker.Process(e);
            if (e is PetCollectionEvent c) Log.Info($"Pet collection loaded: {c.Levels.Count} owned");
        }
    }

    public PetOverlaySnapshot Snapshot()
    {
        lock (_gate) return _tracker.Snapshot(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }
}
