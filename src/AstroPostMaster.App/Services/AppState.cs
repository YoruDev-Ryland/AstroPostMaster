using System.Collections.ObjectModel;
using AstroPostMaster.Core.Captions;
using AstroPostMaster.Core.Catalog;
using AstroPostMaster.Core.Model;
using AstroPostMaster.Core.Storage;

namespace AstroPostMaster.App.Services;

/// <summary>Everything loaded from the data folder: settings, profiles and the target catalog.</summary>
public sealed class AppState
{
    private static readonly Lazy<IReadOnlyList<CatalogEntry>> BundledCatalog = new(TargetCatalog.LoadBundledEntries);

    private AppState(AppStore store, AppSettings settings)
    {
        Store = store;
        Settings = settings;
        Catalog = new TargetCatalog(BundledCatalog.Value, store.LoadUserCatalog());
    }

    public AppStore Store { get; }
    public AppSettings Settings { get; }
    public ObservableCollection<Site> Sites { get; } = [];
    public ObservableCollection<Rig> Rigs { get; } = [];
    public ObservableCollection<SoftwareSet> Software { get; } = [];
    public ObservableCollection<HashtagSet> HashtagSets { get; } = [];
    public TargetCatalog Catalog { get; private set; }

    /// <summary>True while profile collections are being rebuilt; selectors must ignore the transient null selections.</summary>
    public bool IsReplacingProfiles { get; set; }

    public static AppState Load(AppStore store)
    {
        var state = new AppState(store, store.LoadSettings());
        foreach (var s in store.LoadSites()) state.Sites.Add(s);
        foreach (var r in store.LoadRigs()) state.Rigs.Add(r);
        foreach (var w in store.LoadSoftware()) state.Software.Add(w);
        foreach (var h in store.LoadHashtagSets()) state.HashtagSets.Add(h);
        return state;
    }

    public CaptionSources Sources() => new([.. Sites], [.. Rigs], [.. Software], [.. HashtagSets], Catalog);

    public void SaveSettings() => Store.SaveSettings(Settings);

    public void SaveProfiles()
    {
        Store.SaveSites(Sites);
        Store.SaveRigs(Rigs);
        Store.SaveSoftware(Software);
        Store.SaveHashtagSets(HashtagSets);
    }

    public void ReloadCatalog() => Catalog = new TargetCatalog(BundledCatalog.Value, Store.LoadUserCatalog());
}
