using System.Collections.ObjectModel;
using AstroPostMaster.App.Services;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AstroPostMaster.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly IPreviewLoader _loader;
    private readonly IUiDispatcher _ui;

    public MainWindowViewModel(AppState state, IPreviewLoader loader, IUiDispatcher ui)
    {
        State = state;
        _loader = loader;
        _ui = ui;
        state.Store.CorruptFileRecovered += path =>
            _ui.Post(() => StatusMessage = $"{Path.GetFileName(path)} was unreadable and was set aside as .bak.");
    }

    public AppState State { get; }
    public ObservableCollection<PostListItemViewModel> Posts { get; } = [];

    [ObservableProperty] public partial PostListItemViewModel? SelectedPost { get; set; }
    [ObservableProperty] public partial EditorViewModel? Editor { get; private set; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    public partial string? StatusMessage { get; set; }

    /// <summary>The status bar text: the last status message, else the first live warning of the open post.</summary>
    public string? StatusLine => StatusMessage ?? Editor?.SaveError ?? Editor?.Warnings.FirstOrDefault()?.Message;

    public bool NeedsProfileSetup => State.Sites.Count == 0 && State.Rigs.Count == 0 && State.Software.Count == 0;

    /// <summary>Completes when the selected post's source preview has loaded.</summary>
    public Task EditorLoading { get; private set; } = Task.CompletedTask;

    public async Task InitializeAsync()
    {
        foreach (var post in State.Store.LoadPosts()) Posts.Add(new PostListItemViewModel(post));
        SelectedPost = Posts.FirstOrDefault();
        await EditorLoading;
    }

    partial void OnSelectedPostChanged(PostListItemViewModel? oldValue, PostListItemViewModel? newValue)
    {
        var previous = Editor;
        if (previous is not null) _ = previous.FlushAsync();
        if (newValue is null)
        {
            Editor = null;
            previous?.Dispose();
            return;
        }
        var editor = new EditorViewModel(newValue.Post, State, _loader, _ui);
        editor.Saved += newValue.Refresh;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(EditorViewModel.Warnings) or nameof(EditorViewModel.SaveError)) OnPropertyChanged(nameof(StatusLine));
            if (e.PropertyName is nameof(EditorViewModel.Title) or nameof(EditorViewModel.SlideCount)) newValue.Refresh();
        };
        Editor = editor;
        previous?.Dispose();
        EditorLoading = editor.LoadSourceAsync();
    }

    partial void OnEditorChanged(EditorViewModel? value) => OnPropertyChanged(nameof(StatusLine));

    /// <summary>Call after the settings window saved: profiles, template or export options may have changed.</summary>
    public void SettingsChanged()
    {
        OnPropertyChanged(nameof(NeedsProfileSetup));
        Editor?.SettingsChanged();
    }

    [RelayCommand]
    private void NewPost()
    {
        var post = PostFactory.CreateNew([.. State.Sites], [.. State.Rigs], [.. State.Software], [.. State.HashtagSets]);
        Add(post);
    }

    [RelayCommand]
    private void DuplicatePost()
    {
        if (Editor is null) return;
        Add(PostFactory.Duplicate(Editor.Post));
    }

    [RelayCommand]
    private void DeletePost()
    {
        if (SelectedPost is not { } item) return;
        var index = Posts.IndexOf(item);
        try
        {
            State.Store.DeletePost(item.Post.Id);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Couldn't delete “{item.Title}”: {ex.Message}";
            return;
        }
        var deleted = Editor;
        Editor = null;
        deleted?.Dispose();
        Posts.Remove(item);
        SelectedPost = Posts.Count == 0 ? null : Posts[Math.Min(index, Posts.Count - 1)];
    }

    /// <summary>Opens an image: fills the current post if it has no source yet, otherwise starts a new post.</summary>
    public async Task OpenImageAsync(string path)
    {
        // A post whose image went missing is relinked; otherwise a post that already has an image starts a new one.
        if (Editor is null || (Editor.Post.SourcePath is not null && !Editor.SourceMissing)) NewPost();
        try
        {
            await Editor!.SetSourceAsync(path);
            StatusMessage = null;
        }
        catch (Exception ex) when (ex is FileNotFoundException or ImageLoadException or IOException or UnauthorizedAccessException)
        {
            StatusMessage = ex is FileNotFoundException ? $"Couldn't find {Path.GetFileName(path)}." : ex.Message;
        }
    }

    private void Add(Post post)
    {
        State.Store.SavePost(post);
        var item = new PostListItemViewModel(post);
        Posts.Insert(0, item);
        SelectedPost = item;
    }
}
