// Renders the app headlessly to PNG: README images, demo-animation frames, and UI regression checks.
// Usage: dotnet run --project tools/AstroPostMaster.Screenshots -- <outDir> <folder with images>
// The folder needs: Heart Nebula*.jpg, Andromeda.jpg, Rosette Nebula.jpg (any astrophotos with those names).
using System.Diagnostics;
using System.Net;
using AstroPostMaster.App;
using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.App.Views;
using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Storage;
using AstroPostMaster.Handoff;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

if (args.Length < 2)
{
    Console.Error.WriteLine("usage: <outDir> <imageFolder>");
    return 2;
}
var outDir = Path.GetFullPath(args[0]);
var images = Path.GetFullPath(args[1]);
var framesDir = Path.Combine(outDir, "frames");
Directory.CreateDirectory(framesDir);
string Image(string prefix) => Directory.GetFiles(images).Where(f => Path.GetFileName(f).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    .OrderBy(f => f.Length).First();

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .WithInterFont()
    .SetupWithoutStarting();

void Pump(Func<bool> done, int timeoutMs = 30000)
{
    var sw = Stopwatch.StartNew();
    while (!done() && sw.ElapsedMilliseconds < timeoutMs) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); }
    for (var i = 0; i < 10; i++) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(10); }
}
void Wait(Task t) => Pump(() => t.IsCompleted);
void Shot(Window w, string name)
{
    Pump(() => false, 250);
    w.CaptureRenderedFrame()?.Save(Path.Combine(outDir, name));
    Console.WriteLine($"wrote {name}");
}
var frame = 0;
void Frame(Window w, int repeat = 1)
{
    Pump(() => false, 120);
    var bitmap = w.CaptureRenderedFrame();
    for (var i = 0; i < repeat; i++) bitmap?.Save(Path.Combine(framesDir, $"{frame++:000}.png"));
}

AppState NewState(bool profiles)
{
    var dir = Path.Combine(Path.GetTempPath(), "apm-shots-" + Guid.NewGuid().ToString("N"));
    var store = new AppStore(new AppPaths(dir));
    if (profiles)
    {
        store.SaveSites([new Site("s1", "SFRO", 1, IsDefault: true), new Site("s2", "Backyard", 6)]);
        store.SaveRigs([new Rig("r1", "Starlux 190", "SkyWatcher Starlux 190mn", "ASI2600mm Pro", "Scorpio 3nm LRGBSHO", "ZWO AM5", IsDefault: true)]);
        store.SaveSoftware([new SoftwareSet("w1", "Usual workflow", ["Siril - Stacking, Mosaic Stitching", "PixInsight - Processing", "Photoshop - Recomposition"], IsDefault: true)]);
        store.SaveHashtagSets([
            new HashtagSet("h1", "Core", ["#astrophotography", "#astronomy", "#deepsky", "#space"], IsDefault: true),
            new HashtagSet("h2", "Narrowband", ["#narrowband", "#nebula", "#sho"]),
            new HashtagSet("h3", "Galaxy", ["#galaxy"])]);
    }
    return AppState.Load(store);
}

var ui = new AvaloniaUiDispatcher();

// ---------- first run ----------
{
    var state = NewState(profiles: false);
    var vm = new MainWindowViewModel(state, new AvaloniaPreviewLoader(), ui);
    var window = new MainWindow(vm, ui) { Width = 1440, Height = 900 };
    window.Show();
    Wait(vm.InitializeAsync());
    Shot(window, "first-run.png");
    window.Close();
}

// ---------- main editor ----------
var state0 = NewState(profiles: true);
foreach (var (title, file, days) in new[] { ("Rosette Nebula", "Rosette Nebula.jpg", 9), ("Veil Nebula", "Veil Nebula.jpg", 21), ("Soul Nebula", "Soul Nebula-0428262309.jpg", 34), ("Orion Nebula", "Orion Nebula-HOO.jpg", 52) })
{
    var post = PostFactory.CreateNew([.. state0.Sites], [.. state0.Rigs], [.. state0.Software], [.. state0.HashtagSets]);
    post.Title = title;
    post.SourcePath = Path.Combine(images, file);
    post.Slides = [new Slide(SlideKind.Crop, new RectF(0.35, 0.25, 0.2, 0.4)), Slide.FullImage];
    state0.Store.SavePost(post);
    var saved = JsonFile.LoadOrDefault<Post?>(state0.Store.Paths.PostFile(post.Id), () => null)!;
    saved.UpdatedUtc = DateTimeOffset.UtcNow.AddDays(-days);
    JsonFile.SaveAtomic(state0.Store.Paths.PostFile(post.Id), saved);
}

var vm0 = new MainWindowViewModel(state0, new AvaloniaPreviewLoader(), ui);
var main = new MainWindow(vm0, ui) { Width = 1440, Height = 900 };
main.Show();
Wait(vm0.InitializeAsync());
Wait(vm0.OpenImageAsync(Image("Heart Nebula")));
Pump(() => vm0.Editor?.HasSource == true);
var editor = vm0.Editor!;
editor.Title = "Heart Nebula";
editor.AddCropCommand.Execute(null);
editor.UpdateFrame(editor.Slides[0], new RectF(0.39, 0.22, 0.2, 0.46));
editor.AddPanoramaCommand.Execute(2);
editor.SelectedSlide = editor.Slides[0];
var caption = editor.Caption;
foreach (var key in new[] { "Ha", "OIII", "SII" }) caption.NarrowbandChips.Single(c => c.Key == key).IsSelected = true;
foreach (var (filter, subs) in new[] { ("Hα", 60), ("OIII", 50), ("SII", 50) })
{
    var row = caption.AddIntegrationRow();
    row.Filter = filter;
    row.Subs = subs;
    row.SubSeconds = 300;
}
caption.DatesText = "2026-09-12, 2026-09-13, 2026-09-14";
caption.Description = "Three nights under Bortle 1 skies.";
caption.HashtagSets.Single(h => h.Key == "h2").IsSelected = true;
Shot(main, "editor.png");

Composition.ApplyTheme("Light");
Shot(main, "editor-light.png");
Composition.ApplyTheme("Dark");

// Toolbar regression: the canvas must not swallow toolbar clicks.
var aspectBox = main.FindControl<ComboBox>("AspectBox")!;
var openLabel = main.GetVisualDescendants().OfType<TextBlock>().First(t => t.Text == "Open image");
foreach (var (name, control) in new (string, Visual)[] { ("aspect selector", aspectBox), ("Open image", openLabel) })
{
    var at = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), main)!.Value;
    var hit = main.InputHitTest(at) as Visual;
    if (hit is CropCanvas || hit?.FindAncestorOfType<CropCanvas>() is not null)
        throw new InvalidOperationException($"Toolbar control '{name}' is covered by the crop canvas.");
}

// ---------- demo animation ----------
var canvas = main.GetVisualDescendants().OfType<CropCanvas>().Single();
Point ToWindow(double fx, double fy)
{
    var inset = new Rect(canvas.Bounds.Size).Deflate(16);
    var scale = Math.Min(inset.Width / editor.SourceWidth, inset.Height / editor.SourceHeight);
    var (w, h) = (editor.SourceWidth * scale, editor.SourceHeight * scale);
    var local = new Point(inset.X + (inset.Width - w) / 2 + fx * w, inset.Y + (inset.Height - h) / 2 + fy * h);
    return canvas.TranslatePoint(local, main)!.Value;
}
void Drag(Point from, Point to, int steps)
{
    main.MouseDown(from, MouseButton.Left);
    for (var i = 1; i <= steps; i++)
    {
        main.MouseMove(from + (to - from) * ((double)i / steps));
        Frame(main);
    }
    main.MouseUp(to, MouseButton.Left);
}

Frame(main, repeat: 8);
var r = editor.Slides[0].Rect;
Drag(ToWindow(r.CenterX, r.CenterY), ToWindow(r.CenterX - 0.12, r.CenterY + 0.05), 10);
Frame(main, repeat: 3);
r = editor.Slides[0].Rect;
Drag(ToWindow(r.Right, r.Bottom), ToWindow(r.Right + 0.06, r.Bottom + 0.12), 8);
Frame(main, repeat: 4);
editor.SelectedSlide = editor.Slides[1];
Frame(main, repeat: 8);
foreach (var aspect in new[] { AspectRatio.Square, AspectRatio.Landscape16x9, AspectRatio.Portrait4x5 })
{
    editor.Aspect = aspect;
    Frame(main, repeat: 6);
}
editor.SelectedSlide = editor.Slides[0];
Frame(main, repeat: 8);
Console.WriteLine($"wrote {frame} demo frames");

// ---------- panorama & landscape ----------
Wait(vm0.OpenImageAsync(Image("Andromeda")));
Pump(() => vm0.Editor?.Post.Title == "Andromeda" && vm0.Editor.HasSource);
var andromeda = vm0.Editor!;
andromeda.AddPanoramaCommand.Execute(3);
andromeda.AddCropCommand.Execute(null);
andromeda.UpdateFrame(andromeda.Slides[1], new RectF(0.43, 0.32, 0.14, 0.36));
andromeda.SelectedSlide = andromeda.Slides[0];
andromeda.Caption.HashtagSets.Single(h => h.Key == "h3").IsSelected = true;
Shot(main, "panorama.png");

andromeda.Aspect = AspectRatio.Landscape16x9;
andromeda.ExportSize = ExportSize.LongEdge2048;
andromeda.SelectedSlide = andromeda.Slides[1];
Shot(main, "landscape.png");

// ---------- settings ----------
var settings = new SettingsWindow(new SettingsViewModel(state0)) { Width = 820, Height = 620 };
settings.Show();
settings.GetVisualDescendants().OfType<TabControl>().Skip(1).First().SelectedIndex = 1; // Profiles → Rigs
Shot(settings, "settings.png");
settings.Close();

// ---------- send to phone (made-up address and code) ----------
vm0.SelectedPost = vm0.Posts.First(p => p.Title == "Heart Nebula");
Pump(() => vm0.Editor?.Post.Title == "Heart Nebula" && vm0.Editor.HasSource);
var phoneVm = new PhoneExportViewModel(vm0.Editor!, state0, ui, new ReadmeHost());
var phone = new PhoneExportWindow(phoneVm);
phone.Show();
Pump(() => phoneVm.Url is not null || phoneVm.HasError);
if (phoneVm.HasError) throw new InvalidOperationException(phoneVm.Status);
Shot(phone, "send-to-phone.png");
Console.WriteLine($"exported: {Path.GetDirectoryName(phoneVm.ExportedFiles[0])}");
File.WriteAllLines(Path.Combine(outDir, "exported.txt"), phoneVm.ExportedFiles);
File.WriteAllText(Path.Combine(outDir, "caption.txt"), vm0.Editor!.CaptionText);
phone.Close();
Pump(() => false, 300);
main.Close();
return 0;

/// <summary>Stands in for the phone server so README images never show a real address or code.</summary>
internal sealed class ReadmeHost : IHandoffHost
{
    public IReadOnlyList<IPAddress> Addresses() => [IPAddress.Parse("192.168.1.50")];

    public Task<IHandoffSession> StartAsync(HandoffPackage package, IPAddress address, bool https, CancellationToken ct = default) =>
        Task.FromResult<IHandoffSession>(new Session());

    private sealed class Session : IHandoffSession
    {
        public Uri Url { get; } = new("https://192.168.1.50:48443/m3Vq8xKp2LbT7wRz5nYcHd/");
        public bool HasReceivedRequest => false;
        public event Action? RequestReceived { add { } remove { } }
        public event Action? Stopped { add { } remove { } }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
