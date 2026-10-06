using AstroPostMaster.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AstroPostMaster.App.Views;

public partial class InspectorView : UserControl
{
    public InspectorView()
    {
        InitializeComponent();
        TargetBox.AsyncPopulator = (text, _) =>
            Task.FromResult<IEnumerable<object>>((DataContext as EditorViewModel)?.Caption.Suggest(text) ?? []);
        TargetBox.SelectionChanged += (_, _) =>
        {
            if (TargetBox.SelectedItem is TargetSuggestion suggestion && DataContext is EditorViewModel editor)
                editor.Caption.SelectTarget(suggestion.Entry);
        };
    }

    public event EventHandler<string>? StatusRequested;

    private void OnAddRow(object? sender, RoutedEventArgs e) => (DataContext as EditorViewModel)?.Caption.AddIntegrationRow();

    private void OnRemoveRow(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is IntegrationRowViewModel row)
            (DataContext as EditorViewModel)?.Caption.RemoveIntegrationRow(row);
    }

    private async void OnCopyCaption(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not EditorViewModel editor || TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(editor.CaptionText);
        StatusRequested?.Invoke(this, "Caption copied to the clipboard.");
    }
}
