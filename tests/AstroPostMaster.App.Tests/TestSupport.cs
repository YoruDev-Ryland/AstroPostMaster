using AstroPostMaster.App.Services;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.App.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "apm-app-" + Guid.NewGuid().ToString("N"));
    public TempDir() => Directory.CreateDirectory(Path);
    public string File(string name) => System.IO.Path.Combine(Path, name);
    public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch (IOException) { } }
}

/// <summary>Runs posted actions immediately on the calling thread.</summary>
public sealed class ImmediateDispatcher : IUiDispatcher
{
    public void Post(Action action) => action();
}

/// <summary>Returns fixed dimensions without decoding; throws FileNotFound for missing files like the real loader.</summary>
public sealed class FakePreviewLoader(int width = 6000, int height = 4000) : IPreviewLoader
{
    public int Calls { get; private set; }
    public Task<PreviewResult> LoadAsync(string path, CancellationToken ct = default)
    {
        Calls++;
        if (!System.IO.File.Exists(path)) throw new FileNotFoundException("missing", path);
        return Task.FromResult(new PreviewResult(null, width, height));
    }
}

public static class Sample
{
    public static AppState State(TempDir dir, bool withProfiles = true)
    {
        var store = new AppStore(new AppPaths(dir.Path));
        if (withProfiles)
        {
            store.SaveSites([new Site("s1", "SFRO", 1, IsDefault: true)]);
            store.SaveRigs([new Rig("r1", "Starlux", "SkyWatcher Starlux 190mn", "ASI2600mm Pro", "Scorpio 3nm LRGBSHO", "ZWO AM5", IsDefault: true)]);
            store.SaveSoftware([new SoftwareSet("w1", "Default", ["Siril - Stacking", "PixInsight - Processing"], IsDefault: true)]);
            store.SaveHashtagSets([new HashtagSet("h1", "Core", ["#astrophotography", "#astro"], IsDefault: true), new HashtagSet("h2", "Nebula", ["#nebula"])]);
        }
        return AppState.Load(store);
    }

    /// <summary>Creates an empty file standing in for an image (FakePreviewLoader never decodes it).</summary>
    public static string ImageFile(TempDir dir, string name = "Heart Nebula.jpg")
    {
        var path = dir.File(name);
        System.IO.File.WriteAllBytes(path, [0xFF, 0xD8, 0xFF, 0xD9]);
        return path;
    }
}
