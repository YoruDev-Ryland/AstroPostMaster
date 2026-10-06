using System.Net;
using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Handoff;
using Microsoft.Extensions.Time.Testing;
using SkiaSharp;

namespace AstroPostMaster.App.Tests;

public class LockOnExportTests
{
    private sealed class Host : IHandoffHost
    {
        public IReadOnlyList<IPAddress> Addresses() => [IPAddress.Parse("192.168.1.50")];
        public Task<IHandoffSession> StartAsync(HandoffPackage p, IPAddress a, bool https, CancellationToken ct = default) =>
            Task.FromResult<IHandoffSession>(new Session());
        private sealed class Session : IHandoffSession
        {
            public Uri Url { get; } = new("https://192.168.1.50:48443/t/");
            public bool HasReceivedRequest => false;
            public event Action? RequestReceived { add { } remove { } }
            public event Action? Stopped { add { } remove { } }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static async Task<(PhoneExportViewModel Vm, EditorViewModel Editor)> Create(TempDir dir, bool lockOnExport, bool sourceExists = true)
    {
        var state = Sample.State(dir);
        state.Settings.LockOnExport = lockOnExport;
        var post = PostFactory.CreateNew([.. state.Sites], [.. state.Rigs], [.. state.Software], [.. state.HashtagSets]);
        var source = dir.File("src.jpg");
        using (var bmp = new SKBitmap(600, 400))
        using (var data = bmp.Encode(SKEncodedImageFormat.Jpeg, 90))
            File.WriteAllBytes(source, data.ToArray());
        var editor = new EditorViewModel(post, state, new FakePreviewLoader(600, 400), new ImmediateDispatcher()) { SaveDelay = TimeSpan.Zero };
        await editor.SetSourceAsync(source);
        if (!sourceExists) File.Delete(source);
        return (new PhoneExportViewModel(editor, state, new ImmediateDispatcher(), new Host(), new FakeTimeProvider()), editor);
    }

    [Fact]
    public async Task SendToPhone_LocksThePost_WhenTheSettingIsOn()
    {
        using var dir = new TempDir();
        var (vm, editor) = await Create(dir, lockOnExport: true);
        await vm.StartAsync();
        Assert.True(editor.IsLocked);
    }

    [Fact]
    public async Task ExportToFolder_LocksThePost_WhenTheSettingIsOn()
    {
        using var dir = new TempDir();
        var (vm, editor) = await Create(dir, lockOnExport: true);
        Directory.CreateDirectory(dir.File("out"));
        await vm.ExportToFolderAsync(dir.File("out"));
        Assert.True(editor.IsLocked);
    }

    [Fact]
    public async Task Export_DoesNotLock_WhenTheSettingIsOff()
    {
        using var dir = new TempDir();
        var (vm, editor) = await Create(dir, lockOnExport: false);
        await vm.StartAsync();
        Assert.False(editor.IsLocked);
    }

    [Fact]
    public async Task FailedExport_DoesNotLock()
    {
        using var dir = new TempDir();
        var (vm, editor) = await Create(dir, lockOnExport: true, sourceExists: false);
        await vm.StartAsync();
        Assert.True(vm.HasError);
        Assert.False(editor.IsLocked);
    }

    [Fact]
    public void Settings_SavesLockOnExport()
    {
        using var dir = new TempDir();
        var state = Sample.State(dir);
        new SettingsViewModel(state) { LockOnExport = true }.Save();
        Assert.True(AppState.Load(state.Store).Settings.LockOnExport);
    }
}
