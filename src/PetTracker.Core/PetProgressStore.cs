using System.Text.Json;

namespace PetTracker.Core;

/// <param name="Level">0 = not owned yet.</param>
/// <param name="Needed">Souls for the next level; null at max level.</param>
public sealed record PetProgress(int PetId, int Level, int Souls, int? Needed);

/// <summary>What is saved between runs: the last login snapshot with every soul drop since applied.</summary>
public sealed class PetState
{
    public int ActivePetId { get; set; }
    public Dictionary<int, int> Levels { get; set; } = new();
    public Dictionary<int, int> Souls { get; set; } = new();
    /// <summary>When the login snapshot was taken (Unix ms); 0 = never.</summary>
    public long SnapshotMs { get; set; }
    public long UpdatedMs { get; set; }
}

public interface IPetStateStorage
{
    PetState? Load();
    void Save(PetState state);
}

/// <summary>JSON file, by default <c>%APPDATA%\AION2PetTracker\pets-state.json</c>.</summary>
public sealed class JsonFilePetStateStorage(string path) : IPetStateStorage
{
    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AION2PetTracker", "pets-state.json");

    public PetState? Load()
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<PetState>(File.ReadAllText(path)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(PetState state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(state));
        File.Move(tmp, path, overwrite: true);
    }
}

/// <summary>
/// The character's pet levels and souls: replaced by every login snapshot, advanced by every soul drop (levelling up
/// when the drop fills the bar, like the game). Saved after each change so a tracker started mid-session still knows.
/// </summary>
public sealed class PetProgressStore
{
    private readonly PetCatalog _catalog;
    private readonly IPetStateStorage? _storage;
    private PetState _state;

    public PetProgressStore(PetCatalog catalog, IPetStateStorage? storage = null)
    {
        _catalog = catalog;
        _storage = storage;
        _state = storage?.Load() ?? new PetState();
    }

    /// <summary>A login snapshot has been seen, now or in an earlier run.</summary>
    public bool HasSnapshot => _state.SnapshotMs > 0;
    public long SnapshotMs => _state.SnapshotMs;
    public int ActivePetId => _state.ActivePetId;

    public void ApplySnapshot(long timeMs, int activePetId, IReadOnlyDictionary<int, int> levels, IReadOnlyDictionary<int, int> souls)
    {
        _state = new PetState
        {
            ActivePetId = activePetId,
            Levels = new Dictionary<int, int>(levels),
            Souls = new Dictionary<int, int>(souls),
            SnapshotMs = timeMs,
            UpdatedMs = timeMs,
        };
        _storage?.Save(_state);
    }

    /// <summary>Adds a soul drop; ignored (null) until a login snapshot exists, which already counts earlier drops.</summary>
    public PetProgress? ApplySoul(long timeMs, int petId, int gained)
    {
        if (!HasSnapshot) return null;
        var level = _state.Levels.GetValueOrDefault(petId);
        var souls = _state.Souls.GetValueOrDefault(petId) + gained;
        while (Needed(petId, level) is { } needed && souls >= needed)
        {
            souls -= needed;
            level++;
        }
        if (level >= SoulThresholds.MaxLevel) souls = 0;
        if (level > 0) _state.Levels[petId] = level;
        _state.Souls[petId] = souls;
        _state.UpdatedMs = timeMs;
        _storage?.Save(_state);
        return Get(petId);
    }

    public PetProgress Get(int petId)
    {
        var level = _state.Levels.GetValueOrDefault(petId);
        return new PetProgress(petId, level, _state.Souls.GetValueOrDefault(petId), Needed(petId, level));
    }

    private int? Needed(int petId, int level) =>
        SoulThresholds.Needed(level, _catalog.Pet(petId)?.UnlockSouls ?? SoulThresholds.DefaultUnlock);
}
