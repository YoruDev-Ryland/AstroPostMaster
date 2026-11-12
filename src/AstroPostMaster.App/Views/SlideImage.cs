using AstroPostMaster.Core.Model;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AstroPostMaster.App.Views;

/// <summary>
/// Draws one frame of the preview, rotated upright and scaled to fit (thumbnails and the slideshow).
/// A Full slide draws the whole image.
/// </summary>
public sealed class SlideImage : Control
{
    public static readonly StyledProperty<object?> SourceProperty = AvaloniaProperty.Register<SlideImage, object?>(nameof(Source));
    public static readonly StyledProperty<RectF> RectProperty = AvaloniaProperty.Register<SlideImage, RectF>(nameof(Rect));
    public static readonly StyledProperty<double> AngleProperty = AvaloniaProperty.Register<SlideImage, double>(nameof(Angle));
    public static readonly StyledProperty<SlideKind> KindProperty = AvaloniaProperty.Register<SlideImage, SlideKind>(nameof(Kind));

    static SlideImage()
    {
        AffectsRender<SlideImage>(SourceProperty, RectProperty, AngleProperty, KindProperty);
    }

    public SlideImage() => RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);

    public object? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public RectF Rect { get => GetValue(RectProperty); set => SetValue(RectProperty, value); }
    public double Angle { get => GetValue(AngleProperty); set => SetValue(AngleProperty, value); }
    public SlideKind Kind { get => GetValue(KindProperty); set => SetValue(KindProperty, value); }

    public override void Render(DrawingContext context)
    {
        if (Source is not Bitmap bitmap || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var size = bitmap.Size;
        var bounds = new Rect(Bounds.Size);

        if (Kind == SlideKind.Full || Rect.W <= 0 || Rect.H <= 0)
        {
            var s = Math.Min(bounds.Width / size.Width, bounds.Height / size.Height);
            var (w, h) = (size.Width * s, size.Height * s);
            context.DrawImage(bitmap, new Rect(size), new Rect((bounds.Width - w) / 2, (bounds.Height - h) / 2, w, h));
            return;
        }

        var frameWidth = Rect.W * size.Width;
        var frameHeight = Rect.H * size.Height;
        var scale = Math.Min(bounds.Width / frameWidth, bounds.Height / frameHeight);
        var (dw, dh) = (frameWidth * scale, frameHeight * scale);
        var dest = new Rect((bounds.Width - dw) / 2, (bounds.Height - dh) / 2, dw, dh);

        // Source pixels → about the frame centre → turned back by the frame's angle → scaled into dest.
        var transform = Matrix.CreateTranslation(-Rect.CenterX * size.Width, -Rect.CenterY * size.Height)
                        * Matrix.CreateRotation(-Angle * Math.PI / 180)
                        * Matrix.CreateScale(scale, scale)
                        * Matrix.CreateTranslation(dest.Center.X, dest.Center.Y);
        using (context.PushClip(dest))
        using (context.PushTransform(transform))
            context.DrawImage(bitmap, new Rect(size), new Rect(size));
    }
}
