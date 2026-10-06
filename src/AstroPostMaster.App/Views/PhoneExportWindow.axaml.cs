using AstroPostMaster.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AstroPostMaster.App.Views;

public partial class PhoneExportWindow : Window
{
    private readonly PhoneExportViewModel _vm;

    public PhoneExportWindow() : this(null!) { } // designer

    public PhoneExportWindow(PhoneExportViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        Opened += async (_, _) => await _vm.StartAsync();
        Closing += async (_, _) => await _vm.CloseAsync();
    }

    private async void OnCopyLink(object? sender, RoutedEventArgs e)
    {
        if (_vm.Url is { } url && Clipboard is { } clipboard) await clipboard.SetTextAsync(url.ToString());
    }

    private void OnDone(object? sender, RoutedEventArgs e) => Close();

    private async void OnFixFirewall(object? sender, RoutedEventArgs e) => await _vm.FixFirewallAsync();

    private async void OnCopyFirewallCommand(object? sender, RoutedEventArgs e)
    {
        if (_vm.FirewallManualCommand is { } command && Clipboard is { } clipboard) await clipboard.SetTextAsync(command);
    }
}
