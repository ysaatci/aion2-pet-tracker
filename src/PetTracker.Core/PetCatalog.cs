using System.Text.Json;

namespace PetTracker.Core;

public enum Genus : byte
{
    Unknown = 0,
    Cogni,
    Fera,
    Natura,
    Varian,
    Special,
}

/// <param name="UnlockSouls">Souls that unlock the pet (level 1); 5 for most.</param>
/// <param name="SourceNpcs">NPC codes whose kills can drop the pet's soul.</param>
public sealed record PetInfo(int Id, string Name, Genus Genus, int UnlockSouls, IReadOnlyList<int> SourceNpcs);

/// <summary>
/// Pet id → name, genus and the NPCs that drop its soul, and the reverse NPC → pets lookup. Built from
/// <c>data/pets.json</c> (MetaBot.GG's mount pages) plus NPC → pet links learned from soul drops while playing.
/// </summary>
public sealed class PetCatalog
{
    private readonly Dictionary<int, PetInfo> _pets = new();
    private readonly Dictionary<int, HashSet<int>> _petsByNpc = new();

    public static PetCatalog Empty => new();

    public IReadOnlyCollection<PetInfo> Pets => _pets.Values;

    public PetInfo? Pet(int petId) => _pets.GetValueOrDefault(petId);

    /// <summary>Pets whose soul this NPC can drop (usually one).</summary>
    public IReadOnlyCollection<int> PetsDroppedBy(int npcCode) =>
        _petsByNpc.TryGetValue(npcCode, out var pets) ? pets : Array.Empty<int>();

    public void Add(PetInfo pet)
    {
        _pets[pet.Id] = pet;
        foreach (var npc in pet.SourceNpcs) Link(npc, pet.Id);
    }

    /// <summary>Records that <paramref name="npcCode"/> drops <paramref name="petId"/>'s soul; true when it was new.</summary>
    public bool Link(int npcCode, int petId)
    {
        if (!_petsByNpc.TryGetValue(npcCode, out var pets)) _petsByNpc[npcCode] = pets = new HashSet<int>();
        return pets.Add(petId);
    }

    public static PetCatalog Load(string path)
    {
        var catalog = new PetCatalog();
        if (!File.Exists(path)) return catalog;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("pets", out var pets)) return catalog;
        foreach (var p in pets.EnumerateObject())
        {
            if (!int.TryParse(p.Name, out var id)) continue;
            var v = p.Value;
            var name = v.TryGetProperty("name", out var n) ? n.GetString() ?? p.Name : p.Name;
            var genus = v.TryGetProperty("genus", out var g) && Enum.TryParse<Genus>(g.GetString(), true, out var parsed) ? parsed : Genus.Unknown;
            var unlock = v.TryGetProperty("unlockSouls", out var u) && u.ValueKind == JsonValueKind.Number ? u.GetInt32() : SoulThresholds.DefaultUnlock;
            var npcs = v.TryGetProperty("npcs", out var arr) && arr.ValueKind == JsonValueKind.Array
                ? arr.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToList()
                : new List<int>();
            catalog.Add(new PetInfo(id, name, genus, unlock, npcs));
        }
        return catalog;
    }
}
