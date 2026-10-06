using AstroPostMaster.App.Services;
using AstroPostMaster.App.ViewModels;
using AstroPostMaster.Handoff.Firewall;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace AstroPostMaster.App.Views;

public partial class MainWindow : Window
{
    private static readonly FilePickerFileType ImageFiles = new("Images")
    {
        Patterns = ["*.jpg", "*.jpeg", "*.png", "*.tif", "*.tiff"],
        MimeTypes = ["image/jpeg", "image/png", "image/tiff"],
    };

    private readonly MainWindowViewModel _vm;
    private readonly IUiDispatcher _ui;

    public MainWindow() : this(null!, null!) { } // designer

    public MainWindow(MainWindowViewModel vm, IUiDispatcher ui)
    {
        InitializeComponent();
        _vm = vm;
        _ui = ui;
        DataContext = vm;

        Canvas.OpenRequested += (_, _) => OnOpenImage(this, new RoutedEventArgs());
        Inspector.StatusRequested += (_, message) => _vm.StatusMessage = message;
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        KeyDown += OnKeyDown;
        KeyUp += OnKeyUp;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainWindowViewModel.Slideshow)) OnSlideshowChanged();
        };
        Closing += (_, _) => _vm.Editor?.FlushAsync();
    }

    public MainWindowViewModel ViewModel => _vm;

    // ---------- files ----------

    private async void OnOpenImage(object? sender, RoutedEventArgs e)
    {
        var options = new FilePickerOpenOptions { Title = "Open a finished image", AllowMultiple = false, FileTypeFilter = [ImageFiles] };
        if (_vm.State.Settings.DefaultBrowseFolder is { } folder && Directory.Exists(folder))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);
        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) await _vm.OpenImageAsync(path);
    }

    private void OnDragOver(object? sender, DragEventArgs e) =>
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var path = e.DataTransfer.TryGetFiles()?.Select(f => f.TryGetLocalPath()).FirstOrDefault(p => p is not null);
        if (path is not null) await _vm.OpenImageAsync(path);
    }

    // ---------- posts ----------

    private async void OnDeletePost(object? sender, RoutedEventArgs e)
    {
        if (_vm.SelectedPost is not { } post) return;
        var confirmed = await ConfirmWindow.Ask(this, $"Delete “{post.Title}”?",
            "The post and its exported slides are removed. Your original image is not touched.", "Delete");
        if (confirmed) _vm.DeletePostCommand.Execute(null);
    }

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        var settings = new SettingsWindow(new SettingsViewModel(_vm.State, FirewallService.ForCurrentSystem()));
        if (await settings.ShowDialog<bool>(this))
        {
            Composition.ApplyTheme(_vm.State.Settings.Theme);
            _vm.SettingsChanged();
        }
    }

    private async void OnReplaceImage(object? sender, RoutedEventArgs e)
    {
        if (_vm.Editor is not { HasSource: true, IsLocked: false } editor) return;
        var options = new FilePickerOpenOptions { Title = "Replace image", AllowMultiple = false, FileTypeFilter = [ImageFiles] };
        if (editor.Post.SourcePath is { } current && Path.GetDirectoryName(current) is { } folder && Directory.Exists(folder))
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(folder);
        var files = await StorageProvider.OpenFilePickerAsync(options);
        if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
        try
        {
            await editor.ReplaceImageAsync(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or AstroPostMaster.Imaging.ImageLoadException)
        {
            _vm.StatusMessage = ex.Message;
        }
    }

    // ---------- slideshow ----------

    private SlideshowViewModel? _show;
    private CancellationTokenSource? _fade;

    private void OnSlideshowChanged()
    {
        if (_show is not null) _show.PropertyChanged -= OnSlideshowPropertyChanged;
        _show = _vm.Slideshow;
        if (_show is null)
        {
            Canvas.Focus();
            return;
        }
        _show.PropertyChanged += OnSlideshowPropertyChanged;
        SlideshowOverlay.Focus();
        Fade(TimeSpan.FromMilliseconds(500));
    }

    private void OnSlideshowPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SlideshowViewModel.Index) && _show is not null)
            Fade(TimeSpan.FromMilliseconds(_show.LastChangeWasManual ? 160 : 500));
    }

    private void Fade(TimeSpan duration)
    {
        _fade?.Cancel();
        _fade = new CancellationTokenSource();
        var animation = new Animation
        {
            Duration = duration,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0d) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1d) } },
            },
        };
        _ = animation.RunAsync(SlideshowImage, _fade.Token);
    }

    private void OnSlideshowPointerPressed(object? sender, PointerPressedEventArgs e) => _vm.Slideshow?.Close();

    private void OnToggleSidebar(object? sender, RoutedEventArgs e) => Sidebar.IsVisible = !Sidebar.IsVisible;

    // ---------- export ----------

    private async void OnSendToPhone(object? sender, RoutedEventArgs e)
    {
        if (_vm.Editor is not { HasSource: true } editor) return;
        var vm = new PhoneExportViewModel(editor, _vm.State, _ui, new KestrelHandoffHost(_vm.State), firewall: FirewallService.ForCurrentSystem());
        await new PhoneExportWindow(vm).ShowDialog(this);
    }

    private async void OnExportToFolder(object? sender, RoutedEventArgs e)
    {
        if (_vm.Editor is not { HasSource: true } editor) return;
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Export slides to…" });
        if (folders.FirstOrDefault()?.TryGetLocalPath() is not { } folder) return;
        var vm = new PhoneExportViewModel(editor, _vm.State, _ui, new KestrelHandoffHost(_vm.State));
        _vm.StatusMessage = "Exporting…";
        await vm.ExportToFolderAsync(folder);
        _vm.StatusMessage = vm.Status;
    }

    private async void OnCopyCaption(object? sender, RoutedEventArgs e)
    {
        if (_vm.Editor is not { } editor || Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(editor.CaptionText);
        _vm.StatusMessage = "Caption copied to the clipboard.";
    }

    private async void OnSupport(object? sender, RoutedEventArgs e) =>
        await Launcher.LaunchUriAsync(new Uri(SettingsViewModel.KoFiUrl));

    // ---------- keyboard ----------

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm.Slideshow is { } show)
        {
            switch (e.Key)
            {
                case Key.Right or Key.Down or Key.Space or Key.PageDown: show.Next(); break;
                case Key.Left or Key.Up or Key.PageUp: show.Previous(); break;
                case Key.Escape or Key.F5: show.Close(); break;
            }
            e.Handled = true;
            return;
        }

        var typing = FocusManager?.GetFocusedElement() is TextBox;
        if (e.Key == Key.Space && !typing)
        {
            Canvas.PanModifier = true;
            e.Handled = true;
            return;
        }
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var editor = _vm.Editor;

        switch (e.Key)
        {
            case Key.N when ctrl: _vm.NewPostCommand.Execute(null); break;
            case Key.O when ctrl: OnOpenImage(this, e); break;
            case Key.E when ctrl: OnSendToPhone(this, e); break;
            case Key.OemComma when ctrl: OnOpenSettings(this, e); break;
            case Key.C when !ctrl && !typing && editor is not null: editor.AddCropCommand.Execute(null); break;
            case Key.P when !ctrl && !typing && editor is not null: editor.AddPanoramaCommand.Execute(3); break;
            case Key.Delete when !typing && editor is not null: editor.RemoveSelectedSlideCommand.Execute(null); break;
            case Key.F5: _vm.StartSlideshowCommand.Execute(null); break;
            case Key.D0 or Key.NumPad0 when !typing && !ctrl: Canvas.ResetZoom(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space) Canvas.PanModifier = false;
    }
}
