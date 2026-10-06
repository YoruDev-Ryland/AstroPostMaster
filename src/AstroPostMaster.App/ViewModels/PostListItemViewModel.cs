using AstroPostMaster.Core.Model;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

public sealed partial class PostListItemViewModel(Post post) : ObservableObject
{
    public Post Post { get; } = post;
    public string Title => Post.Title;
    public string Subtitle
    {
        get
        {
            var count = Core.Slides.PostValidator.SlideCount(Post);
            return $"{Post.UpdatedUtc.ToLocalTime():MMM d, yyyy} · {count} {(count == 1 ? "slide" : "slides")}";
        }
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
    }
}
