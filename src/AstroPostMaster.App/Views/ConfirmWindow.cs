using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AstroPostMaster.App.Views;

/// <summary>A small yes/no dialog built in code (no extra XAML for a three-control window).</summary>
internal static class ConfirmWindow
{
    public static Task<bool> Ask(Window owner, string title, string message, string confirm)
    {
        var window = new Window
        {
            Title = title,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        var yes = new Button { Content = confirm };
        var no = new Button { Content = "Cancel" };
        yes.Click += (_, _) => window.Close(true);
        no.Click += (_, _) => window.Close(false);
        window.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, Classes = { "title" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new TextBlock { Text = message, Classes = { "muted" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { no, yes } },
            },
        };
        return window.ShowDialog<bool>(owner);
    }
}
