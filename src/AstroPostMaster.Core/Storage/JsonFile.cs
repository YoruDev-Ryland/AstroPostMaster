using System.Text.Json;

namespace AstroPostMaster.Core.Storage;

public static class JsonFile
{
    /// <summary>
    /// Loads <paramref name="path"/>. Missing file → fallback. Unreadable JSON → file moved to "*.bak",
    /// <paramref name="onCorrupt"/> invoked with the original path, fallback returned.
    /// </summary>
    public static T LoadOrDefault<T>(string path, Func<T> fallback, Action<string>? onCorrupt = null)
    {
        if (!File.Exists(path)) return fallback();
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json.Options) ?? fallback();
        }
        catch (JsonException)
        {
            File.Move(path, path + ".bak", overwrite: true);
            onCorrupt?.Invoke(path);
            return fallback();
        }
    }

    /// <summary>Writes to "*.tmp" then renames over the target, so a crash never leaves a half-written file.</summary>
    public static void SaveAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(value, Json.Options));
        File.Move(tmp, path, overwrite: true);
    }
}
