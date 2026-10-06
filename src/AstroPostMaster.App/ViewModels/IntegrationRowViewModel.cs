using AstroPostMaster.Core.Captions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AstroPostMaster.App.ViewModels;

public sealed class IntegrationRowViewModel(IntegrationRow row, Action changed) : ObservableObject
{
    private string _filter = row.Filter;
    private int _subs = row.Subs;
    private int _subSeconds = row.SubSeconds;

    public string Filter { get => _filter; set { if (SetProperty(ref _filter, value)) changed(); } }
    public int Subs { get => _subs; set { if (SetProperty(ref _subs, Math.Max(0, value))) changed(); } }
    public int SubSeconds { get => _subSeconds; set { if (SetProperty(ref _subSeconds, Math.Max(0, value))) changed(); } }

    public IntegrationRow ToModel() => new(Filter, Subs, SubSeconds);
}
