using AstroPostMaster.App.Services;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

/// <summary>One slide as it will be posted; panorama panels are separate items.</summary>
public sealed record SlideshowItem(SlideKind Kind, RectF Rect);

/// <summary>Plays the post's slides in order: each fades in and holds for <see cref="Interval"/>; arrows jump at once.</summary>
public sealed partial class SlideshowViewModel : ObservableObject
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);

    private readonly EditorViewModel _editor;
    private readonly IUiDispatcher _ui;
    private readonly TimeProvider _time;
    private ITimer? _timer;

    public SlideshowViewModel(EditorViewModel editor, IUiDispatcher ui, TimeProvider time)
    {
        _editor = editor;
        _ui = ui;
        _time = time;
        Items = editor.Slides.SelectMany(s => s.Kind switch
        {
            SlideKind.Panorama => s.PanelRects.Select(r => new SlideshowItem(SlideKind.Crop, r)),
            _ => [new SlideshowItem(s.Kind, s.Rect)],
        }).ToList();
        Ratio = AspectSizes.Ratio(editor.Aspect);
    }

    public IReadOnlyList<SlideshowItem> Items { get; }
    public int Count => Items.Count;
    public double Ratio { get; }
    public object? Preview => _editor.Preview;

    /// <summary>Fixed design size of a slide; the view scales it to fit the window.</summary>
    public double FrameWidth => 1080;
    public double FrameHeight => 1080 / Ratio;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Current), nameof(Position))]
    public partial int Index { get; private set; }

    [ObservableProperty] public partial bool IsPlaying { get; private set; }

    /// <summary>True when the latest slide change came from an arrow key (the view uses a quicker fade).</summary>
    public bool LastChangeWasManual { get; private set; }

    public SlideshowItem? Current => Count == 0 ? null : Items[Index];
    public string Position => Count == 0 ? "" : $"{Index + 1} / {Count}";

    public event Action? Closed;

    public void Start()
    {
        IsPlaying = Count > 1;
        RestartTimer();
    }

    public void Next() => Step(+1);

    public void Previous() => Step(-1);

    public void Close()
    {
        IsPlaying = false;
        _timer?.Dispose();
        _timer = null;
        Closed?.Invoke();
    }

    private void Step(int delta)
    {
        var target = Math.Clamp(Index + delta, 0, Math.Max(0, Count - 1));
        if (target != Index)
        {
            LastChangeWasManual = true;
            Index = target;
        }
        IsPlaying = Index < Count - 1;
        RestartTimer();
    }

    private void RestartTimer()
    {
        _timer?.Dispose();
        _timer = IsPlaying ? _time.CreateTimer(_ => _ui.Post(Tick), null, Interval, Interval) : null;
    }

    private void Tick()
    {
        if (!IsPlaying) return;
        if (Index < Count - 1)
        {
            LastChangeWasManual = false;
            Index++;
        }
        if (Index >= Count - 1)
        {
            IsPlaying = false;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
