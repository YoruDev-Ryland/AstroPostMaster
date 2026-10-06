using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Slides;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

public sealed partial class SlideViewModel(Slide model) : ObservableObject
{
    [ObservableProperty] public partial Slide Model { get; internal set; } = model;
    [ObservableProperty] public partial string Label { get; internal set; } = "";
    [ObservableProperty] public partial bool IsSelected { get; internal set; }
    /// <summary>Filmstrip thumbnail width at <see cref="ThumbHeight"/>, matching the slide (or panorama strip) shape.</summary>
    [ObservableProperty] public partial double ThumbWidth { get; internal set; } = ThumbHeight * 0.8;

    public const double ThumbHeight = 84;

    public SlideKind Kind => Model.Kind;
    public RectF Rect => Model.Rect;
    public int Panels => Model.Kind == SlideKind.Panorama ? Math.Max(1, Model.Panels) : 1;
    public bool IsFull => Model.Kind == SlideKind.Full;

    public IReadOnlyList<RectF> PanelRects =>
        Kind == SlideKind.Panorama ? SlideGeometry.SplitPanorama(Rect, Panels) : [Rect];

    partial void OnModelChanged(Slide value)
    {
        OnPropertyChanged(nameof(Kind));
        OnPropertyChanged(nameof(Rect));
        OnPropertyChanged(nameof(Panels));
        OnPropertyChanged(nameof(PanelRects));
    }
}
