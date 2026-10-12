using System.Text.Json;

namespace PetTracker.Core;

/// <summary>NPC → pet links learned from soul drops, kept across runs (a JSON list of [npcCode, petId]).</summary>
public sealed class LearnedSources(string path)
{
    public void LoadInto(PetCatalog catalog)
    {
        try
        {
            if (!File.Exists(path)) return;
            foreach (var pair in JsonSerializer.Deserialize<int[][]>(File.ReadAllText(path)) ?? [])
                if (pair.Length == 2) catalog.Link(pair[0], pair[1]);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
        }
    }

    public void Add(int npcCode, int petId)
    {
        var all = new List<int[]>();
        try
        {
            if (File.Exists(path)) all = JsonSerializer.Deserialize<List<int[]>>(File.ReadAllText(path)) ?? all;
        }
        catch (JsonException)
        {
        }
        if (all.Any(p => p.Length == 2 && p[0] == npcCode && p[1] == petId)) return;
        all.Add([npcCode, petId]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(all));
    }
}
