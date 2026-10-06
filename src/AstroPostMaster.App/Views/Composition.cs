using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Core.Storage;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Styling;

namespace AstroPostMaster.App.Views;

/// <summary>Composition root: loads the data folder, builds services and the main window.</summary>
internal static class Composition
{
    public static Window CreateMainWindow(string[] args)
    {
        var state = AppState.Load(new AppStore(AppPaths.ForCurrentUser()));
        ApplyTheme(state.Settings.Theme);
        var ui = new AvaloniaUiDispatcher();
        var vm = new MainWindowViewModel(state, new AvaloniaPreviewLoader(), ui);
        var window = new MainWindow(vm, ui);
        // Safety net: an unexpected exception in a UI handler is reported instead of closing the app.
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            e.Handled = true;
            Console.Error.WriteLine(e.Exception);
            vm.StatusMessage = $"Something went wrong: {e.Exception.Message}";
        };
        window.Opened += async (_, _) =>
        {
            await vm.InitializeAsync();
            if (args.FirstOrDefault(File.Exists) is { } file) await vm.OpenImageAsync(file);
        };
        return window;
    }

    public static void ApplyTheme(string theme)
    {
        if (Application.Current is { } app)
            app.RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark;
    }
}
