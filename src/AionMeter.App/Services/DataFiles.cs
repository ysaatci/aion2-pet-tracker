using System.IO;
using System.Reflection;

namespace AionMeter.App.Services;

/// <summary>
/// Where the game data tables (<c>data\…</c>) are read from. A development build has the folder next to the program.
/// The released single exe carries them as embedded resources instead (so no code ever has to be unpacked to disk
/// outside the signed exe): they are written once per version to <c>%LOCALAPPDATA%\AION2PetTracker\data\&lt;version&gt;</c>.
/// </summary>
public static class DataFiles
{
    private const string Prefix = "data/";
    private const string DoneMarker = ".complete";

    public static string Resolve()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "data");
        if (Directory.Exists(beside)) return beside;

        var assembly = typeof(DataFiles).Assembly;
        var version = assembly.GetName().Version?.ToString() ?? "0";
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AION2PetTracker", "data", version);
        if (File.Exists(Path.Combine(target, DoneMarker))) return target;

        try
        {
            Extract(assembly, target);
            File.WriteAllText(Path.Combine(target, DoneMarker), "");
            RemoveOtherVersions(Path.GetDirectoryName(target)!, version);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("Unpacking the game data failed", ex);
        }
        return target;
    }

    private static void Extract(Assembly assembly, string target)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var relative = name.Replace('\\', '/');
            if (!relative.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            relative = relative[Prefix.Length..];
            // Resource names come from our own build, but never write outside the target folder.
            var path = Path.GetFullPath(Path.Combine(target, relative));
            if (!path.StartsWith(Path.GetFullPath(target) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var source = assembly.GetManifestResourceStream(name)!;
            using var file = File.Create(path);
            source.CopyTo(file);
        }
    }

    /// <summary>Data unpacked by earlier versions is not needed any more.</summary>
    private static void RemoveOtherVersions(string root, string keep)
    {
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (Path.GetFileName(dir) == keep) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
