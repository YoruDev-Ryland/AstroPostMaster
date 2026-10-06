using AstroPostMaster.App.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AstroPostMaster.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow() : this(null!) { } // designer

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            _vm.Save();
            Close(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorText.Text = $"Couldn't save settings: {ex.Message}";
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    // A checked "default" box makes that profile the only default of its kind.
    private void OnSiteDefault(object? sender, RoutedEventArgs e) { if (_vm.SelectedSite is { IsDefault: true } s) _vm.SetDefaultSiteCommand.Execute(s); }
    private void OnRigDefault(object? sender, RoutedEventArgs e) { if (_vm.SelectedRig is { IsDefault: true } r) _vm.SetDefaultRigCommand.Execute(r); }
    private void OnSoftwareDefault(object? sender, RoutedEventArgs e) { if (_vm.SelectedSoftware is { IsDefault: true } w) _vm.SetDefaultSoftwareCommand.Execute(w); }

    private async void OnBrowseFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Default image folder" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is { } path) _vm.DefaultBrowseFolder = path;
    }

    private async void OnCheckConnection(object? sender, RoutedEventArgs e) => await _vm.CheckPhoneConnectionAsync();

    private async void OnFixConnection(object? sender, RoutedEventArgs e) => await _vm.FixConnectionAsync();

    private async void OnKoFi(object? sender, RoutedEventArgs e) => await Launcher.LaunchUriAsync(new Uri(SettingsViewModel.KoFiUrl));
}
