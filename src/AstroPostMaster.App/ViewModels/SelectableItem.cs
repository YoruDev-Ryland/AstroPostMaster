using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

/// <summary>A checkable option (filter chip, hashtag set) that reports toggles to its owner.</summary>
public sealed class SelectableItem(string key, string label, bool isSelected, Action<SelectableItem> onChanged) : ObservableObject
{
    private bool _isSelected = isSelected;

    public string Key { get; } = key;
    public string Label { get; } = label;

    public bool IsSelected
    {
        get => _isSelected;
        set { if (SetProperty(ref _isSelected, value)) onChanged(this); }
    }

    /// <summary>Restores a value without notifying the owner (used when a locked post rejects a toggle).</summary>
    public void Revert(bool value)
    {
        _isSelected = value;
        OnPropertyChanged(nameof(IsSelected));
    }
}
