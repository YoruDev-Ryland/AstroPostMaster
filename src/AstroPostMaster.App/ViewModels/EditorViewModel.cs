using System.Collections.ObjectModel;
using AstroPostMaster.App.Services;
using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using AstroPostMaster.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroPostMaster.App.ViewModels;

/// <summary>One post being edited: source image, slides, caption, warnings and debounced autosave.</summary>
public sealed partial class EditorViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly IPreviewLoader _loader;
    private readonly IUiDispatcher _ui;
    private CancellationTokenSource? _pendingSave;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _sourceLoaded;
    private bool _dirty;
    private bool _disposed;

    public EditorViewModel(Post post, AppState state, IPreviewLoader loader, IUiDispatcher ui)
    {
        Post = post;
        _state = state;
        _loader = loader;
        _ui = ui;
        foreach (var slide in post.Slides) Slides.Add(new SlideViewModel(slide));
        Renumber();
        Caption = new CaptionViewModel(post.Caption, state, Changed, () => Post.IsLocked);
        Recompute();
    }

    public Post Post { get; }
    public AppState State => _state;
    public CaptionViewModel Caption { get; }
    public ObservableCollection<SlideViewModel> Slides { get; } = [];
    public event Action? Saved;
    /// <summary>Raised whenever frames, their selection or the source change, so the canvas can redraw.</summary>
    public event Action? FramesChanged;

    public static IReadOnlyList<AspectOption> AspectOptions => AspectOption.All;
    public static IReadOnlyList<ExportSizeOption> ExportSizeOptions => ExportSizeOption.All;

    public int AspectIndex
    {
        get => AspectSizes.All.ToList().IndexOf(Aspect);
        set { if (value >= 0 && value < AspectSizes.All.Count) Aspect = AspectSizes.All[value]; }
    }

    /// <summary>The post's own size if set, else the Settings default.</summary>
    public ExportSize ExportSize
    {
        get => Post.ExportSize ?? _state.Settings.ExportSize;
        set
        {
            if (Post.ExportSize == value) return;
            if (IsLocked) { RaiseExportSize(); return; }
            Post.ExportSize = value;
            RememberExportSize(value);
            RaiseExportSize();
            Changed();
        }
    }

    public int ExportSizeIndex
    {
        get => ExportSizeOption.IndexOf(ExportSize);
        set { if (value >= 0 && value < ExportSizeOption.All.Count) ExportSize = ExportSizeOption.All[value].Size; }
    }

    public string ExportSizeLabel => ExportSizeOption.All[ExportSizeIndex].Label;

    /// <summary>The last size picked becomes the default for new posts.</summary>
    private void RememberExportSize(ExportSize size)
    {
        if (_state.Settings.ExportSize == size) return;
        _state.Settings.ExportSize = size;
        try { _state.SaveSettings(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* still remembered for this session */ }
    }

    private void RaiseExportSize()
    {
        OnPropertyChanged(nameof(ExportSize));
        OnPropertyChanged(nameof(ExportSizeIndex));
        OnPropertyChanged(nameof(ExportSizeLabel));
    }

    /// <summary>Delay between the last edit and writing post.json. Zero saves immediately.</summary>
    public TimeSpan SaveDelay { get; set; } = TimeSpan.FromMilliseconds(500);
    internal int SaveCount { get; private set; }

    [ObservableProperty] public partial object? Preview { get; private set; }
    [ObservableProperty] public partial int SourceWidth { get; private set; }
    [ObservableProperty] public partial int SourceHeight { get; private set; }
    [ObservableProperty] public partial bool SourceMissing { get; private set; }
    [ObservableProperty] public partial string? LoadError { get; private set; }
    [ObservableProperty] public partial string? SaveError { get; private set; }

    /// <summary>Index for <see cref="MoveSlide"/> when dropping slide <paramref name="from"/> into insertion slot <paramref name="slot"/>.</summary>
    public static int DropIndex(int from, int slot) => slot > from ? slot - 1 : slot;
    [ObservableProperty] public partial SlideViewModel? SelectedSlide { get; set; }
    [ObservableProperty] public partial IReadOnlyList<PostWarning> Warnings { get; private set; } = [];
    [ObservableProperty] public partial int CharacterCount { get; private set; }
    [ObservableProperty] public partial int HashtagCount { get; private set; }

    public bool HasSource => SourceWidth > 0;
    public string CaptionText => Caption.Preview;
    public int SlideCount => PostValidator.SlideCount(Post);
    public bool TooManyHashtags => HashtagCount > Hashtags.InstagramLimit;
    public bool TooLong => CharacterCount > CaptionStats.MaxCharacters;
    public bool TooManySlides => SlideCount > PostValidator.MaxSlides;
    public string AspectLabel => AspectSizes.Label(Aspect);

    public string Title
    {
        get => Post.Title;
        set
        {
            if (Post.Title == value) return;
            if (IsLocked) { OnPropertyChanged(); return; }
            Post.Title = value;
            OnPropertyChanged();
            Changed();
        }
    }

    /// <summary>A locked post is read-only: slides, image, shape, size, title and caption.</summary>
    public bool IsLocked
    {
        get => Post.IsLocked;
        set
        {
            if (Post.IsLocked == value) return;
            Post.IsLocked = value;
            OnPropertyChanged();
            FramesChanged?.Invoke();
            Changed();
        }
    }

    public AspectRatio Aspect
    {
        get => Post.Aspect;
        set
        {
            if (Post.Aspect == value) return;
            if (IsLocked) { OnPropertyChanged(); OnPropertyChanged(nameof(AspectIndex)); return; }
            Post.Aspect = value;
            Refit();
            OnPropertyChanged();
            OnPropertyChanged(nameof(AspectLabel));
            OnPropertyChanged(nameof(AspectIndex));
            Renumber();
            FramesChanged?.Invoke();
            Changed();
        }
    }

    partial void OnSelectedSlideChanged(SlideViewModel? oldValue, SlideViewModel? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        FramesChanged?.Invoke();
    }

    partial void OnSourceWidthChanged(int value) => OnPropertyChanged(nameof(HasSource));

    // ---------- source ----------

    /// <summary>Loads the current source's preview. A missing or unreadable file sets SourceMissing/LoadError.</summary>
    public async Task LoadSourceAsync()
    {
        if (Post.SourcePath is null) return;
        try
        {
            var result = await _loader.LoadAsync(Post.SourcePath, _lifetime.Token);
            _ui.Post(() =>
            {
                if (_disposed) { (result.Image as IDisposable)?.Dispose(); return; }
                ApplySource(result);
            });
        }
        catch (OperationCanceledException)
        {
            // Post switched away while loading.
        }
        catch (FileNotFoundException)
        {
            _ui.Post(() =>
            {
                SourceMissing = true;
                _sourceLoaded = true;
                LoadError = $"Source image not found: {Post.SourcePath}. Open or drop the image to relink it.";
                Recompute();
                FramesChanged?.Invoke();
            });
        }
        catch (Exception ex) when (ex is ImageLoadException or IOException or UnauthorizedAccessException)
        {
            _ui.Post(() => { LoadError = ex.Message; _sourceLoaded = true; Recompute(); FramesChanged?.Invoke(); });
        }
    }

    /// <summary>Sets or relinks the source image. Frames are re-fitted to the new image. Throws if unreadable.</summary>
    public async Task SetSourceAsync(string path)
    {
        var result = await _loader.LoadAsync(path, _lifetime.Token);
        _ui.Post(() =>
        {
            if (_disposed) { (result.Image as IDisposable)?.Dispose(); return; }
            Post.SourcePath = path;
            ApplySource(result);
            Refit();
            if (Post.Title == "Untitled") Title = Path.GetFileNameWithoutExtension(path);
            Caption.TryAutoTarget(Path.GetFileNameWithoutExtension(path));
            Changed();
        });
    }

    /// <summary>Swaps in a different image, keeping every slide (re-fitted to the new image). Ignored when locked.</summary>
    public Task ReplaceImageAsync(string path) => IsLocked ? Task.CompletedTask : SetSourceAsync(path);

    private void ApplySource(PreviewResult result)
    {
        var old = Preview;
        Preview = result.Image;
        if (!ReferenceEquals(old, result.Image)) _ui.Post(() => (old as IDisposable)?.Dispose());
        SourceWidth = result.SourceWidth;
        SourceHeight = result.SourceHeight;
        SourceMissing = false;
        LoadError = null;
        _sourceLoaded = true;
        // Frames may predate an aspect change made while the source was unavailable.
        if (Refit()) Changed();
        Recompute();
        FramesChanged?.Invoke();
    }

    // ---------- slides ----------

    [RelayCommand]
    private void AddCrop()
    {
        if (!HasSource || IsLocked) return;
        Insert(new Slide(SlideKind.Crop, SlideGeometry.DefaultCrop(Aspect, SourceWidth, SourceHeight)));
    }

    [RelayCommand]
    private void AddPanorama(int panels)
    {
        if (!HasSource || IsLocked) return;
        panels = Math.Clamp(panels, 2, 5);
        Insert(new Slide(SlideKind.Panorama, SlideGeometry.DefaultPanorama(Aspect, panels, SourceWidth, SourceHeight), panels));
    }

    [RelayCommand]
    private void AddFullImage()
    {
        if (IsLocked || Slides.Any(s => s.IsFull)) return;
        var slide = new SlideViewModel(Slide.FullImage);
        Slides.Add(slide);
        SelectedSlide = slide;
        SlidesChanged();
    }

    [RelayCommand]
    private void RemoveSelectedSlide()
    {
        if (IsLocked || SelectedSlide is not { } slide) return;
        var index = Slides.IndexOf(slide);
        Slides.RemoveAt(index);
        SelectedSlide = Slides.Count == 0 ? null : Slides[Math.Min(index, Slides.Count - 1)];
        SlidesChanged();
    }

    public void MoveSlide(int from, int to)
    {
        if (IsLocked || from == to || from < 0 || to < 0 || from >= Slides.Count || to >= Slides.Count) return;
        Slides.Move(from, to);
        SlidesChanged();
    }

    /// <summary>Applies a proposed frame (from dragging) after locking its ratio and clamping it inside the image.</summary>
    public void UpdateFrame(SlideViewModel slide, RectF proposed)
    {
        if (slide.IsFull || !HasSource || IsLocked) return;
        var fitted = SlideGeometry.FitLocked(proposed, SlideGeometry.FrameRatio(Aspect, slide.Panels), SourceWidth, SourceHeight);
        if (fitted == slide.Rect) return;
        slide.Model = slide.Model with { Rect = fitted };
        SlidesChanged();
    }

    private void Insert(Slide model)
    {
        var slide = new SlideViewModel(model);
        var index = Slides.Count > 0 && Slides[^1].IsFull ? Slides.Count - 1 : Slides.Count;
        Slides.Insert(index, slide);
        SelectedSlide = slide;
        SlidesChanged();
    }

    /// <summary>Re-fits every frame to the current aspect and image. Returns true if any frame moved.</summary>
    private bool Refit()
    {
        if (!HasSource) return false;
        var changed = false;
        foreach (var slide in Slides.Where(s => !s.IsFull))
        {
            var fitted = SlideGeometry.FitLocked(slide.Rect, SlideGeometry.FrameRatio(Aspect, slide.Panels), SourceWidth, SourceHeight);
            if (Same(fitted, slide.Rect)) continue;
            slide.Model = slide.Model with { Rect = fitted };
            changed = true;
        }
        if (changed) SyncModel();
        return changed;
    }

    private static bool Same(RectF a, RectF b) =>
        Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6 && Math.Abs(a.W - b.W) < 1e-6 && Math.Abs(a.H - b.H) < 1e-6;

    private void SlidesChanged()
    {
        SyncModel();
        Renumber();
        FramesChanged?.Invoke();
        OnPropertyChanged(nameof(SlideCount));
        OnPropertyChanged(nameof(TooManySlides));
        Changed();
    }

    private void SyncModel() => Post.Slides = Slides.Select(s => s.Model).ToList();

    private void Renumber()
    {
        var number = 1;
        foreach (var slide in Slides)
        {
            slide.Label = slide.Panels > 1 ? $"{number}–{number + slide.Panels - 1}" : number.ToString();
            slide.ThumbWidth = SlideViewModel.ThumbHeight * AspectSizes.Ratio(Aspect) * slide.Panels;
            number += slide.Panels;
        }
    }

    // ---------- caption, warnings, saving ----------

    /// <summary>Profiles or settings were edited elsewhere: re-resolve them and rebuild the caption.</summary>
    public void SettingsChanged()
    {
        RaiseExportSize();
        Caption.Reload();
        Recompute();
    }

    internal void Changed()
    {
        Recompute();
        ScheduleSave();
    }

    private void Recompute()
    {
        OnPropertyChanged(nameof(CaptionText));
        CharacterCount = CaptionStats.CharacterCount(CaptionText);
        HashtagCount = CaptionStats.HashtagCount(CaptionText);
        OnPropertyChanged(nameof(TooManyHashtags));
        OnPropertyChanged(nameof(TooLong));

        (int, int)? size = HasSource ? (SourceWidth, SourceHeight) : null;
        var warnings = PostValidator.Validate(Post, CaptionText, size, ExportSize);
        // Until the first load attempt finishes, "missing" just means "not loaded yet".
        Warnings = _sourceLoaded ? warnings : warnings.Where(w => w.Kind != WarningKind.SourceMissing).ToList();
    }

    private void ScheduleSave()
    {
        if (_disposed) return;
        _pendingSave?.Cancel();
        if (SaveDelay <= TimeSpan.Zero)
        {
            SaveNow();
            return;
        }
        var cts = _pendingSave = new CancellationTokenSource();
        _ = Task.Delay(SaveDelay, cts.Token).ContinueWith(
            t => { if (!t.IsCanceled) _ui.Post(() => { if (ReferenceEquals(_pendingSave, cts)) SaveNow(); }); },
            TaskScheduler.Default);
    }

    /// <summary>Writes any pending edit now (call before switching posts or closing).</summary>
    public Task FlushAsync()
    {
        if (_pendingSave is not null || _dirty) SaveNow();
        return Task.CompletedTask;
    }

    /// <summary>The post is no longer shown: stop loading, drop pending saves and free the preview bitmap.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        DiscardPendingSave();
        var old = Preview;
        Preview = null;
        _ui.Post(() => (old as IDisposable)?.Dispose());
    }

    /// <summary>Drops a pending save (the post is being deleted).</summary>
    public void DiscardPendingSave()
    {
        _pendingSave?.Cancel();
        _pendingSave = null;
    }

    private void SaveNow()
    {
        _pendingSave?.Cancel();
        _pendingSave = null;
        if (_disposed) return;
        try
        {
            _state.Store.SavePost(Post);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Keep the edit pending; the next change or flush retries.
            _dirty = true;
            SaveError = $"Couldn't save this post: {ex.Message}";
            return;
        }
        _dirty = false;
        SaveError = null;
        SaveCount++;
        Saved?.Invoke();
    }
}
